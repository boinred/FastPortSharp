using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Net.Sockets;
using FastPort.Protocols.Commons;
using FastPort.Protocols.Tests;
using FastPortTestLoadRunner;
using Google.Protobuf;
using LibTestTelemetry;

namespace FastPortTests;

[TestClass]
public sealed class FastPortTestLoadRunnerTests
{
    [TestMethod]
    public void LoadRunnerOptions_TryParse_UsesDefaults()
    {
        bool result = LoadRunnerOptions.TryParse([], out var options, out var errorMessage);

        Assert.IsTrue(result, errorMessage);
        Assert.AreEqual("127.0.0.1", options.Host);
        Assert.AreEqual(6628, options.Port);
        Assert.AreEqual(1, options.Sessions);
        Assert.AreEqual(PayloadMode.Fixed, options.Payload.Mode);
        Assert.AreEqual(8192, options.Payload.MinBytes);
        Assert.AreEqual(1, options.SendRatePerSession);
        Assert.AreEqual(TimeSpan.FromSeconds(10), options.RampUp);
        Assert.AreEqual(TimeSpan.FromMinutes(1), options.Duration);
        Assert.AreEqual(TimeSpan.FromSeconds(1), options.MetricsInterval);
        Assert.IsNull(options.OutputPath);
        Assert.AreEqual(TimeSpan.FromSeconds(30), options.HeartbeatInterval);
        Assert.IsNull(options.MaxPendingRequestsPerSession);
        Assert.AreEqual(LoadPacingPolicy.None, options.Pacing.Policy);
    }

    [TestMethod]
    public void LoadRunnerOptions_TryParse_AcceptsScenario()
    {
        string[] args =
        [
            "--host", "localhost",
            "--port", "7000",
            "--sessions", "10000",
            "--payload", "random:4096-16384",
            "--rate", "20",
            "--ramp-up", "60s",
            "--duration", "5m",
            "--metrics-interval", "2s",
            "--output", "metrics.jsonl",
            "--heartbeat-interval", "15s",
            "--max-pending-requests-per-session", "4"
        ];

        bool result = LoadRunnerOptions.TryParse(args, out var options, out var errorMessage);

        Assert.IsTrue(result, errorMessage);
        Assert.AreEqual("localhost", options.Host);
        Assert.AreEqual(7000, options.Port);
        Assert.AreEqual(10000, options.Sessions);
        Assert.AreEqual(PayloadMode.Random, options.Payload.Mode);
        Assert.AreEqual(4096, options.Payload.MinBytes);
        Assert.AreEqual(16384, options.Payload.MaxBytes);
        Assert.AreEqual(20, options.SendRatePerSession);
        Assert.AreEqual(TimeSpan.FromSeconds(60), options.RampUp);
        Assert.AreEqual(TimeSpan.FromMinutes(5), options.Duration);
        Assert.AreEqual(TimeSpan.FromSeconds(2), options.MetricsInterval);
        Assert.AreEqual("metrics.jsonl", options.OutputPath);
        Assert.AreEqual(TimeSpan.FromSeconds(15), options.HeartbeatInterval);
        Assert.AreEqual(4, options.MaxPendingRequestsPerSession);
        Assert.AreEqual(LoadPacingPolicy.FixedWindow, options.Pacing.Policy);
        Assert.AreEqual(4, options.Pacing.FixedWindow);
    }

    [TestMethod]
    public void LoadRunnerOptions_TryParse_AcceptsAdaptivePacing()
    {
        string[] args =
        [
            "--pacing-policy", "adaptive-window",
            "--pacing-min-window", "2",
            "--pacing-initial-window", "4",
            "--pacing-max-window", "8",
            "--pacing-rtt-target-ms", "1000",
            "--pacing-rtt-high-ms", "2000",
            "--pacing-increase-every", "3"
        ];

        bool result = LoadRunnerOptions.TryParse(args, out var options, out var errorMessage);

        Assert.IsTrue(result, errorMessage);
        Assert.AreEqual(LoadPacingPolicy.AdaptiveWindow, options.Pacing.Policy);
        Assert.AreEqual(2, options.Pacing.MinWindow);
        Assert.AreEqual(4, options.Pacing.InitialWindow);
        Assert.AreEqual(8, options.Pacing.MaxWindow);
        Assert.AreEqual(1000, options.Pacing.RttTargetMs);
        Assert.AreEqual(2000, options.Pacing.RttHighMs);
        Assert.AreEqual(3, options.Pacing.IncreaseEveryResponses);
        Assert.IsNull(options.MaxPendingRequestsPerSession);
    }

    [TestMethod]
    public void LoadRunnerOptions_TryParse_UsesTunedAdaptiveDefaults()
    {
        bool result = LoadRunnerOptions.TryParse(
            ["--pacing-policy", "adaptive-window"],
            out var options,
            out var errorMessage);

        Assert.IsTrue(result, errorMessage);
        Assert.AreEqual(LoadPacingPolicy.AdaptiveWindow, options.Pacing.Policy);
        Assert.AreEqual(1, options.Pacing.MinWindow);
        Assert.AreEqual(4, options.Pacing.InitialWindow);
        Assert.AreEqual(8, options.Pacing.MaxWindow);
        Assert.AreEqual(14_000, options.Pacing.RttTargetMs);
        Assert.AreEqual(24_000, options.Pacing.RttHighMs);
        Assert.AreEqual(128, options.Pacing.IncreaseEveryResponses);
    }

    [TestMethod]
    public void LoadRunnerOptions_TryParse_RejectsInvalidValues()
    {
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--port", "70000"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--sessions", "0"], out _, out _));
        Assert.IsTrue(LoadRunnerOptions.TryParse(["--rate", "0"], out var heartbeatOnlyOptions, out _));
        Assert.AreEqual(0, heartbeatOnlyOptions.SendRatePerSession);
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--rate", "-1"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--duration", "5x"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--heartbeat-interval", "0s"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--payload", "random:16384-4096"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--max-pending-requests-per-session", "0"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--pacing-policy", "adaptive-window", "--max-pending-requests-per-session", "4"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--pacing-policy", "adaptive-window", "--pacing-min-window", "8", "--pacing-initial-window", "4"], out _, out _));
    }

    [TestMethod]
    public void LoadRunnerOptions_TryParse_DisablesHeartbeat()
    {
        bool result = LoadRunnerOptions.TryParse(
            ["--heartbeat-interval", "none"],
            out var options,
            out var errorMessage);

        Assert.IsTrue(result, errorMessage);
        Assert.AreEqual(TimeSpan.Zero, options.HeartbeatInterval);
    }

    [TestMethod]
    public void PayloadProfile_TryParse_FixedPayload()
    {
        bool result = PayloadProfile.TryParse("fixed:8192", out var profile);

        Assert.IsTrue(result);
        Assert.AreEqual(PayloadMode.Fixed, profile.Mode);
        Assert.AreEqual(8192, profile.MinBytes);
        Assert.AreEqual(8192, profile.MaxBytes);
        Assert.AreEqual(8192, profile.GetNextSize(new Random(1)));
    }

    [TestMethod]
    public void PayloadProfile_TryParse_RandomPayload()
    {
        bool result = PayloadProfile.TryParse("random:4096-16384", out var profile);
        var random = new Random(1);

        Assert.IsTrue(result);
        Assert.AreEqual(PayloadMode.Random, profile.Mode);
        Assert.AreEqual(4096, profile.MinBytes);
        Assert.AreEqual(16384, profile.MaxBytes);

        for (int i = 0; i < 100; i++)
        {
            int size = profile.GetNextSize(random);
            Assert.IsTrue(size >= 4096);
            Assert.IsTrue(size <= 16384);
        }
    }

    [TestMethod]
    public void PayloadGenerator_CreatePayload_UsesProfileSize()
    {
        var generator = new PayloadGenerator(PayloadProfile.Fixed(128), seed: 1);

        byte[] payload = generator.CreatePayload();

        Assert.AreEqual(128, payload.Length);
    }

    [TestMethod]
    public async Task LoadSession_WaitForPendingRequestBudget_BlocksUntilOutstandingDrops()
    {
        LoadSession session = CreateLoadSession(maxPendingRequestsPerSession: 1);
        await session.WaitForPendingRequestBudgetAsync(
            TimeSpan.FromMilliseconds(1),
            CancellationToken.None);

        Task waitTask = session.WaitForPendingRequestBudgetAsync(
            TimeSpan.FromMilliseconds(1),
            CancellationToken.None);

        await Task.Delay(50);
        Assert.IsFalse(waitTask.IsCompleted);

        Assert.IsTrue(session.ParseEchoResponse(CreateEchoResponseBody()));
        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsTrue(session.ParseEchoResponse(CreateEchoResponseBody()));
        Assert.AreEqual(0, session.OutstandingRequests);
    }

    [TestMethod]
    public async Task LoadSession_WaitForPendingRequestBudget_CancellationExitsGate()
    {
        LoadSession session = CreateLoadSession(maxPendingRequestsPerSession: 1);
        await session.WaitForPendingRequestBudgetAsync(
            TimeSpan.FromMilliseconds(1),
            CancellationToken.None);
        using var cancellationSource = new CancellationTokenSource();

        Task waitTask = session.WaitForPendingRequestBudgetAsync(
            TimeSpan.FromMilliseconds(1),
            cancellationSource.Token);

        cancellationSource.Cancel();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await waitTask);
    }

    [TestMethod]
    public async Task LoadSession_RunDuplexAsync_CancelsSendWhenReceiveCompletes()
    {
        var sendCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await LoadSession.RunDuplexAsync(
                async cancellationToken =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        sendCancelled.TrySetResult();
                        throw;
                    }
                },
                _ => Task.CompletedTask,
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        await sendCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task LoadSession_RunDuplexAsync_CancelsSendWhenReceiveFails()
    {
        var sendCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
        {
            await LoadSession.RunDuplexAsync(
                async cancellationToken =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        sendCancelled.TrySetResult();
                        throw;
                    }
                },
                _ => Task.FromException(new InvalidOperationException("receive failed")),
                CancellationToken.None);
        });

        await sendCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task LoadSession_ReadExactAsync_RecordsHeaderEofClose()
    {
        var collector = new MetricsCollector(targetSessions: 1);
        LoadSession session = CreateLoadSession(maxPendingRequestsPerSession: null, collector);
        using TcpClient client = new();
        using TcpClient server = await ConnectTcpPairAsync(client);
        using NetworkStream stream = client.GetStream();

        server.Close();

        bool read = await session.ReadExactAsync(
                stream,
                new byte[2],
                "receive-header",
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        MetricsSnapshot snapshot = collector.CreateSnapshot();
        Assert.IsFalse(read);
        Assert.AreEqual(1, snapshot.ReceiveCloseCountsByOperation!["receive-header"]);
        Assert.AreEqual(1, snapshot.ReceiveCloseCountsByReason!["eof"]);
        Assert.AreEqual(1, snapshot.ReceiveCloseCountsByClass!["receive-header|eof"]);
        Assert.AreEqual(0, snapshot.MaxOutstandingRequestsAtReceiveClose);
    }

    [TestMethod]
    public async Task LoadSession_ReadExactAsync_RecordsBodyEofClose()
    {
        var collector = new MetricsCollector(targetSessions: 1);
        LoadSession session = CreateLoadSession(maxPendingRequestsPerSession: null, collector);
        session.IncrementOutstandingRequests();
        using TcpClient client = new();
        using TcpClient server = await ConnectTcpPairAsync(client);
        using NetworkStream stream = client.GetStream();

        server.Close();

        bool read = await session.ReadExactAsync(
                stream,
                new byte[2],
                "receive-body",
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        MetricsSnapshot snapshot = collector.CreateSnapshot();
        Assert.IsFalse(read);
        Assert.AreEqual(1, snapshot.ReceiveCloseCountsByOperation!["receive-body"]);
        Assert.AreEqual(1, snapshot.ReceiveCloseCountsByReason!["eof"]);
        Assert.AreEqual(1, snapshot.ReceiveCloseCountsByClass!["receive-body|eof"]);
        Assert.AreEqual(1, snapshot.MaxOutstandingRequestsAtReceiveClose);
    }

    [TestMethod]
    public async Task LoadSession_ReadExactAsync_RecordsPartialBodyEofClose()
    {
        var collector = new MetricsCollector(targetSessions: 1);
        LoadSession session = CreateLoadSession(maxPendingRequestsPerSession: null, collector);
        session.IncrementOutstandingRequests();
        using TcpClient client = new();
        using TcpClient server = await ConnectTcpPairAsync(client);
        using NetworkStream clientStream = client.GetStream();
        using NetworkStream serverStream = server.GetStream();

        await serverStream.WriteAsync(new byte[] { 1 });
        await serverStream.FlushAsync();
        server.Close();

        bool read = await session.ReadExactAsync(
                clientStream,
                new byte[2],
                "receive-body",
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));

        MetricsSnapshot snapshot = collector.CreateSnapshot();
        Assert.IsFalse(read);
        Assert.AreEqual(1, snapshot.ReceiveCloseCountsByOperation!["receive-body"]);
        Assert.AreEqual(1, snapshot.ReceiveCloseCountsByReason!["partial-eof"]);
        Assert.AreEqual(1, snapshot.ReceiveCloseCountsByClass!["receive-body|partial-eof"]);
        Assert.AreEqual(1, snapshot.MaxOutstandingRequestsAtReceiveClose);
    }

    [TestMethod]
    public async Task OutstandingRequestPacer_FixedWindow_WaitsForResponseSignal()
    {
        var collector = new MetricsCollector(targetSessions: 1);
        var pacer = new OutstandingRequestPacer(LoadPacingOptions.Fixed(1), collector);

        await pacer.WaitForPermitAsync(CancellationToken.None);
        Task waitTask = pacer.WaitForPermitAsync(CancellationToken.None).AsTask();

        await Task.Delay(50);
        Assert.IsFalse(waitTask.IsCompleted);

        pacer.OnResponse(rttMs: 1);
        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));

        MetricsSnapshot snapshot = collector.CreateSnapshot();
        Assert.AreEqual(1, snapshot.TotalPacingWaitCount);
        Assert.AreEqual(1, snapshot.MinObservedPacingWindow);
        Assert.AreEqual(1, snapshot.MaxObservedPacingWindow);
    }

    [TestMethod]
    public async Task OutstandingRequestPacer_OnRequestAbandoned_ReleasesReservedPermit()
    {
        var collector = new MetricsCollector(targetSessions: 1);
        var pacer = new OutstandingRequestPacer(LoadPacingOptions.Fixed(1), collector);

        await pacer.WaitForPermitAsync(CancellationToken.None);
        Task waitTask = pacer.WaitForPermitAsync(CancellationToken.None).AsTask();

        await Task.Delay(50);
        Assert.IsFalse(waitTask.IsCompleted);

        pacer.OnRequestAbandoned();
        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, pacer.InFlight);
        MetricsSnapshot snapshot = collector.CreateSnapshot();
        Assert.AreEqual(1, snapshot.TotalPacingWaitCount);
    }

    [TestMethod]
    public void OutstandingRequestPacer_AdaptiveWindow_IncreasesAndDecreases()
    {
        var options = new LoadPacingOptions(
            LoadPacingPolicy.AdaptiveWindow,
            FixedWindow: null,
            MinWindow: 1,
            InitialWindow: 2,
            MaxWindow: 4,
            RttTargetMs: 10,
            RttHighMs: 100,
            IncreaseEveryResponses: 2);
        var collector = new MetricsCollector(targetSessions: 1);
        var pacer = new OutstandingRequestPacer(options, collector);

        pacer.ReserveForTest();
        pacer.OnResponse(rttMs: 1);
        pacer.ReserveForTest();
        pacer.OnResponse(rttMs: 1);

        Assert.AreEqual(3, pacer.CurrentWindow);

        pacer.ReserveForTest();
        pacer.OnResponse(rttMs: 200);

        Assert.AreEqual(1, pacer.CurrentWindow);
        MetricsSnapshot snapshot = collector.CreateSnapshot();
        Assert.AreEqual(1, snapshot.PacingWindowIncreaseCount);
        Assert.AreEqual(1, snapshot.PacingWindowDecreaseCount);
        Assert.AreEqual(1, snapshot.MinObservedPacingWindow);
        Assert.AreEqual(3, snapshot.MaxObservedPacingWindow);
    }

    [TestMethod]
    public void LoadSession_ParseEchoResponse_DecrementsOutstandingRequestsOnValidResponse()
    {
        LoadSession session = CreateLoadSession(maxPendingRequestsPerSession: 1);
        session.IncrementOutstandingRequests();
        byte[] body = CreateEchoResponseBody();

        bool parsed = session.ParseEchoResponse(body);

        Assert.IsTrue(parsed);
        Assert.AreEqual(0, session.OutstandingRequests);
    }

    [TestMethod]
    public void LoadSession_ParseEchoResponse_RecordsSessionRtt()
    {
        var collector = new MetricsCollector(targetSessions: 1);
        LoadSession session = CreateLoadSession(maxPendingRequestsPerSession: 1, collector);
        byte[] body = CreateEchoResponseBody();

        bool parsed = session.ParseEchoResponse(body);

        Assert.IsTrue(parsed);
        MetricsSnapshot snapshot = collector.CreateSnapshot();
        Assert.IsNotNull(snapshot.SessionRtt);
        Assert.AreEqual(1, snapshot.SessionRtt.TrackedSessionCount);
        Assert.AreEqual(0, snapshot.SessionRtt.EligibleSessionCount);
        Assert.AreEqual(1, snapshot.SessionRtt.ExcludedLowSampleSessionCount);
    }

    [TestMethod]
    public async Task LoadSession_ParseEchoResponse_DoesNotReleasePacingForHeartbeat()
    {
        LoadSession session = CreateLoadSession(maxPendingRequestsPerSession: 1);
        await session.WaitForPendingRequestBudgetAsync(
            TimeSpan.FromMilliseconds(1),
            CancellationToken.None);
        Task waitTask = session.WaitForPendingRequestBudgetAsync(
            TimeSpan.FromMilliseconds(1),
            CancellationToken.None);

        bool parsed = session.ParseEchoResponse(CreateHeartbeatResponseBody());
        await Task.Delay(50);

        Assert.IsTrue(parsed);
        Assert.IsFalse(waitTask.IsCompleted);

        Assert.IsTrue(session.ParseEchoResponse(CreateEchoResponseBody()));
        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public void MetricsCollector_CreateSnapshot_TracksTotalsAndRates()
    {
        var collector = new MetricsCollector(targetSessions: 10);
        collector.RecordConnectAttempt();
        collector.RecordSessionConnected();
        collector.RecordSentPacket(100);
        collector.RecordReceivedPacket(80);
        collector.RecordSchedulerDrift(5.25);
        collector.RecordSocketError();

        var previous = new MetricsSnapshot(
            DateTimeOffset.Now.AddSeconds(-1),
            TargetSessions: 10,
            ConnectedSessions: 0,
            TotalSentPackets: 0,
            TotalReceivedPackets: 0,
            TotalSentBytes: 0,
            TotalReceivedBytes: 0,
            SentPacketsPerSecond: 0,
            ReceivedPacketsPerSecond: 0,
            SentBytesPerSecond: 0,
            ReceivedBytesPerSecond: 0,
            Tps: 0,
            RttAverageMs: 0,
            RttP50Ms: 0,
            RttP95Ms: 0,
            RttP99Ms: 0,
            AcceptCount: 0,
            DisconnectCount: 0,
            SocketErrorCount: 0,
            SocketErrorRate: 0);

        MetricsSnapshot snapshot = collector.CreateSnapshot(previous);

        Assert.AreEqual(10, snapshot.TargetSessions);
        Assert.AreEqual(1, snapshot.ConnectedSessions);
        Assert.AreEqual(1, snapshot.TotalSentPackets);
        Assert.AreEqual(1, snapshot.TotalReceivedPackets);
        Assert.AreEqual(100, snapshot.TotalSentBytes);
        Assert.AreEqual(80, snapshot.TotalReceivedBytes);
        Assert.AreEqual(1, snapshot.ConnectAttemptCount);
        Assert.AreEqual(0, snapshot.ConnectFailureCount);
        Assert.AreEqual(0, snapshot.PendingRequestCount);
        Assert.AreEqual(1, snapshot.MaxPendingRequestCount);
        Assert.AreEqual(0.1, snapshot.ActiveSessionRatio, 0.0001);
        Assert.AreEqual(5.25, snapshot.SchedulerDriftAverageMs, 0.001);
        Assert.AreEqual(5.25, snapshot.SchedulerDriftMaxMs, 0.001);
        Assert.AreEqual(1, snapshot.SocketErrorCount);
        Assert.IsTrue(snapshot.SentPacketsPerSecond > 0);
        Assert.IsTrue(snapshot.ReceivedPacketsPerSecond > 0);
        Assert.IsTrue(snapshot.SentBytesPerSecond > 0);
        Assert.IsTrue(snapshot.ReceivedBytesPerSecond > 0);
        Assert.IsTrue(snapshot.SocketErrorRate > 0);
    }

    [TestMethod]
    public void MetricsCollector_RecordOperationDuration_TracksDurationSummary()
    {
        var collector = new MetricsCollector(targetSessions: 1);

        collector.RecordOperationDuration("receive-body", TimeSpan.FromMilliseconds(5));
        collector.RecordOperationDuration("receive-body", TimeSpan.FromMilliseconds(15));
        collector.RecordOperationDuration("send-write", TimeSpan.FromMilliseconds(3));

        MetricsSnapshot snapshot = collector.CreateSnapshot();

        Assert.IsNotNull(snapshot.OperationDurations);
        Assert.AreEqual(2, snapshot.OperationDurations["receive-body"].Count);
        Assert.AreEqual(10, snapshot.OperationDurations["receive-body"].AverageMs, 0.001);
        Assert.AreEqual(15, snapshot.OperationDurations["receive-body"].MaxMs, 0.001);
        Assert.AreEqual(1, snapshot.OperationDurations["send-write"].Count);
        Assert.AreEqual(3, snapshot.OperationDurations["send-write"].MaxMs, 0.001);
    }

    [TestMethod]
    public void MetricsCollector_RecordReceiveCloseAndPhaseCompletion_TracksClassifications()
    {
        var collector = new MetricsCollector(targetSessions: 1);

        collector.RecordReceiveClose("receive-body", "partial-eof", outstandingRequests: 7);
        collector.RecordReceiveClose("receive-header", "eof", outstandingRequests: 2);
        collector.RecordPhaseCompletion("receive", "completed");
        collector.RecordPhaseCompletion("send", "cancelled");

        MetricsSnapshot snapshot = collector.CreateSnapshot();

        Assert.AreEqual(1, snapshot.ReceiveCloseCountsByOperation!["receive-body"]);
        Assert.AreEqual(1, snapshot.ReceiveCloseCountsByOperation["receive-header"]);
        Assert.AreEqual(1, snapshot.ReceiveCloseCountsByReason!["partial-eof"]);
        Assert.AreEqual(1, snapshot.ReceiveCloseCountsByReason["eof"]);
        Assert.AreEqual(1, snapshot.ReceiveCloseCountsByClass!["receive-body|partial-eof"]);
        Assert.AreEqual(7, snapshot.MaxOutstandingRequestsAtReceiveClose);
        Assert.AreEqual(1, snapshot.PhaseCompletionCounts!["receive|completed"]);
        Assert.AreEqual(1, snapshot.PhaseCompletionCounts["send|cancelled"]);
    }

    [TestMethod]
    public void MetricsCollector_RecordRtt_TracksSessionRttSummary()
    {
        var collector = new MetricsCollector(targetSessions: 2);
        long start = Stopwatch.GetTimestamp();

        for (int i = 1; i <= 8; i++)
        {
            collector.RecordRtt(sessionId: 1, start, TimestampAfter(start, i));
            collector.RecordRtt(sessionId: 2, start, TimestampAfter(start, i * 10));
        }

        MetricsSnapshot snapshot = collector.CreateSnapshot();

        Assert.IsNotNull(snapshot.SessionRtt);
        Assert.AreEqual(2, snapshot.SessionRtt.TrackedSessionCount);
        Assert.AreEqual(2, snapshot.SessionRtt.EligibleSessionCount);
        Assert.AreEqual(0, snapshot.SessionRtt.ExcludedLowSampleSessionCount);
        Assert.AreEqual(8, snapshot.SessionRtt.MinSamplesPerSession);
        Assert.AreEqual(2, snapshot.SessionRtt.SlowestSessions[0].SessionId);
        Assert.AreEqual(76.5, snapshot.SessionRtt.SlowestSessions[0].RttP95Ms, 0.001);
        Assert.AreEqual(42.075, snapshot.SessionRtt.P50OfSessionP95Ms, 0.001);
        Assert.AreEqual(73.0575, snapshot.SessionRtt.P95OfSessionP95Ms, 0.001);
        Assert.AreEqual(75.8115, snapshot.SessionRtt.P99OfSessionP95Ms, 0.001);
        Assert.AreEqual(76.5, snapshot.SessionRtt.MaxSessionP95Ms, 0.001);
        Assert.AreEqual(80, snapshot.SessionRtt.MaxSessionMaxMs, 0.001);
    }

    [TestMethod]
    public void MetricsCollector_RecordRtt_ExcludesLowSampleSessions()
    {
        var collector = new MetricsCollector(targetSessions: 2);
        long start = Stopwatch.GetTimestamp();

        for (int i = 1; i <= 7; i++)
        {
            collector.RecordRtt(sessionId: 1, start, TimestampAfter(start, i));
        }

        for (int i = 1; i <= 8; i++)
        {
            collector.RecordRtt(sessionId: 2, start, TimestampAfter(start, i * 10));
        }

        MetricsSnapshot snapshot = collector.CreateSnapshot();

        Assert.IsNotNull(snapshot.SessionRtt);
        Assert.AreEqual(2, snapshot.SessionRtt.TrackedSessionCount);
        Assert.AreEqual(1, snapshot.SessionRtt.EligibleSessionCount);
        Assert.AreEqual(1, snapshot.SessionRtt.ExcludedLowSampleSessionCount);
        Assert.AreEqual(1, snapshot.SessionRtt.SlowestSessions.Count);
        Assert.AreEqual(2, snapshot.SessionRtt.SlowestSessions[0].SessionId);
    }

    [TestMethod]
    public void MetricsCollector_RecordRtt_CapsPerSessionSamples()
    {
        var collector = new MetricsCollector(targetSessions: 1);
        long start = Stopwatch.GetTimestamp();

        for (int i = 1; i <= 300; i++)
        {
            collector.RecordRtt(sessionId: 1, start, TimestampAfter(start, i));
        }

        MetricsSnapshot snapshot = collector.CreateSnapshot();

        Assert.IsNotNull(snapshot.SessionRtt);
        Assert.AreEqual(1, snapshot.SessionRtt.SlowestSessions.Count);
        Assert.AreEqual(256, snapshot.SessionRtt.SlowestSessions[0].SampleCount);
        Assert.AreEqual(300, snapshot.SessionRtt.SlowestSessions[0].TotalSampleCount);
    }

    [TestMethod]
    public void MetricsCollector_RecordRtt_OrdersSlowestSessionsByTieBreakers()
    {
        var collector = new MetricsCollector(targetSessions: 4);
        long start = Stopwatch.GetTimestamp();

        RecordRttSamples(collector, sessionId: 1, start, CreateTieBreakSamples(p99: 110, max: 120));
        RecordRttSamples(collector, sessionId: 2, start, CreateTieBreakSamples(p99: 120, max: 130));
        RecordRttSamples(collector, sessionId: 4, start, CreateTieBreakSamples(p99: 120, max: 140));
        RecordRttSamples(collector, sessionId: 3, start, CreateTieBreakSamples(p99: 120, max: 140));

        MetricsSnapshot snapshot = collector.CreateSnapshot();

        Assert.IsNotNull(snapshot.SessionRtt);
        CollectionAssert.AreEqual(
            new[] { 3, 4, 2, 1 },
            snapshot.SessionRtt.SlowestSessions.Select(session => session.SessionId).Take(4).ToArray());
    }

    [TestMethod]
    public void MetricsCollector_RecordRtt_IsSafeAcrossConcurrentSessions()
    {
        var collector = new MetricsCollector(targetSessions: 32);
        long start = Stopwatch.GetTimestamp();

        Parallel.For(1, 33, sessionId =>
        {
            for (int i = 1; i <= 10; i++)
            {
                collector.RecordRtt(sessionId, start, TimestampAfter(start, sessionId + i));
            }
        });

        MetricsSnapshot snapshot = collector.CreateSnapshot();

        Assert.IsNotNull(snapshot.SessionRtt);
        Assert.AreEqual(32, snapshot.SessionRtt.TrackedSessionCount);
        Assert.AreEqual(32, snapshot.SessionRtt.EligibleSessionCount);
        Assert.AreEqual(0, snapshot.SessionRtt.ExcludedLowSampleSessionCount);
        Assert.AreEqual(20, snapshot.SessionRtt.SlowestSessions.Count);
    }

    private static LoadSession CreateLoadSession(int? maxPendingRequestsPerSession, MetricsCollector? metricsCollector = null)
    {
        var scenario = new LoadScenario(
            Host: "127.0.0.1",
            Port: 1,
            Sessions: 1,
            Payload: PayloadProfile.Fixed(1),
            SendRatePerSession: 1,
            RampUp: TimeSpan.Zero,
            Duration: TimeSpan.FromSeconds(1),
            MetricsInterval: TimeSpan.FromSeconds(1),
            OutputPath: null,
            HeartbeatInterval: TimeSpan.FromSeconds(30),
            Pacing: maxPendingRequestsPerSession is int cap
                ? LoadPacingOptions.Fixed(cap)
                : LoadPacingOptions.None);

        return new LoadSession(
            sessionId: 1,
            scenario,
            new PayloadGenerator(PayloadProfile.Fixed(1), seed: 1),
            metricsCollector ?? new MetricsCollector(targetSessions: 1));
    }

    private static async Task<TcpClient> ConnectTcpPairAsync(TcpClient client)
    {
        var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        try
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
            await client.ConnectAsync(endpoint.Address, endpoint.Port);
            return await acceptTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static long TimestampAfter(long timestamp, double elapsedMs)
    {
        return timestamp + (long)Math.Round(elapsedMs * Stopwatch.Frequency / 1000.0, MidpointRounding.AwayFromZero);
    }

    private static void RecordRttSamples(MetricsCollector collector, int sessionId, long start, IEnumerable<double> elapsedMsValues)
    {
        foreach (double elapsedMs in elapsedMsValues)
        {
            collector.RecordRtt(sessionId, start, TimestampAfter(start, elapsedMs));
        }
    }

    private static double[] CreateTieBreakSamples(double p99, double max)
    {
        return Enumerable
            .Repeat(1.0, 95)
            .Concat([100.0, 105.0, 105.0, 105.0, p99, max])
            .ToArray();
    }

    private static byte[] CreateEchoResponseBody()
    {
        return CreateEchoResponseBody(requestId: 1, clientSendTimestamp: (ulong)Stopwatch.GetTimestamp());
    }

    private static byte[] CreateHeartbeatResponseBody()
    {
        return CreateEchoResponseBody(requestId: 0, clientSendTimestamp: 0);
    }

    private static byte[] CreateEchoResponseBody(ulong requestId, ulong clientSendTimestamp)
    {
        var response = new EchoResponse
        {
            Header = new Header
            {
                RequestId = requestId,
                ClientSendTs = clientSendTimestamp
            },
            Data = ByteString.CopyFrom([1])
        };
        byte[] message = response.ToByteArray();
        byte[] body = new byte[4 + message.Length];
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0, 4), (int)ProtocolId.Tests);
        message.CopyTo(body.AsSpan(4));
        return body;
    }

    [TestMethod]
    public void MetricsCollector_CreateSnapshot_TracksSocketErrorClassifications()
    {
        var collector = new MetricsCollector(targetSessions: 10);

        collector.RecordSocketError("receive", new SocketException((int)SocketError.ConnectionReset));
        collector.RecordProtocolError("unexpected-protocol-id");

        MetricsSnapshot snapshot = collector.CreateSnapshot();

        Assert.AreEqual(2, snapshot.SocketErrorCount);
        Assert.AreEqual(1, snapshot.SocketErrorCountsByPhase!["receive"]);
        Assert.AreEqual(1, snapshot.SocketErrorCountsByPhase!["protocol"]);
        Assert.AreEqual(1, snapshot.SocketErrorCountsByType!["SocketException"]);
        Assert.AreEqual(1, snapshot.SocketErrorCountsByType!["unexpected-protocol-id"]);
        Assert.AreEqual(1, snapshot.SocketErrorCountsByCode!["ConnectionReset"]);
        Assert.AreEqual(1, snapshot.SocketErrorCountsByClass!["receive|SocketException|ConnectionReset"]);
        Assert.AreEqual(1, snapshot.SocketErrorCountsByClass!["protocol|unexpected-protocol-id|none"]);
    }

    [TestMethod]
    public void JsonMetricsReporter_SerializeSnapshot_WritesObservedClientEnvelope()
    {
        var sessionRtt = new SessionRttSummarySnapshot(
            TrackedSessionCount: 2,
            EligibleSessionCount: 1,
            ExcludedLowSampleSessionCount: 1,
            MinSamplesPerSession: 8,
            P50OfSessionP95Ms: 4,
            P95OfSessionP95Ms: 5,
            P99OfSessionP95Ms: 6,
            MaxSessionP95Ms: 5,
            MaxSessionP99Ms: 6,
            MaxSessionMaxMs: 7,
            SlowestSessions:
            [
                new SlowSessionRttSnapshot(7, 8, 10, 2, 3, 5, 6, 7)
            ]);
        var snapshot = new MetricsSnapshot(
            Timestamp: new DateTimeOffset(2026, 4, 28, 9, 0, 0, TimeSpan.Zero),
            TargetSessions: 100,
            ConnectedSessions: 90,
            TotalSentPackets: 1000,
            TotalReceivedPackets: 990,
            TotalSentBytes: 8192,
            TotalReceivedBytes: 4096,
            SentPacketsPerSecond: 100,
            ReceivedPacketsPerSecond: 99,
            SentBytesPerSecond: 819.2,
            ReceivedBytesPerSecond: 409.6,
            Tps: 99,
            RttAverageMs: 2.5,
            RttP50Ms: 2,
            RttP95Ms: 4,
            RttP99Ms: 6,
            AcceptCount: 100,
            DisconnectCount: 10,
            SocketErrorCount: 1,
            SocketErrorRate: 0.001,
            ConnectAttemptCount: 105,
            ConnectFailureCount: 5,
            PendingRequestCount: 10,
            MaxPendingRequestCount: 20,
            ActiveSessionRatio: 0.9,
            SchedulerDriftAverageMs: 1.5,
            SchedulerDriftMaxMs: 3.5,
            SocketErrorCountsByPhase: new Dictionary<string, long> { ["receive"] = 2 },
            SocketErrorCountsByType: new Dictionary<string, long> { ["SocketException"] = 2 },
            SocketErrorCountsByCode: new Dictionary<string, long> { ["ConnectionReset"] = 2 },
            SocketErrorCountsByClass: new Dictionary<string, long> { ["receive|SocketException|ConnectionReset"] = 2 },
            TotalPacingWaitCount: 5,
            PacingAverageWaitMs: 1.5,
            PacingWindowIncreaseCount: 2,
            PacingWindowDecreaseCount: 1,
            MinObservedPacingWindow: 2,
            MaxObservedPacingWindow: 8,
            SessionRtt: sessionRtt,
            OperationDurations: new Dictionary<string, ObservedOperationDurationSnapshot>
            {
                ["receive-body"] = new(Count: 2, AverageMs: 4.5, MaxMs: 8.5)
            },
            ReceiveCloseCountsByClass: new Dictionary<string, long>
            {
                ["receive-body|partial-eof"] = 1
            },
            MaxOutstandingRequestsAtReceiveClose: 3,
            PhaseCompletionCounts: new Dictionary<string, long>
            {
                ["receive|completed"] = 1,
                ["send|cancelled"] = 1
            });

        string json = JsonMetricsReporter.SerializeSnapshot(snapshot);

        Assert.IsTrue(json.Contains("\"clientObserved\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"serverObserved\":null", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("\"connectedSessions\"", StringComparison.Ordinal));

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.IsTrue(root.TryGetProperty("clientObserved", out JsonElement clientObserved));
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("serverObserved").ValueKind);
        Assert.IsFalse(root.TryGetProperty("connectedSessions", out _));
        Assert.AreEqual(90, clientObserved.GetProperty("currentSessions").GetInt32());
        Assert.AreEqual(100, clientObserved.GetProperty("connectCount").GetInt64());
        Assert.AreEqual(105, clientObserved.GetProperty("connectAttemptCount").GetInt64());
        Assert.AreEqual(5, clientObserved.GetProperty("connectFailureCount").GetInt64());
        Assert.AreEqual(10, clientObserved.GetProperty("pendingRequestCount").GetInt64());
        Assert.AreEqual(20, clientObserved.GetProperty("maxPendingRequestCount").GetInt64());
        Assert.AreEqual(0.9, clientObserved.GetProperty("activeSessionRatio").GetDouble(), 0.0001);
        Assert.AreEqual(3.5, clientObserved.GetProperty("schedulerDriftMaxMs").GetDouble(), 0.0001);
        Assert.AreEqual(990, clientObserved.GetProperty("totalReceivedPackets").GetInt64());
        Assert.AreEqual(5, clientObserved.GetProperty("totalPacingWaitCount").GetInt64());
        Assert.AreEqual(1.5, clientObserved.GetProperty("pacingAverageWaitMs").GetDouble(), 0.0001);
        Assert.AreEqual(8, clientObserved.GetProperty("maxObservedPacingWindow").GetInt64());
        Assert.AreEqual(8.5, clientObserved.GetProperty("operationDurations").GetProperty("receive-body").GetProperty("maxMs").GetDouble(), 0.0001);
        Assert.AreEqual(1, clientObserved.GetProperty("receiveCloseCountsByClass").GetProperty("receive-body|partial-eof").GetInt64());
        Assert.AreEqual(3, clientObserved.GetProperty("maxOutstandingRequestsAtReceiveClose").GetInt64());
        Assert.AreEqual(1, clientObserved.GetProperty("phaseCompletionCounts").GetProperty("receive|completed").GetInt64());
        Assert.AreEqual(2, clientObserved.GetProperty("sessionRtt").GetProperty("trackedSessionCount").GetInt32());
        Assert.AreEqual(8, clientObserved.GetProperty("sessionRtt").GetProperty("minSamplesPerSession").GetInt32());
        Assert.IsFalse(clientObserved.GetProperty("sessionRtt").TryGetProperty("minSampleCountForTail", out _));
        Assert.AreEqual(7, clientObserved.GetProperty("sessionRtt").GetProperty("slowestSessions")[0].GetProperty("sessionId").GetInt32());
        Assert.AreEqual(2, clientObserved.GetProperty("socketErrorCountsByPhase").GetProperty("receive").GetInt64());
        Assert.AreEqual(2, clientObserved.GetProperty("socketErrorCountsByClass").GetProperty("receive|SocketException|ConnectionReset").GetInt64());
    }
}
