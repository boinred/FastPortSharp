using System.Text.Json;
using LibNetworks.Telemetry;

namespace FastPortLoadValidation;

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
            "| Stage | Result | Target | Peak | Peak Ratio | Max TPS | Max Pending Req | Max Drift | RTT P95 | RTT P99 | Socket Errors | Samples |",
            "|-------|--------|--------|------|------------|---------|-----------------|-----------|---------|---------|---------------|---------|"
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
                $"{stage.MaxSchedulerDriftMs:F2}ms",
                $"{stage.MaxRttP95Ms:F2}ms",
                $"{stage.MaxRttP99Ms:F2}ms",
                stage.MaxSocketErrorRate.ToString("P2"),
                stage.JsonSamples.ToString(),
                string.Empty));

            foreach (string failure in stage.Failures)
            {
                lines.Add($"- {stage.StageId}: {failure}");
            }
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }
}
