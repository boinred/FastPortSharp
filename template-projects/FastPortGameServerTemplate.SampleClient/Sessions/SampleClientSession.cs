using FastPortGameServerTemplate.Protocols;
using LibCommons;
using LibNetworks.Extensions;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Sockets;

namespace FastPortGameServerTemplate.SampleClient.Sessions;

// Connecting-out session (BaseSessionServer = "the remote is a server").
// On connect: send EchoRequest(1001).
// On receive: parse EchoResponse(1002), measure RTT, signal completion.
public sealed class SampleClientSession : BaseSessionServer
{
    private readonly EchoSignal m_Signal;
    private readonly SampleClientOptions m_Options;
    private long m_SendTimestamp;

    public SampleClientSession(
        ILogger<BaseSessionServer> logger,
        Socket socket,
        IBuffers receivedBuffers,
        IBuffers sendBuffers,
        EchoSignal signal,
        IOptions<SampleClientOptions> options)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {
        m_Signal = signal;
        m_Options = options.Value;
    }

    public override void OnConnected()
    {
        base.OnConnected();
        m_Logger.LogInformation("Connected. Sending EchoRequest. Message=\"{Message}\"", m_Options.Message);

        m_SendTimestamp = Stopwatch.GetTimestamp();
        var request = new EchoRequest { Message = m_Options.Message };
        RequestSendMessage((int)PacketIds.EchoRequest, request);
    }

    protected override void OnReceived(BasePacket packet)
    {
        base.OnReceived(packet);

        if (!packet.ParseMessageFromPacket<EchoResponse>(out var packetId, out var response) || response is null)
        {
            m_Logger.LogError("ParseMessageFromPacket failed. PacketId={PacketId}, DataSize={DataSize}", packetId, packet.DataSize);
            return;
        }

        if (packetId != (int)PacketIds.EchoResponse)
        {
            m_Logger.LogWarning("Unexpected packet id. Expected={Expected}, Got={Actual}", (int)PacketIds.EchoResponse, packetId);
            return;
        }

        long recvTimestamp = Stopwatch.GetTimestamp();
        double rttMs = (recvTimestamp - m_SendTimestamp) * 1000.0 / Stopwatch.Frequency;

        m_Logger.LogInformation(
            "EchoResponse received. Message=\"{Message}\", ServerUnixMs={ServerUnixMs}, RTT={RttMs:F3}ms",
            response.Message, response.ServerUnixMs, rttMs);

        m_Signal.Complete(new EchoResult(response.Message, response.ServerUnixMs, rttMs));
    }
}
