using System.Text.Json;
using LibNetworks.Telemetry;

namespace FastPortTestLoadValidation;

internal sealed class LoadValidationSummaryWriter
{
    private static readonly JsonSerializerOptions s_JsonOptions = new(ObservedMetricsJson.SerializerOptions)
    {
        WriteIndented = true
    };

    public async Task WriteManifestAsync(
        string outputDirectory,
        LoadValidationRunManifest manifest,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        string path = Path.Combine(outputDirectory, "manifest.json");
        string json = JsonSerializer.Serialize(manifest, s_JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public async Task WriteSummaryAsync(
        string outputDirectory,
        LoadValidationRunSummary summary,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        string jsonPath = Path.Combine(outputDirectory, "summary.json");
        string markdownPath = Path.Combine(outputDirectory, "summary.md");

        string json = JsonSerializer.Serialize(summary, s_JsonOptions);
        await File.WriteAllTextAsync(jsonPath, json, cancellationToken);
        await File.WriteAllTextAsync(markdownPath, ToMarkdown(summary), cancellationToken);
    }

    public async Task WriteObservedMetricsJsonlAsync(
        string path,
        IReadOnlyList<ObservedMetricsSnapshot> samples,
        CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var writer = new StreamWriter(File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read));
        foreach (ObservedMetricsSnapshot sample in samples)
        {
            string json = ObservedMetricsJson.Serialize(sample);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        }
    }

    private static string ToMarkdown(LoadValidationRunSummary summary)
    {
        var lines = new List<string>
        {
            $"# Load Validation Summary: {summary.RunId}",
            string.Empty,
            $"Status: {(summary.Passed ? "Passed" : "Failed")}",
            $"Started: {summary.StartedAt:O}",
            $"Completed: {summary.CompletedAt:O}",
            string.Empty,
            "| Stage | Result | Target | Peak | Peak Ratio | Max TPS | Max Pending Req | Max Pending Send | Server Backpressure | Rejected Send | Drain Yield | Pacing | Merge | Max Drift | RTT P95 | RTT P99 | Session RTT | Socket Errors | Samples |",
            "|-------|--------|--------|------|------------|---------|-----------------|------------------|---------------------|---------------|-------------|--------|-------|-----------|---------|---------|-------------|---------------|---------|"
        };

        foreach (LoadValidationStageSummary stage in summary.Stages)
        {
            lines.Add(string.Join(
                " | ",
                string.Empty,
                stage.StageId,
                stage.Passed ? "Passed" : "Failed",
                stage.TargetSessions.ToString(),
                stage.PeakCurrentSessions.ToString(),
                stage.PeakSessionRatio.ToString("P2"),
                stage.MaxTps.ToString("F2"),
                stage.MaxPendingRequestCount.ToString(),
                stage.MaxPendingSendRequests.ToString(),
                stage.MaxSendBackpressureEvents.ToString(),
                $"{stage.MaxSendRejectedRequests}/{stage.MaxSendRejectedBytes}",
                $"{stage.MaxSendDrainYieldCount}/{stage.MaxSendDrainYieldQueuedBytes}",
                FormatPacing(stage),
                $"{stage.MergedSamples}/{stage.UnmatchedClientSamples}",
                $"{stage.MaxSchedulerDriftMs:F2}ms",
                $"{stage.MaxRttP95Ms:F2}ms",
                $"{stage.MaxRttP99Ms:F2}ms",
                FormatSessionRtt(stage),
                stage.MaxSocketErrorRate.ToString("P2"),
                stage.JsonSamples.ToString(),
                string.Empty));

            foreach (string failure in stage.Failures)
            {
                lines.Add($"- {stage.StageId}: {failure}");
            }

            if (stage.SocketErrorCountsByClass is { Count: > 0 })
            {
                foreach (KeyValuePair<string, long> pair in stage.SocketErrorCountsByClass
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Take(5))
                {
                    lines.Add($"- {stage.StageId}: socket {pair.Key} = {pair.Value}");
                }
            }

            if (stage.SessionRttTrackedSessionCount > 0)
            {
                lines.Add($"- {stage.StageId}: session RTT excluded low-sample sessions = {stage.SessionRttExcludedLowSampleSessionCount}");
            }

            if (stage.OperationDurations is { Count: > 0 })
            {
                foreach (KeyValuePair<string, ObservedOperationDurationSnapshot> pair in stage.OperationDurations
                    .OrderByDescending(pair => pair.Value.MaxMs)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Take(5))
                {
                    lines.Add($"- {stage.StageId}: operation {pair.Key} count={pair.Value.Count} avg={pair.Value.AverageMs:F2}ms max={pair.Value.MaxMs:F2}ms");
                }
            }

            if (stage.SlowestSessions is { Count: > 0 })
            {
                foreach (SlowSessionRttSnapshot session in stage.SlowestSessions.Take(5))
                {
                    lines.Add($"- {stage.StageId}: slow session {session.SessionId} p95={session.RttP95Ms:F2}ms p99={session.RttP99Ms:F2}ms max={session.RttMaxMs:F2}ms samples={session.SampleCount}/{session.TotalSampleCount}");
                }
            }
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatPacing(LoadValidationStageSummary stage)
    {
        if (stage.MaxPacingWaitCount <= 0
            && stage.MinObservedPacingWindow <= 0
            && stage.MaxObservedPacingWindow <= 0)
        {
            return "none";
        }

        return $"waits={stage.MaxPacingWaitCount}, avg={stage.MaxPacingAverageWaitMs:F2}ms, win={stage.MinObservedPacingWindow}-{stage.MaxObservedPacingWindow}, +/-={stage.MaxPacingWindowIncreaseCount}/{stage.MaxPacingWindowDecreaseCount}";
    }

    private static string FormatSessionRtt(LoadValidationStageSummary stage)
    {
        if (stage.SessionRttTrackedSessionCount <= 0)
        {
            return "none";
        }

        return $"eligible={stage.SessionRttEligibleSessionCount}/{stage.SessionRttTrackedSessionCount}, p50/p95/p99-of-p95={stage.MaxSessionRttP50OfP95Ms:F2}/{stage.MaxSessionRttP95OfP95Ms:F2}/{stage.MaxSessionRttP99OfP95Ms:F2}ms, max-p95={stage.MaxSessionRttMaxSessionP95Ms:F2}ms";
    }
}
