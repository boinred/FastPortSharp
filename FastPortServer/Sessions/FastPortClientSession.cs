using Google.Protobuf;
using LibCommons;
using LibNetworks.Extensions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Sockets;

namespace FastPortServer.Sessions;

public class FastPortClientSession : LibNetworks.Sessions.BaseSessionClient
{
    private static readonly LibCommons.IDGenerator m_IdGenerator = new IDGenerator();

    private readonly long m_Id = m_IdGenerator.GetNextGeneratedId();

    // Latency 통계 수집기 (모든 세션이 공유)
    private static readonly LatencyStats s_LatencyStats = new(new LatencyStatsOptions
    {
        EnableConsoleOutput = true,
        EnableFileOutput = false,
        MaxSamplesInMemory = 10000
    });

    /// <summary>
    /// 전역 Latency 통계 접근자
    /// </summary>
    public static LatencyStats LatencyStatistics => s_LatencyStats;

    public override long Id => m_Id;

    public FastPortClientSession(ILogger<LibNetworks.Sessions.BaseSessionClient> logger, Socket socket, LibCommons.IBuffers receivedBuffers, LibCommons.IBuffers sendBuffers)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {
    }

    public void SendMessage<T>(FastPort.Protocols.Commons.ProtocolId protocolId, T message) where T : IMessage<T> => RequestSendMessage((int)protocolId, message);

    protected override void OnReceived(BasePacket packet)
    {
        // T2: 서버가 요청을 받은 시간 기록
        long serverRecvTs = LatencyStats.RecordServerReceive();

        base.OnReceived(packet);

        if (!packet.ParseMessageFromPacket(out int packetId, out FastPort.Protocols.Tests.EchoRequest? request))
        {
            m_Logger.LogError("FastPortClientSession, OnReceived, ParseMessageFromPacket failed.");
            return;
        }

        var requestHeader = request!.Header;

        // T3: 서버가 응답을 보내기 직전 시간 기록
        long serverSendTs = LatencyStats.RecordServerSend();

        FastPort.Protocols.Tests.EchoResponse response = new FastPort.Protocols.Tests.EchoResponse
        {
            Header = new FastPort.Protocols.Commons.Header
            {
                RequestId = requestHeader.RequestId,
                ClientSendTs = requestHeader.ClientSendTs,  // T1: 클라이언트가 보낸 원본 타임스탬프 유지
                ServerRecvTs = (ulong)serverRecvTs,          // T2: 서버 수신 시간
                ServerSendTs = (ulong)serverSendTs           // T3: 서버 송신 시간
            },
            Result = FastPort.Protocols.Commons.ResultCode.Ok,
            DataStr = request.DataStr,
            Data = request.Data
        };

        SendMessage(FastPort.Protocols.Commons.ProtocolId.Tests, response);

        m_Logger.LogDebug(
            "FastPortClientSession, OnReceived, ReqId:{RequestId}, PacketSize:{PacketSize}, DataSize:{DataSize}",
            requestHeader.RequestId, packet.PacketSize, packet.DataSize);
    }

    public override void OnAccepted()
    {
        base.OnAccepted();
        m_Logger.LogInformation("FastPortClientSession, OnAccepted. Id:{Id}", Id);
    }

    protected override void OnDisconnected()
    {
        base.OnDisconnected();
        m_Logger.LogInformation("FastPortClientSession, OnDisconnected. Id:{Id}, RemoteEndPoint:{Address}", Id, GetSessionAddress());
    }
}
