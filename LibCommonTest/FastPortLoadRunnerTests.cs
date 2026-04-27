using FastPortLoadRunner;

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
            "--output", "metrics.jsonl"
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
    }

    [TestMethod]
    public void LoadRunnerOptions_TryParse_RejectsInvalidValues()
    {
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--port", "70000"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--sessions", "0"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--rate", "0"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--duration", "5x"], out _, out _));
        Assert.IsFalse(LoadRunnerOptions.TryParse(["--payload", "random:16384-4096"], out _, out _));
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
    public void MetricsCollector_CreateSnapshot_TracksTotalsAndRates()
    {
        var collector = new MetricsCollector(targetSessions: 10);
        collector.RecordSessionConnected();
        collector.RecordSentPacket(100);
        collector.RecordReceivedPacket(80);
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
        Assert.AreEqual(1, snapshot.SocketErrorCount);
        Assert.IsTrue(snapshot.SentPacketsPerSecond > 0);
        Assert.IsTrue(snapshot.ReceivedPacketsPerSecond > 0);
        Assert.IsTrue(snapshot.SentBytesPerSecond > 0);
        Assert.IsTrue(snapshot.ReceivedBytesPerSecond > 0);
        Assert.IsTrue(snapshot.SocketErrorRate > 0);
    }
}
