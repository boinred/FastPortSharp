using CommunityToolkit.Mvvm.Input;
using FastPortDashboard.Maui.Adapters;
using FastPortDashboard.Maui.ViewModels;

namespace FastPortDashboardTests.E2E;

// Design Ref: §2 (dashboard-e2e-mock-tests) — UI 없이 ViewModel + MockPollingAdapter
// end-to-end pipeline 검증. macOS 26 SwiftUI Observation crash로 UI 실행 검증 차단된
// 환경에서 비즈니스 로직 회귀 자동 감지 경로.
[TestClass]
public class MockE2ETests
{
    private const int MockIntervalMs = 30;
    private const int PumpDurationMs = 250;

    private static async Task PumpMockForAsync(DashboardViewModel vm, int durationMs, int intervalMs)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(durationMs));
        var adapter = new MockPollingAdapter(interval: TimeSpan.FromMilliseconds(intervalMs));
        try
        {
            await vm.PumpAsync(adapter, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // PumpAsync may exit via yield break or propagate cancellation — both OK.
        }
    }

    // E2E-1: Full pipeline — MockAdapter → ViewModel.PumpAsync → 모든 series 채워짐.
    [TestMethod]
    public async Task Mock_FullPipeline_PopulatesAllSeries()
    {
        var vm = new DashboardViewModel();
        await PumpMockForAsync(vm, PumpDurationMs, MockIntervalMs);

        Assert.IsTrue(vm.ClientRttSeries.Count >= 3,
            $"ClientRttSeries count={vm.ClientRttSeries.Count}, expected >= 3");
        Assert.IsTrue(vm.ThroughputSeries.Count >= 3,
            $"ThroughputSeries count={vm.ThroughputSeries.Count}, expected >= 3");
        Assert.AreNotEqual(DateTimeOffset.MinValue, vm.LastUpdate,
            "LastUpdate should advance from initial MinValue");
    }

    // E2E-2: RTT P50/P95/P99 모두 populated + sane 범위.
    [TestMethod]
    public async Task Mock_AllRttPercentiles_Populated()
    {
        var vm = new DashboardViewModel();
        await PumpMockForAsync(vm, PumpDurationMs, MockIntervalMs);

        Assert.IsTrue(vm.ClientRttSeries.Count > 0, "ClientRttSeries 비어있지 않음");
        foreach (var p in vm.ClientRttSeries)
        {
            Assert.IsTrue(p.P50Ms > 0, $"P50 > 0 (actual {p.P50Ms})");
            Assert.IsTrue(p.P95Ms > 0, $"P95 > 0 (actual {p.P95Ms})");
            Assert.IsTrue(p.P99Ms > 0, $"P99 > 0 (actual {p.P99Ms})");
            // Mock random walk은 각 percentile을 독립 random으로 생성 → strict P50≤P95≤P99 보장 안 함.
            // Sane 범위 (< 1초) 검증만.
            Assert.IsTrue(p.P50Ms < 1000, $"P50 sane range (< 1000ms), actual {p.P50Ms}");
            Assert.IsTrue(p.P99Ms < 1000, $"P99 sane range (< 1000ms), actual {p.P99Ms}");
        }
    }

    // E2E-3: Cancellation 시 graceful — ViewModel state 유효 + 최소 1 sample 수신.
    [TestMethod]
    public async Task Mock_Cancellation_GracefullyTerminates()
    {
        var vm = new DashboardViewModel();
        await PumpMockForAsync(vm, 100, MockIntervalMs);

        // 100ms 내 최소 1-2 snapshot 수신 후 cancel. 예외 없이 종료되어야.
        Assert.IsTrue(vm.ClientRttSeries.Count >= 1,
            $"최소 1 sample 수신 후 cancel (count={vm.ClientRttSeries.Count})");
    }

    // E2E-4: KPI 단조 증가 — Mock random walk은 totalAccepted/totalSentBytes를 증가만.
    [TestMethod]
    public async Task Mock_KpiUpdatesMonotonically()
    {
        var vm = new DashboardViewModel();
        long initialAccepted = vm.TotalAcceptedSessions;
        long initialSentBytes = vm.TotalSentBytes;

        await PumpMockForAsync(vm, PumpDurationMs, MockIntervalMs);

        Assert.IsTrue(vm.TotalAcceptedSessions >= initialAccepted,
            $"TotalAcceptedSessions monotonic: initial={initialAccepted}, final={vm.TotalAcceptedSessions}");
        Assert.IsTrue(vm.TotalSentBytes >= initialSentBytes,
            $"TotalSentBytes monotonic: initial={initialSentBytes}, final={vm.TotalSentBytes}");
        // Mock는 항상 accepted 증가 → final > initial 가능성 매우 큼.
        Assert.IsTrue(vm.TotalAcceptedSessions > 0 || vm.TotalSentBytes > 0,
            "최소 하나는 양수 (Mock 동작 검증)");
    }

    // E2E-5: ViewModel lifecycle — UseMock + ConnectCommand → Polling → Disconnect → Disconnected.
    [TestMethod]
    public async Task Mock_StartAsync_FullLifecycle()
    {
        var vm = new DashboardViewModel();
        vm.UseMock = true;

        var connectCmd = (IAsyncRelayCommand)vm.ConnectCommand;
        var connectTask = connectCmd.ExecuteAsync(null);

        // Polling 시작 대기 — 첫 yield 까지 + buffer.
        await Task.Delay(150);
        Assert.AreEqual(PollingState.Polling, vm.State,
            $"After connect, state should be Polling (actual {vm.State})");

        // Disconnect — _cts.Cancel() → OperationCanceledException → State = Disconnected.
        vm.DisconnectCommand.Execute(null);
        await connectTask;

        Assert.AreEqual(PollingState.Disconnected, vm.State,
            $"After disconnect, state should be Disconnected (actual {vm.State})");
        Assert.IsTrue(vm.ClientRttSeries.Count >= 1, "최소 1 sample 수신");
    }
}
