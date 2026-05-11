---
template: design
version: 1.3
feature: dashboard-chart-multi-rtt-overlay-v2
date: 2026-05-11
author: boinred
project: FastPortSharp
---

# dashboard-chart-multi-rtt-overlay-v2 Design Document

> **Summary**: SkiaSharp 없이 P50/P95/P99 multi-line overlay를 새 `MultiLineChartDrawable` + `LineChartSeries` 로 복원.
>
> **Project**: FastPortSharp
> **Version**: 0.1.0
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **Planning Doc**: [dashboard-chart-multi-rtt-overlay-v2.plan.md](../../01-plan/features/dashboard-chart-multi-rtt-overlay-v2.plan.md)

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | percentile 분포 가시성은 latency 회귀 진단에 핵심. 직전 cycle의 P95-only 축소는 임시 trade-off. |
| **WHO** | macOS Catalyst + Windows 개발자, percentile 비교로 tail latency 회귀 감지. |
| **RISK** | multi-series drawable이 Observation crash 재유발 가능성 (낮음), 색상/legend 가독성. |
| **SUCCESS** | RTT 3-line + Throughput parity + 32+ tests green + macOS Catalyst Debug 무 crash + SkiaSharp 잔재 0. |
| **SCOPE** | RTT 차트 한정 multi-line. Hover/tooltip/grid 라인은 OOS. |

---

## 1. Overview

### 1.1 Design Goals

- 직전 cycle의 single-series `LineChartDrawable` 동작을 손대지 않고 (Throughput 회귀 0) RTT 차트만 multi-line으로 확장.
- 시각적 명확함: 3-line이 동일 Y축 좌표계, 우상단 legend로 색상-라벨 매핑.
- 순수 함수(통합 min/max 계산)는 Core에 위치 → net10.0 단위 테스트 가능.
- 빈/단일/부분 시리즈 입력에 예외 없이 처리.

### 1.2 Design Principles

- **단일 drawable이 모든 series를 같은 좌표계로 그린다** — 개별 drawable overlay는 ComputeRange가 분리되어 시각이 misleading.
- **Series는 immutable record** — 매 update마다 새 list 할당.
- **Single-series 동작 보존** — `LineChartDrawable`은 손대지 않고 `MultiLineChartDrawable`을 신규 추가.

---

## 2. Architecture Options

### 2.0 Architecture Comparison

| Criteria | Option A: 기존 확장 | Option B: 분리 신규 (Recommended) | Option C: Strategy 패턴 |
|----------|:-:|:-:|:-:|
| **Approach** | `LineChartDrawable`에 `IReadOnlyList<LineChartSeries>` property 추가, single/multi 모드 분기 | 신규 `MultiLineChartDrawable` + `LineChartSeries` record, 기존 `LineChartDrawable` 무변경 | 추상 `ChartRenderStrategy` + `SingleLineStrategy`/`MultiLineStrategy` |
| **New Files** | 1 (`LineChartSeries.cs`) | 2 (`MultiLineChartDrawable.cs`, `LineChartSeries.cs`) | 4 (interface + 2 impl + record) |
| **Modified Files** | 3 (csproj? no, drawable, math, xaml.cs) | 2 (math 확장, xaml.cs) | 3+ |
| **Throughput 회귀 위험** | Medium (단일 drawable 내부 분기 → 회귀 가능) | **None** (기존 클래스 0 줄 변경) | Medium (기존 drawable 교체) |
| **복잡도** | Medium | Low | High |
| **단위 테스트** | 가능 | 쉬움 | 가능 |
| **Recommendation** | 단순화 욕심 | **본 cycle 회귀 위험 최소, 코드 양 적정** | over-engineering |

**Selected**: **Option B — 분리 신규 `MultiLineChartDrawable`**

**Rationale**: 직전 cycle의 single-series `LineChartDrawable`은 Throughput 차트에서 잘 동작 중이고, Plan의 FR-05 "Throughput 변경 0" 요구를 절대적으로 보장하려면 그 클래스를 건드리지 않는 것이 가장 안전하다. multi-line은 본질적으로 입력 모델(series list)과 좌표계 계산(통합 min/max)이 single-line과 다르므로 분기보다는 별 클래스가 더 명확. 코드 중복은 거의 없음 — math 헬퍼는 공유.

### 2.1 Component Diagram

```
┌────────────────────────────────────────┐
│ DashboardViewModel                     │  (불변)
│  ClientRttSeries: TimedRttPoint        │   (P50/P95/P99 모두 보존)
│  ThroughputSeries                      │
└────────────┬───────────────────────────┘
             │ CollectionChanged
             ▼
┌────────────────────────────────────────┐
│ MainPage.xaml.cs                       │
│  UpdateRttChart  → 3 LineChartSeries   │
│                      snapshot          │
│  UpdateThroughputChart → 1 list        │  (변경 0)
└──┬──────────────────────────────────┬──┘
   │                                  │
   ▼                                  ▼
┌──────────────────────┐    ┌──────────────────────┐
│ MultiLineChartDrawable│    │ LineChartDrawable    │
│  (NEW)                │    │  (불변 — 직전 cycle) │
│  Series: List<Series> │    │  Values: List<double>│
│  Draw: 통합 좌표계     │    └──────────┬───────────┘
└──────────┬───────────┘                │
           │                            │
           ▼                            ▼
   RttChartView                  ThroughputChartView
   (GraphicsView)                (GraphicsView)
```

### 2.2 Data Flow

```
ClientRttSeries 변경
  → MainPage.UpdateRttChart()
  → 3 series snapshot 생성:
      LineChartSeries(BlueColor, P50List, "P50")
      LineChartSeries(OrangeColor, P95List, "P95")
      LineChartSeries(RedColor, P99List, "P99")
  → _rttMultiDrawable.Series = [...]
  → RttChartView.Invalidate()
  → MultiLineChartDrawable.Draw(canvas, rect):
      통합 min/max 계산 (LineChartMath.ComputeRangeMulti)
      각 series에 대해 stepX 계산 + path 그리기
      우상단 legend 그리기
```

### 2.3 Dependencies

| Component | Depends On |
|-----------|-----------|
| `MultiLineChartDrawable` | `Microsoft.Maui.Graphics`, `FastPortDashboard.Core.Charts.LineChartMath` |
| `LineChartSeries` | `Microsoft.Maui.Graphics.Color` |
| `MainPage.xaml.cs` | 위 두 신규 + ViewModel + 기존 `LineChartDrawable` |

---

## 3. Data Model

### 3.1 `LineChartSeries` (신규 record)

```csharp
namespace FastPortDashboard.Maui.Views;

public sealed record LineChartSeries(
    Color LineColor,
    IReadOnlyList<double> Values,
    string Label,
    float LineWidth = 2f);
```

설계 의도:
- `record` — value equality, immutable.
- `Label`은 legend에서 사용 ("P50", "P95", "P99").
- `LineWidth` 기본 2 (직전 single과 동일).

### 3.2 `MultiLineChartDrawable` (신규)

```csharp
public sealed class MultiLineChartDrawable : IDrawable
{
    public IReadOnlyList<LineChartSeries> Series { get; set; } = Array.Empty<LineChartSeries>();
    public bool ShowLegend { get; set; } = true;
    public string ValueFormat { get; set; } = "F0";

    public void Draw(ICanvas canvas, RectF dirtyRect) { ... }
}
```

### 3.3 `LineChartMath.ComputeRangeMulti` (Core 확장)

```csharp
public static (double Min, double Max) ComputeRangeMulti(IEnumerable<IReadOnlyList<double>> seriesList);
```

빈 입력/모두 빈 series → `(0, 1)`. 동일 값 → padding.

---

## 4. API Specification

해당 없음 (UI cycle).

---

## 5. UI/UX Design

### 5.1 Screen Layout

기존 layout 그대로. RTT 차트 frame 내부의 GraphicsView가 multi-line drawable로 교체될 뿐.

### 5.2 Legend Layout

```
┌────────────────────────────── RTT Chart (160h) ──────────────────────────────┐
│                                                            ━ P50 ━ P95 ━ P99 │  ← legend 우상단
│                                                                              │
│   (P50 라인 — blue)                                                          │
│   (P95 라인 — orange)                                                        │
│   (P99 라인 — red)                                                           │
└──────────────────────────────────────────────────────────────────────────────┘
```

- Legend는 drawable 내부에서 `canvas.DrawString` + 짧은 색상 라인으로 그림.
- 위치: `dirtyRect.Y + 4` 부터 우측 정렬, 폭 ~150px reserve.
- 폰트 11pt.

### 5.3 Component List

| Component | Location | Responsibility |
|-----------|----------|----------------|
| `LineChartSeries` (record) | `Views/LineChartSeries.cs` | series 단위 모델 |
| `MultiLineChartDrawable` | `Views/MultiLineChartDrawable.cs` | N-series 통합 좌표계 그리기 + legend |
| `LineChartMath.ComputeRangeMulti` | `Core/Charts/LineChartMath.cs` (확장) | 통합 min/max |

### 5.4 Page UI Checklist

#### Dashboard MainPage (RTT Frame)

- [ ] 3 라인 동시 표시 (P50 blue / P95 orange / P99 red).
- [ ] Y축은 3 series 통합 min/max 자동 스케일.
- [ ] 우상단 legend: 3개 색상 막대 + "P50 P95 P99" 텍스트.
- [ ] series가 비었을 때 라인 미렌더, legend는 표시되거나 미표시(둘 다 OK, 일관되게).
- [ ] HeightRequest=160 그대로.

#### Throughput Frame

- [ ] 직전 cycle 동작 그대로 (single-line, 초록 `#4CAF50`, 마지막 값 라벨).

---

## 6. Error Handling

| Case | Handling |
|------|----------|
| `Series` 비어있음 (count 0) | early return |
| 모든 series.Values 비어있음 | early return |
| 일부 series만 비어있음 | 비어있는 series는 skip, 나머지만 그림 |
| 통합 min == max | `LineChartMath.ComputeRangeMulti`가 padding ±0.5 적용 |
| dirtyRect 비정상 (width/height ≤ 0) | early return |

---

## 7. Security Considerations

해당 없음.

---

## 8. Test Plan

### 8.1 Test Scope

| Type | Target | Tool | Phase |
|------|--------|------|-------|
| L0: Unit | `LineChartMath.ComputeRangeMulti` 다양한 입력 | MSTest | Do |
| L1: Regression | 기존 32 tests | MSTest | Do |
| L2: Manual UI | macOS Catalyst Debug 3-line 렌더 + 무 crash | 수동 | Check |

### 8.2 L0 Test Scenarios

| # | Target | Description | Expected |
|---|--------|-------------|----------|
| U1 | `ComputeRangeMulti(empty)` | 빈 list | `(0, 1)` |
| U2 | `ComputeRangeMulti([empty, empty])` | 모두 비어있는 series | `(0, 1)` |
| U3 | `ComputeRangeMulti([[1,2,3], [10,20]])` | 정상 멀티 | `(1, 20)` |
| U4 | `ComputeRangeMulti([[5,5,5], [5,5]])` | 동일 값 | range > 0 (padding) |
| U5 | `ComputeRangeMulti([[1,2], [], [9,8]])` | 일부 빈 series 섞임 | `(1, 9)` |

### 8.3 L1 Regression

`dotnet test`로 기존 32 tests 모두 green. Throughput 차트 동작 변경 0이므로 ViewModel/Adapter 영향 없음.

### 8.4 L2 Manual

| # | Step | Expected |
|---|------|----------|
| M1 | `dotnet build` 두 TFM 성공 | warning 0 |
| M2 | macOS Catalyst Debug 실행 + UseMock + Connect | 10초+ crash 0 |
| M3 | RTT 차트에 3-line + legend 보임 | P50 blue / P95 orange / P99 red 구분 가능 |
| M4 | Throughput 차트 정상 (직전 cycle parity) | 초록 single line + 마지막 값 라벨 |
| M5 | DiagnosticReports 신규 IPS 없음 | crash 0 evidence |

---

## 9. Clean Architecture

| Layer | Component |
|-------|-----------|
| Presentation | `MainPage.xaml`, `MainPage.xaml.cs`, `MultiLineChartDrawable`, `LineChartSeries` |
| Application | `DashboardViewModel` (불변) |
| Domain | `TimedRttPoint` (불변), `LineChartMath` (Core) |
| Infrastructure | 변경 없음 |

본 cycle 영향은 Presentation + Core charts 헬퍼에 한정.

---

## 10. Coding Convention Reference

| Item | Convention |
|------|------------|
| 네임스페이스 | `FastPortDashboard.Maui.Views` 또는 `FastPortDashboard.Core.Charts` |
| 파일명 | `MultiLineChartDrawable.cs`, `LineChartSeries.cs` |
| Color 상수 | `MainPage.xaml.cs` 상단에 `RttP50Color`, `RttP95Color`, `RttP99Color` 통합 |
| Design Ref 주석 | 모든 신규 멤버 |
| Series property setter | mutable property로 매 update마다 새 list 할당 (`LineChartDrawable` 패턴 일관) |

---

## 11. Implementation Guide

### 11.1 File Structure

```
FastPortDashboard.Core/
└── Charts/LineChartMath.cs          (MODIFIED: ComputeRangeMulti 추가)

FastPortDashboard.Maui/
├── MainPage.xaml.cs                 (MODIFIED: _rttMultiDrawable 사용, color 상수 3개, UpdateRttChart 재작성)
└── Views/
    ├── LineChartDrawable.cs         (UNCHANGED — Throughput용)
    ├── LineChartSeries.cs           (NEW: record)
    └── MultiLineChartDrawable.cs    (NEW: IDrawable)

tests-projects/FastPortDashboardTests/Charts/
└── LineChartMathTests.cs            (MODIFIED: ComputeRangeMulti 5 tests 추가)
```

### 11.2 Implementation Order

1. [ ] `LineChartMath.ComputeRangeMulti` 추가 — 빈 입력/일부 빈/정상/동일 값 케이스 안전.
2. [ ] `LineChartMathTests`에 U1~U5 추가.
3. [ ] `LineChartSeries.cs` 신규 (record).
4. [ ] `MultiLineChartDrawable.cs` 신규:
   - `Series` property mutable.
   - `Draw` 알고리즘:
     ```
     if (Series.Count == 0 || dirtyRect 비정상) return;
     var nonEmpty = Series.Where(s => s.Values.Count > 0).ToArray();
     if (nonEmpty.Length == 0) return;
     var (min, max) = LineChartMath.ComputeRangeMulti(nonEmpty.Select(s => s.Values));
     double range = max - min; if (range <= 0) range = 1;
     marginTop = 18 (legend 공간), marginBottom = 6, legendReserveRight = 160;
     plotWidth = dirtyRect.Width;  // legend는 위쪽 별도 라인이라 plot 가로 폭 안 줄임
     plotHeight = dirtyRect.Height - marginTop - marginBottom;
     foreach series in nonEmpty:
       n = series.Values.Count; if (n < 1) continue;
       stepX = ComputeStepX(n, plotWidth);
       path = PathF();
       for i in 0..n-1:
         x = dirtyRect.X + i*stepX
         yNorm = (values[i] - min)/range
         y = dirtyRect.Y + marginTop + (1-yNorm)*plotHeight
         if i==0 MoveTo else LineTo
       canvas.StrokeColor = series.LineColor
       canvas.StrokeSize = series.LineWidth
       canvas.DrawPath(path)
     if (ShowLegend) draw legend at top-right:
       float legendY = dirtyRect.Y + 2;
       float x = dirtyRect.X + dirtyRect.Width - 4;
       foreach (series in nonEmpty.Reverse()):  // right-to-left so visually L→R
         // measure label width approx: labelWidth = series.Label.Length * 7
         // draw color bar (10x3) then label
         ...
     ```
   - Stateless: 외부 mutable state 의존 없음.
5. [ ] `MainPage.xaml.cs` 수정:
   - `RttP50Color`, `RttP95Color`, `RttP99Color`, `ThroughputLineColor` 4개 상수.
   - 기존 `_rttDrawable` (single-line) 제거 또는 보존? — **제거**. RTT는 MultiLine 전용.
   - `_rttMultiDrawable = new MultiLineChartDrawable()`, `RttChartView.Drawable = _rttMultiDrawable;`.
   - `UpdateRttChart`:
     ```csharp
     var points = _viewModel.ClientRttSeries.ToArray();
     _rttMultiDrawable.Series = new[]
     {
         new LineChartSeries(RttP50Color, points.Select(p => p.P50Ms).ToArray(), "P50"),
         new LineChartSeries(RttP95Color, points.Select(p => p.P95Ms).ToArray(), "P95"),
         new LineChartSeries(RttP99Color, points.Select(p => p.P99Ms).ToArray(), "P99"),
     };
     RttChartView.Invalidate();
     ```
   - `UpdateThroughputChart` 그대로.
6. [ ] Build 두 TFM + dotnet test 실행.
7. [ ] Manual M2~M5.

### 11.3 Session Guide

#### Module Map

| Module | Scope Key | Description | Estimated Turns |
|--------|-----------|-------------|:---------------:|
| Math + tests | `module-1` | `ComputeRangeMulti` 확장 + 5 tests | 4-6 |
| Drawable + series record | `module-2` | `LineChartSeries`, `MultiLineChartDrawable` | 6-8 |
| Page wiring + manual | `module-3` | `MainPage.xaml.cs` 교체 + 빌드/실행 검증 | 4-6 |

작업이 작아 한 세션에 module-1+2+3 통합 가능.

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-11 | Initial draft (Option B 선택, MultiLineChartDrawable 분리 신규) | boinred |
