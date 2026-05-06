using System.Text.Json;
using FastPortTestLoadRunner;
using LibTestTelemetry;

namespace FastPortTests;

[TestClass]
public sealed class ObservedMetricsTests
{
    [TestMethod]
    public void ServerObservedMetricsSnapshot_MapsCurrentTelemetrySemantics()
    {
        var raw = new ServerTelemetrySnapshot(
            Timestamp: new DateTimeOffset(2026, 4, 28, 9, 0, 0, TimeSpan.Zero),
            AcceptedSessions: 10,
            DisconnectedSessions: 3,
            ConnectedSessions: 7,
            ReceivedPackets: 100,
            SentPackets: 80,
            ReceivedBytes: 4096,
            SentBytes: 2048,
            AcceptErrors: 1,
            SocketErrors: 2,
            ParseErrors: 3,
            ProtocolErrors: 4,
            SocketErrorRate: 0.01,
            SendRequests: 90,
            PendingSendRequests: 10,
            MaxPendingSendRequests: 12,
            SendBackpressureEvents: 5,
            SendRejectedRequests: 6,
            SendRejectedBytes: 512,
            SendDrainYieldCount: 7,
            MaxSendDrainYieldQueuedBytes: 768,
            SendBufferBytes: 1024,
            MaxSendBufferBytes: 2048,
            SendAbandonedRequests: 9,
            SocketErrorCountsByPhase: new Dictionary<string, long> { ["send"] = 2 },
            SocketErrorCountsByType: new Dictionary<string, long> { ["SocketException"] = 2 },
            SocketErrorCountsByCode: new Dictionary<string, long> { ["ConnectionReset"] = 2 },
            SocketErrorCountsByClass: new Dictionary<string, long> { ["send|SocketException|ConnectionReset"] = 2 },
            DisconnectCountsByReason: new Dictionary<string, long> { ["idle-timeout"] = 1 },
            IdleTimeoutDisconnects: 1,
            MaxIdleTimeoutAgeMs: 1234);

        ServerObservedMetricsSnapshot observed = ServerObservedMetricsSnapshot.FromTelemetry(raw);

        Assert.AreEqual(raw.Timestamp, observed.Timestamp);
        Assert.AreEqual(7, observed.CurrentSessions);
        Assert.AreEqual(10, observed.TotalAcceptedSessions);
        Assert.AreEqual(3, observed.TotalDisconnectedSessions);
        Assert.AreEqual(100, observed.TotalReceivedPackets);
        Assert.AreEqual(80, observed.TotalSendCompletions);
        Assert.AreEqual(4096, observed.TotalParsedPacketBytes);
        Assert.AreEqual(2048, observed.TotalSentBytes);
        Assert.AreEqual(90, observed.TotalSendRequests);
        Assert.AreEqual(10, observed.PendingSendRequests);
        Assert.AreEqual(12, observed.MaxPendingSendRequests);
        Assert.AreEqual(5, observed.SendBackpressureEvents);
        Assert.AreEqual(6, observed.SendRejectedRequests);
        Assert.AreEqual(512, observed.SendRejectedBytes);
        Assert.AreEqual(7, observed.SendDrainYieldCount);
        Assert.AreEqual(768, observed.MaxSendDrainYieldQueuedBytes);
        Assert.AreEqual(1024, observed.SendBufferBytes);
        Assert.AreEqual(2048, observed.MaxSendBufferBytes);
        Assert.AreEqual(9, observed.SendAbandonedRequests);
        Assert.AreEqual(2, observed.SocketErrorCountsByPhase!["send"]);
        Assert.AreEqual(2, observed.SocketErrorCountsByType!["SocketException"]);
        Assert.AreEqual(2, observed.SocketErrorCountsByCode!["ConnectionReset"]);
        Assert.AreEqual(2, observed.SocketErrorCountsByClass!["send|SocketException|ConnectionReset"]);
        Assert.AreEqual(1, observed.DisconnectCountsByReason!["idle-timeout"]);
        Assert.AreEqual(1, observed.IdleTimeoutDisconnects);
        Assert.AreEqual(1234, observed.MaxIdleTimeoutAgeMs);
        Assert.AreEqual(1, observed.AcceptErrorCount);
        Assert.AreEqual(2, observed.SocketErrorCount);
        Assert.AreEqual(3, observed.ParseErrorCount);
        Assert.AreEqual(4, observed.ProtocolErrorCount);
        Assert.AreEqual(0.01, observed.SocketErrorRate);
    }

    [TestMethod]
    public void ServerObservedMetricsSnapshot_PerSecondFields_UsePreviousSnapshotDelta()
    {
        var previousRaw = new ServerTelemetrySnapshot(
            Timestamp: new DateTimeOffset(2026, 4, 28, 9, 0, 0, TimeSpan.Zero),
            AcceptedSessions: 10,
            DisconnectedSessions: 2,
            ConnectedSessions: 8,
            ReceivedPackets: 100,
            SentPackets: 90,
            ReceivedBytes: 1000,
            SentBytes: 800,
            AcceptErrors: 0,
            SocketErrors: 0,
            ParseErrors: 0,
            ProtocolErrors: 0,
            SocketErrorRate: 0,
            SendRequests: 100,
            SendBackpressureEvents: 2,
            SendRejectedRequests: 4,
            SendRejectedBytes: 400,
            SendDrainYieldCount: 8,
            SendAbandonedRequests: 1,
            IdleTimeoutDisconnects: 2);

        var currentRaw = previousRaw with
        {
            Timestamp = previousRaw.Timestamp.AddSeconds(2),
            AcceptedSessions = 16,
            DisconnectedSessions = 4,
            ConnectedSessions = 12,
            ReceivedPackets = 140,
            SentPackets = 120,
            ReceivedBytes = 1800,
            SentBytes = 1400,
            SendRequests = 150,
            SendBackpressureEvents = 6,
            SendRejectedRequests = 14,
            SendRejectedBytes = 1400,
            SendDrainYieldCount = 20,
            SendAbandonedRequests = 5,
            IdleTimeoutDisconnects = 8
        };

        ServerObservedMetricsSnapshot previous = ServerObservedMetricsSnapshot.FromTelemetry(previousRaw);
        ServerObservedMetricsSnapshot current = ServerObservedMetricsSnapshot.FromTelemetry(currentRaw, previous);

        Assert.AreEqual(20, current.ReceivedPacketsPerSecond);
        Assert.AreEqual(15, current.SendCompletionsPerSecond);
        Assert.AreEqual(400, current.ParsedPacketBytesPerSecond);
        Assert.AreEqual(300, current.SentBytesPerSecond);
        Assert.AreEqual(3, current.AcceptedSessionsPerSecond);
        Assert.AreEqual(1, current.DisconnectedSessionsPerSecond);
        Assert.AreEqual(25, current.SendRequestsPerSecond);
        Assert.AreEqual(2, current.SendBackpressureEventsPerSecond);
        Assert.AreEqual(5, current.SendRejectedRequestsPerSecond);
        Assert.AreEqual(500, current.SendRejectedBytesPerSecond);
        Assert.AreEqual(6, current.SendDrainYieldCountPerSecond);
        Assert.AreEqual(2, current.SendAbandonedRequestsPerSecond);
        Assert.AreEqual(3, current.IdleTimeoutDisconnectsPerSecond);
    }

    [TestMethod]
    public void ServerObservedMetricsSnapshot_FirstSnapshot_PerSecondFieldsAreZero()
    {
        var raw = new ServerTelemetrySnapshot(
            Timestamp: DateTimeOffset.Now,
            AcceptedSessions: 1,
            DisconnectedSessions: 0,
            ConnectedSessions: 1,
            ReceivedPackets: 10,
            SentPackets: 5,
            ReceivedBytes: 100,
            SentBytes: 50,
            AcceptErrors: 0,
            SocketErrors: 0,
            ParseErrors: 0,
            ProtocolErrors: 0,
            SocketErrorRate: 0,
            SendRequests: 5,
            SendBackpressureEvents: 1,
            SendRejectedRequests: 1,
            SendRejectedBytes: 100,
            SendDrainYieldCount: 1);

        ServerObservedMetricsSnapshot observed = ServerObservedMetricsSnapshot.FromTelemetry(raw);

        Assert.AreEqual(0, observed.ReceivedPacketsPerSecond);
        Assert.AreEqual(0, observed.SendCompletionsPerSecond);
        Assert.AreEqual(0, observed.ParsedPacketBytesPerSecond);
        Assert.AreEqual(0, observed.SentBytesPerSecond);
        Assert.AreEqual(0, observed.AcceptedSessionsPerSecond);
        Assert.AreEqual(0, observed.DisconnectedSessionsPerSecond);
        Assert.AreEqual(0, observed.SendRequestsPerSecond);
        Assert.AreEqual(0, observed.SendBackpressureEventsPerSecond);
        Assert.AreEqual(0, observed.SendRejectedRequestsPerSecond);
        Assert.AreEqual(0, observed.SendRejectedBytesPerSecond);
        Assert.AreEqual(0, observed.SendDrainYieldCountPerSecond);
    }

    [TestMethod]
    public void ObservedMetricsJson_SerializesCamelCase()
    {
        var observed = ServerObservedMetricsSnapshot.FromTelemetry(new ServerTelemetrySnapshot(
            Timestamp: new DateTimeOffset(2026, 4, 28, 9, 0, 0, TimeSpan.Zero),
            AcceptedSessions: 1,
            DisconnectedSessions: 0,
            ConnectedSessions: 1,
            ReceivedPackets: 10,
            SentPackets: 5,
            ReceivedBytes: 100,
            SentBytes: 50,
            AcceptErrors: 0,
            SocketErrors: 0,
            ParseErrors: 0,
            ProtocolErrors: 0,
            SocketErrorRate: 0,
            SendRequests: 5,
            SendBackpressureEvents: 1,
            SendRejectedRequests: 2,
            SendRejectedBytes: 256,
            SendDrainYieldCount: 3,
            IdleTimeoutDisconnects: 1,
            MaxIdleTimeoutAgeMs: 1234));

        string json = ObservedMetricsJson.Serialize(ObservedMetricsSnapshot.FromServer(observed));

        Assert.IsTrue(json.Contains("\"serverObserved\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"totalSendCompletions\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"totalSendRequests\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"sendBackpressureEvents\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"sendRejectedRequests\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"sendDrainYieldCount\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"sendAbandonedRequests\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"idleTimeoutDisconnects\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"maxIdleTimeoutAgeMs\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"totalParsedPacketBytes\"", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("TotalSendCompletions", StringComparison.Ordinal));

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.IsTrue(document.RootElement.TryGetProperty("serverObserved", out JsonElement serverObserved));
        Assert.IsTrue(serverObserved.TryGetProperty("totalSendCompletions", out _));
        Assert.AreEqual(5, serverObserved.GetProperty("totalSendRequests").GetInt64());
    }

    [TestMethod]
    public void ObservedMetricsJson_DeserializesClientPacingFields()
    {
        var sessionRtt = new SessionRttSummarySnapshot(
            TrackedSessionCount: 2,
            EligibleSessionCount: 1,
            ExcludedLowSampleSessionCount: 1,
            MinSamplesPerSession: 8,
            P50OfSessionP95Ms: 10,
            P95OfSessionP95Ms: 12,
            P99OfSessionP95Ms: 14,
            MaxSessionP95Ms: 16,
            MaxSessionP99Ms: 18,
            MaxSessionMaxMs: 20,
            SlowestSessions:
            [
                new SlowSessionRttSnapshot(7, 8, 10, 5, 6, 16, 18, 20)
            ]);
        var client = new ClientObservedMetricsSnapshot(
            Timestamp: new DateTimeOffset(2026, 4, 29, 9, 0, 0, TimeSpan.Zero),
            TargetSessions: 100,
            CurrentSessions: 90,
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
            ConnectCount: 100,
            DisconnectCount: 10,
            SocketErrorCount: 1,
            SocketErrorRate: 0.001,
            TotalPacingWaitCount: 5,
            PacingWaitsPerSecond: 2.5,
            TotalPacingWaitTimeMs: 7.5,
            PacingAverageWaitMs: 1.5,
            PacingWindowIncreaseCount: 2,
            PacingWindowDecreaseCount: 1,
            MinObservedPacingWindow: 2,
            MaxObservedPacingWindow: 8,
            SessionRtt: sessionRtt,
            OperationDurations: new Dictionary<string, ObservedOperationDurationSnapshot>
            {
                ["receive-body"] = new(Count: 3, AverageMs: 4.5, MaxMs: 9.5)
            },
            ReceiveCloseCountsByClass: new Dictionary<string, long>
            {
                ["receive-body|partial-eof"] = 2
            },
            MaxOutstandingRequestsAtReceiveClose: 4,
            PhaseCompletionCounts: new Dictionary<string, long>
            {
                ["receive|completed"] = 1
            });

        string json = ObservedMetricsJson.Serialize(ObservedMetricsSnapshot.FromClient(client));

        ObservedMetricsSnapshot? observed = JsonSerializer.Deserialize<ObservedMetricsSnapshot>(
            json,
            ObservedMetricsJson.SerializerOptions);

        Assert.IsNotNull(observed);
        Assert.IsNotNull(observed.ClientObserved);
        Assert.AreEqual(5, observed.ClientObserved.TotalPacingWaitCount);
        Assert.AreEqual(2.5, observed.ClientObserved.PacingWaitsPerSecond);
        Assert.AreEqual(7.5, observed.ClientObserved.TotalPacingWaitTimeMs);
        Assert.AreEqual(1.5, observed.ClientObserved.PacingAverageWaitMs);
        Assert.AreEqual(2, observed.ClientObserved.PacingWindowIncreaseCount);
        Assert.AreEqual(1, observed.ClientObserved.PacingWindowDecreaseCount);
        Assert.AreEqual(2, observed.ClientObserved.MinObservedPacingWindow);
        Assert.AreEqual(8, observed.ClientObserved.MaxObservedPacingWindow);
        Assert.IsNotNull(observed.ClientObserved.OperationDurations);
        Assert.AreEqual(3, observed.ClientObserved.OperationDurations["receive-body"].Count);
        Assert.AreEqual(9.5, observed.ClientObserved.OperationDurations["receive-body"].MaxMs, 0.001);
        Assert.IsNotNull(observed.ClientObserved.ReceiveCloseCountsByClass);
        Assert.AreEqual(2, observed.ClientObserved.ReceiveCloseCountsByClass["receive-body|partial-eof"]);
        Assert.AreEqual(4, observed.ClientObserved.MaxOutstandingRequestsAtReceiveClose);
        Assert.IsNotNull(observed.ClientObserved.PhaseCompletionCounts);
        Assert.AreEqual(1, observed.ClientObserved.PhaseCompletionCounts["receive|completed"]);
        Assert.IsNotNull(observed.ClientObserved.SessionRtt);
        Assert.AreEqual(2, observed.ClientObserved.SessionRtt.TrackedSessionCount);
        Assert.AreEqual(1, observed.ClientObserved.SessionRtt.EligibleSessionCount);
        Assert.AreEqual(7, observed.ClientObserved.SessionRtt.SlowestSessions[0].SessionId);
    }

    [TestMethod]
    public void ObservedMetricsJson_DeserializesClientWithoutSessionRtt()
    {
        string json = """
            {
              "timestamp": "2026-04-29T09:00:00+00:00",
              "clientObserved": {
                "timestamp": "2026-04-29T09:00:00+00:00",
                "targetSessions": 10,
                "currentSessions": 10,
                "totalSentPackets": 10,
                "totalReceivedPackets": 10,
                "totalSentBytes": 1024,
                "totalReceivedBytes": 1024,
                "sentPacketsPerSecond": 1,
                "receivedPacketsPerSecond": 1,
                "sentBytesPerSecond": 1024,
                "receivedBytesPerSecond": 1024,
                "tps": 1,
                "rttAverageMs": 1,
                "rttP50Ms": 1,
                "rttP95Ms": 2,
                "rttP99Ms": 3,
                "connectCount": 10,
                "disconnectCount": 0,
                "socketErrorCount": 0,
                "socketErrorRate": 0
              },
              "serverObserved": null
            }
            """;

        ObservedMetricsSnapshot? observed = JsonSerializer.Deserialize<ObservedMetricsSnapshot>(
            json,
            ObservedMetricsJson.SerializerOptions);

        Assert.IsNotNull(observed);
        Assert.IsNotNull(observed.ClientObserved);
        Assert.IsNull(observed.ClientObserved.SessionRtt);
    }

    [TestMethod]
    public void ClientObservedMetricsSnapshot_MapsLoadRunnerMetrics()
    {
        var sessionRtt = new SessionRttSummarySnapshot(
            TrackedSessionCount: 1,
            EligibleSessionCount: 1,
            ExcludedLowSampleSessionCount: 0,
            MinSamplesPerSession: 8,
            P50OfSessionP95Ms: 4,
            P95OfSessionP95Ms: 4,
            P99OfSessionP95Ms: 5,
            MaxSessionP95Ms: 4,
            MaxSessionP99Ms: 5,
            MaxSessionMaxMs: 6,
            SlowestSessions:
            [
                new SlowSessionRttSnapshot(3, 8, 8, 2, 3, 4, 5, 6)
            ]);
        var raw = new MetricsSnapshot(
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
            TotalPacingWaitCount: 5,
            PacingAverageWaitMs: 1.5,
            PacingWindowIncreaseCount: 2,
            PacingWindowDecreaseCount: 1,
            MinObservedPacingWindow: 2,
            MaxObservedPacingWindow: 8,
            SessionRtt: sessionRtt,
            OperationDurations: new Dictionary<string, ObservedOperationDurationSnapshot>
            {
                ["send-write"] = new(Count: 4, AverageMs: 1.5, MaxMs: 3.5)
            },
            ReceiveCloseCountsByClass: new Dictionary<string, long>
            {
                ["receive-header|eof"] = 1
            },
            MaxOutstandingRequestsAtReceiveClose: 6,
            PhaseCompletionCounts: new Dictionary<string, long>
            {
                ["send|cancelled"] = 1
            });

        ClientObservedMetricsSnapshot observed = raw.ToClientObservedMetricsSnapshot();

        Assert.AreEqual(raw.Timestamp, observed.Timestamp);
        Assert.AreEqual(raw.TargetSessions, observed.TargetSessions);
        Assert.AreEqual(raw.ConnectedSessions, observed.CurrentSessions);
        Assert.AreEqual(raw.TotalSentPackets, observed.TotalSentPackets);
        Assert.AreEqual(raw.TotalReceivedPackets, observed.TotalReceivedPackets);
        Assert.AreEqual(raw.TotalSentBytes, observed.TotalSentBytes);
        Assert.AreEqual(raw.TotalReceivedBytes, observed.TotalReceivedBytes);
        Assert.AreEqual(raw.Tps, observed.Tps);
        Assert.AreEqual(raw.AcceptCount, observed.ConnectCount);
        Assert.AreEqual(raw.DisconnectCount, observed.DisconnectCount);
        Assert.AreEqual(raw.SocketErrorCount, observed.SocketErrorCount);
        Assert.AreEqual(raw.SocketErrorRate, observed.SocketErrorRate);
        Assert.AreEqual(raw.ConnectAttemptCount, observed.ConnectAttemptCount);
        Assert.AreEqual(raw.ConnectFailureCount, observed.ConnectFailureCount);
        Assert.AreEqual(raw.PendingRequestCount, observed.PendingRequestCount);
        Assert.AreEqual(raw.MaxPendingRequestCount, observed.MaxPendingRequestCount);
        Assert.AreEqual(raw.ActiveSessionRatio, observed.ActiveSessionRatio);
        Assert.AreEqual(raw.SchedulerDriftAverageMs, observed.SchedulerDriftAverageMs);
        Assert.AreEqual(raw.SchedulerDriftMaxMs, observed.SchedulerDriftMaxMs);
        Assert.AreEqual(raw.TotalPacingWaitCount, observed.TotalPacingWaitCount);
        Assert.AreEqual(raw.PacingAverageWaitMs, observed.PacingAverageWaitMs);
        Assert.AreEqual(raw.PacingWindowIncreaseCount, observed.PacingWindowIncreaseCount);
        Assert.AreEqual(raw.PacingWindowDecreaseCount, observed.PacingWindowDecreaseCount);
        Assert.AreEqual(raw.MinObservedPacingWindow, observed.MinObservedPacingWindow);
        Assert.AreEqual(raw.MaxObservedPacingWindow, observed.MaxObservedPacingWindow);
        Assert.IsNotNull(observed.OperationDurations);
        Assert.AreEqual(4, observed.OperationDurations["send-write"].Count);
        Assert.AreEqual(3.5, observed.OperationDurations["send-write"].MaxMs, 0.001);
        Assert.IsNotNull(observed.ReceiveCloseCountsByClass);
        Assert.AreEqual(1, observed.ReceiveCloseCountsByClass["receive-header|eof"]);
        Assert.AreEqual(6, observed.MaxOutstandingRequestsAtReceiveClose);
        Assert.IsNotNull(observed.PhaseCompletionCounts);
        Assert.AreEqual(1, observed.PhaseCompletionCounts["send|cancelled"]);
        Assert.IsNotNull(observed.SessionRtt);
        Assert.AreEqual(1, observed.SessionRtt.TrackedSessionCount);
        Assert.AreEqual(3, observed.SessionRtt.SlowestSessions[0].SessionId);
    }
}
