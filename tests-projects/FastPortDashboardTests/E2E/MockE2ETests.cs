using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using FastPortDashboard.Maui.Adapters;
using FastPortDashboard.Maui.ViewModels;
using LibTestTelemetry;

namespace FastPortDashboardTests.E2E;

// Design Ref: §2 (dashboard-e2e-mock-tests) — UI 없이 ViewModel + MockPollingAdapter
// end-to-end pipeline 검증. macOS 26 SwiftUI Observation crash로 UI 실행 검증 차단된
// 환경에서 비즈니스 로직 회귀 자동 감지 경로.
[TestClass]
public class MockE2ETests
{
    private const int MockIntervalMs = 30;

    // 용도: pump가 끝나지 않을 때의 안전 상한. 정상 경로는 샘플 수로 끝나므로 이 시간에 의존하지 않음.
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(10);

    // 용도: 정해진 개수의 snapshot을 받은 뒤 pump 종료.
    // 목적: 고정 시간 창(예: 250ms) 대신 샘플 수로 끝내 CI runner 부하에 흔들리지 않게 함.
    private static async Task PumpMockSamplesAsync(DashboardViewModel vm, int sampleCount, int intervalMs)
    {
        using var cts = new CancellationTokenSource(SafetyTimeout);
        var adapter = new TakeAdapter(
            new MockPollingAdapter(interval: TimeSpan.FromMilliseconds(intervalMs)), sampleCount);
        await vm.PumpAsync(adapter, cts.Token);
    }

    // 용도: 내부 adapter에서 count개만 yield하고 끝내는 테스트용 래퍼
    private sealed class TakeAdapter(IPollingAdapter inner, int count) : IPollingAdapter
    {
        public async IAsyncEnumerable<ObservedMetricsSnapshot> StreamAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            int yielded = 0;
            await foreach (var snap in inner.StreamAsync(ct))
            {
                yield return snap;
                // 종료 조건: 요청한 샘플 수 도달
                if (++yielded >= count) { yield break; }
            }
        }
    }

    // 용도: count개를 yield한 직후 외부 CTS를 취소하는 테스트용 래퍼 (취소 경로 검증)
    private sealed class CancelAfterAdapter(IPollingAdapter inner, int count, CancellationTokenSource cts)
        : IPollingAdapter
    {
        public async IAsyncEnumerable<ObservedMetricsSnapshot> StreamAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            int yielded = 0;
            await foreach (var snap in inner.StreamAsync(ct))
            {
                yield return snap;
                // 흐름: count 도달 시 취소 → 내부 adapter가 delay 중 취소를 보고 종료
                if (++yielded == count) { cts.Cancel(); }
            }
        }
    }

    // E2E-1: Full pipeline — MockAdapter → ViewModel.PumpAsync → 모든 series 채워짐.
    [TestMethod]
    public async Task Mock_FullPipeline_PopulatesAllSeries()
    {
        var vm = new DashboardViewModel();
        await PumpMockSamplesAsync(vm, 3, MockIntervalMs);

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
        await PumpMockSamplesAsync(vm, 5, MockIntervalMs);

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

    // E2E-3: Cancellation 시 graceful — ViewModel state 유효 + 취소 전 받은 sample 유지.
    [TestMethod]
    public async Task Mock_Cancellation_GracefullyTerminates()
    {
        var vm = new DashboardViewModel();
        using var cts = new CancellationTokenSource();
        var adapter = new CancelAfterAdapter(
            new MockPollingAdapter(interval: TimeSpan.FromMilliseconds(MockIntervalMs)), 2, cts);

        // 흐름: 2번째 sample 직후 취소 → pump가 예외 없이 끝나야 함
        Task pump = vm.PumpAsync(adapter, cts.Token);
        Task finished = await Task.WhenAny(pump, Task.Delay(SafetyTimeout));
        Assert.AreSame(pump, finished, "취소 후 pump 종료");
        await pump;

        Assert.AreEqual(2, vm.ClientRttSeries.Count,
            $"취소 전까지 받은 2 sample 유지 (count={vm.ClientRttSeries.Count})");
    }

    // E2E-4: KPI 단조 증가 — Mock random walk은 totalAccepted/totalSentBytes를 증가만.
    [TestMethod]
    public async Task Mock_KpiUpdatesMonotonically()
    {
        var vm = new DashboardViewModel();
        long initialAccepted = vm.TotalAcceptedSessions;
        long initialSentBytes = vm.TotalSentBytes;

        await PumpMockSamplesAsync(vm, 5, MockIntervalMs);

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
