using System.Text.Json;
using FastPortLoadValidation;
using LibNetworks.Telemetry;

namespace LibCommonTest;

[TestClass]
public sealed class FastPortLoadValidationTests
{
    [TestMethod]
    public void LoadValidationOptions_TryParse_UsesDefaults()
    {
        bool result = LoadValidationOptions.TryParse([], out LoadValidationOptions options, out string errorMessage);

        Assert.IsTrue(result, errorMessage);
        Assert.AreEqual("smoke", options.Profile);
        Assert.AreEqual("127.0.0.1", options.Host);
        Assert.AreEqual(6628, options.Port);
        Assert.AreEqual("FastPortLoadRunner", options.RunnerProject);
        Assert.AreEqual("Release", options.Configuration);
        Assert.IsFalse(options.DryRun);
        Assert.IsFalse(options.ContinueOnFailure);
        StringAssert.Contains(options.OutputDirectory, Path.Combine("artifacts", "load-validation"));
    }

    [TestMethod]
    public void LoadValidationOptions_TryParse_AcceptsScenario()
    {
        string[] args =
        [
            "--profile", "staged",
            "--host", "localhost",
            "--port", "7000",
            "--output", "out",
            "--stage", "s5-random-10k",
            "--runner-project", "runner",
            "--configuration", "Debug",
            "--dry-run",
            "--continue-on-failure"
        ];

        bool result = LoadValidationOptions.TryParse(args, out LoadValidationOptions options, out string errorMessage);

        Assert.IsTrue(result, errorMessage);
        Assert.AreEqual("staged", options.Profile);
        Assert.AreEqual("localhost", options.Host);
        Assert.AreEqual(7000, options.Port);
        Assert.AreEqual("out", options.OutputDirectory);
        Assert.AreEqual("s5-random-10k", options.StageId);
        Assert.AreEqual("runner", options.RunnerProject);
        Assert.AreEqual("Debug", options.Configuration);
        Assert.IsTrue(options.DryRun);
        Assert.IsTrue(options.ContinueOnFailure);
    }

    [TestMethod]
    public void LoadValidationProfiles_StagedProfile_HasExpectedStages()
    {
        LoadValidationProfile profile = LoadValidationProfiles.Get("staged");

        Assert.AreEqual("staged", profile.Name);
        Assert.AreEqual(5, profile.Stages.Count);
        Assert.AreEqual("s1-fixed-1k", profile.Stages[0].Id);
        Assert.AreEqual(1_000, profile.Stages[0].Sessions);
        Assert.AreEqual("fixed:8192", profile.Stages[0].Payload);
        Assert.AreEqual("s5-random-10k", profile.Stages[^1].Id);
        Assert.AreEqual(10_000, profile.Stages[^1].Sessions);
        Assert.AreEqual("random:4096-16384", profile.Stages[^1].Payload);
    }

    [TestMethod]
    public void LoadRunnerCommandBuilder_BuildsStageCommand()
    {
        var options = new LoadValidationOptions(
            Profile: "staged",
            Host: "localhost",
            Port: 7000,
            OutputDirectory: "out",
            StageId: null,
            RunnerProject: "FastPortLoadRunner",
            Configuration: "Release",
            DryRun: false,
            ContinueOnFailure: false);
        LoadValidationStage stage = LoadValidationProfiles.Get("staged").Stages[^1];
        var builder = new LoadRunnerCommandBuilder();

        LoadRunnerCommand command = builder.Build(options, stage);

        Assert.AreEqual("dotnet", command.FileName);
        CollectionAssert.Contains(command.Arguments.ToArray(), "--sessions");
        CollectionAssert.Contains(command.Arguments.ToArray(), "10000");
        CollectionAssert.Contains(command.Arguments.ToArray(), "random:4096-16384");
        CollectionAssert.Contains(command.Arguments.ToArray(), "120s");
        CollectionAssert.Contains(command.Arguments.ToArray(), "5m");
        StringAssert.EndsWith(command.Arguments[^1], Path.Combine("out", "s5-random-10k.metrics.jsonl"));
    }

    [TestMethod]
    public async Task JsonlObservedMetricsReader_ReadsClientObservedSamples()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "metrics.jsonl");
        ClientObservedMetricsSnapshot first = CreateSample(currentSessions: 9, targetSessions: 10);
        ClientObservedMetricsSnapshot second = CreateSample(currentSessions: 10, targetSessions: 10, totalReceivedPackets: 20);
        string[] lines =
        [
            ObservedMetricsJson.Serialize(ObservedMetricsSnapshot.FromClient(first)),
            string.Empty,
            ObservedMetricsJson.Serialize(ObservedMetricsSnapshot.FromClient(second))
        ];
        await File.WriteAllLinesAsync(path, lines);
        var reader = new JsonlObservedMetricsReader();

        JsonlReadResult result = await reader.ReadClientSamplesAsync(path);

        Assert.AreEqual(0, result.Errors.Count);
        Assert.AreEqual(2, result.Samples.Count);
        Assert.AreEqual(9, result.Samples[0].CurrentSessions);
        Assert.AreEqual(20, result.Samples[1].TotalReceivedPackets);
    }

    [TestMethod]
    public void LoadValidationEvaluator_PassesHealthySamples()
    {
        LoadValidationStage stage = CreateStage(targetSessions: 10);
        JsonlReadResult readResult = new(
            [
                CreateSample(currentSessions: 9, targetSessions: 10, totalReceivedPackets: 10, tps: 10),
                CreateSample(currentSessions: 10, targetSessions: 10, totalReceivedPackets: 20, tps: 12, maxPendingRequestCount: 7, schedulerDriftMaxMs: 2.5),
                CreateSample(currentSessions: 10, targetSessions: 10, totalReceivedPackets: 30, tps: 15, maxPendingRequestCount: 3, schedulerDriftMaxMs: 1.5)
            ],
            []);
        var evaluator = new LoadValidationEvaluator();

        LoadValidationStageSummary summary = evaluator.Evaluate(stage, "metrics.jsonl", readResult);

        Assert.IsTrue(summary.Passed, string.Join(Environment.NewLine, summary.Failures));
        Assert.AreEqual(10, summary.PeakCurrentSessions);
        Assert.AreEqual(1.0, summary.PeakSessionRatio);
        Assert.AreEqual(30, summary.TotalReceivedPackets);
        Assert.AreEqual(15, summary.MaxTps);
        Assert.AreEqual(7, summary.MaxPendingRequestCount);
        Assert.AreEqual(2.5, summary.MaxSchedulerDriftMs);
        Assert.AreEqual(1.0, summary.MaxActiveSessionRatio);
        Assert.AreEqual(10, summary.FinalConnectAttemptCount);
        Assert.AreEqual(0, summary.FinalConnectFailureCount);
    }

    [TestMethod]
    public void LoadValidationEvaluator_FailsLowPeakSessions()
    {
        LoadValidationStage stage = CreateStage(targetSessions: 10);
        JsonlReadResult readResult = new(
            [
                CreateSample(currentSessions: 5, targetSessions: 10, totalReceivedPackets: 10, tps: 1),
                CreateSample(currentSessions: 6, targetSessions: 10, totalReceivedPackets: 20, tps: 1),
                CreateSample(currentSessions: 6, targetSessions: 10, totalReceivedPackets: 30, tps: 1)
            ],
            []);
        var evaluator = new LoadValidationEvaluator();

        LoadValidationStageSummary summary = evaluator.Evaluate(stage, "metrics.jsonl", readResult);

        Assert.IsFalse(summary.Passed);
        Assert.IsTrue(summary.Failures.Any(failure => failure.Contains("Peak session ratio", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void LoadValidationEvaluator_FailsSocketErrors()
    {
        LoadValidationStage stage = CreateStage(targetSessions: 10);
        JsonlReadResult readResult = new(
            [
                CreateSample(currentSessions: 10, targetSessions: 10, totalReceivedPackets: 10, tps: 1),
                CreateSample(currentSessions: 10, targetSessions: 10, totalReceivedPackets: 20, tps: 1, socketErrorRate: 0.20),
                CreateSample(currentSessions: 10, targetSessions: 10, totalReceivedPackets: 30, tps: 1)
            ],
            []);
        var evaluator = new LoadValidationEvaluator();

        LoadValidationStageSummary summary = evaluator.Evaluate(stage, "metrics.jsonl", readResult);

        Assert.IsFalse(summary.Passed);
        Assert.IsTrue(summary.Failures.Any(failure => failure.Contains("Socket error rate", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task LoadValidationSummaryWriter_WritesSummaryFiles()
    {
        string directory = CreateTempDirectory();
        var summary = new LoadValidationRunSummary(
            "test-run",
            Passed: true,
            StartedAt: new DateTimeOffset(2026, 4, 28, 9, 0, 0, TimeSpan.Zero),
            CompletedAt: new DateTimeOffset(2026, 4, 28, 9, 1, 0, TimeSpan.Zero),
            [
                new LoadValidationStageSummary(
                    "smoke-fixed-10",
                    Passed: true,
                    TargetSessions: 10,
                    PeakCurrentSessions: 10,
                    PeakSessionRatio: 1.0,
                    TotalSentPackets: 10,
                    TotalReceivedPackets: 10,
                    MaxSocketErrorRate: 0,
                    FinalDisconnectCount: 0,
                    MaxTps: 10,
                    MaxSentBytesPerSecond: 1024,
                    MaxReceivedBytesPerSecond: 1024,
                    MaxRttP95Ms: 1,
                    MaxRttP99Ms: 2,
                    JsonSamples: 3,
                    MetricsPath: "metrics.jsonl",
                    Failures: [])
            ]);
        var writer = new LoadValidationSummaryWriter();

        await writer.WriteSummaryAsync(directory, summary);

        Assert.IsTrue(File.Exists(Path.Combine(directory, "summary.json")));
        Assert.IsTrue(File.Exists(Path.Combine(directory, "summary.md")));

        string json = await File.ReadAllTextAsync(Path.Combine(directory, "summary.json"));
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.IsTrue(document.RootElement.GetProperty("passed").GetBoolean());

        string markdown = await File.ReadAllTextAsync(Path.Combine(directory, "summary.md"));
        StringAssert.Contains(markdown, "Max Pending Req");
        StringAssert.Contains(markdown, "RTT P99");
    }

    private static LoadValidationStage CreateStage(int targetSessions)
    {
        return new LoadValidationStage(
            "test-stage",
            targetSessions,
            "fixed:1024",
            SendRatePerSession: 1,
            RampUp: TimeSpan.FromSeconds(1),
            Duration: TimeSpan.FromSeconds(1),
            MetricsInterval: TimeSpan.FromSeconds(1),
            Thresholds: LoadValidationThresholds.Default);
    }

    private static ClientObservedMetricsSnapshot CreateSample(
        int currentSessions,
        int targetSessions,
        long totalReceivedPackets = 1,
        double tps = 1,
        double socketErrorRate = 0,
        long maxPendingRequestCount = 0,
        double schedulerDriftMaxMs = 0,
        long connectFailureCount = 0)
    {
        return new ClientObservedMetricsSnapshot(
            Timestamp: new DateTimeOffset(2026, 4, 28, 9, 0, 0, TimeSpan.Zero),
            TargetSessions: targetSessions,
            CurrentSessions: currentSessions,
            TotalSentPackets: totalReceivedPackets,
            TotalReceivedPackets: totalReceivedPackets,
            TotalSentBytes: totalReceivedPackets * 1024,
            TotalReceivedBytes: totalReceivedPackets * 1024,
            SentPacketsPerSecond: tps,
            ReceivedPacketsPerSecond: tps,
            SentBytesPerSecond: tps * 1024,
            ReceivedBytesPerSecond: tps * 1024,
            Tps: tps,
            RttAverageMs: 1,
            RttP50Ms: 1,
            RttP95Ms: 2,
            RttP99Ms: 3,
            ConnectCount: currentSessions,
            DisconnectCount: 0,
            SocketErrorCount: socketErrorRate > 0 ? 1 : 0,
            SocketErrorRate: socketErrorRate,
            ConnectAttemptCount: currentSessions + connectFailureCount,
            ConnectFailureCount: connectFailureCount,
            PendingRequestCount: 0,
            MaxPendingRequestCount: maxPendingRequestCount,
            ActiveSessionRatio: targetSessions <= 0 ? 0 : currentSessions / (double)targetSessions,
            SchedulerDriftAverageMs: schedulerDriftMaxMs,
            SchedulerDriftMaxMs: schedulerDriftMaxMs);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fastport-load-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
