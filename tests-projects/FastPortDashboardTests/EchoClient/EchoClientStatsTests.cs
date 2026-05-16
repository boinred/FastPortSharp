// Design Ref: §8.2 L1-01/02/03 — Stats pure layer 단위 테스트.
using FastPortDashboard.Maui.EchoClient;

namespace FastPortDashboardTests.EchoClient;

[TestClass]
public sealed class EchoClientStatsTests
{
    private static readonly DateTime T0 = new(2026, 5, 13, 0, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void Snapshot_AfterSingleRoundTrip_ReturnsAccumulatedCounters()
    {
        // L1-01: send/recv count + bytes 누적 정확성.
        var stats = new EchoClientStats();
        stats.RecordSend(T0, bytesSent: 11);
        stats.RecordReceive(T0.AddMilliseconds(2), bytesRecv: 13, rttMs: 2.0);

        var snap = stats.Snapshot(T0.AddMilliseconds(5));

        Assert.AreEqual(1, snap.SendCount);
        Assert.AreEqual(1, snap.RecvCount);
        Assert.AreEqual(11, snap.TotalBytesSent);
        Assert.AreEqual(13, snap.TotalBytesRecv);
        Assert.AreEqual(2.0, snap.LastRttMs, 1e-9);
        Assert.AreEqual(2.0, snap.AvgRttMs, 1e-9);
    }

    [TestMethod]
    public void Snapshot_RatePerSecond_ReflectsLast1sWindow()
    {
        // L1-02: 1초 윈도우 안의 이벤트만 rate에 포함.
        var stats = new EchoClientStats();
        // T0 시점에 3건, T0+0.5s 시점에 2건.
        for (int i = 0; i < 3; i++) stats.RecordSend(T0, 10);
        for (int i = 0; i < 2; i++) stats.RecordSend(T0.AddMilliseconds(500), 10);

        // T0+0.9s에 snapshot: 5건 모두 window 안.
        var snap1 = stats.Snapshot(T0.AddMilliseconds(900));
        Assert.AreEqual(5.0, snap1.SendRatePerSec, 1e-9);

        // T0+1.5s에 snapshot: T0의 3건은 cutoff(T0+0.5s) 이하라 제외 → 2건만.
        var snap2 = stats.Snapshot(T0.AddMilliseconds(1500));
        Assert.AreEqual(2.0, snap2.SendRatePerSec, 1e-9);
    }

    [TestMethod]
    public void Snapshot_AvgRttMs_IsArithmeticMeanOfRecordedRtts()
    {
        // L1-03: 누적 평균.
        var stats = new EchoClientStats();
        stats.RecordReceive(T0, 10, rttMs: 1.0);
        stats.RecordReceive(T0, 10, rttMs: 3.0);
        stats.RecordReceive(T0, 10, rttMs: 5.0);

        var snap = stats.Snapshot(T0);

        Assert.AreEqual(3.0, snap.AvgRttMs, 1e-9);
        Assert.AreEqual(5.0, snap.LastRttMs, 1e-9);
    }

    [TestMethod]
    public void Reset_ClearsAllCountersAndWindows()
    {
        // FR-06: Disconnect 후 재Connect 시 통계 reset.
        var stats = new EchoClientStats();
        stats.RecordSend(T0, 100);
        stats.RecordReceive(T0, 100, 5.0);

        stats.Reset();
        var snap = stats.Snapshot(T0);

        Assert.AreEqual(0, snap.SendCount);
        Assert.AreEqual(0, snap.RecvCount);
        Assert.AreEqual(0, snap.TotalBytesSent);
        Assert.AreEqual(0, snap.TotalBytesRecv);
        Assert.AreEqual(0.0, snap.LastRttMs, 1e-9);
        Assert.AreEqual(0.0, snap.AvgRttMs, 1e-9);
        Assert.AreEqual(0.0, snap.SendRatePerSec, 1e-9);
        Assert.AreEqual(0.0, snap.RecvRatePerSec, 1e-9);
    }

    [TestMethod]
    public void Snapshot_AfterIdle_DropsAllWindowedEvents()
    {
        // 1초 이상 idle: rate 0, 누적은 보존.
        var stats = new EchoClientStats();
        stats.RecordSend(T0, 10);
        stats.RecordReceive(T0, 10, 1.0);

        var snap = stats.Snapshot(T0.AddSeconds(5));

        Assert.AreEqual(0.0, snap.SendRatePerSec, 1e-9);
        Assert.AreEqual(0.0, snap.RecvRatePerSec, 1e-9);
        Assert.AreEqual(1, snap.SendCount);
        Assert.AreEqual(1, snap.RecvCount);
    }
}
