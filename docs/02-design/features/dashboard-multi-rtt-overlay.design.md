# dashboard-multi-rtt-overlay Design

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **Plan**: `docs/01-plan/features/dashboard-multi-rtt-overlay.plan.md`

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Tail latency (P95/P99) 분석은 게임서버 핵심. 단일 P95만으론 distribution 모름. |
| **WHO** | boinred + 미래 contributor + 게임서버 운영자. |
| **RISK** | (R-1) custom Skia 코드 / (R-2) 좌표 변환 / (R-3) test 회귀 / (R-4) Y축 자동 스케일 |
| **SUCCESS** | 3 line overlay + Mock 갱신 + 빌드 0/0 + 20 tests 회귀 0 + 회귀 sln 0 |
| **SCOPE** | Core (TimedRttPoint + ViewModel) + Maui (XAML + code-behind) + Tests 갱신 |

---

## 1. Overview

RTT chart `microcharts:ChartView` → `skia:SKCanvasView` 교체. `TimedRttPoint(ts, p50, p95, p99)` 신규 struct로 ViewModel 단일 collection. Code-behind에서 SkiaSharp 직접 draw — 3 line (P50/P95/P99) color-coded + legend.

---

## 2. Architecture Decision

### 2.1 Options Compared

| Option | Approach | LOC | 선택 |
|---|---|---|---|
| **A — Custom SKCanvasView (선택)** | XAML에 `SKCanvasView` + PaintSurface 핸들러로 3 line 직접 draw | ~80 | ✅ |
| B — 3 stacked Microcharts | Frame × 3, 각자 P50/P95/P99 LineChart | ~50 | ❌ overlay 아님 |
| C — 별도 `MultiLineChartView` UserControl | XAML UserControl 추출 | ~120 | ❌ overengineering |

### 2.2 Selected: Option A — Custom SKCanvasView

**선택 근거**:
- "Overlay" 의미 충족 (3 line 한 chart에 겹침)
- LiveCharts2 (crash) 회피
- Code-behind 직접 draw → ViewModel/Core 의존성 ↑ 없이 MAUI 측만 처리

---

## 3. Detailed Design

### 3.1 TimedRttPoint Struct (신규)

```csharp
// FastPortDashboard.Core/ViewModels/TimedRttPoint.cs
namespace FastPortDashboard.Maui.ViewModels;

// Design Ref: §3.1 (dashboard-multi-rtt-overlay) — RTT 3 percentile 묶음.
public readonly record struct TimedRttPoint(
    double TimestampUnixMs,
    double P50Ms,
    double P95Ms,
    double P99Ms);
```

### 3.2 DashboardViewModel 변경

```csharp
// Before
public ObservableCollection<TimedDoublePoint> ClientRttSeries { get; } = new();

// After
public ObservableCollection<TimedRttPoint> ClientRttSeries { get; } = new();

private void ApplyClientSnapshot(ClientObservedMetricsSnapshot client)
{
    double tsMs = client.Timestamp.ToUnixTimeMilliseconds();
    ClientRttSeries.Add(new TimedRttPoint(tsMs, client.RttP50Ms, client.RttP95Ms, client.RttP99Ms));
    while (ClientRttSeries.Count > MaxChartPoints)
    {
        ClientRttSeries.RemoveAt(0);
    }
}
```

`TimedDoublePoint`은 `ThroughputSeries`가 그대로 사용 → 보존.

### 3.3 MainPage.xaml 변경

```xml
<!-- xmlns 추가 -->
xmlns:skia="clr-namespace:SkiaSharp.Views.Maui.Controls;assembly=SkiaSharp.Views.Maui.Controls"

<!-- RTT Frame 내부 -->
<Frame Padding="12" CornerRadius="8" BorderColor="Gray" HeightRequest="220">
    <VerticalStackLayout>
        <Label Text="Client RTT P50/P95/P99 (ms)" FontAttributes="Bold" />
        <Label Text="{Binding ClientRttSeries.Count, StringFormat='최근 sample 수: {0}'}"
               FontSize="11" TextColor="Gray" />
        <skia:SKCanvasView x:Name="RttCanvasView"
                           PaintSurface="OnRttCanvasPaintSurface"
                           HeightRequest="160" />
    </VerticalStackLayout>
</Frame>
```

기존 `microcharts:ChartView x:Name="RttChartView"` → 위로 교체.

### 3.4 MainPage.xaml.cs 변경 (custom Skia drawing)

```csharp
// 상수
private static readonly SKColor RttP50Color = SKColor.Parse("#2196F3");   // blue
private static readonly SKColor RttP95Color = SKColor.Parse("#FF9800");   // orange
private static readonly SKColor RttP99Color = SKColor.Parse("#F44336");   // red

public MainPage()
{
    InitializeComponent();
    _viewModel = new DashboardViewModel();
    BindingContext = _viewModel;

    // RTT: SKCanvasView invalidate
    _viewModel.ClientRttSeries.CollectionChanged += (_, _) => RttCanvasView.InvalidateSurface();
    // Throughput: 기존 패턴 유지
    _viewModel.ThroughputSeries.CollectionChanged += (_, _) => UpdateThroughputChart();
    UpdateThroughputChart();
}

private void OnRttCanvasPaintSurface(object? sender, SkiaSharp.Views.Maui.SKPaintSurfaceEventArgs e)
{
    var canvas = e.Surface.Canvas;
    var info = e.Info;
    canvas.Clear(SKColors.Transparent);

    var series = _viewModel.ClientRttSeries;
    if (series.Count < 2) return;  // 최소 2개 point 필요

    // Y축 max — P99 series 기준 (가장 위 line)
    float maxY = 0f;
    foreach (var p in series)
    {
        if (p.P99Ms > maxY) maxY = (float)p.P99Ms;
    }
    if (maxY <= 0f) maxY = 1f;  // div-by-zero guard
    maxY *= 1.1f;  // 10% 상단 여유

    // Layout: padding + chart area
    const float padLeft = 8f;
    const float padRight = 80f;  // legend 영역
    const float padTop = 8f;
    const float padBottom = 8f;
    float chartW = info.Width - padLeft - padRight;
    float chartH = info.Height - padTop - padBottom;
    if (chartW <= 0 || chartH <= 0) return;

    // X 좌표: index 기반 선형 분배
    float xStep = chartW / Math.Max(1, series.Count - 1);

    using var paint = new SKPaint
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 2f,
    };

    // 3 line draw helper (closure로 series + paint 참조)
    void DrawLine(Func<TimedRttPoint, double> getValue, SKColor color)
    {
        paint.Color = color;
        using var path = new SKPath();
        for (int i = 0; i < series.Count; i++)
        {
            float x = padLeft + i * xStep;
            float y = padTop + chartH - (float)(getValue(series[i]) / maxY) * chartH;
            if (i == 0) path.MoveTo(x, y);
            else path.LineTo(x, y);
        }
        canvas.DrawPath(path, paint);
    }

    DrawLine(p => p.P50Ms, RttP50Color);
    DrawLine(p => p.P95Ms, RttP95Color);
    DrawLine(p => p.P99Ms, RttP99Color);

    // Legend (우상단)
    DrawLegend(canvas, info);
}

private static void DrawLegend(SKCanvas canvas, SKImageInfo info)
{
    float legendX = info.Width - 76f;
    float legendY = 4f;
    float boxSize = 10f;
    float rowH = 16f;

    using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
    using var textPaint = new SKPaint
    {
        IsAntialias = true,
        Color = SKColors.Black,
        TextSize = 11f,
    };

    void Row(int idx, SKColor color, string label)
    {
        float y = legendY + idx * rowH;
        fillPaint.Color = color;
        canvas.DrawRect(legendX, y, boxSize, boxSize, fillPaint);
        canvas.DrawText(label, legendX + boxSize + 4f, y + boxSize - 1f, textPaint);
    }

    Row(0, RttP50Color, "P50");
    Row(1, RttP95Color, "P95");
    Row(2, RttP99Color, "P99");
}
```

기존 `UpdateRttChart()` 메서드 제거. `RttChartView`는 SKCanvasView로 교체되어 더 이상 존재 안 함.

### 3.5 Tests 갱신

```csharp
// T-VM-11 (After)
[TestMethod]
public void ApplySnapshot_ClientObserved_AppendsRttSeries()
{
    var vm = new DashboardViewModel();
    var combined = ObservedMetricsSnapshot.Combined(
        MakeClientSnap(rttP95Ms: 87.5),  // helper 갱신 — P50/P99도 명시
        MakeServerSnap());

    vm.ApplySnapshot(combined);

    Assert.AreEqual(1, vm.ClientRttSeries.Count);
    Assert.AreEqual(30, vm.ClientRttSeries[0].P50Ms);   // MakeClientSnap default
    Assert.AreEqual(87.5, vm.ClientRttSeries[0].P95Ms);
    Assert.AreEqual(150, vm.ClientRttSeries[0].P99Ms);
}

// T-VM-12 (After)
[TestMethod]
public void ApplyClientSnapshot_TrimsRttSeriesAt600()
{
    // ... 601 snapshot 추가 ...
    Assert.AreEqual(600, vm.ClientRttSeries.Count);
    Assert.AreEqual((double)1, vm.ClientRttSeries[0].P95Ms);
    Assert.AreEqual((double)600, vm.ClientRttSeries[^1].P95Ms);
}
```

### 3.6 File-Level Changes

| 파일 | 작업 | 예상 라인 |
|---|---|---|
| `FastPortDashboard.Core/ViewModels/TimedRttPoint.cs` | new | ~6 |
| `FastPortDashboard.Core/ViewModels/DashboardViewModel.cs` | edit (collection type + ApplyClient) | ±5 |
| `FastPortDashboard.Maui/MainPage.xaml` | edit (xmlns + ChartView → SKCanvasView) | ±5 |
| `FastPortDashboard.Maui/MainPage.xaml.cs` | edit (UpdateRttChart → OnRttCanvasPaintSurface + DrawLegend) | ±70 (-15 + 85) |
| `tests-projects/FastPortDashboardTests/ViewModels/DashboardViewModelTests.cs` | edit (T-VM-11/12 + helper) | ±15 |

총 5 파일, ~100 lines net.

---

## 4. Risks and Mitigation

| Risk | Mitigation |
|---|---|
| (R-1) Custom Skia 코드 시간 | ~80 lines, P95 LineChart 코드 패턴 변형 |
| (R-2) 좌표 변환 정확성 | `padTop + chartH - (value/maxY) * chartH` 표준 식. 수동 시각 검증. |
| (R-3) Test 회귀 | T-VM-11/12 명시적 갱신, helper도 P50/P99 명시. |
| (R-4) Y축 스케일 적정성 | `max(P99) × 1.1` (10% 여유). P50/P95 모두 항상 visible. |
| (R-5) Legend 영역 부족 | `padRight=80f` 확보. |
| (R-6) macOS Release crash | Memory: maccatalyst-26-swiftui-observation-release-crash. Debug 빌드로만 수동 검증. |

---

## 5. Implementation Guide

### 5.1 Implementation Order

1. `TimedRttPoint.cs` 신규
2. `DashboardViewModel.cs`: ClientRttSeries 타입 변경 + ApplyClientSnapshot 갱신
3. `MainPage.xaml`: xmlns:skia 추가 + ChartView → SKCanvasView
4. `MainPage.xaml.cs`: RttLineColor 상수 제거 + RttP50/P95/P99Color 추가, UpdateRttChart 제거, OnRttCanvasPaintSurface + DrawLegend 추가, ctor에서 RttCanvasView.InvalidateSurface 구독
5. `DashboardViewModelTests.cs`: T-VM-11/T-VM-12 + MakeClientSnap helper 갱신
6. Dashboard 빌드 0/0
7. Dashboard test 20/0/0
8. FastPortSharp.sln 회귀 0
9. (수동) macOS Catalyst Debug 실행 → Mock Connect → 3 line overlay 갱신 확인
10. 단일 commit

### 5.2 Session Plan

총 ≤ 12 turn 예상.

---

## 6. Test Plan

| Level | Test | Pass Criteria |
|---|---|---|
| Build | Dashboard sln Release | 0 errors |
| Unit | Dashboard test (T-VM-1~10/13~20 + T-VM-11/12 갱신) | 20/0/0 |
| Regression Build | FastPortSharp.sln Release | 0 errors |
| Regression Test | 139 tests | 139/0/0 |
| Manual | macOS Catalyst Debug + Mock Connect | 3 color line overlay 갱신 (P50 blue, P95 orange, P99 red) + 우상단 legend |

---

## 7. Out of Scope

- Tooltip / zoom / pan
- Y축 라벨 / 그리드
- Throughput chart 변경
- RttAverageMs series 추가
- LiveCharts2 재도입

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial design (Option A — Custom SKCanvasView + 3-line direct Skia draw, ~100 lines) | boinred |
