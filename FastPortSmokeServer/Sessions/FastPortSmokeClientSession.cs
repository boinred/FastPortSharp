using Google.Protobuf;
using LibCommons;
using LibNetworks.Extensions;
using LibNetworks.Sessions;
using LibNetworks.Telemetry;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace FastPortSmokeServer.Sessions;

public class FastPortSmokeClientSession : BaseSessionClient
{
    private static readonly IDGenerator m_IdGenerator = new IDGenerator();

    private readonly long m_Id = m_IdGenerator.GetNextGeneratedId();

    private static readonly LatencyStats s_LatencyStats = new(new LatencyStatsOptions
    {
        EnableConsoleOutput = true,
        EnableFileOutput = false,
        MaxSamplesInMemory = 10000
    });

    public static LatencyStats LatencyStatistics => s_LatencyStats;

    public override long Id => m_Id;

    public FastPortSmokeClientSession(
        ILogger<BaseSessionClient> logger,
        Socket socket,
        IBuffers receivedBuffers,
        IBuffers sendBuffers,
        IServerTelemetry serverTelemetry)
        : base(logger, socket, receivedBuffers, sendBuffers, serverTelemetry)
    {
    }

    public void SendMessage<T>(FastPort.Protocols.Commons.ProtocolId protocolId, T message)
        where T : IMessage<T> => RequestSendMessage((int)protocolId, message);

    protected override void OnReceived(BasePacket packet)
    {
        long serverRecvTs = LatencyStats.RecordServerReceive();

        base.OnReceived(packet);

        if (!TryParseEchoRequest(packet, out int packetId, out FastPort.Protocols.Tests.EchoRequest? request))
        {
            m_Logger.LogError("FastPortSmokeClientSession, OnReceived, ParseMessageFromPacket failed.");
            return;
        }

        if (packetId != (int)FastPort.Protocols.Commons.ProtocolId.Tests)
        {
            ServerTelemetry.RecordProtocolError();
            m_Logger.LogError(
                "FastPortSmokeClientSession, OnReceived, Unexpected ProtocolId. PacketId:{PacketId}",
                packetId);
            return;
        }

        var requestHeader = request!.Header;
        long serverSendTs = LatencyStats.RecordServerSend();

        FastPort.Protocols.Tests.EchoResponse response = new FastPort.Protocols.Tests.EchoResponse
        {
            Header = new FastPort.Protocols.Commons.Header
            {
                RequestId = requestHeader.RequestId,
                ClientSendTs = requestHeader.ClientSendTs,
                ServerRecvTs = (ulong)serverRecvTs,
                ServerSendTs = (ulong)serverSendTs
            },
            Result = FastPort.Protocols.Commons.ResultCode.Ok,
            DataStr = request.DataStr,
            Data = request.Data
        };

        SendMessage(FastPort.Protocols.Commons.ProtocolId.Tests, response);

        m_Logger.LogDebug(
            "FastPortSmokeClientSession, OnReceived, ReqId:{RequestId}, PacketSize:{PacketSize}, DataSize:{DataSize}",
            requestHeader.RequestId,
            packet.PacketSize,
            packet.DataSize);
    }

    private bool TryParseEchoRequest(BasePacket packet, out int packetId, out FastPort.Protocols.Tests.EchoRequest? request)
    {
        try
        {
            if (packet.ParseMessageFromPacket(out packetId, out request))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "FastPortSmokeClientSession, TryParseEchoRequest, Exception.");
        }

        packetId = 0;
        request = null;
        ServerTelemetry.RecordParseError();
        return false;
    }

    public override void OnAccepted()
    {
        base.OnAccepted();
        m_Logger.LogInformation("FastPortSmokeClientSession, OnAccepted. Id:{Id}", Id);
    }

    protected override void OnDisconnected()
    {
        base.OnDisconnected();
        m_Logger.LogInformation("FastPortSmokeClientSession, OnDisconnected. Id:{Id}, RemoteEndPoint:{Address}", Id, GetSessionAddress());
    }
}
