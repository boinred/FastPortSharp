using LibNetworks.Telemetry;

namespace FastPortLoadRunner;

internal static class ObservedMetricsExtensions
{
    public static ClientObservedMetricsSnapshot ToClientObservedMetricsSnapshot(this MetricsSnapshot snapshot)
    {
        return new ClientObservedMetricsSnapshot(
            snapshot.Timestamp,
            snapshot.TargetSessions,
            snapshot.ConnectedSessions,
            snapshot.TotalSentPackets,
            snapshot.TotalReceivedPackets,
            snapshot.TotalSentBytes,
            snapshot.TotalReceivedBytes,
            snapshot.SentPacketsPerSecond,
            snapshot.ReceivedPacketsPerSecond,
            snapshot.SentBytesPerSecond,
            snapshot.ReceivedBytesPerSecond,
            snapshot.Tps,
            snapshot.RttAverageMs,
            snapshot.RttP50Ms,
            snapshot.RttP95Ms,
            snapshot.RttP99Ms,
            snapshot.AcceptCount,
            snapshot.DisconnectCount,
            snapshot.SocketErrorCount,
            snapshot.SocketErrorRate,
            snapshot.ConnectAttemptCount,
            snapshot.ConnectFailureCount,
            snapshot.PendingRequestCount,
            snapshot.MaxPendingRequestCount,
            snapshot.ActiveSessionRatio,
            snapshot.SchedulerDriftAverageMs,
            snapshot.SchedulerDriftMaxMs);
    }
}
