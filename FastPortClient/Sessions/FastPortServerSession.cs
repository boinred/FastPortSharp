using Google.Protobuf;
using LibCommons;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using LibNetworks.Extensions;

namespace FastPortClient.Sessions;

public class FastPortServerSession : LibNetworks.Sessions.BaseSessionServer
{
    // Latency 통계 수집기 (DI로 주입받은 공유 인스턴스)
    private static LatencyStats? m_LatencyStats;

    /// <summary>
    /// 전역 Latency 통계 설정 (앱 시작 시 한 번 호출)
    /// </summary>
    public static void ConfigureLatencyStats(LatencyStats latencyStats)
    {
        m_LatencyStats = latencyStats;
    }

    /// <summary>
    /// 전역 Latency 통계 접근자
    /// </summary>
    public static LatencyStats? LatencyStatistics => m_LatencyStats;

    public FastPortServerSession(ILogger<FastPortServerSession> logger, System.Net.Sockets.Socket socket, LibCommons.IBuffers receivedBuffers, LibCommons.IBuffers sendBuffers)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {
    }

    public void SendMessage<T>(FastPort.Protocols.Commons.ProtocolId protocolId, T message) where T : IMessage<T> => RequestSendMessage((int)protocolId, message);

    public override void OnConnected()
    {
        base.OnConnected();

        SendEchoRequest(requestId: 1);
    }

    protected override void OnReceived(BasePacket basePacket)
    {
        // T4: 클라이언트가 응답을 받은 시간 기록
        long clientRecvTs = Stopwatch.GetTimestamp();
        
        base.OnReceived(basePacket);

        if (!basePacket.ParseMessageFromPacket(out int packetId, out FastPort.Protocols.Tests.EchoResponse? response))
        {
            m_Logger.LogError("FastPortServerSession, OnReceived, ParseMessageFromPacket failed.");
            return;
        }

        var header = response!.Header;

        // Latency 통계 기록 (T1, T2, T3, T4)
        m_LatencyStats?.RecordSample(
            requestId: (long)header.RequestId,
            clientSendTs: (long)header.ClientSendTs,
            serverRecvTs: (long)header.ServerRecvTs,
            serverSendTs: (long)header.ServerSendTs,
            clientRecvTs: clientRecvTs
        );

        m_Logger.LogDebug(
            "FastPortServerSession, OnReceived, ReqId:{RequestId}, PacketSize:{PacketSize}, DataSize:{DataSize}",
            header.RequestId, basePacket.PacketSize, basePacket.DataSize);

        // 다음 요청 전송
        SendEchoRequest(requestId: (long)header.RequestId + 1);
    }

    private void SendEchoRequest(long requestId)
    {
        // T1: 클라이언트가 요청을 보내는 시간 기록
        long clientSendTs = Stopwatch.GetTimestamp();

        var request = new FastPort.Protocols.Tests.EchoRequest
        {
            Header = new FastPort.Protocols.Commons.Header
            {
                RequestId = (ulong)requestId,
                ClientSendTs = (ulong)clientSendTs,
                ServerRecvTs = 0,  // 서버에서 설정
                ServerSendTs = 0   // 서버에서 설정
            },
            DataStr = $"Echo Request #{requestId}"
        };

        request.Data = ByteString.CopyFrom(request.DataStr, System.Text.Encoding.UTF8);

        SendMessage(FastPort.Protocols.Commons.ProtocolId.Tests, request);
    }

    /// <summary>
    /// 현재 Latency 통계 출력
    /// </summary>
    public static void PrintLatencyStats()
    {
        if (m_LatencyStats != null)
        {
            Console.WriteLine(m_LatencyStats.GetSummaryString());
        }
        else
        {
            Console.WriteLine("Latency statistics not configured.");
        }
    }

    /// <summary>
    /// 파일에 통계 저장
    /// </summary>
    public static async Task SaveLatencyStatsAsync()
    {
        if (m_LatencyStats != null)
        {
            await m_LatencyStats.SaveToFileAsync();
        }
    }
}

