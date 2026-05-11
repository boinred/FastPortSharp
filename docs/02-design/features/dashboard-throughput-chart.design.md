# dashboard-throughput-chart Design

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **Plan**: `docs/01-plan/features/dashboard-throughput-chart.plan.md`

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Server bandwidth(throughput) 시각화로 latency + bandwidth 동시 관찰. |
| **WHO** | boinred + 미래 contributor + 게임서버 운영자. |
| **RISK** | (R-1) 두 ChartView 동시 rendering / (R-2) ThroughputSeries.Count placeholder 표시 정리 |
| **SUCCESS** | Throughput LineChart 렌더 + RTT 무영향 + 빌드 0/0 + 20 tests 회귀 0 + 회귀 sln 0 |
| **SCOPE** | `FastPortDashboard.Maui` only (MainPage.xaml + MainPage.xaml.cs) |

---

## 1. Overview

RTT chart 아래에 Throughput LineChart Frame을 수직 스택 추가. RTT cycle의 code-behind 패턴(`UpdateRttChart` → `UpdateThroughputChart` mirror)을 그대로 재사용. ViewModel/Core/Tests 변경 0.

---

## 2. Architecture Decision

### 2.1 Options Compared

| Option | Method 구조 | LOC | 일관성 | 선택 |
|---|---|---|---|---|
| **A — Mirror method (선택)** | `UpdateRttChart()` + `UpdateThroughputChart()` 별도 method, 각자 own series + color | ~25 | 직전 cycle 패턴 그대로 | ✅ |
| B — Generic helper | `UpdateChart(IEnumerable<TimedDoublePoint>, ChartView, SKColor)` 통합 | ~20 | 추상화 ↑, 첫 chart cycle엔 unfit | ❌ |
| C — Chart Manager class | `ChartUpdater` DI service | ~50 | overengineering | ❌ |

### 2.2 Selected: Option A — Mirror method

**선택 근거**:
- 직전 RTT cycle과 동일 패턴 → 코드 일관성 ↑
- Option B(generic)는 chart 3개 이상부터 가치, 현재는 premature abstraction
- `UpdateThroughputChart()`는 `UpdateRttChart()` 거의 copy로 명확함

---

## 3. Detailed Design

### 3.1 MainPage.xaml Diff

```xml
<!-- RTT chart Frame 다음에 -->
<Frame Padding="12" CornerRadius="8" BorderColor="Gray" HeightRequest="220">
    <VerticalStackLayout>
        <Label Text="Server Throughput (B/s)" FontAttributes="Bold" />
        <Label Text="{Binding ThroughputSeries.Count, StringFormat='최근 sample 수: {0}'}"
               FontSize="11" TextColor="Gray" />
        <microcharts:ChartView x:Name="ThroughputChartView" HeightRequest="160" />
    </VerticalStackLayout>
</Frame>
```

(RTT Frame과 동일 구조, x:Name 다름)

### 3.2 MainPage.xaml.cs Diff

```csharp
private static readonly SKColor RttLineColor = SKColor.Parse("#2196F3");
private static readonly SKColor ThroughputLineColor = SKColor.Parse("#4CAF50");  // green

public MainPage()
{
    InitializeComponent();
    _viewModel = new DashboardViewModel();
    BindingContext = _viewModel;

    _viewModel.ClientRttSeries.CollectionChanged += (_, _) => UpdateRttChart();
    _viewModel.ThroughputSeries.CollectionChanged += (_, _) => UpdateThroughputChart();  // 추가
    UpdateRttChart();
    UpdateThroughputChart();  // 추가
}

private void UpdateRttChart() { /* unchanged */ }

// 신규 (UpdateRttChart mirror)
private void UpdateThroughputChart()
{
    var entries = _viewModel.ThroughputSeries
        .Select(p => new ChartEntry((float)p.Value)
        {
            Label = string.Empty,
            ValueLabel = ((long)p.Value).ToString(),
            Color = ThroughputLineColor,
        })
        .ToArray();

    ThroughputChartView.Chart = new LineChart
    {
        Entries = entries,
        LineMode = LineMode.Straight,
        LineSize = 2,
        PointMode = PointMode.None,
        BackgroundColor = SKColors.Transparent,
    };
}
```

### 3.3 File-Level Changes

| 파일 | 작업 | 예상 라인 |
|---|---|---|
| `FastPortDashboard.Maui/MainPage.xaml` | +1 Frame | +9 |
| `FastPortDashboard.Maui/MainPage.xaml.cs` | +ThroughputLineColor + subscribe + UpdateThroughputChart | +22 |

총 2 파일, ~31 lines.

---

## 4. Risks and Mitigation

| Risk | Mitigation |
|---|---|
| (R-1) 두 ChartView 동시 rendering | Microcharts.Maui multi-instance 표준 지원. 첫 빌드 + 수동 검증. |
| (R-2) `ThroughputSeries.Count` 표시 중복 | 부제 label로 trend 보조 정보 유지 (사용자가 sample 수 통해 chart 신뢰도 판단). |
| (R-3) ThroughputLineColor 시각 구분 | RTT 파란색(`#2196F3`) vs Throughput 녹색(`#4CAF50`) → Material Design 보색 |

---

## 5. Implementation Guide

### 5.1 Implementation Order

1. `MainPage.xaml`: RTT Frame 다음에 Throughput Frame 추가
2. `MainPage.xaml.cs`:
   - `ThroughputLineColor` 상수 추가
   - constructor에 ThroughputSeries.CollectionChanged subscribe + UpdateThroughputChart() 초기 호출
   - `UpdateThroughputChart()` method 추가
3. Dashboard 빌드 0/0
4. Dashboard test 20/0/0 (회귀 0)
5. 회귀 sln + 139 tests
6. (수동) macOS Catalyst Release Mock Connect → RTT + Throughput chart 동시 갱신
7. 단일 commit

### 5.2 Session Plan

총 ≤ 8 turn 예상. `--scope` 분할 불필요.

---

## 6. Test Plan

| Level | Test | Pass Criteria |
|---|---|---|
| Build | Dashboard sln Release | 0 errors |
| Unit | Dashboard test | 20/0/0 회귀 0 |
| Regression Build | FastPortSharp.sln Release | 0 errors |
| Regression Test | 139 tests | 139/0/0 |
| Manual | macOS Catalyst Mock Connect | 두 chart 동시 갱신 |

---

## 7. Out of Scope

- ViewModel/Adapter 변경
- 신규 test
- Chart interaction
- LiveCharts2 재도입
- Multi-axis chart
- Y축 단위 변환 (raw B/s)

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial design (Option A — mirror method, ~31 lines, 2 files) | boinred |
