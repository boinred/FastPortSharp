using Google.Protobuf;
using LibCommons;
using LibNetworks.Extensions;
using LibNetworks.Sessions;
using LibTestTelemetry;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace FastPortTestSmokeServer.Sessions;

public class FastPortTestSmokeClientSession : BaseSessionClient, IIdleTrackedSession
{
    private static readonly IDGenerator m_IdGenerator = new IDGenerator();

    private readonly long m_Id = m_IdGenerator.GetNextGeneratedId();
    private readonly IServerTelemetry m_ServerTelemetry;
    private readonly SessionIdleTracker m_SessionIdleTracker;

    private static readonly LatencyStats s_LatencyStats = new(new LatencyStatsOptions
    {
        EnableConsoleOutput = true,
        EnableFileOutput = false,
        MaxSamplesInMemory = 10000
    });

    public static LatencyStats LatencyStatistics => s_LatencyStats;

    public override long Id => m_Id;

    public FastPortTestSmokeClientSession(
        ILogger<BaseSessionClient> logger,
        Socket socket,
        IBuffers receivedBuffers,
        IBuffers sendBuffers,
        IServerTelemetry serverTelemetry,
        SessionIdleTracker sessionIdleTracker)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {
        m_ServerTelemetry = serverTelemetry;
        m_SessionIdleTracker = sessionIdleTracker;
    }

    public bool SendMessage<T>(FastPort.Protocols.Commons.ProtocolId protocolId, T message)
        where T : IMessage<T> => TryRequestSendMessage((int)protocolId, message);

    protected override void OnReceived(BasePacket packet)
    {
        long serverRecvTs = LatencyStats.RecordServerReceive();

        base.OnReceived(packet);

        if (!TryParseEchoRequest(packet, out int packetId, out FastPort.Protocols.Tests.EchoRequest? request))
        {
            m_Logger.LogError("FastPortTestSmokeClientSession, OnReceived, ParseMessageFromPacket failed.");
            return;
        }

        if (packetId != (int)FastPort.Protocols.Commons.ProtocolId.Tests)
        {
            m_ServerTelemetry.RecordProtocolError();
            m_Logger.LogError(
                "FastPortTestSmokeClientSession, OnReceived, Unexpected ProtocolId. PacketId:{PacketId}",
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

        if (!SendMessage(FastPort.Protocols.Commons.ProtocolId.Tests, response))
        {
            m_Logger.LogDebug(
                "FastPortTestSmokeClientSession, OnReceived, Dropped echo response due to send backpressure. ReqId:{RequestId}",
                requestHeader.RequestId);
            return;
        }

        m_Logger.LogDebug(
            "FastPortTestSmokeClientSession, OnReceived, ReqId:{RequestId}, PacketSize:{PacketSize}, DataSize:{DataSize}",
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
            m_Logger.LogError(ex, "FastPortTestSmokeClientSession, TryParseEchoRequest, Exception.");
        }

        packetId = 0;
        request = null;
        m_ServerTelemetry.RecordParseError();
        return false;
    }

    protected override void OnNetworkSessionDisconnected(NetworkDisconnectReason reason)
    {
        m_ServerTelemetry.RecordSessionDisconnected(ToTelemetryReason(reason));
    }

    protected override void OnNetworkSocketError(string phase, SocketError? socketError, Exception? exception)
    {
        m_ServerTelemetry.RecordSocketError(phase, socketError, exception);
    }

    protected override void OnNetworkPacketReceived(BasePacket packet)
    {
        m_ServerTelemetry.RecordReceived(packet.PacketSize);
    }

    protected override void OnNetworkBytesSent(int bytes)
    {
        m_ServerTelemetry.RecordSent(bytes);
    }

    protected override void OnNetworkSendRequested(int bytes, int queuedBytes)
    {
        m_ServerTelemetry.RecordSendRequested(bytes, queuedBytes);
    }

    protected override void OnNetworkSendCompleted()
    {
        m_ServerTelemetry.RecordSendCompleted();
    }

    protected override void OnNetworkSendAbandoned(int count)
    {
        m_ServerTelemetry.RecordSendAbandoned(count);
    }

    protected override void OnNetworkSendBackpressure()
    {
        m_ServerTelemetry.RecordSendBackpressure();
    }

    protected override void OnNetworkSendRejected(int bytes, int queuedBytes)
    {
        m_ServerTelemetry.RecordSendRejected(bytes, queuedBytes);
    }

    protected override void OnNetworkSendDrainYield(int queuedBytes)
    {
        m_ServerTelemetry.RecordSendDrainYield(queuedBytes);
    }

    protected override void OnNetworkSendBufferSample(int queuedBytes)
    {
        m_ServerTelemetry.RecordSendBufferSample(queuedBytes);
    }

    public override void OnAccepted()
    {
        // Activity: accept 직후 idle scan 기준 시각 갱신
        MarkNetworkActivity();
        // Registry: TimerQueue 기반 idle cleanup 대상 등록
        m_SessionIdleTracker.Register(this);
        base.OnAccepted();
        m_Logger.LogInformation("FastPortTestSmokeClientSession, OnAccepted. Id:{Id}", Id);
    }

    protected override void OnDisconnected()
    {
        // Registry: disconnect 완료 시 stale tracker entry 제거
        m_SessionIdleTracker.Unregister(Id);
        base.OnDisconnected();
        m_Logger.LogInformation("FastPortTestSmokeClientSession, OnDisconnected. Id:{Id}, RemoteEndPoint:{Address}", Id, GetSessionAddress());
    }

    private static string ToTelemetryReason(NetworkDisconnectReason reason)
    {
        return reason switch
        {
            NetworkDisconnectReason.RemoteClosed => "remote-closed",
            NetworkDisconnectReason.ReceiveSocketError => "receive-socket-error",
            NetworkDisconnectReason.ReceiveRequestError => "receive-request-error",
            NetworkDisconnectReason.SendSocketError => "send-socket-error",
            NetworkDisconnectReason.SendZeroBytes => "send-zero-bytes",
            NetworkDisconnectReason.IdleTimeout => "idle-timeout",
            NetworkDisconnectReason.LocalShutdown => "local-shutdown",
            _ => "unknown"
        };
    }
}
