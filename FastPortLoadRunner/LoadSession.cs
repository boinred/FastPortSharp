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

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var client = new TcpClient
        {
            NoDelay = true
        };

        try
        {
            await client.ConnectAsync(scenario.Host, scenario.Port, cancellationToken);
            metricsCollector.RecordSessionConnected();

            await using NetworkStream stream = client.GetStream();
            var sendTask = SendLoopAsync(stream, cancellationToken);
            var receiveTask = ReceiveLoopAsync(stream, cancellationToken);

            await Task.WhenAll(sendTask, receiveTask);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            metricsCollector.RecordSocketError();
        }
        finally
        {
            metricsCollector.RecordSessionDisconnected();
        }
    }

    private async Task SendLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(1.0 / scenario.SendRatePerSession);

        while (!cancellationToken.IsCancellationRequested)
        {
            long startedAt = Stopwatch.GetTimestamp();
            byte[] packet = CreateEchoRequestPacket();

            await stream.WriteAsync(packet, cancellationToken);
            await stream.FlushAsync(cancellationToken);
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
                metricsCollector.RecordSocketError();
                return;
            }

            byte[] body = new byte[packetSize - PacketHeaderSize];
            if (!await ReadExactAsync(stream, body, cancellationToken))
            {
                return;
            }

            metricsCollector.RecordReceivedPacket(packetSize);
            ParseEchoResponse(body);
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

    private void ParseEchoResponse(byte[] body)
    {
        int packetId = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(0, ProtocolHeaderSize));
        if (packetId != (int)ProtocolId.Tests)
        {
            metricsCollector.RecordSocketError();
            return;
        }

        var response = EchoResponse.Parser.ParseFrom(body.AsSpan(ProtocolHeaderSize).ToArray());
        if (response.Header?.ClientSendTs > 0)
        {
            metricsCollector.RecordRtt((long)response.Header.ClientSendTs, Stopwatch.GetTimestamp());
        }
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
}
