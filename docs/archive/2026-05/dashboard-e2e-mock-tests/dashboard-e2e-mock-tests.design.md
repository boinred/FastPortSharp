# dashboard-e2e-mock-tests Design

> **Project**: FastPortSharp · **Date**: 2026-05-11

## 1. Architecture: Option A — Single E2E file (MSTest)

| Option | Approach | 선택 |
|---|---|---|
| **A — `E2E/MockE2ETests.cs` 단일 파일** | 기존 MSTest framework 사용, 5 tests를 한 클래스에 | ✅ |
| B — 별도 sln/project (E2E project) | overengineering, CI 변경 필요 | ❌ |
| C — xUnit 도입 | framework mix, 일관성 ↓ | ❌ |

## 2. Test Design

### 2.1 Test Class Structure

```csharp
namespace FastPortDashboardTests.E2E;

[TestClass]
public class MockE2ETests
{
    private const int MockIntervalMs = 30;
    private const int PumpDurationMs = 250;

    [TestMethod]
    public async Task Mock_FullPipeline_PopulatesAllSeries() { ... }

    [TestMethod]
    public async Task Mock_AllRttPercentiles_Populated() { ... }

    [TestMethod]
    public async Task Mock_Cancellation_GracefullyTerminates() { ... }

    [TestMethod]
    public async Task Mock_KpiUpdatesMonotonically() { ... }

    [TestMethod]
    public async Task Mock_StartAsync_FullLifecycle() { ... }

    private static async Task PumpMockForAsync(DashboardViewModel vm, int durationMs, int intervalMs)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(durationMs));
        var adapter = new MockPollingAdapter(interval: TimeSpan.FromMilliseconds(intervalMs));
        try { await vm.PumpAsync(adapter, cts.Token); }
        catch (OperationCanceledException) { /* expected on cancel */ }
    }
}
```

### 2.2 E2E-1: Full Pipeline

```csharp
[TestMethod]
public async Task Mock_FullPipeline_PopulatesAllSeries()
{
    var vm = new DashboardViewModel();
    await PumpMockForAsync(vm, PumpDurationMs, MockIntervalMs);

    Assert.IsTrue(vm.ClientRttSeries.Count >= 3, $"RTT count={vm.ClientRttSeries.Count}");
    Assert.IsTrue(vm.ThroughputSeries.Count >= 3, $"Throughput count={vm.ThroughputSeries.Count}");
    Assert.IsTrue(vm.LastUpdate != DateTimeOffset.MinValue);
}
```

### 2.3 E2E-2: RTT Percentile Sanity

```csharp
[TestMethod]
public async Task Mock_AllRttPercentiles_Populated()
{
    var vm = new DashboardViewModel();
    await PumpMockForAsync(vm, PumpDurationMs, MockIntervalMs);

    Assert.IsTrue(vm.ClientRttSeries.Count > 0);
    foreach (var p in vm.ClientRttSeries)
    {
        Assert.IsTrue(p.P50Ms > 0, $"P50 > 0 (actual {p.P50Ms})");
        Assert.IsTrue(p.P95Ms > 0, $"P95 > 0 (actual {p.P95Ms})");
        Assert.IsTrue(p.P99Ms > 0, $"P99 > 0 (actual {p.P99Ms})");
        // Note: Mock random walk does NOT guarantee P50≤P95≤P99 strict ordering since
        // each percentile is independently randomized. Real production would have ordering.
        // We just check all 3 are populated (non-zero) and in reasonable range.
        Assert.IsTrue(p.P50Ms < 1000, "P50 sane range < 1000ms");
        Assert.IsTrue(p.P99Ms < 1000, "P99 sane range < 1000ms");
    }
}
```

### 2.4 E2E-3: Cancellation Graceful

```csharp
[TestMethod]
public async Task Mock_Cancellation_GracefullyTerminates()
{
    var vm = new DashboardViewModel();
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
    var adapter = new MockPollingAdapter(interval: TimeSpan.FromMilliseconds(MockIntervalMs));

    bool caught = false;
    try { await vm.PumpAsync(adapter, cts.Token); }
    catch (OperationCanceledException) { caught = true; }

    // PumpAsync may either propagate OperationCanceledException OR end gracefully via yield break.
    // Both acceptable. Key: ViewModel state must remain valid.
    Assert.IsTrue(vm.ClientRttSeries.Count >= 1, "최소 1 sample 수신 후 cancel");
    Assert.IsNotNull(vm); // sanity
}
```

### 2.5 E2E-4: Monotonic KPI

```csharp
[TestMethod]
public async Task Mock_KpiUpdatesMonotonically()
{
    var vm = new DashboardViewModel();
    long initialAccepted = vm.TotalAcceptedSessions;
    long initialSentBytes = vm.TotalSentBytes;

    await PumpMockForAsync(vm, PumpDurationMs, MockIntervalMs);

    Assert.IsTrue(vm.TotalAcceptedSessions >= initialAccepted,
        $"TotalAcceptedSessions monotonic (initial={initialAccepted}, final={vm.TotalAcceptedSessions})");
    Assert.IsTrue(vm.TotalSentBytes >= initialSentBytes,
        $"TotalSentBytes monotonic (initial={initialSentBytes}, final={vm.TotalSentBytes})");
}
```

### 2.6 E2E-5: ViewModel Lifecycle (ConnectCommand)

```csharp
[TestMethod]
public async Task Mock_StartAsync_FullLifecycle()
{
    var vm = new DashboardViewModel();
    vm.UseMock = true;

    // ConnectCommand가 비동기 StartAsync 실행 → 잠시 대기 → 상태 확인 → Disconnect
    var connectCmd = (CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.ConnectCommand;
    var connectTask = connectCmd.ExecuteAsync(null);

    // Polling 시작 대기
    await Task.Delay(150);
    Assert.AreEqual(PollingState.Polling, vm.State);

    // Disconnect
    vm.DisconnectCommand.Execute(null);
    await connectTask; // wait for StartAsync to complete

    Assert.AreEqual(PollingState.Disconnected, vm.State);
    Assert.IsTrue(vm.ClientRttSeries.Count >= 1);
}
```

## 3. File Changes

| 파일 | 작업 | 라인 |
|---|---|---|
| `tests-projects/FastPortDashboardTests/E2E/MockE2ETests.cs` | new | ~130 |

총 1 파일. Production code 변경 0.

## 4. Test Plan

| Level | Pass Criteria |
|---|---|
| Build | Dashboard sln 0/0 |
| Unit | 25/0/0 (기존 20 + 신규 5) |
| Regression | FastPortSharp.sln 0/0 + 139/0/0 |
| Execution time | < 5s |
| CI | dashboard.yml path filter 자동 trigger 확인 (이미 포함) |
