using System.Runtime.CompilerServices;
using LibTestTelemetry;

namespace FastPortDashboard.Maui.Adapters;

// Design Ref: §3.3 — Mock data adapter.
// 실제 server 없이 UI 검증용. 1초 간격으로 그럴듯한 server snapshot을 yield.
public sealed class MockPollingAdapter : IPollingAdapter
{
    private readonly TimeSpan _interval;
    private readonly Random _rng;

    public MockPollingAdapter(TimeSpan? interval = null, int seed = 42)
    {
        _interval = interval ?? TimeSpan.FromSeconds(1);
        _rng = new Random(seed);
    }

    public async IAsyncEnumerable<ObservedMetricsSnapshot> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        long currentSessions = 0;
        long totalAccepted = 0;
        long totalDisconnected = 0;
        long totalReceived = 0;
        long totalSentBytes = 0;
        long totalSendCompletions = 0;
        long pendingSendRequests = 0;
        long sendBufferBytes = 0;
        long totalSendRequests = 0;

        while (!ct.IsCancellationRequested)
        {
            // 세션 변동 (랜덤 walk)
            int sessionDelta = _rng.Next(-2, 5);
            currentSessions = Math.Max(0, currentSessions + sessionDelta);
            if (sessionDelta > 0) { totalAccepted += sessionDelta; }
            else if (sessionDelta < 0) { totalDisconnected += -sessionDelta; }

            // 패킷/바이트 누적
            long deltaPackets = _rng.Next(50, 250) * Math.Max(1, currentSessions);
            long deltaBytes = deltaPackets * _rng.Next(64, 512);
            totalReceived += deltaPackets;
            totalSentBytes += deltaBytes;
            totalSendCompletions += deltaPackets;
            totalSendRequests += deltaPackets;

            pendingSendRequests = _rng.Next(0, 10);
            sendBufferBytes = pendingSendRequests * 256;

            var serverSnap = new ServerObservedMetricsSnapshot(
                Timestamp: DateTimeOffset.UtcNow,
                CurrentSessions: currentSessions,
                TotalAcceptedSessions: totalAccepted,
                TotalDisconnectedSessions: totalDisconnected,
                TotalReceivedPackets: totalReceived,
                TotalSendCompletions: totalSendCompletions,
                TotalParsedPacketBytes: totalSentBytes,
                TotalSentBytes: totalSentBytes,
                ReceivedPacketsPerSecond: deltaPackets,
                SendCompletionsPerSecond: deltaPackets,
                ParsedPacketBytesPerSecond: deltaBytes,
                SentBytesPerSecond: deltaBytes,
                AcceptedSessionsPerSecond: Math.Max(0, sessionDelta),
                DisconnectedSessionsPerSecond: Math.Max(0, -sessionDelta),
                AcceptErrorCount: 0,
                SocketErrorCount: 0,
                ParseErrorCount: 0,
                ProtocolErrorCount: 0,
                SocketErrorRate: 0,
                TotalSendRequests: totalSendRequests,
                PendingSendRequests: pendingSendRequests,
                SendBufferBytes: sendBufferBytes);

            // Design Ref: §3.4 (dashboard-rtt-chart) — Client RTT 시뮬레이션 (P95 50~100ms random walk).
            var clientSnap = new ClientObservedMetricsSnapshot(
                Timestamp: DateTimeOffset.UtcNow,
                TargetSessions: 100,
                CurrentSessions: (int)currentSessions,
                TotalSentPackets: 0,
                TotalReceivedPackets: 0,
                TotalSentBytes: 0,
                TotalReceivedBytes: 0,
                SentPacketsPerSecond: 0,
                ReceivedPacketsPerSecond: 0,
                SentBytesPerSecond: 0,
                ReceivedBytesPerSecond: 0,
                Tps: 0,
                RttAverageMs: 30 + _rng.NextDouble() * 20,
                RttP50Ms: 25 + _rng.NextDouble() * 15,
                RttP95Ms: 50 + _rng.NextDouble() * 50,
                RttP99Ms: 100 + _rng.NextDouble() * 100,
                ConnectCount: 0,
                DisconnectCount: 0,
                SocketErrorCount: 0,
                SocketErrorRate: 0);

            yield return ObservedMetricsSnapshot.Combined(clientSnap, serverSnap);

            try
            {
                await Task.Delay(_interval, ct);
            }
            catch (OperationCanceledException) { yield break; }
        }
    }
}
