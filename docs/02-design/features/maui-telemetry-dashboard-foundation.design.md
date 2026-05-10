# maui-telemetry-dashboard-foundation Design

> **Summary**: Option B — MVVM 표준. `FastPortDashboard.Maui` (root) + `FastPortSharp.Dashboard.sln` (root) + LiveCharts2 + `IPollingAdapter` 추상화 (Mock / Jsonl 두 구현). 단일 view (MainPage) + ViewModel (DashboardViewModel) + Polling 1초 (Task.Delay 기반) + JSONL `FileShare.ReadWrite` reader.
>
> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-10
> **Status**: Draft
> **Plan**: [../../01-plan/features/maui-telemetry-dashboard-foundation.plan.md](../../01-plan/features/maui-telemetry-dashboard-foundation.plan.md)
> **PRD**: [../../00-pm/maui-telemetry-dashboard-foundation.prd.md](../../00-pm/maui-telemetry-dashboard-foundation.prd.md)

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Server JSONL telemetry 시각화 GUI 부재. dashboard track 시드. |
| **WHO** | boinred + 미래 contributor + AI agent. |
| **RISK** | (R-1) MAUI workload 부담 / (R-2) chart lib 호환성 / (R-3) JSONL 동시 read race / (R-4) 첫 MAUI 학습 / (R-5) 기존 build.yml CI 회귀 |
| **SUCCESS** | 새 sln macOS Catalyst 빌드 + chart live update + 139 tests 회귀 0 + dashboard unit test |
| **SCOPE** | 신규 `FastPortDashboard.Maui/` + `FastPortSharp.Dashboard.sln` + LiveCharts2 + Mock/Jsonl adapter + 단일 view |

---

## 1. Overview

### 1.1 Design Goals

1. **MVVM 표준 적용**: ViewModel + INotifyPropertyChanged + ObservableCollection. unit test가 용이한 layered structure.
2. **Adapter 분리**: `IPollingAdapter` 인터페이스로 Mock과 Jsonl 두 구현. 다음 cycle (예: HTTP/SignalR adapter)도 같은 contract.
3. **CI 격리**: 별도 sln (`FastPortSharp.Dashboard.sln`)으로 기존 build.yml 회귀 0.
4. **File share 규약 준수**: 직전 cycle lesson — `FileShare.ReadWrite` 명시.
5. **Foundation scope 협소화**: 단일 view, 1-2개 chart, KPI 4-6개. 다음 cycle에서 view 추가.

### 1.2 Design Principles

- **Test-friendly**: ViewModel은 UI 비-의존. adapter 인터페이스 mock으로 unit test.
- **Cancellation-aware**: polling loop는 `CancellationToken` 항상 휴대. tab 닫기 / disconnect에 즉시 반응.
- **Observable contract**: chart binding은 `ObservableCollection<TPoint>` + `INotifyPropertyChanged` KPI.
- **Defensive read**: JSONL 마지막 line이 partial일 수 있어 `Try` parse + skip.

---

## 2. Architecture Options (Selected)

### 2.0 Comparison

| Criteria | Option A: Code-behind | **Option B: MVVM** | Option C: Rx |
|---|:-:|:-:|:-:|
| Foundation 적합성 | High | **High** | Low |
| Unit test 용이 | Low | **High** | Medium |
| 향후 cycle 확장 | Low | **High** | High |
| 학습 비용 | Low | Medium | High |
| 보일러플레이트 | Low | Medium | High |
| Adapter 분리 | △ | **✅** | ✅ |
| **Recommendation** | | **Selected** | |

### 2.1 Selected: Option B — MVVM 표준

**Rationale**:
- Plan §4.1 SC가 "dashboard adapter unit test 1-2개 + chart binding test 1개" 명시 — Option A로는 UI에 비즈니스 로직 묶여 테스트 어려움.
- 다음 cycle (run viewer / report export)에서 view가 추가될 때 ViewModel 분리되어 있으면 재사용 가능.
- LiveCharts2도 ObservableCollection / INotifyPropertyChanged 패턴 친화적 (공식 sample이 MVVM).

### 2.2 Component Diagram

```
┌──────────────────────────────────────────────────────────────────────┐
│  FastPortDashboard.Maui/                                             │
│  ├── App.xaml / App.xaml.cs                  (MAUI app shell)        │
│  ├── MauiProgram.cs                          (DI / app builder)      │
│  ├── Platforms/                              (MAUI 기본 — macOS,    │
│  │   ├── MacCatalyst/                        Windows, iOS, Android)  │
│  │   └── Windows/                                                    │
│  ├── Resources/                              (icons, fonts)          │
│  │                                                                   │
│  ├── Views/                                                          │
│  │   ├── MainPage.xaml                       (file picker + chart    │
│  │   └── MainPage.xaml.cs                     + KPI binding)         │
│  │                                                                   │
│  ├── ViewModels/                                                     │
│  │   ├── DashboardViewModel.cs               (INotifyPropertyChanged │
│  │   │   ├── CurrentSnapshot (KPI source)     + commands)            │
│  │   │   ├── TpsSeries (ObservableCollection)                        │
│  │   │   ├── RttP95Series (ObservableCollection)                     │
│  │   │   ├── ConnectCommand                                          │
│  │   │   ├── DisconnectCommand                                       │
│  │   │   └── UseMockToggle                                           │
│  │   └── PollingState.cs                     (enum: Idle/Polling/    │
│  │                                            Disconnected/Error)    │
│  │                                                                   │
│  ├── Adapters/                                                       │
│  │   ├── IPollingAdapter.cs                  (interface)             │
│  │   ├── JsonlPollingAdapter.cs              (FileShare.ReadWrite,   │
│  │   │                                        1s Task.Delay)         │
│  │   └── MockPollingAdapter.cs               (in-memory sample)      │
│  │                                                                   │
│  └── FastPortDashboard.Maui.csproj           (MAUI + LiveCharts2     │
│                                                + tests-projects/     │
│                                                  LibTestTelemetry)   │
│                                                                      │
│  FastPortSharp.Dashboard.sln                  (new sln, root)        │
│   - FastPortDashboard.Maui                                           │
│   - tests-projects/LibTestTelemetry          (data contracts)        │
└──────────────────────────────────────────────────────────────────────┘
                              │
                              │ (test, 다음 cycle 후보)
                              ▼
┌──────────────────────────────────────────────────────────────────────┐
│  (Optional) FastPortDashboard.Maui.Tests/                            │
│   - JsonlPollingAdapter 단위 테스트                                  │
│   - DashboardViewModel KPI binding 테스트                            │
│   - (Foundation은 csproj 안 주석 처리하거나 별도 미니 프로젝트)        │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 3. Data Model

### 3.1 IPollingAdapter

```csharp
public interface IPollingAdapter
{
    /// <summary>
    /// 1초 단위로 ObservedMetricsSnapshot을 yield. cancellationToken으로 즉시 종료.
    /// </summary>
    IAsyncEnumerable<ObservedMetricsSnapshot> StreamAsync(CancellationToken ct);
}
```

### 3.2 JsonlPollingAdapter

```csharp
public sealed class JsonlPollingAdapter : IPollingAdapter
{
    private readonly string _path;
    private readonly TimeSpan _interval;

    public JsonlPollingAdapter(string path, TimeSpan? interval = null)
    {
        _path = path;
        _interval = interval ?? TimeSpan.FromSeconds(1);
    }

    public async IAsyncEnumerable<ObservedMetricsSnapshot> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        long lastReadOffset = 0;
        while (!ct.IsCancellationRequested)
        {
            // Lesson from fix-server-telemetry-export-jsonl-flush-flakiness:
            //   Reader는 FileShare.ReadWrite로 open해야 producer가 write 중이어도 read 가능.
            try
            {
                if (File.Exists(_path))
                {
                    using var fs = new FileStream(
                        _path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    fs.Seek(lastReadOffset, SeekOrigin.Begin);
                    using var sr = new StreamReader(fs);

                    string? line;
                    while ((line = await sr.ReadLineAsync(ct)) is not null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) { continue; }
                        ObservedMetricsSnapshot? snap = TryDeserialize(line);
                        if (snap is not null) { yield return snap; }
                    }
                    lastReadOffset = fs.Position;
                }
            }
            catch (IOException)
            {
                // 충돌/truncation: 다음 polling에서 재시도
            }

            await Task.Delay(_interval, ct);
        }
    }

    private static ObservedMetricsSnapshot? TryDeserialize(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<ObservedMetricsSnapshot>(
                line, ObservedMetricsJson.SerializerOptions);
        }
        catch (JsonException)
        {
            return null;  // partial / malformed line → skip
        }
    }
}
```

### 3.3 MockPollingAdapter

```csharp
public sealed class MockPollingAdapter : IPollingAdapter
{
    public async IAsyncEnumerable<ObservedMetricsSnapshot> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var rnd = new Random(seed: 42);
        long sentBytes = 0;
        long sentPackets = 0;
        int sessions = 0;
        while (!ct.IsCancellationRequested)
        {
            sentBytes += rnd.Next(1024, 8192);
            sentPackets += rnd.Next(10, 50);
            sessions = Math.Clamp(sessions + rnd.Next(-2, 3), 0, 100);

            yield return ObservedMetricsSnapshot.FromServer(
                new ServerObservedMetricsSnapshot(
                    Timestamp: DateTimeOffset.UtcNow,
                    TotalAcceptedSessions: (long)sessions,
                    /* ... 다른 필드 mock 값 ... */));

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }
}
```

> Foundation에서는 `MockPollingAdapter` 필드 일부만 채우고, dashboard도 그 일부만 표시. 나머지 KPI는 추후 cycle에서 채움.

### 3.4 DashboardViewModel

```csharp
public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private IPollingAdapter? _adapter;
    private CancellationTokenSource? _cts;

    public ObservableCollection<TimedDoublePoint> TpsSeries { get; } = new();
    public ObservableCollection<TimedDoublePoint> RttP95Series { get; } = new();

    private long _totalSessions;
    public long TotalSessions
    {
        get => _totalSessions;
        private set { if (_totalSessions != value) { _totalSessions = value; OnPropertyChanged(); } }
    }

    /* ... 다른 KPI 5-6개 동일 패턴 ... */

    private PollingState _state = PollingState.Idle;
    public PollingState State { get => _state; private set { ... } }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }

    public bool UseMock { get; set; }
    public string? FilePath { get; set; }

    public DashboardViewModel()
    {
        ConnectCommand = new Command(async () => await StartAsync(), () => State == PollingState.Idle);
        DisconnectCommand = new Command(() => Stop(), () => State == PollingState.Polling);
    }

    private async Task StartAsync()
    {
        _adapter = UseMock
            ? new MockPollingAdapter()
            : new JsonlPollingAdapter(FilePath ?? throw new InvalidOperationException("path required"));
        _cts = new CancellationTokenSource();
        State = PollingState.Polling;

        try
        {
            await foreach (var snap in _adapter.StreamAsync(_cts.Token))
            {
                ApplySnapshot(snap);
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            State = PollingState.Error;
            return;
        }

        State = PollingState.Disconnected;
    }

    private void ApplySnapshot(ObservedMetricsSnapshot snap)
    {
        var server = snap.ServerObserved;
        if (server is null) { return; }

        // KPI 갱신
        TotalSessions = server.TotalAcceptedSessions;
        // ... 다른 KPI

        // Chart 갱신 (UI thread로 dispatch는 chart binding이 자체 처리)
        TpsSeries.Add(new TimedDoublePoint(server.Timestamp, server.SentPacketsPerSecond));
        RttP95Series.Add(new TimedDoublePoint(server.Timestamp, server.RttP95Ms));

        // 최근 N개만 유지 (예: 600 = 10분치)
        const int MaxPoints = 600;
        while (TpsSeries.Count > MaxPoints) { TpsSeries.RemoveAt(0); }
        while (RttP95Series.Count > MaxPoints) { RttP95Series.RemoveAt(0); }
    }

    private void Stop()
    {
        _cts?.Cancel();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

### 3.5 PollingState

```csharp
public enum PollingState { Idle, Polling, Disconnected, Error }
```

---

## 4. API Specification

해당 없음 (외부 HTTP API 없음). dashboard 자체 contract:

| Surface | 위치 |
|---|---|
| `IPollingAdapter` | `Adapters/IPollingAdapter.cs` |
| `DashboardViewModel` | `ViewModels/DashboardViewModel.cs` |
| `PollingState` | `ViewModels/PollingState.cs` |
| `TimedDoublePoint` | `ViewModels/TimedDoublePoint.cs` (chart 좌표 record) |

---

## 5. UI/UX Design

### 5.1 MainPage 레이아웃 (rough sketch)

```
+-----------------------------------------------------------+
| [📁 Browse] /path/to/server.metrics.jsonl  [☑ Use Mock]  |
| [⚡ Connect] [⏹ Disconnect]              State: Polling   |
+-----------------------------------------------------------+
| KPI:                                                       |
|   Sessions: 42      SentBytes: 1.2MB     Pending: 3       |
|   SentPackets: 8.4K  RecvPackets: 7.9K   LastUpdate: 12:34|
+-----------------------------------------------------------+
|                                                            |
|   [Chart: TPS over last 10 min]                           |
|                                                            |
+-----------------------------------------------------------+
|                                                            |
|   [Chart: RTT P95 over last 10 min]                       |
|                                                            |
+-----------------------------------------------------------+
```

### 5.2 사용자 흐름

1. App 실행 → MainPage
2. Mock 토글 끄고 → Browse → JSONL 파일 선택
3. Connect 버튼
4. 1초마다 chart + KPI 갱신
5. Disconnect 또는 종료 → polling stop

### 5.3 에러 처리 UX

- File 없음 → "File not found: <path>" 빨간 라벨
- Polling 중 IOException 누적 → 경고 토스트 (10초마다 1회)
- JSON parse 실패 → silent skip (Foundation 한정, 향후 cycle에서 카운터 표시)

---

## 6. Error Handling

| 상황 | 처리 |
|---|---|
| File missing | adapter는 `File.Exists` 체크 후 skip + polling 계속. UI는 State="Polling" 유지하되 KPI 갱신 0. |
| `IOException` (sharing/truncation) | catch + 다음 polling 재시도 |
| `JsonException` (partial line) | TryDeserialize null 반환 → skip |
| `OperationCanceledException` | 정상 종료 (Disconnect command) |
| 기타 예외 | ViewModel.ErrorMessage 설정, State=Error |

---

## 7. Security Considerations

- File path는 사용자 local. 외부 URL X.
- JSONL은 텍스트 read-only. 실행 X.
- Mock data는 hard-coded random — 외부 입력 0.

---

## 8. Test Plan

### 8.1 Unit Tests (Foundation 1-2개)

| # | 테스트 | 위치 |
|---|---|---|
| 1 | `JsonlPollingAdapter`가 sample JSONL (3 lines)을 정확히 yield | `Adapters/JsonlPollingAdapterTests.cs` |
| 2 | `DashboardViewModel.ApplySnapshot`이 KPI + chart series를 갱신 | `ViewModels/DashboardViewModelTests.cs` |
| (선택) 3 | `MockPollingAdapter` 1초 간격 yield | 동일 |

### 8.2 Manual Verification

| # | 시나리오 |
|---|---|
| 1 | macOS Catalyst 빌드 + 실행 + Mock 토글 → chart 갱신 |
| 2 | `FastPortTestSmokeServer` 띄우고 → metrics file 지정 → live update |
| 3 | Disconnect 후 재연결 |
| 4 | File path 잘못 입력 → 에러 라벨 표시 |

### 8.3 회귀

| # | 검증 |
|---|---|
| 1 | `dotnet build FastPortSharp.sln -c Release` 0/0 |
| 2 | `dotnet test FastPortSharp.sln -c Release --no-build` 139/0/0 |
| 3 | `tests/scaffold/run.sh` 7/7 |

---

## 9. Clean Architecture (.NET 적용)

| 계층 | 위치 |
|---|---|
| **Presentation** (View) | `Views/MainPage.xaml` |
| **Application** (ViewModel) | `ViewModels/DashboardViewModel.cs` |
| **Domain** (data contract) | `tests-projects/LibTestTelemetry/ObservedMetrics.cs` (재사용) |
| **Infrastructure** (adapter) | `Adapters/JsonlPollingAdapter.cs`, `MockPollingAdapter.cs` |

ViewModel은 View / Adapter에 의존, Adapter는 Domain에 의존, View는 ViewModel binding만. Domain은 외부 의존 0.

---

## 10. Coding Convention Reference

### 10.1 File Layout

| 영역 | 위치 |
|---|---|
| Project | `FastPortDashboard.Maui/` |
| Sln | `FastPortSharp.Dashboard.sln` |
| Views | `FastPortDashboard.Maui/Views/` |
| ViewModels | `FastPortDashboard.Maui/ViewModels/` |
| Adapters | `FastPortDashboard.Maui/Adapters/` |
| Tests | `FastPortDashboard.Maui/Adapters/*.Tests.cs` (csproj 내부) — Foundation은 별도 test 프로젝트 미신설 |

### 10.2 Naming

- ViewModel: `<Area>ViewModel.cs` (PascalCase)
- Adapter: `<Source>PollingAdapter.cs`
- ObservableCollection 필드: 단수형 + `Series` suffix (`TpsSeries`)

### 10.3 한국어 주석

직전 cycles와 동일 — 의도 / 비-자명 lock semantics만 한국어 1-2 line.

---

## 11. Implementation Guide

### 11.1 File Structure (구체)

```
FastPortSharp/
├── FastPortDashboard.Maui/                    ← NEW
│   ├── FastPortDashboard.Maui.csproj
│   ├── App.xaml
│   ├── App.xaml.cs
│   ├── MauiProgram.cs
│   ├── Platforms/
│   │   ├── MacCatalyst/Program.cs
│   │   └── Windows/...
│   ├── Resources/
│   │   ├── AppIcon/
│   │   └── Fonts/
│   ├── Views/
│   │   ├── MainPage.xaml
│   │   └── MainPage.xaml.cs
│   ├── ViewModels/
│   │   ├── DashboardViewModel.cs
│   │   ├── DashboardViewModelTests.cs   (선택, Foundation 단순화)
│   │   ├── PollingState.cs
│   │   └── TimedDoublePoint.cs
│   ├── Adapters/
│   │   ├── IPollingAdapter.cs
│   │   ├── JsonlPollingAdapter.cs
│   │   ├── JsonlPollingAdapterTests.cs   (선택)
│   │   └── MockPollingAdapter.cs
│   └── README.md
│
├── FastPortSharp.Dashboard.sln                ← NEW (root)
│
├── README.md                                   ← MODIFY (폴더 구조 +1 line)
├── README.ko.md                                ← MODIFY (동일 한국어)
└── HANDOFF.md                                  ← MODIFY (Roadmap §4 status)
```

### 11.2 Implementation Order

| 순서 | 작업 | 검증 |
|---|---|---|
| 1 | `dotnet new maui` 또는 manual csproj/구조 생성, `FastPortDashboard.Maui.csproj` | `dotnet workload list`로 maui 확인 |
| 2 | `FastPortSharp.Dashboard.sln` 생성 + FastPortDashboard.Maui + tests-projects/LibTestTelemetry 추가 | `dotnet sln list` |
| 3 | LiveCharts2 NuGet 추가 (PackageReference) | `dotnet restore` |
| 4 | `IPollingAdapter` + `MockPollingAdapter` 구현 | unit test 1개 |
| 5 | `JsonlPollingAdapter` 구현 + sample JSONL로 검증 | unit test 1개 |
| 6 | `DashboardViewModel` + commands + KPI binding | unit test 1개 |
| 7 | `MainPage.xaml` + chart binding (LiveCharts2) | manual run, mock 모드로 chart 갱신 확인 |
| 8 | 실제 `FastPortTestSmokeServer` 띄워서 live update 검증 | manual |
| 9 | `FastPortDashboard.Maui/README.md` 작성 (빌드/실행/스크린샷) | |
| 10 | root README/README.ko/HANDOFF 갱신 (폴더 구조 + Roadmap status) | |
| 11 | 회귀: `dotnet build FastPortSharp.sln` / `dotnet test` / `tests/scaffold/run.sh` | 모두 PASS |
| 12 | commit + push | 단일 commit (직전 cycle 패턴) |

### 11.3 Session Guide

> 신규 프로젝트라 turn 비용 큼. 다중 세션 권장.

#### Module Map

| Module | Scope Key | Description | Estimated Turns |
|---|---|---|:-:|
| Project skeleton | `project-skeleton` | csproj + sln + MAUI structure + LiveCharts2 nuget + 빌드 sanity | 25-35 |
| Data adapter | `data-adapter` | IPollingAdapter + Mock + Jsonl + unit test | 20-30 |
| ViewModel | `viewmodel` | DashboardViewModel + KPI + commands + unit test | 20-30 |
| UI | `ui` | MainPage.xaml + binding + chart visual + manual mock 검증 | 25-35 |
| Live verify + docs | `live-verify-docs` | 실 server 검증 + README × 3 + commit | 12-18 |

#### Recommended Session Plan

| Session | Phase | Scope | Turns |
|---|---|---|:-:|
| 1 | Plan + Design | 전체 | 20 (already done) |
| 2 | Do | `--scope project-skeleton` | 25-35 |
| 3 | Do | `--scope data-adapter,viewmodel` | 40-60 |
| 4 | Do | `--scope ui` | 25-35 |
| 5 | Do | `--scope live-verify-docs` | 12-18 |
| 6 | Check + Report + Archive | 전체 | 20-25 |

> 시간 충분하면 Session 3-4를 한 번에 묶어도 OK. Foundation scope 협소화로 turn 절약 의도.

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-10 | Initial design — Option B MVVM, IPollingAdapter (Mock/Jsonl), LiveCharts2, Polling 1s | boinred |
