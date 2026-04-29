using LibNetworks.Telemetry;

namespace LibCommonTest;

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
        telemetry.RecordSendBackpressure();

        ServerTelemetrySnapshot snapshot = telemetry.CreateSnapshot();

        Assert.AreEqual(2, snapshot.SendRequests);
        Assert.AreEqual(1, snapshot.PendingSendRequests);
        Assert.AreEqual(2, snapshot.MaxPendingSendRequests);
        Assert.AreEqual(1, snapshot.SendBackpressureEvents);
        Assert.AreEqual(768, snapshot.SendBufferBytes);
        Assert.AreEqual(768, snapshot.MaxSendBufferBytes);
        Assert.AreEqual(1, snapshot.SentPackets);
        Assert.AreEqual(100, snapshot.SentBytes);
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
        telemetry.RecordSocketError();
        telemetry.RecordParseError();
        telemetry.RecordProtocolError();
        telemetry.RecordAcceptError();

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
        Assert.AreEqual(0, snapshot.SendBackpressureEvents);
        Assert.AreEqual(0, snapshot.SendBufferBytes);
        Assert.AreEqual(0, snapshot.MaxSendBufferBytes);
        Assert.AreEqual(0, snapshot.SocketErrors);
        Assert.AreEqual(0, snapshot.ParseErrors);
        Assert.AreEqual(0, snapshot.ProtocolErrors);
        Assert.AreEqual(0, snapshot.AcceptErrors);
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
    public void FastPortSmokeServerOptions_Defaults_ToProductionAddress()
    {
        var options = new FastPortSmokeServer.FastPortSmokeServerOptions();

        Assert.AreEqual("0.0.0.0", options.Host);
        Assert.AreEqual(6628, options.Port);
    }
}
