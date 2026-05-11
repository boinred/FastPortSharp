# dashboard-revert-skcanvasview-keep-data Design

> **Project**: FastPortSharp
> **Date**: 2026-05-11
> **Plan**: `docs/01-plan/features/dashboard-revert-skcanvasview-keep-data.plan.md`

---

## 1. Architecture: Option A — Direct revert (UI only)

| Option | Approach | 선택 |
|---|---|---|
| **A — Direct revert (UI only)** | xaml/xaml.cs만 P95 LineChart로 복귀, data layer 보존 | ✅ |
| B — Full cycle revert (`git revert cbca03b`) | TimedRttPoint + struct migration 다 되돌림 | ❌ data 가치 손실 |
| C — SKCanvasView를 Microcharts ChartView로 wrap (커스텀 chart class) | overengineering, crash 재현 risk | ❌ |

## 2. File Changes

### MainPage.xaml

```xml
<!-- Before (multi-rtt-overlay) -->
<skia:SKCanvasView x:Name="RttCanvasView" PaintSurface="OnRttCanvasPaintSurface" HeightRequest="160" />

<!-- After (revert) -->
<microcharts:ChartView x:Name="RttChartView" HeightRequest="160" />
```

Label "Client RTT P50/P95/P99 (ms)" → "Client RTT P95 (ms)" (안전한 단일 시각화).

xmlns:skia 보존 (향후 cycle에서 다시 사용 가능).

### MainPage.xaml.cs

- `OnRttCanvasPaintSurface` 메서드 제거 (~50 lines)
- `DrawLegend` 메서드 제거 (~25 lines)
- `using SkiaSharp.Views.Maui;` 보존 (다른 사용처 없으면 제거)
- `UpdateRttChart()` 복원:
  ```csharp
  private void UpdateRttChart()
  {
      var entries = _viewModel.ClientRttSeries
          .Select(p => new ChartEntry((float)p.P95Ms)  // ← TimedRttPoint.P95Ms 접근
          {
              Label = string.Empty,
              ValueLabel = ((int)p.P95Ms).ToString(),
              Color = RttP95Color,  // 또는 단일 RttLineColor
          })
          .ToArray();
      RttChartView.Chart = new LineChart { ... };
  }
  ```
- ctor에서 `RttCanvasView.InvalidateSurface()` → `UpdateRttChart()`
- `RttP50Color` / `RttP99Color` 상수 제거 (또는 보존 — 향후 활용)

## 3. Test Plan

| Level | Pass Criteria |
|---|---|
| Build | Dashboard sln 0/0 |
| Unit | 20/0/0 회귀 0 (T-VM-11/12 그대로) |
| Regression | FastPortSharp.sln 0/0 + 139/0/0 |
| Manual | Debug 실행 → crash 없음 → Mock Connect → P95 line 갱신 |
