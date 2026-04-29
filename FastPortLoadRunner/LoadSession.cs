using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using FastPort.Protocols.Commons;
using FastPort.Protocols.Tests;
using Google.Protobuf;

namespace FastPortLoadRunner;

internal sealed class LoadSession(
    int sessionId,
    LoadScenario scenario,
    PayloadGenerator payloadGenerator,
    MetricsCollector metricsCollector)
{
    private const int PacketHeaderSize = 2;
    private const int ProtocolHeaderSize = 4;

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

            await using NetworkStream stream = client.GetStream();
            var sendTask = RunPhaseAsync("send", () => SendLoopAsync(stream, cancellationToken));
            var receiveTask = RunPhaseAsync("receive", () => ReceiveLoopAsync(stream, cancellationToken));

            await Task.WhenAll(sendTask, receiveTask);
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
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            metricsCollector.RecordSocketError(phase, ex);
            throw new LoadSessionPhaseException(phase, ex);
        }
    }

    private async Task SendLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(1.0 / scenario.SendRatePerSession);

        while (!cancellationToken.IsCancellationRequested)
        {
            long startedAt = Stopwatch.GetTimestamp();
            await WaitForPendingRequestBudgetAsync(interval, cancellationToken);

            byte[] packet = CreateEchoRequestPacket();

            await stream.WriteAsync(packet, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            IncrementOutstandingRequests();
            metricsCollector.RecordSentPacket(packet.Length);

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
            TimeSpan delay = interval - elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task ReceiveLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[PacketHeaderSize];

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await ReadExactAsync(stream, header, cancellationToken))
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
            if (!await ReadExactAsync(stream, body, cancellationToken))
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
            if (response.Header?.ClientSendTs > 0)
            {
                metricsCollector.RecordRtt((long)response.Header.ClientSendTs, Stopwatch.GetTimestamp());
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

    private static async Task<bool> ReadExactAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[totalRead..], cancellationToken);
            if (read <= 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }

    internal long OutstandingRequests => Interlocked.Read(ref _outstandingRequests);

    internal async Task WaitForPendingRequestBudgetAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        int? maxPendingRequestsPerSession = scenario.MaxPendingRequestsPerSession;
        if (maxPendingRequestsPerSession is null)
        {
            return;
        }

        TimeSpan delay = interval < TimeSpan.FromMilliseconds(1)
            ? interval
            : TimeSpan.FromMilliseconds(1);

        while (!cancellationToken.IsCancellationRequested
            && Interlocked.Read(ref _outstandingRequests) >= maxPendingRequestsPerSession.Value)
        {
            await Task.Delay(delay, cancellationToken);
        }
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
}

internal sealed class LoadSessionPhaseException : Exception
{
    public LoadSessionPhaseException(string phase, Exception innerException)
        : base($"Load session phase failed: {phase}", innerException)
    {
    }
}
