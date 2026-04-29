using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Net.Sockets;
using FastPort.Protocols.Commons;
using FastPort.Protocols.Tests;
using FastPortLoadRunner;
using Google.Protobuf;

namespace LibCommonTest;

[TestClass]
public sealed class FastPortLoadRunnerTests
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
        Assert.IsNull(options.MaxPendingRequestsPerSession);
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
        Assert.AreEqual(4, options.MaxPendingRequestsPerSession);
    }

    [TestMethod]
    public void LoadRunnerOptions_TryParse_RejectsInvalidValues()
    {
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--port", "70000"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--sessions", "0"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--rate", "0"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--duration", "5x"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--payload", "random:16384-4096"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--max-pending-requests-per-session", "0"], out _, out _));
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
        session.IncrementOutstandingRequests();

        Task waitTask = session.WaitForPendingRequestBudgetAsync(
            TimeSpan.FromMilliseconds(1),
            CancellationToken.None);

        await Task.Delay(50);
        Assert.IsFalse(waitTask.IsCompleted);

        session.DecrementOutstandingRequests();
        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(0, session.OutstandingRequests);
    }

    [TestMethod]
    public async Task LoadSession_WaitForPendingRequestBudget_CancellationExitsGate()
    {
        LoadSession session = CreateLoadSession(maxPendingRequestsPerSession: 1);
        session.IncrementOutstandingRequests();
        using var cancellationSource = new CancellationTokenSource();

        Task waitTask = session.WaitForPendingRequestBudgetAsync(
            TimeSpan.FromMilliseconds(1),
            cancellationSource.Token);

        cancellationSource.Cancel();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await waitTask);
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

    private static LoadSession CreateLoadSession(int? maxPendingRequestsPerSession)
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
            MaxPendingRequestsPerSession: maxPendingRequestsPerSession);

        return new LoadSession(
            sessionId: 1,
            scenario,
            new PayloadGenerator(PayloadProfile.Fixed(1), seed: 1),
            new MetricsCollector(targetSessions: 1));
    }

    private static byte[] CreateEchoResponseBody()
    {
        var response = new EchoResponse
        {
            Header = new Header
            {
                RequestId = 1,
                ClientSendTs = (ulong)Stopwatch.GetTimestamp()
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
            SocketErrorCountsByClass: new Dictionary<string, long> { ["receive|SocketException|ConnectionReset"] = 2 });

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
        Assert.AreEqual(2, clientObserved.GetProperty("socketErrorCountsByPhase").GetProperty("receive").GetInt64());
        Assert.AreEqual(2, clientObserved.GetProperty("socketErrorCountsByClass").GetProperty("receive|SocketException|ConnectionReset").GetInt64());
    }
}
