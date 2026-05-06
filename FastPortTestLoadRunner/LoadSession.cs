using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using FastPort.Protocols.Commons;
using FastPort.Protocols.Tests;
using Google.Protobuf;

namespace FastPortTestLoadRunner;

internal sealed class LoadSession(
    int sessionId,
    LoadScenario scenario,
    PayloadGenerator payloadGenerator,
    MetricsCollector metricsCollector)
{
    private const int PacketHeaderSize = 2;
    private const int ProtocolHeaderSize = 4;
    private const ulong HeartbeatRequestId = 0;
    private const ulong HeartbeatClientSendTimestamp = 0;

    // 동기화: send loop와 heartbeat loop가 같은 NetworkStream에 동시에 쓰지 않도록 직렬화
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly OutstandingRequestPacer _pacer = new(scenario.Pacing, metricsCollector);
    // 상태: 테스트 클라이언트가 마지막으로 서버에 패킷을 쓴 시각
    private long _lastClientPacketSentTimestamp = Stopwatch.GetTimestamp();
    private long _requestId;
    private long _outstandingRequests;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var client = new TcpClient
        {
            NoDelay = true
        };
        bool connected = false;

        try
        {
            metricsCollector.RecordConnectAttempt();
            await client.ConnectAsync(scenario.Host, scenario.Port, cancellationToken);
            connected = true;
            metricsCollector.RecordSessionConnected();
            Volatile.Write(ref _lastClientPacketSentTimestamp, Stopwatch.GetTimestamp());

            await using NetworkStream stream = client.GetStream();
            await RunDuplexAsync(
                phaseCancellation => RunPhaseAsync("send", () => SendLoopAsync(stream, phaseCancellation)),
                phaseCancellation => RunPhaseAsync("receive", () => ReceiveLoopAsync(stream, phaseCancellation)),
                scenario.HeartbeatInterval > TimeSpan.Zero
                    ? phaseCancellation => RunPhaseAsync("heartbeat", () => HeartbeatLoopAsync(stream, phaseCancellation))
                    : null,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (LoadSessionPhaseException)
        {
        }
        catch (Exception ex)
        {
            if (!connected)
            {
                metricsCollector.RecordConnectFailure();
                metricsCollector.RecordSocketError("connect", ex);
            }
            else
            {
                metricsCollector.RecordSocketError("unknown", ex);
            }
        }
        finally
        {
            if (connected)
            {
                metricsCollector.RecordSessionDisconnected();
            }
        }
    }

    private async Task RunPhaseAsync(string phase, Func<Task> runAsync)
    {
        try
        {
            await runAsync();
            metricsCollector.RecordPhaseCompletion(phase, "completed");
        }
        catch (OperationCanceledException)
        {
            metricsCollector.RecordPhaseCompletion(phase, "cancelled");
        }
        catch (Exception ex)
        {
            metricsCollector.RecordPhaseCompletion(phase, "faulted");
            metricsCollector.RecordSocketError(phase, ex);
            throw new LoadSessionPhaseException(phase, ex);
        }
    }

    internal static async Task RunDuplexAsync(
        Func<CancellationToken, Task> sendAsync,
        Func<CancellationToken, Task> receiveAsync,
        Func<CancellationToken, Task>? heartbeatAsync,
        CancellationToken cancellationToken)
    {
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task sendTask = sendAsync(sessionCancellation.Token);
        Task receiveTask = receiveAsync(sessionCancellation.Token);
        Task? heartbeatTask = heartbeatAsync?.Invoke(sessionCancellation.Token);

        Task[] phaseTasks = heartbeatTask is null
            ? [sendTask, receiveTask]
            : [sendTask, receiveTask, heartbeatTask];

        await Task.WhenAny(phaseTasks);
        await sessionCancellation.CancelAsync();
        try
        {
            await Task.WhenAll(phaseTasks);
        }
        catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested && phaseTasks.All(task => !task.IsFaulted))
        {
        }
    }

    internal static Task RunDuplexAsync(
        Func<CancellationToken, Task> sendAsync,
        Func<CancellationToken, Task> receiveAsync,
        CancellationToken cancellationToken)
    {
        return RunDuplexAsync(sendAsync, receiveAsync, heartbeatAsync: null, cancellationToken);
    }

    private async Task SendLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        // 정책: 0 rate는 일반 echo traffic 없이 heartbeat만 보내는 idle session 검증 모드
        if (scenario.SendRatePerSession == 0)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return;
        }

        // 정책: 양수 rate는 기존 부하 테스트처럼 일정 간격으로 echo request 전송
        TimeSpan interval = TimeSpan.FromSeconds(1.0 / scenario.SendRatePerSession);

        while (!cancellationToken.IsCancellationRequested)
        {
            long startedAt = Stopwatch.GetTimestamp();
            await _pacer.WaitForPermitAsync(cancellationToken);

            byte[] packet = CreateEchoRequestPacket();

            try
            {
                await WritePacketAsync(stream, packet, "send-write", cancellationToken);
                _pacer.OnRequestSent();
                IncrementOutstandingRequests();
                metricsCollector.RecordSentPacket(packet.Length);
            }
            catch
            {
                _pacer.OnRequestAbandoned();
                throw;
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
            TimeSpan delay = interval - elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task HeartbeatLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(scenario.HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            long lastSentAt = Volatile.Read(ref _lastClientPacketSentTimestamp);
            if (Stopwatch.GetElapsedTime(lastSentAt) < scenario.HeartbeatInterval)
            {
                continue;
            }

            await SendHeartbeatAsync(stream, cancellationToken);
        }
    }

    private async Task ReceiveLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[PacketHeaderSize];

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await ReadExactAsync(stream, header, "receive-header", cancellationToken))
            {
                return;
            }

            int packetSize = BinaryPrimitives.ReadUInt16LittleEndian(header);
            if (packetSize < PacketHeaderSize + ProtocolHeaderSize)
            {
                metricsCollector.RecordProtocolError("invalid-packet-size");
                return;
            }

            byte[] body = new byte[packetSize - PacketHeaderSize];
            if (!await ReadExactAsync(stream, body, "receive-body", cancellationToken))
            {
                return;
            }

            metricsCollector.RecordReceivedPacket(packetSize);
            if (!ParseEchoResponse(body))
            {
                return;
            }
        }
    }

    private byte[] CreateEchoRequestPacket()
    {
        byte[] payload = payloadGenerator.CreatePayload();
        long requestId = Interlocked.Increment(ref _requestId);
        long clientSendTs = Stopwatch.GetTimestamp();

        var request = new EchoRequest
        {
            Header = new Header
            {
                RequestId = (ulong)(((long)sessionId << 32) | requestId),
                ClientSendTs = (ulong)clientSendTs
            },
            Data = ByteString.CopyFrom(payload)
        };

        byte[] message = request.ToByteArray();
        int packetSize = PacketHeaderSize + ProtocolHeaderSize + message.Length;
        if (packetSize > ushort.MaxValue)
        {
            throw new InvalidOperationException($"Packet size exceeds UInt16 max: {packetSize}");
        }

        byte[] packet = new byte[packetSize];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, PacketHeaderSize), (ushort)packetSize);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(PacketHeaderSize, ProtocolHeaderSize), (int)ProtocolId.Tests);
        message.CopyTo(packet.AsSpan(PacketHeaderSize + ProtocolHeaderSize));
        return packet;
    }

    private async Task SendHeartbeatAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] packet = CreateHeartbeatPacket();
        await WritePacketAsync(stream, packet, "heartbeat-write", cancellationToken);
        metricsCollector.RecordSentPacket(packet.Length);
    }

    private byte[] CreateHeartbeatPacket()
    {
        var request = new EchoRequest
        {
            Header = new Header
            {
                RequestId = HeartbeatRequestId,
                ClientSendTs = HeartbeatClientSendTimestamp
            },
            Data = ByteString.Empty
        };

        byte[] message = request.ToByteArray();
        int packetSize = PacketHeaderSize + ProtocolHeaderSize + message.Length;
        byte[] packet = new byte[packetSize];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, PacketHeaderSize), (ushort)packetSize);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(PacketHeaderSize, ProtocolHeaderSize), (int)ProtocolId.Tests);
        message.CopyTo(packet.AsSpan(PacketHeaderSize + ProtocolHeaderSize));
        return packet;
    }

    private async Task WritePacketAsync(
        NetworkStream stream,
        byte[] packet,
        string operation,
        CancellationToken cancellationToken)
    {
        long startedAt = Stopwatch.GetTimestamp();
        bool lockTaken = false;
        try
        {
            await _writeLock.WaitAsync(cancellationToken);
            lockTaken = true;
            await stream.WriteAsync(packet, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            Volatile.Write(ref _lastClientPacketSentTimestamp, Stopwatch.GetTimestamp());
        }
        finally
        {
            if (lockTaken)
            {
                _writeLock.Release();
            }

            metricsCollector.RecordOperationDuration(operation, Stopwatch.GetElapsedTime(startedAt));
        }
    }

    internal bool ParseEchoResponse(byte[] body)
    {
        int packetId = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(0, ProtocolHeaderSize));
        if (packetId != (int)ProtocolId.Tests)
        {
            metricsCollector.RecordProtocolError("unexpected-protocol-id");
            return false;
        }

        try
        {
            var response = EchoResponse.Parser.ParseFrom(body.AsSpan(ProtocolHeaderSize).ToArray());
            if (IsHeartbeatResponse(response))
            {
                return true;
            }

            if (response.Header?.ClientSendTs > 0)
            {
                long clientSendTs = (long)response.Header.ClientSendTs;
                long clientReceiveTs = Stopwatch.GetTimestamp();
                _pacer.OnResponse(CalculateRttMs(clientSendTs, clientReceiveTs));
                metricsCollector.RecordRtt(sessionId, clientSendTs, clientReceiveTs);
            }
            else
            {
                _pacer.OnResponse(0);
            }

            DecrementOutstandingRequests();
        }
        catch (Exception ex)
        {
            metricsCollector.RecordSocketError("protocol", ex);
            return false;
        }

        return true;
    }

    private static bool IsHeartbeatResponse(EchoResponse response)
    {
        return response.Header is not null
            && response.Header.RequestId == HeartbeatRequestId
            && response.Header.ClientSendTs == HeartbeatClientSendTimestamp;
    }

    internal async Task<bool> ReadExactAsync(
        NetworkStream stream,
        Memory<byte> buffer,
        string operation,
        CancellationToken cancellationToken)
    {
        long startedAt = Stopwatch.GetTimestamp();
        int totalRead = 0;
        try
        {
            while (totalRead < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer[totalRead..], cancellationToken);
                if (read <= 0)
                {
                    string reason = totalRead == 0 ? "eof" : "partial-eof";
                    metricsCollector.RecordReceiveClose(operation, reason, OutstandingRequests);
                    return false;
                }

                totalRead += read;
            }

            return true;
        }
        finally
        {
            metricsCollector.RecordOperationDuration(operation, Stopwatch.GetElapsedTime(startedAt));
        }
    }

    internal long OutstandingRequests => Interlocked.Read(ref _outstandingRequests);

    internal async Task WaitForPendingRequestBudgetAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        _ = interval;
        await _pacer.WaitForPermitAsync(cancellationToken);
    }

    internal void IncrementOutstandingRequests()
    {
        Interlocked.Increment(ref _outstandingRequests);
    }

    internal void DecrementOutstandingRequests()
    {
        long current;
        do
        {
            current = Interlocked.Read(ref _outstandingRequests);
            if (current <= 0)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _outstandingRequests, current - 1, current) != current);
    }

    private static double CalculateRttMs(long clientSendTimestamp, long clientReceiveTimestamp)
    {
        long elapsedTicks = clientReceiveTimestamp - clientSendTimestamp;
        if (elapsedTicks <= 0)
        {
            return 0;
        }

        return elapsedTicks * 1000.0 / Stopwatch.Frequency;
    }
}

internal sealed class LoadSessionPhaseException : Exception
{
    public LoadSessionPhaseException(string phase, Exception innerException)
        : base($"Load session phase failed: {phase}", innerException)
    {
    }
}
