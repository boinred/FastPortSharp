using System.Text.Json;
using FastPortLoadRunner;
using LibNetworks.Telemetry;

namespace LibCommonTest;

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
            SendBufferBytes: 1024,
            MaxSendBufferBytes: 2048);

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
        Assert.AreEqual(1024, observed.SendBufferBytes);
        Assert.AreEqual(2048, observed.MaxSendBufferBytes);
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
            SendBackpressureEvents: 2);

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
            SendBackpressureEvents = 6
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
            SendBackpressureEvents: 1);

        ServerObservedMetricsSnapshot observed = ServerObservedMetricsSnapshot.FromTelemetry(raw);

        Assert.AreEqual(0, observed.ReceivedPacketsPerSecond);
        Assert.AreEqual(0, observed.SendCompletionsPerSecond);
        Assert.AreEqual(0, observed.ParsedPacketBytesPerSecond);
        Assert.AreEqual(0, observed.SentBytesPerSecond);
        Assert.AreEqual(0, observed.AcceptedSessionsPerSecond);
        Assert.AreEqual(0, observed.DisconnectedSessionsPerSecond);
        Assert.AreEqual(0, observed.SendRequestsPerSecond);
        Assert.AreEqual(0, observed.SendBackpressureEventsPerSecond);
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
            SendBackpressureEvents: 1));

        string json = ObservedMetricsJson.Serialize(ObservedMetricsSnapshot.FromServer(observed));

        Assert.IsTrue(json.Contains("\"serverObserved\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"totalSendCompletions\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"totalSendRequests\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"sendBackpressureEvents\"", StringComparison.Ordinal));
        Assert.IsTrue(json.Contains("\"totalParsedPacketBytes\"", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("TotalSendCompletions", StringComparison.Ordinal));

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.IsTrue(document.RootElement.TryGetProperty("serverObserved", out JsonElement serverObserved));
        Assert.IsTrue(serverObserved.TryGetProperty("totalSendCompletions", out _));
        Assert.AreEqual(5, serverObserved.GetProperty("totalSendRequests").GetInt64());
    }

    [TestMethod]
    public void ClientObservedMetricsSnapshot_MapsLoadRunnerMetrics()
    {
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
            SchedulerDriftMaxMs: 3.5);

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
    }
}
