using LibNetworks.Telemetry;

namespace FastPortTestLoadValidation;

internal sealed class LoadValidationEvaluator
{
    private const int MaxSlowSessionEntries = 20;

    public LoadValidationStageSummary Evaluate(
        LoadValidationStage stage,
        string metricsPath,
        JsonlReadResult readResult,
        int exitCode = 0,
        string? serverMetricsPath = null,
        JsonlObservedMetricsReadResult? serverReadResult = null,
        ObservedMetricsMergeResult? mergeResult = null,
        string? combinedMetricsPath = null)
    {
        var failures = new List<string>(readResult.Errors);
        IReadOnlyList<ClientObservedMetricsSnapshot> samples = readResult.Samples;
        SessionRttSummarySnapshot[] sessionRttSamples = samples
            .Select(sample => sample.SessionRtt)
            .Where(sample => sample is not null)
            .Cast<SessionRttSummarySnapshot>()
            .ToArray();
        IReadOnlyList<ServerObservedMetricsSnapshot> mergedServerSamples = mergeResult?.CombinedSamples
            .Select(sample => sample.ServerObserved)
            .Where(sample => sample is not null)
            .Cast<ServerObservedMetricsSnapshot>()
            .ToArray()
            ?? Array.Empty<ServerObservedMetricsSnapshot>();

        if (exitCode != 0)
        {
            failures.Add($"LoadRunner exited with code {exitCode}.");
        }

        if (!string.IsNullOrWhiteSpace(serverMetricsPath))
        {
            if (serverReadResult is null)
            {
                failures.Add($"Server metrics were requested but not read: {serverMetricsPath}");
            }
            else
            {
                failures.AddRange(serverReadResult.Errors);
                if (serverReadResult.ServerSamples.Count == 0)
                {
                    failures.Add($"Expected server metrics samples, but got 0 from {serverMetricsPath}.");
                }
            }

            if (samples.Count > 0 && mergeResult is not null && mergeResult.MatchedSamples == 0)
            {
                failures.Add("No client samples matched server samples within merge tolerance.");
            }
        }

        if (samples.Count < stage.Thresholds.MinJsonSamples)
        {
            failures.Add($"Expected at least {stage.Thresholds.MinJsonSamples} JSON samples, but got {samples.Count}.");
        }

        int peakSessions = samples.Count == 0 ? 0 : samples.Max(sample => sample.CurrentSessions);
        double peakSessionRatio = stage.Sessions <= 0 ? 0 : peakSessions / (double)stage.Sessions;
        if (peakSessionRatio < stage.Thresholds.MinPeakSessionRatio)
        {
            failures.Add($"Peak session ratio {peakSessionRatio:P2} is below {stage.Thresholds.MinPeakSessionRatio:P2}.");
        }

        double maxSocketErrorRate = samples.Count == 0 ? 0 : samples.Max(sample => sample.SocketErrorRate);
        if (maxSocketErrorRate > stage.Thresholds.MaxSocketErrorRate)
        {
            failures.Add($"Socket error rate {maxSocketErrorRate:P2} exceeds {stage.Thresholds.MaxSocketErrorRate:P2}.");
        }

        ClientObservedMetricsSnapshot? finalSample = samples.LastOrDefault();
        long finalDisconnectCount = finalSample?.DisconnectCount ?? 0;
        double disconnectRatio = stage.Sessions <= 0 ? 0 : finalDisconnectCount / (double)stage.Sessions;
        if (finalDisconnectCount > stage.Thresholds.MaxFinalDisconnectCount)
        {
            failures.Add($"Final disconnect count {finalDisconnectCount} exceeds {stage.Thresholds.MaxFinalDisconnectCount}.");
        }

        if (disconnectRatio > stage.Thresholds.MaxDisconnectRatio)
        {
            failures.Add($"Disconnect ratio {disconnectRatio:P2} exceeds {stage.Thresholds.MaxDisconnectRatio:P2}.");
        }

        IReadOnlyDictionary<string, long>? socketErrorCountsByPhase = CopyCounters(finalSample?.SocketErrorCountsByPhase);
        IReadOnlyDictionary<string, long>? socketErrorCountsByClass = CopyCounters(finalSample?.SocketErrorCountsByClass);
        AddSocketClassFailures(stage.Thresholds, socketErrorCountsByClass, failures);

        long totalSentPackets = finalSample?.TotalSentPackets ?? 0;
        long totalReceivedPackets = finalSample?.TotalReceivedPackets ?? 0;
        long finalConnectAttemptCount = finalSample?.ConnectAttemptCount ?? 0;
        long finalConnectFailureCount = finalSample?.ConnectFailureCount ?? 0;
        double maxTps = samples.Count == 0 ? 0 : samples.Max(sample => sample.Tps);
        long minObservedPacingWindow = samples
            .Select(sample => sample.MinObservedPacingWindow)
            .Where(window => window > 0)
            .DefaultIfEmpty(0)
            .Min();

        if (totalReceivedPackets <= 0)
        {
            failures.Add("Total received packets must be greater than zero.");
        }

        if (maxTps <= 0)
        {
            failures.Add("Max TPS must be greater than zero.");
        }

        return new LoadValidationStageSummary(
            stage.Id,
            failures.Count == 0,
            stage.Sessions,
            peakSessions,
            peakSessionRatio,
            totalSentPackets,
            totalReceivedPackets,
            maxSocketErrorRate,
            finalDisconnectCount,
            maxTps,
            samples.Count == 0 ? 0 : samples.Max(sample => sample.SentBytesPerSecond),
            samples.Count == 0 ? 0 : samples.Max(sample => sample.ReceivedBytesPerSecond),
            samples.Count == 0 ? 0 : samples.Max(sample => sample.RttP95Ms),
            samples.Count == 0 ? 0 : samples.Max(sample => sample.RttP99Ms),
            samples.Count,
            metricsPath,
            failures,
            FinalConnectAttemptCount: finalConnectAttemptCount,
            FinalConnectFailureCount: finalConnectFailureCount,
            MaxPendingRequestCount: samples.Count == 0 ? 0 : samples.Max(sample => sample.MaxPendingRequestCount),
            MaxSchedulerDriftMs: samples.Count == 0 ? 0 : samples.Max(sample => sample.SchedulerDriftMaxMs),
            MaxActiveSessionRatio: samples.Count == 0 ? 0 : samples.Max(sample => sample.ActiveSessionRatio),
            ServerMetricsPath: serverMetricsPath,
            CombinedMetricsPath: combinedMetricsPath,
            ServerJsonSamples: serverReadResult?.ServerSamples.Count ?? 0,
            MergedSamples: mergeResult?.MatchedSamples ?? 0,
            UnmatchedClientSamples: mergeResult?.UnmatchedClientSamples ?? 0,
            MaxMergeSkewMs: mergeResult?.MaxSkewMs ?? 0,
            MaxPendingSendRequests: mergedServerSamples.Count == 0 ? 0 : mergedServerSamples.Max(sample => sample.MaxPendingSendRequests),
            MaxSendBackpressureEvents: mergedServerSamples.Count == 0 ? 0 : mergedServerSamples.Max(sample => sample.SendBackpressureEvents),
            MaxSendRejectedRequests: mergedServerSamples.Count == 0 ? 0 : mergedServerSamples.Max(sample => sample.SendRejectedRequests),
            MaxSendRejectedBytes: mergedServerSamples.Count == 0 ? 0 : mergedServerSamples.Max(sample => sample.SendRejectedBytes),
            MaxSendDrainYieldCount: mergedServerSamples.Count == 0 ? 0 : mergedServerSamples.Max(sample => sample.SendDrainYieldCount),
            MaxSendDrainYieldQueuedBytes: mergedServerSamples.Count == 0 ? 0 : mergedServerSamples.Max(sample => sample.MaxSendDrainYieldQueuedBytes),
            MaxSendBufferBytes: mergedServerSamples.Count == 0 ? 0 : mergedServerSamples.Max(sample => sample.MaxSendBufferBytes),
            MaxSendRequestsPerSecond: mergedServerSamples.Count == 0 ? 0 : mergedServerSamples.Max(sample => sample.SendRequestsPerSecond),
            MaxSendCompletionsPerSecond: mergedServerSamples.Count == 0 ? 0 : mergedServerSamples.Max(sample => sample.SendCompletionsPerSecond),
            MaxSendRejectedRequestsPerSecond: mergedServerSamples.Count == 0 ? 0 : mergedServerSamples.Max(sample => sample.SendRejectedRequestsPerSecond),
            MaxSendDrainYieldCountPerSecond: mergedServerSamples.Count == 0 ? 0 : mergedServerSamples.Max(sample => sample.SendDrainYieldCountPerSecond),
            MaxPacingWaitCount: samples.Count == 0 ? 0 : samples.Max(sample => sample.TotalPacingWaitCount),
            MaxPacingAverageWaitMs: samples.Count == 0 ? 0 : samples.Max(sample => sample.PacingAverageWaitMs),
            MaxPacingWindowIncreaseCount: samples.Count == 0 ? 0 : samples.Max(sample => sample.PacingWindowIncreaseCount),
            MaxPacingWindowDecreaseCount: samples.Count == 0 ? 0 : samples.Max(sample => sample.PacingWindowDecreaseCount),
            MinObservedPacingWindow: minObservedPacingWindow,
            MaxObservedPacingWindow: samples.Count == 0 ? 0 : samples.Max(sample => sample.MaxObservedPacingWindow),
            SocketErrorCountsByPhase: socketErrorCountsByPhase,
            SocketErrorCountsByClass: socketErrorCountsByClass,
            SessionRttTrackedSessionCount: MaxSessionRttCount(sessionRttSamples, sample => sample.TrackedSessionCount),
            SessionRttEligibleSessionCount: MaxSessionRttCount(sessionRttSamples, sample => sample.EligibleSessionCount),
            SessionRttExcludedLowSampleSessionCount: MaxSessionRttCount(sessionRttSamples, sample => sample.ExcludedLowSampleSessionCount),
            MaxSessionRttP50OfP95Ms: MaxSessionRttValue(sessionRttSamples, sample => sample.P50OfSessionP95Ms),
            MaxSessionRttP95OfP95Ms: MaxSessionRttValue(sessionRttSamples, sample => sample.P95OfSessionP95Ms),
            MaxSessionRttP99OfP95Ms: MaxSessionRttValue(sessionRttSamples, sample => sample.P99OfSessionP95Ms),
            MaxSessionRttMaxSessionP95Ms: MaxSessionRttValue(sessionRttSamples, sample => sample.MaxSessionP95Ms),
            MaxSessionRttMaxSessionP99Ms: MaxSessionRttValue(sessionRttSamples, sample => sample.MaxSessionP99Ms),
            MaxSessionRttMaxSessionMaxMs: MaxSessionRttValue(sessionRttSamples, sample => sample.MaxSessionMaxMs),
            SlowestSessions: SelectSlowestSessions(sessionRttSamples),
            OperationDurations: CopyOperationDurations(finalSample?.OperationDurations));
    }

    private static int MaxSessionRttCount(
        IReadOnlyCollection<SessionRttSummarySnapshot> samples,
        Func<SessionRttSummarySnapshot, int> selector)
    {
        return samples.Count == 0 ? 0 : samples.Max(selector);
    }

    private static double MaxSessionRttValue(
        IReadOnlyCollection<SessionRttSummarySnapshot> samples,
        Func<SessionRttSummarySnapshot, double> selector)
    {
        return samples.Count == 0 ? 0 : samples.Max(selector);
    }

    private static IReadOnlyList<SlowSessionRttSnapshot>? SelectSlowestSessions(
        IReadOnlyCollection<SessionRttSummarySnapshot> samples)
    {
        SlowSessionRttSnapshot[] slowestSessions = samples
            .SelectMany(sample => sample.SlowestSessions ?? Array.Empty<SlowSessionRttSnapshot>())
            .GroupBy(session => session.SessionId)
            .Select(group => group
                .OrderByDescending(session => session.RttP95Ms)
                .ThenByDescending(session => session.RttP99Ms)
                .ThenByDescending(session => session.RttMaxMs)
                .First())
            .OrderByDescending(session => session.RttP95Ms)
            .ThenByDescending(session => session.RttP99Ms)
            .ThenByDescending(session => session.RttMaxMs)
            .ThenBy(session => session.SessionId)
            .Take(MaxSlowSessionEntries)
            .ToArray();

        return slowestSessions.Length == 0 ? null : slowestSessions;
    }

    private static IReadOnlyDictionary<string, long>? CopyCounters(IReadOnlyDictionary<string, long>? counters)
    {
        if (counters is null || counters.Count == 0)
        {
            return counters;
        }

        return counters
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, ObservedOperationDurationSnapshot>? CopyOperationDurations(
        IReadOnlyDictionary<string, ObservedOperationDurationSnapshot>? durations)
    {
        if (durations is null || durations.Count == 0)
        {
            return durations;
        }

        return durations
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static void AddSocketClassFailures(
        LoadValidationThresholds thresholds,
        IReadOnlyDictionary<string, long>? socketErrorCountsByClass,
        ICollection<string> failures)
    {
        foreach (KeyValuePair<string, long> threshold in thresholds.MaxSocketErrorCountsByClass.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            long observed = socketErrorCountsByClass?.GetValueOrDefault(threshold.Key) ?? 0;
            if (observed > threshold.Value)
            {
                failures.Add($"Socket error class '{threshold.Key}' count {observed} exceeds {threshold.Value}.");
            }
        }
    }
}
