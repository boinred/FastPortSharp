using FastPortDashboard.Maui.Adapters;
using LibTestTelemetry;

namespace FastPortDashboardTests.Adapters;

// Design Ref: §3.4 — MockPollingAdapter 동작 검증.
[TestClass]
public class MockPollingAdapterTests
{
    // T-MA-1
    [TestMethod]
    public async Task StreamAsync_YieldsSnapshotsAtInterval()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var adapter = new MockPollingAdapter(interval: TimeSpan.FromMilliseconds(20));

        var snapshots = new List<ObservedMetricsSnapshot>();
        await foreach (var snap in adapter.StreamAsync(cts.Token))
        {
            snapshots.Add(snap);
            if (snapshots.Count >= 3) { break; }
        }

        Assert.IsTrue(snapshots.Count >= 3, $"snapshots count >= 3, actual={snapshots.Count}");
        foreach (var s in snapshots)
        {
            Assert.IsNotNull(s.ServerObserved);
        }
    }

    // T-MA-2
    [TestMethod]
    public async Task StreamAsync_CancelledToken_TerminatesGracefully()
    {
        using var cts = new CancellationTokenSource();
        var adapter = new MockPollingAdapter(interval: TimeSpan.FromMilliseconds(20));

        var task = Task.Run(async () =>
        {
            int n = 0;
            await foreach (var _ in adapter.StreamAsync(cts.Token))
            {
                n++;
                if (n == 2) { cts.Cancel(); }
            }
            return n;
        });

        // 2초 내 종료
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.AreSame(task, completed, "취소 후 2초 안에 enumeration 종료");
        Assert.IsTrue(task.Result >= 2);
    }

    // T-MA-3
    [TestMethod]
    public async Task StreamAsync_DifferentSeed_DifferentValues()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var a1 = new MockPollingAdapter(interval: TimeSpan.FromMilliseconds(5), seed: 42);
        var a2 = new MockPollingAdapter(interval: TimeSpan.FromMilliseconds(5), seed: 100);

        ObservedMetricsSnapshot? first1 = null, first2 = null;
        await foreach (var s in a1.StreamAsync(cts.Token)) { first1 = s; break; }
        await foreach (var s in a2.StreamAsync(cts.Token)) { first2 = s; break; }

        Assert.IsNotNull(first1?.ServerObserved);
        Assert.IsNotNull(first2?.ServerObserved);
        // seed가 다르면 random walk 첫 step도 다를 가능성 매우 큼.
        // CurrentSessions 또는 SentBytesPerSecond 중 최소 하나는 달라야 함.
        bool differs = first1!.ServerObserved!.CurrentSessions != first2!.ServerObserved!.CurrentSessions
            || Math.Abs(first1.ServerObserved.SentBytesPerSecond - first2.ServerObserved.SentBytesPerSecond) > 0.01;
        Assert.IsTrue(differs, "seed 다르면 random 결과도 달라야 함");
    }
}
