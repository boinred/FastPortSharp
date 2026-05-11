# dashboard-rtt-chart Design

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **Plan**: `docs/01-plan/features/dashboard-rtt-chart.plan.md`

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | KPI 수치만으론 trend 파악 어려움. RTT는 게임서버 latency budget 핵심 지표. |
| **WHO** | boinred + 미래 contributor + 게임서버 운영자. |
| **RISK** | (R-1) Microcharts.Maui MAUI 10 호환 / (R-2) Mock Adapter ClientObserved 부재 / (R-3) macOS Catalyst rendering / (R-4) memory growth |
| **SUCCESS** | RTT line chart 렌더 + Mock 갱신 + 빌드 0/0 + test 회귀 0 + 회귀 sln 0 + 수동 chart 시각 확인 |
| **SCOPE** | `FastPortDashboard.Core` (ClientRtt series + Apply) + `FastPortDashboard.Maui` (Microcharts pkg + XAML) |

---

## 1. Overview

Microcharts.Maui v1.0.1 LineChart로 Client RTT P95Ms 시각화. ViewModel에 `ClientRttSeries` + `ApplyClientSnapshot` 추가. MockPollingAdapter는 ServerObserved + ClientObserved 모두 시뮬레이션. MainPage.xaml placeholder를 `<microcharts:ChartView>`로 교체.

---

## 2. Architecture Decision

### 2.1 Options Compared

| Option | Series 위치 | ViewModel ↔ Chart binding | 복잡도 |
|---|---|---|---|
| **A — Maui-only ChartView (선택)** | `Core` (TimedDoublePoint collection) + `Maui` 측 converter로 ChartView.Chart에 LineChart 인스턴스 binding | ViewModel은 TimedDoublePoint, Maui는 ChartEntry 변환 | Low |
| B — Core에 IChartEntry collection 직접 노출 | Core가 Microcharts SkiaSharpEntry로 직접 모델링 | Core ↔ Microcharts 의존 (transitive로 test도 끌어옴) | Medium |
| C — Wrapper service `IRttChartProvider` | DI service로 chart 데이터 변환 위임 | overengineering for single chart | High |

### 2.2 Selected: Option A — Maui-only ChartView

**선택 근거**:
- Microcharts 의존을 Maui csproj에 격리 → Core lib 의존성 ↓ + Test 영향 0
- ViewModel은 TimedDoublePoint (도메인 데이터) 유지, MAUI 측 IValueConverter로 ChartEntry 변환
- Plan 원칙 "Production 코드 변경 최소 + test 회귀 0" 준수

---

## 3. Detailed Design

### 3.1 NuGet Dependency

```xml
<!-- FastPortDashboard.Maui.csproj -->
<PackageReference Include="Microcharts.Maui" Version="1.0.1" />
```

→ SkiaSharp.Views.Maui.Controls transitive 포함.

### 3.2 MauiProgram.cs Update

```csharp
builder
    .UseMauiApp<App>()
    .UseMicrocharts()        // 추가: Microcharts handler 등록
    .ConfigureFonts(...);
```

(Microcharts.Maui 1.0.x: `UseMicrocharts()` extension method 제공.)

### 3.3 DashboardViewModel Extension

```csharp
// 신규: ClientRttSeries (P95Ms)
public ObservableCollection<TimedDoublePoint> ClientRttSeries { get; } = new();

public void ApplySnapshot(ObservedMetricsSnapshot snap)
{
    // 기존 ServerObserved 처리 (변경 0)
    var server = snap.ServerObserved;
    if (server is not null)
    {
        CurrentSessions = server.CurrentSessions;
        // ... (기존 매핑)
        double tsMs = server.Timestamp.ToUnixTimeMilliseconds();
        ThroughputSeries.Add(new TimedDoublePoint(tsMs, server.SentBytesPerSecond));
        while (ThroughputSeries.Count > MaxChartPoints) ThroughputSeries.RemoveAt(0);
    }

    // 신규: ClientObserved 분기
    var client = snap.ClientObserved;
    if (client is not null)
    {
        ApplyClientSnapshot(client);
    }
}

private void ApplyClientSnapshot(ClientObservedMetricsSnapshot client)
{
    double tsMs = client.Timestamp.ToUnixTimeMilliseconds();
    ClientRttSeries.Add(new TimedDoublePoint(tsMs, client.RttP95Ms));
    while (ClientRttSeries.Count > MaxChartPoints)
    {
        ClientRttSeries.RemoveAt(0);
    }
}
```

**Backward compat**: ClientObserved가 null이면 client 부분 skip → 기존 18 tests 회귀 0.

### 3.4 MockPollingAdapter Update — Combined Snapshot

```csharp
// 기존 yield ObservedMetricsSnapshot.FromServer(serverSnap)
// 변경 yield ObservedMetricsSnapshot.Combined(clientSnap, serverSnap)

var clientSnap = new ClientObservedMetricsSnapshot(
    Timestamp: now,
    TargetSessions: 100,
    CurrentSessions: (int)currentSessions,
    // ...
    RttAverageMs: 30 + _rng.NextDouble() * 20,        // 30~50ms random walk
    RttP50Ms: 25 + _rng.NextDouble() * 15,
    RttP95Ms: 50 + _rng.NextDouble() * 50,            // 50~100ms — 본 cycle 시각화 대상
    RttP99Ms: 100 + _rng.NextDouble() * 100,
    // 기타 필드 0
    ...);

yield return ObservedMetricsSnapshot.Combined(timestamp: now, clientSnap, serverSnap);
```

**Note**: `Combined` static factory signature는 LibTestTelemetry §1.1에서 확인 후 정확히 호출. 만약 signature가 `Combined(...)` 외 형태면 적절히 wrap.

### 3.5 MainPage.xaml ChartView

```xml
<!-- xmlns 추가 -->
xmlns:microcharts="clr-namespace:Microcharts.Maui;assembly=Microcharts.Maui"

<!-- Frame 내부 -->
<Frame Padding="12" CornerRadius="8" BorderColor="Gray" HeightRequest="200">
    <VerticalStackLayout>
        <Label Text="Client RTT P95 (ms)" FontAttributes="Bold" />
        <microcharts:ChartView x:Name="RttChartView" HeightRequest="160" />
    </VerticalStackLayout>
</Frame>
```

### 3.6 ChartView Data Binding (Code-behind)

ViewModel ↔ Microcharts 변환은 code-behind에서 `CollectionChanged` 구독:

```csharp
// MainPage.xaml.cs constructor (BindingContext 설정 후)
_viewModel.ClientRttSeries.CollectionChanged += (_, _) => UpdateChart();
UpdateChart();

private void UpdateChart()
{
    var entries = _viewModel.ClientRttSeries
        .Select(p => new ChartEntry((float)p.Value)
        {
            Label = string.Empty,
            ValueLabel = ((int)p.Value).ToString(),
            Color = SKColor.Parse("#2196F3"),
        })
        .ToArray();

    RttChartView.Chart = new LineChart
    {
        Entries = entries,
        LineMode = LineMode.Straight,
        LineSize = 2,
        PointMode = PointMode.None,
        BackgroundColor = SKColors.Transparent,
    };
}
```

**Threading**: ApplySnapshot은 UI thread에서 호출됨 (기존 contract). CollectionChanged event도 UI thread에서 fire → UpdateChart 안전.

**Why code-behind vs XAML binding**: Microcharts ChartView.Chart는 IChart 객체 binding. ViewModel이 IChart 직접 만들면 SkiaSharp 의존이 Core로 새 들어옴 (Option B와 동일). Code-behind 변환이 격리 유지.

### 3.7 File-Level Changes

| 파일 | 작업 | 예상 라인 |
|---|---|---|
| `FastPortDashboard.Maui.csproj` | +1 PackageReference | +1 |
| `FastPortDashboard.Maui/MauiProgram.cs` | +UseMicrocharts() | +1 |
| `FastPortDashboard.Maui/MainPage.xaml` | xmlns + Chart placeholder 교체 | ±10 |
| `FastPortDashboard.Maui/MainPage.xaml.cs` | CollectionChanged + UpdateChart | +35 |
| `FastPortDashboard.Core/ViewModels/DashboardViewModel.cs` | +ClientRttSeries +ApplyClientSnapshot + ApplySnapshot 분기 | +15 |
| `FastPortDashboard.Core/Adapters/MockPollingAdapter.cs` | Combined snapshot | ±30 |
| `tests-projects/FastPortDashboardTests/ViewModels/DashboardViewModelTests.cs` | +2 tests | +35 |

총 7 파일, ~125 lines.

---

## 4. Risks and Mitigation

| Risk | Mitigation |
|---|---|
| (R-1) Microcharts.Maui 1.0.1 + MAUI 10 호환 미검증 | 1.0.0 stable + 1.0.1 latest, MAUI 10 명시 호환 표기. 첫 빌드에서 검증. 안 되면 0.9.5.9 fallback. |
| (R-2) Mock Combined 시 기존 18 tests 회귀 | T-MA-1/2/3 (Mock test)은 ServerObserved.* 만 확인. Combined snapshot에도 ServerObserved 그대로 포함 → pass 예상. |
| (R-3) macOS Catalyst rendering crash | Microcharts SkiaSharp는 SwiftUI bridging 안 함. LiveCharts2 history 무관. |
| (R-4) ClientRttSeries 600 trim 검증 | 신규 test T-VM-12로 검증. |
| (R-5) `Combined` factory signature 미확인 | LibTestTelemetry §1.1에서 정확히 확인 후 호출. |

---

## 5. Implementation Guide

### 5.1 Implementation Order

1. NuGet 추가 (`FastPortDashboard.Maui.csproj`)
2. `MauiProgram.cs`에 `.UseMicrocharts()` 추가
3. `DashboardViewModel.cs`에 `ClientRttSeries` + `ApplyClientSnapshot` + 분기 추가
4. `MockPollingAdapter.cs`에 ClientObserved 시뮬레이션 + `Combined` yield
5. `MainPage.xaml` xmlns + Chart 교체
6. `MainPage.xaml.cs`에 CollectionChanged + UpdateChart
7. `DashboardViewModelTests.cs`에 신규 2 tests:
   - T-VM-11 `ApplySnapshot_ClientObserved_AppendsRttSeries`
   - T-VM-12 `ApplyClientSnapshot_TrimsAt600`
8. Dashboard sln 빌드 0/0 + test ≥ 20
9. 회귀 sln 빌드 0/0 + 139 tests
10. (수동) macOS Catalyst Release 실행 → Mock Connect → chart 시각
11. 단일 commit

### 5.2 Session Guide (Module Map)

| Module Key | Description | Turns |
|---|---|---|
| `module-1-pkg` | NuGet + MauiProgram | 1 |
| `module-2-vm` | ViewModel ClientRttSeries + Apply | 2 |
| `module-3-mock` | MockPollingAdapter Combined | 2 |
| `module-4-xaml` | XAML + code-behind chart | 2-3 |
| `module-5-test` | DashboardViewModelTests +2 | 2 |
| `module-6-verify` | Build + test 양쪽 sln | 2 |

**Recommended**: 한 세션 ≤ 15 turn.

---

## 6. Test Plan

| Level | Test | Pass Criteria |
|---|---|---|
| Build | `dotnet build FastPortSharp.Dashboard.sln -c Release` | 0 errors |
| Unit | `dotnet test FastPortSharp.Dashboard.sln -c Release` | ≥ 20 passed (기존 18 + 신규 2) |
| Regression Build | `dotnet build FastPortSharp.sln -c Release` | 0 errors |
| Regression Test | `dotnet test FastPortSharp.sln -c Release` | 139/0/0 |
| Manual | macOS Catalyst app + Mock Connect | RTT chart line이 1초 간격 갱신 |

---

## 7. Out of Scope

- Server throughput chart visual (별도 cycle)
- RTT P50/P99 추가 series
- Chart interaction (zoom, pan, tooltip)
- LiveCharts2 재도입
- Production 코드 (LibTestTelemetry 등) 변경
- iOS/Android TFM

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial design (Option A — Maui-only ChartView, Microcharts.Maui 1.0.1, code-behind binding) | boinred |
