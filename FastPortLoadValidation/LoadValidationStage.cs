namespace FastPortLoadValidation;

internal sealed record LoadValidationStage(
    string Id,
    int Sessions,
    string Payload,
    int SendRatePerSession,
    TimeSpan RampUp,
    TimeSpan Duration,
    TimeSpan MetricsInterval,
    LoadValidationThresholds Thresholds);

internal sealed record LoadValidationThresholds(
    double MinPeakSessionRatio,
    double MaxSocketErrorRate,
    double MaxDisconnectRatio,
    int MinJsonSamples)
{
    public static LoadValidationThresholds Default { get; } = new(
        MinPeakSessionRatio: 0.95,
        MaxSocketErrorRate: 0.01,
        MaxDisconnectRatio: 0.05,
        MinJsonSamples: 3);
}

internal sealed record LoadValidationRunManifest(
    string RunId,
    DateTimeOffset StartedAt,
    string Profile,
    string Host,
    int Port,
    IReadOnlyList<LoadValidationStage> Stages);

internal sealed record LoadValidationStageSummary(
    string StageId,
    bool Passed,
    int TargetSessions,
    int PeakCurrentSessions,
    double PeakSessionRatio,
    long TotalSentPackets,
    long TotalReceivedPackets,
    double MaxSocketErrorRate,
    long FinalDisconnectCount,
    double MaxTps,
    double MaxSentBytesPerSecond,
    double MaxReceivedBytesPerSecond,
    double MaxRttP95Ms,
    double MaxRttP99Ms,
    int JsonSamples,
    string MetricsPath,
    IReadOnlyList<string> Failures,
    long FinalConnectAttemptCount = 0,
    long FinalConnectFailureCount = 0,
    long MaxPendingRequestCount = 0,
    double MaxSchedulerDriftMs = 0,
    double MaxActiveSessionRatio = 0,
    string? ServerMetricsPath = null,
    string? CombinedMetricsPath = null,
    int ServerJsonSamples = 0,
    int MergedSamples = 0,
    int UnmatchedClientSamples = 0,
    double MaxMergeSkewMs = 0,
    long MaxPendingSendRequests = 0,
    long MaxSendBackpressureEvents = 0,
    long MaxSendRejectedRequests = 0,
    long MaxSendRejectedBytes = 0,
    long MaxSendDrainYieldCount = 0,
    long MaxSendDrainYieldQueuedBytes = 0,
    long MaxSendBufferBytes = 0,
    double MaxSendRequestsPerSecond = 0,
    double MaxSendCompletionsPerSecond = 0,
    double MaxSendRejectedRequestsPerSecond = 0,
    double MaxSendDrainYieldCountPerSecond = 0,
    IReadOnlyDictionary<string, long>? SocketErrorCountsByPhase = null,
    IReadOnlyDictionary<string, long>? SocketErrorCountsByClass = null);

internal sealed record LoadValidationRunSummary(
    string RunId,
    bool Passed,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<LoadValidationStageSummary> Stages);
