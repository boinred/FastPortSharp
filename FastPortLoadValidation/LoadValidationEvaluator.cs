using LibNetworks.Telemetry;

namespace FastPortLoadValidation;

internal sealed class LoadValidationEvaluator
{
    public LoadValidationStageSummary Evaluate(
        LoadValidationStage stage,
        string metricsPath,
        JsonlReadResult readResult,
        int exitCode = 0)
    {
        var failures = new List<string>(readResult.Errors);
        IReadOnlyList<ClientObservedMetricsSnapshot> samples = readResult.Samples;

        if (exitCode != 0)
        {
            failures.Add($"LoadRunner exited with code {exitCode}.");
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
        if (disconnectRatio > stage.Thresholds.MaxDisconnectRatio)
        {
            failures.Add($"Disconnect ratio {disconnectRatio:P2} exceeds {stage.Thresholds.MaxDisconnectRatio:P2}.");
        }

        long totalSentPackets = finalSample?.TotalSentPackets ?? 0;
        long totalReceivedPackets = finalSample?.TotalReceivedPackets ?? 0;
        long finalConnectAttemptCount = finalSample?.ConnectAttemptCount ?? 0;
        long finalConnectFailureCount = finalSample?.ConnectFailureCount ?? 0;
        double maxTps = samples.Count == 0 ? 0 : samples.Max(sample => sample.Tps);

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
            MaxActiveSessionRatio: samples.Count == 0 ? 0 : samples.Max(sample => sample.ActiveSessionRatio));
    }
}
