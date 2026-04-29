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
    double MaxActiveSessionRatio = 0);

internal sealed record LoadValidationRunSummary(
    string RunId,
    bool Passed,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<LoadValidationStageSummary> Stages);
