using System.Text.Json;
using LibNetworks;
using LibTestTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Sockets;

namespace FastPortTests;

[TestClass]
public sealed class ServerTelemetryTests
{
    [TestMethod]
    public void ServerTelemetryCollector_CreateSnapshot_ReturnsDerivedConnectedSessions()
    {
        var telemetry = new ServerTelemetryCollector();

        telemetry.RecordAccept();
        telemetry.RecordAccept();
        telemetry.RecordSessionDisconnected();
        telemetry.RecordReceived(128);
        telemetry.RecordSent(256);

        ServerTelemetrySnapshot snapshot = telemetry.CreateSnapshot();

        Assert.AreEqual(2, snapshot.AcceptedSessions);
        Assert.AreEqual(1, snapshot.DisconnectedSessions);
        Assert.AreEqual(1, snapshot.ConnectedSessions);
        Assert.AreEqual(1, snapshot.ReceivedPackets);
        Assert.AreEqual(1, snapshot.SentPackets);
        Assert.AreEqual(128, snapshot.ReceivedBytes);
        Assert.AreEqual(256, snapshot.SentBytes);
    }

    [TestMethod]
    public void ServerTelemetryCollector_SendCounters_TrackPendingAndMaxSamples()
    {
        var telemetry = new ServerTelemetryCollector();

        telemetry.RecordSendRequested(100, queuedBytes: 512);
        telemetry.RecordSendRequested(200, queuedBytes: 768);
        telemetry.RecordSent(100);
        telemetry.RecordSendCompleted();
        telemetry.RecordSendAbandoned(1);
        telemetry.RecordSendBackpressure();
        telemetry.RecordSendDrainYield(700);
        telemetry.RecordSendRejected(300, queuedBytes: 900);

        ServerTelemetrySnapshot snapshot = telemetry.CreateSnapshot();

        Assert.AreEqual(2, snapshot.SendRequests);
        Assert.AreEqual(0, snapshot.PendingSendRequests);
        Assert.AreEqual(2, snapshot.MaxPendingSendRequests);
        Assert.AreEqual(1, snapshot.SendAbandonedRequests);
        Assert.AreEqual(1, snapshot.SendBackpressureEvents);
        Assert.AreEqual(1, snapshot.SendRejectedRequests);
        Assert.AreEqual(300, snapshot.SendRejectedBytes);
        Assert.AreEqual(1, snapshot.SendDrainYieldCount);
        Assert.AreEqual(700, snapshot.MaxSendDrainYieldQueuedBytes);
        Assert.AreEqual(900, snapshot.SendBufferBytes);
        Assert.AreEqual(900, snapshot.MaxSendBufferBytes);
        Assert.AreEqual(1, snapshot.SentPackets);
        Assert.AreEqual(100, snapshot.SentBytes);
    }

    [TestMethod]
    public void ServerTelemetryCollector_DisconnectReason_TracksIdleTimeout()
    {
        var telemetry = new ServerTelemetryCollector();

        telemetry.RecordSessionDisconnected("idle-timeout");
        telemetry.RecordIdleTimeoutDisconnect(TimeSpan.FromMilliseconds(1234));

        ServerTelemetrySnapshot snapshot = telemetry.CreateSnapshot();

        Assert.AreEqual(1, snapshot.DisconnectedSessions);
        Assert.AreEqual(1, snapshot.DisconnectCountsByReason!["idle-timeout"]);
        Assert.AreEqual(1, snapshot.IdleTimeoutDisconnects);
        Assert.AreEqual(1234, snapshot.MaxIdleTimeoutAgeMs);
    }

    [TestMethod]
    public void ServerTelemetryCollector_OperationDurations_TrackCountAverageAndMax()
    {
        var telemetry = new ServerTelemetryCollector();

        telemetry.RecordOperationDuration("accept-session-create", TimeSpan.FromMilliseconds(5));
        telemetry.RecordOperationDuration("accept-session-create", TimeSpan.FromMilliseconds(15));
        telemetry.RecordOperationDuration("accept-task-start", TimeSpan.FromMilliseconds(3));
        telemetry.RecordOperationDuration("ignore-zero", TimeSpan.Zero);

        ServerTelemetrySnapshot snapshot = telemetry.CreateSnapshot();

        Assert.IsNotNull(snapshot.OperationDurations);
        Assert.AreEqual(2, snapshot.OperationDurations["accept-session-create"].Count);
        Assert.AreEqual(10, snapshot.OperationDurations["accept-session-create"].AverageMs, 0.001);
        Assert.AreEqual(15, snapshot.OperationDurations["accept-session-create"].MaxMs, 0.001);
        Assert.AreEqual(1, snapshot.OperationDurations["accept-task-start"].Count);
        Assert.IsFalse(snapshot.OperationDurations.ContainsKey("ignore-zero"));
    }

    [TestMethod]
    public void ServerTelemetryCollector_Reset_ClearsCounters()
    {
        var telemetry = new ServerTelemetryCollector();
        telemetry.RecordAccept();
        telemetry.RecordSessionDisconnected();
        telemetry.RecordReceived(128);
        telemetry.RecordSendRequested(256, queuedBytes: 512);
        telemetry.RecordSent(256);
        telemetry.RecordSendBackpressure();
        telemetry.RecordSendRejected(512, queuedBytes: 1024);
        telemetry.RecordSendDrainYield(256);
        telemetry.RecordSessionDisconnected("idle-timeout");
        telemetry.RecordIdleTimeoutDisconnect(TimeSpan.FromSeconds(1));
        telemetry.RecordSocketError("send", SocketError.ConnectionReset, new SocketException((int)SocketError.ConnectionReset));
        telemetry.RecordParseError();
        telemetry.RecordProtocolError();
        telemetry.RecordAcceptError();
        telemetry.RecordOperationDuration("accept-first-receive", TimeSpan.FromMilliseconds(5));

        telemetry.Reset();

        ServerTelemetrySnapshot snapshot = telemetry.CreateSnapshot();
        Assert.AreEqual(0, snapshot.AcceptedSessions);
        Assert.AreEqual(0, snapshot.DisconnectedSessions);
        Assert.AreEqual(0, snapshot.ConnectedSessions);
        Assert.AreEqual(0, snapshot.ReceivedPackets);
        Assert.AreEqual(0, snapshot.SentPackets);
        Assert.AreEqual(0, snapshot.ReceivedBytes);
        Assert.AreEqual(0, snapshot.SentBytes);
        Assert.AreEqual(0, snapshot.SendRequests);
        Assert.AreEqual(0, snapshot.PendingSendRequests);
        Assert.AreEqual(0, snapshot.MaxPendingSendRequests);
        Assert.AreEqual(0, snapshot.SendAbandonedRequests);
        Assert.AreEqual(0, snapshot.SendBackpressureEvents);
        Assert.AreEqual(0, snapshot.SendRejectedRequests);
        Assert.AreEqual(0, snapshot.SendRejectedBytes);
        Assert.AreEqual(0, snapshot.SendDrainYieldCount);
        Assert.AreEqual(0, snapshot.MaxSendDrainYieldQueuedBytes);
        Assert.AreEqual(0, snapshot.SendBufferBytes);
        Assert.AreEqual(0, snapshot.MaxSendBufferBytes);
        Assert.AreEqual(0, snapshot.SocketErrors);
        Assert.AreEqual(0, snapshot.DisconnectCountsByReason!.Count);
        Assert.AreEqual(0, snapshot.IdleTimeoutDisconnects);
        Assert.AreEqual(0, snapshot.MaxIdleTimeoutAgeMs);
        Assert.AreEqual(0, snapshot.SocketErrorCountsByPhase!.Count);
        Assert.AreEqual(0, snapshot.SocketErrorCountsByType!.Count);
        Assert.AreEqual(0, snapshot.SocketErrorCountsByCode!.Count);
        Assert.AreEqual(0, snapshot.SocketErrorCountsByClass!.Count);
        Assert.AreEqual(0, snapshot.ParseErrors);
        Assert.AreEqual(0, snapshot.ProtocolErrors);
        Assert.AreEqual(0, snapshot.AcceptErrors);
        Assert.IsNull(snapshot.OperationDurations);
    }

    [TestMethod]
    public void ServerTelemetryCollector_SocketErrorRate_UsesPacketsAndErrors()
    {
        var telemetry = new ServerTelemetryCollector();

        telemetry.RecordReceived(10);
        telemetry.RecordSent(10);
        telemetry.RecordSocketError();

        ServerTelemetrySnapshot snapshot = telemetry.CreateSnapshot();

        Assert.AreEqual(1.0 / 3.0, snapshot.SocketErrorRate, 0.0001);
    }

    [TestMethod]
    public void ServerTelemetryCollector_SocketErrorClassification_TracksPhaseTypeCodeAndClass()
    {
        var telemetry = new ServerTelemetryCollector();

        telemetry.RecordSocketError("send", SocketError.ConnectionReset, new SocketException((int)SocketError.ConnectionReset));

        ServerTelemetrySnapshot snapshot = telemetry.CreateSnapshot();

        Assert.AreEqual(1, snapshot.SocketErrors);
        Assert.AreEqual(1, snapshot.SocketErrorCountsByPhase!["send"]);
        Assert.AreEqual(1, snapshot.SocketErrorCountsByType!["SocketException"]);
        Assert.AreEqual(1, snapshot.SocketErrorCountsByCode!["ConnectionReset"]);
        Assert.AreEqual(1, snapshot.SocketErrorCountsByClass!["send|SocketException|ConnectionReset"]);
    }

    [TestMethod]
    public void FastPortTestSmokeServerOptions_Defaults_ToProductionAddress()
    {
        var options = new FastPortTestSmokeServer.FastPortTestSmokeServerOptions();

        Assert.AreEqual("0.0.0.0", options.Host);
        Assert.AreEqual(6628, options.Port);
        Assert.AreEqual(4096, options.ListenBacklog);
        Assert.AreEqual(1, options.OutstandingAccepts);
    }

    [TestMethod]
    public void FastPortTestSmokeServerConfiguration_UsesNewSectionFirst()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FastPortTestSmokeServer:Host"] = "127.0.0.2",
                ["FastPortTestSmokeServer:ListenBacklog"] = "4096",
                ["FastPortTestSmokeServer:OutstandingAccepts"] = "4",
                ["FastPortSmokeServer:Host"] = "127.0.0.1"
            })
            .Build();

        IConfigurationSection section = FastPortTestSmokeServer.FastPortTestSmokeServerConfiguration.GetServerSection(configuration);

        Assert.AreEqual("127.0.0.2", section["Host"]);
        Assert.AreEqual("4096", section["ListenBacklog"]);
        Assert.AreEqual("4", section["OutstandingAccepts"]);
    }

    [TestMethod]
    public void FastPortTestSmokeServerConfiguration_FallsBackToLegacySection()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FastPortSmokeServer:Host"] = "127.0.0.1"
            })
            .Build();

        IConfigurationSection section = FastPortTestSmokeServer.FastPortTestSmokeServerConfiguration.GetServerSection(configuration);

        Assert.AreEqual("127.0.0.1", section["Host"]);
    }

    [TestMethod]
    public void BaseListener_NormalizeOutstandingAccepts_UsesDefaultForInvalidValues()
    {
        Assert.AreEqual(1, BaseListener.NormalizeOutstandingAccepts(0));
        Assert.AreEqual(1, BaseListener.NormalizeOutstandingAccepts(-1));
    }

    [TestMethod]
    public void BaseListener_NormalizeOutstandingAccepts_ClampsLargeValues()
    {
        Assert.AreEqual(64, BaseListener.NormalizeOutstandingAccepts(1024));
        Assert.AreEqual(4, BaseListener.NormalizeOutstandingAccepts(4));
    }

    [TestMethod]
    public async Task ServerTelemetryExportBackgroundService_WritesServerObservedJsonl()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"fastport-server-telemetry-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "server.metrics.jsonl");
        var telemetry = new ServerTelemetryCollector();
        telemetry.RecordAccept();
        telemetry.RecordSendRequested(128, queuedBytes: 256);
        var exporter = new ServerTelemetryExporter(telemetry);
        var options = new FastPortTestSmokeServer.FastPortTestSmokeServerTelemetryOptions
        {
            Output = path,
            IntervalSeconds = 1
        };
        var service = new FastPortTestSmokeServer.ServerTelemetryExportBackgroundService(
            NullLogger<FastPortTestSmokeServer.ServerTelemetryExportBackgroundService>.Instance,
            exporter,
            options);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(1200);
        await service.StopAsync(CancellationToken.None);

        Assert.IsTrue(File.Exists(path));
        string[] lines = await File.ReadAllLinesAsync(path);
        Assert.IsTrue(lines.Length >= 1);

        ObservedMetricsSnapshot? snapshot = JsonSerializer.Deserialize<ObservedMetricsSnapshot>(
            lines[0],
            ObservedMetricsJson.SerializerOptions);

        Assert.IsNotNull(snapshot);
        Assert.IsNull(snapshot.ClientObserved);
        Assert.IsNotNull(snapshot.ServerObserved);
        Assert.AreEqual(1, snapshot.ServerObserved!.TotalAcceptedSessions);
        Assert.AreEqual(1, snapshot.ServerObserved.TotalSendRequests);
        Assert.AreEqual(1, snapshot.ServerObserved.PendingSendRequests);
        Assert.AreEqual(256, snapshot.ServerObserved.SendBufferBytes);
    }
}
