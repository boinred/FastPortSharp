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
        Assert.IsNull(options.ServerMetricsPath);
        Assert.AreEqual(TimeSpan.FromMilliseconds(1500), options.MergeTolerance);
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
            "--server-metrics", "server.metrics.jsonl",
            "--merge-tolerance-ms", "2500",
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
        Assert.AreEqual("server.metrics.jsonl", options.ServerMetricsPath);
        Assert.AreEqual(TimeSpan.FromMilliseconds(2500), options.MergeTolerance);
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
            ServerMetricsPath: null,
            MergeTolerance: TimeSpan.FromMilliseconds(1500),
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
    public async Task JsonlObservedMetricsReader_ReadsServerObservedSamples()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "server.metrics.jsonl");
        ServerObservedMetricsSnapshot serverSample = CreateServerSample(
            new DateTimeOffset(2026, 4, 28, 9, 0, 0, TimeSpan.Zero),
            pendingSendRequests: 12,
            sendBackpressureEvents: 3,
            sendRejectedRequests: 2,
            sendRejectedBytes: 2048);
        await File.WriteAllLinesAsync(path, [ObservedMetricsJson.Serialize(ObservedMetricsSnapshot.FromServer(serverSample))]);
        var reader = new JsonlObservedMetricsReader();

        JsonlObservedMetricsReadResult result = await reader.ReadServerSamplesAsync(path);

        Assert.AreEqual(0, result.Errors.Count);
        Assert.AreEqual(1, result.Samples.Count);
        Assert.AreEqual(0, result.ClientSamples.Count);
        Assert.AreEqual(1, result.ServerSamples.Count);
        Assert.AreEqual(12, result.ServerSamples[0].PendingSendRequests);
        Assert.AreEqual(3, result.ServerSamples[0].SendBackpressureEvents);
        Assert.AreEqual(2, result.ServerSamples[0].SendRejectedRequests);
        Assert.AreEqual(2048, result.ServerSamples[0].SendRejectedBytes);
    }

    [TestMethod]
    public void ObservedMetricsMerger_MatchesNearestServerSampleWithinTolerance()
    {
        DateTimeOffset timestamp = new(2026, 4, 28, 9, 0, 0, TimeSpan.Zero);
        ClientObservedMetricsSnapshot client = CreateSample(
            currentSessions: 10,
            targetSessions: 10,
            timestamp: timestamp);
        ServerObservedMetricsSnapshot server = CreateServerSample(timestamp.AddMilliseconds(250), pendingSendRequests: 9);
        var merger = new ObservedMetricsMerger();

        ObservedMetricsMergeResult result = merger.Merge([client], [server], TimeSpan.FromMilliseconds(500));

        Assert.AreEqual(1, result.CombinedSamples.Count);
        Assert.AreEqual(1, result.MatchedSamples);
        Assert.AreEqual(0, result.UnmatchedClientSamples);
        Assert.AreEqual(250, result.MaxSkewMs);
        Assert.IsNotNull(result.CombinedSamples[0].ServerObserved);
        Assert.AreEqual(9, result.CombinedSamples[0].ServerObserved!.PendingSendRequests);
    }

    [TestMethod]
    public void ObservedMetricsMerger_RecordsUnmatchedClientSampleOutsideTolerance()
    {
        DateTimeOffset timestamp = new(2026, 4, 28, 9, 0, 0, TimeSpan.Zero);
        ClientObservedMetricsSnapshot client = CreateSample(
            currentSessions: 10,
            targetSessions: 10,
            timestamp: timestamp);
        ServerObservedMetricsSnapshot server = CreateServerSample(timestamp.AddSeconds(5));
        var merger = new ObservedMetricsMerger();

        ObservedMetricsMergeResult result = merger.Merge([client], [server], TimeSpan.FromMilliseconds(500));

        Assert.AreEqual(0, result.MatchedSamples);
        Assert.AreEqual(1, result.UnmatchedClientSamples);
        Assert.IsNull(result.CombinedSamples[0].ServerObserved);
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
    public void LoadValidationEvaluator_IncludesMergedServerAndSocketClassifications()
    {
        LoadValidationStage stage = CreateStage(targetSessions: 10);
        DateTimeOffset timestamp = new(2026, 4, 28, 9, 0, 0, TimeSpan.Zero);
        ClientObservedMetricsSnapshot client = CreateSample(
            currentSessions: 10,
            targetSessions: 10,
            totalReceivedPackets: 20,
            tps: 10,
            timestamp: timestamp,
            socketErrorCountsByPhase: new Dictionary<string, long> { ["receive"] = 2 },
            socketErrorCountsByClass: new Dictionary<string, long> { ["receive|SocketException|ConnectionReset"] = 2 });
        ServerObservedMetricsSnapshot server = CreateServerSample(
            timestamp.AddMilliseconds(100),
            pendingSendRequests: 12,
            sendBackpressureEvents: 4,
            sendRejectedRequests: 5,
            sendRejectedBytes: 8192,
            sendRejectedRequestsPerSecond: 2.5,
            maxSendBufferBytes: 4096);
        JsonlReadResult readResult = new([client, client, client], []);
        JsonlObservedMetricsReadResult serverReadResult = new(
            [ObservedMetricsSnapshot.FromServer(server)],
            [],
            [server],
            []);
        ObservedMetricsMergeResult mergeResult = new(
            [new ObservedMetricsSnapshot(timestamp, client, server)],
            MatchedSamples: 1,
            UnmatchedClientSamples: 0,
            MaxSkewMs: 100);
        var evaluator = new LoadValidationEvaluator();

        LoadValidationStageSummary summary = evaluator.Evaluate(
            stage,
            "metrics.jsonl",
            readResult,
            serverMetricsPath: "server.metrics.jsonl",
            serverReadResult: serverReadResult,
            mergeResult: mergeResult,
            combinedMetricsPath: "combined.metrics.jsonl");

        Assert.IsTrue(summary.Passed, string.Join(Environment.NewLine, summary.Failures));
        Assert.AreEqual("server.metrics.jsonl", summary.ServerMetricsPath);
        Assert.AreEqual("combined.metrics.jsonl", summary.CombinedMetricsPath);
        Assert.AreEqual(1, summary.ServerJsonSamples);
        Assert.AreEqual(1, summary.MergedSamples);
        Assert.AreEqual(12, summary.MaxPendingSendRequests);
        Assert.AreEqual(4, summary.MaxSendBackpressureEvents);
        Assert.AreEqual(5, summary.MaxSendRejectedRequests);
        Assert.AreEqual(8192, summary.MaxSendRejectedBytes);
        Assert.AreEqual(2.5, summary.MaxSendRejectedRequestsPerSecond);
        Assert.AreEqual(4096, summary.MaxSendBufferBytes);
        Assert.AreEqual(2, summary.SocketErrorCountsByPhase!["receive"]);
        Assert.AreEqual(2, summary.SocketErrorCountsByClass!["receive|SocketException|ConnectionReset"]);
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
        StringAssert.Contains(markdown, "Max Pending Send");
        StringAssert.Contains(markdown, "Rejected Send");
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
        long connectFailureCount = 0,
        DateTimeOffset? timestamp = null,
        IReadOnlyDictionary<string, long>? socketErrorCountsByPhase = null,
        IReadOnlyDictionary<string, long>? socketErrorCountsByClass = null)
    {
        return new ClientObservedMetricsSnapshot(
            Timestamp: timestamp ?? new DateTimeOffset(2026, 4, 28, 9, 0, 0, TimeSpan.Zero),
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
            SchedulerDriftMaxMs: schedulerDriftMaxMs,
            SocketErrorCountsByPhase: socketErrorCountsByPhase,
            SocketErrorCountsByClass: socketErrorCountsByClass);
    }

    private static ServerObservedMetricsSnapshot CreateServerSample(
        DateTimeOffset timestamp,
        long pendingSendRequests = 0,
        long sendBackpressureEvents = 0,
        long sendRejectedRequests = 0,
        long sendRejectedBytes = 0,
        double sendRejectedRequestsPerSecond = 0,
        long maxSendBufferBytes = 0)
    {
        return new ServerObservedMetricsSnapshot(
            Timestamp: timestamp,
            CurrentSessions: 10,
            TotalAcceptedSessions: 10,
            TotalDisconnectedSessions: 0,
            TotalReceivedPackets: 20,
            TotalSendCompletions: 20,
            TotalParsedPacketBytes: 1024,
            TotalSentBytes: 1024,
            ReceivedPacketsPerSecond: 10,
            SendCompletionsPerSecond: 11,
            ParsedPacketBytesPerSecond: 1024,
            SentBytesPerSecond: 1024,
            AcceptedSessionsPerSecond: 0,
            DisconnectedSessionsPerSecond: 0,
            AcceptErrorCount: 0,
            SocketErrorCount: 0,
            ParseErrorCount: 0,
            ProtocolErrorCount: 0,
            SocketErrorRate: 0,
            TotalSendRequests: 25,
            PendingSendRequests: pendingSendRequests,
            MaxPendingSendRequests: pendingSendRequests,
            SendRequestsPerSecond: 12,
            SendBackpressureEvents: sendBackpressureEvents,
            SendBackpressureEventsPerSecond: 2,
            SendRejectedRequests: sendRejectedRequests,
            SendRejectedRequestsPerSecond: sendRejectedRequestsPerSecond,
            SendRejectedBytes: sendRejectedBytes,
            SendRejectedBytesPerSecond: 0,
            SendBufferBytes: maxSendBufferBytes,
            MaxSendBufferBytes: maxSendBufferBytes);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fastport-load-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
