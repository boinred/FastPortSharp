using Google.Protobuf;
using LibCommons;
using LibNetworks.Extensions;
using LibNetworks.Sessions;
using LibTestTelemetry;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Sockets;

namespace FastPortTestSmokeServer.Sessions;

public class FastPortTestSmokeClientSession : BaseSessionClient, IIdleTrackedSession
{
    private static readonly IDGenerator m_IdGenerator = new IDGenerator();

    private readonly long m_Id = m_IdGenerator.GetNextGeneratedId();
    private readonly IServerTelemetry m_ServerTelemetry;
    private readonly SessionIdleTracker m_SessionIdleTracker;
    // 상태: listener accept completion callback 진입 timestamp
    private long m_AcceptCompletedTimestamp;
    // 상태: OnAccepted task 실행 시작 timestamp
    private long m_OnAcceptedStartedTimestamp;
    // 상태: first socket receive duration 중복 기록 방지 flag
    private int m_AcceptFirstSocketReceiveRecorded;
    // 상태: first receive duration 중복 기록 방지 flag
    private int m_AcceptFirstReceiveRecorded;

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

    // 계측 전달: listener가 accept completion 기준점을 session에 전달
    public void MarkAcceptedByListener(long acceptCompletedTimestamp)
    {
        // 상태: listener accept completion 기준 timestamp 저장
        Volatile.Write(ref m_AcceptCompletedTimestamp, acceptCompletedTimestamp);
    }

    // 계측 전달: listener가 OnAccepted task 시작 기준점을 session에 전달
    public void MarkOnAcceptedStarted(long onAcceptedStartedTimestamp)
    {
        // 상태: OnAccepted task 실행 시작 기준 timestamp 저장
        Volatile.Write(ref m_OnAcceptedStartedTimestamp, onAcceptedStartedTimestamp);
    }

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

    protected override void OnNetworkReceiveCompleted(int bytes, TimeSpan duration)
    {
        // 계측: ReceiveAsync 요청부터 socket byte 도착까지의 대기 시간
        m_ServerTelemetry.RecordOperationDuration("receive-await", duration);
        // 계측: accept completion부터 첫 socket byte 도착까지의 startup receive 경로
        RecordAcceptPathFirstSocketReceive();
    }

    protected override void OnNetworkOperationDuration(string operation, TimeSpan duration)
    {
        // 계측: BaseSession 내부 receive/parse 단계별 duration summary
        m_ServerTelemetry.RecordOperationDuration(operation, duration);
    }

    protected override void OnNetworkPacketReceived(BasePacket packet)
    {
        // 계측: 첫 parsed packet 시점에서 accept 이후 receive 경로 지연 확정
        RecordAcceptPathFirstReceive();
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

    private void RecordAcceptPathFirstSocketReceive()
    {
        // 중복 방지: 세션별 첫 socket receive에서만 accept path duration 기록
        if (Interlocked.Exchange(ref m_AcceptFirstSocketReceiveRecorded, 1) != 0)
        {
            return;
        }

        // 계측: accept completion부터 첫 socket receive completion까지의 전체 startup receive 대기
        long acceptCompletedTimestamp = Volatile.Read(ref m_AcceptCompletedTimestamp);
        if (acceptCompletedTimestamp <= 0)
        {
            return;
        }

        m_ServerTelemetry.RecordOperationDuration(
            "accept-first-socket-receive",
            Stopwatch.GetElapsedTime(acceptCompletedTimestamp));
    }

    private void RecordAcceptPathFirstReceive()
    {
        // 중복 방지: 세션별 첫 packet에서만 accept path duration 기록
        if (Interlocked.Exchange(ref m_AcceptFirstReceiveRecorded, 1) != 0)
        {
            return;
        }

        // 계측: accept completion부터 첫 parsed packet까지의 전체 session-start 경로
        long acceptCompletedTimestamp = Volatile.Read(ref m_AcceptCompletedTimestamp);
        if (acceptCompletedTimestamp > 0)
        {
            m_ServerTelemetry.RecordOperationDuration(
                "accept-first-receive",
                Stopwatch.GetElapsedTime(acceptCompletedTimestamp));
        }

        // 계측: OnAccepted task 시작 이후 첫 parsed packet까지의 receive 준비/수신 경로
        long onAcceptedStartedTimestamp = Volatile.Read(ref m_OnAcceptedStartedTimestamp);
        if (onAcceptedStartedTimestamp > 0)
        {
            m_ServerTelemetry.RecordOperationDuration(
                "onaccepted-start-first-receive",
                Stopwatch.GetElapsedTime(onAcceptedStartedTimestamp));
        }
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
