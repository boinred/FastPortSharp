---
template: design
version: 1.3
feature: dashboard-chart-graphicsview-migration
date: 2026-05-11
author: boinred
project: FastPortSharp
---

# dashboard-chart-graphicsview-migration Design Document

> **Summary**: SkiaSharp 기반 chart view를 `Microsoft.Maui.Graphics` + `GraphicsView` + `IDrawable`로 교체하여 macOS 26 SwiftUI Observation crash 회피.
>
> **Project**: FastPortSharp
> **Version**: 0.1.0
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **Planning Doc**: [dashboard-chart-graphicsview-migration.plan.md](../../01-plan/features/dashboard-chart-graphicsview-migration.plan.md)

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | SkiaSharp 기반 view가 macOS 26 SwiftUI Observation framework crash trigger임이 직전 cycle 진단으로 100% 재현 확인. |
| **WHO** | macOS Catalyst + Windows 양쪽에서 FastPort 메트릭을 관찰하는 개발자. |
| **RISK** | (a) `GraphicsView` 자체도 Observation 경로 진입 가능성, (b) 자작 line chart 좌표/스케일 버그. |
| **SUCCESS** | macOS Catalyst Debug 10초+ 무 crash + 라인 정상 렌더 + 기존 25개 테스트 회귀 0 + Skia 참조 0건. |
| **SCOPE** | RTT P95 + Throughput 단일 라인 두 개. P50/P99 overlay는 OOS (data layer만 유지). |

---

## 1. Overview

### 1.1 Design Goals

- SkiaSharp / Microcharts 의존성 완전 제거.
- 두 차트(`RttChartView`, `ThroughputChartView`)의 시각적 parity 유지: 라인 + 마지막 sample 값 라벨.
- 차트 렌더링 로직을 ViewModel에서 분리하여 순수 함수로 단위 테스트 가능하게 한다.
- 향후 P50/P99 overlay 확장이 가능하도록 drawable 인터페이스를 series 단위로 설계.

### 1.2 Design Principles

- **Stateless drawable**: `IDrawable.Draw`는 입력 snapshot list와 RectF만 보고 결과를 그린다. 외부 state 의존 없음.
- **Snapshot 패턴**: ViewModel의 `ObservableCollection`을 drawable이 직접 읽지 않고, View 코드가 `IReadOnlyList<double>` snapshot으로 변환하여 주입.
- **명시적 invalidate**: CollectionChanged → snapshot 갱신 → `GraphicsView.Invalidate()`로 재렌더.
- **최소 footprint**: 새 파일 1개 + 두 기존 파일 수정만으로 cycle 완결.

---

## 2. Architecture Options

### 2.0 Architecture Comparison

| Criteria | Option A: Minimal (inline) | Option B: Clean (renderer 추상화) | **Option C: Pragmatic** |
|----------|:-:|:-:|:-:|
| **Approach** | `MainPage.xaml.cs` 내부에 익명 `IDrawable` 두 개 직접 정의 | `IChartRenderer` + 시리즈 모델 + DI 컨테이너 | 재사용 가능한 `LineChartDrawable` 1개 클래스, snapshot list 주입 |
| **New Files** | 0 | 4 (인터페이스, renderer, factory, model) | 1 (`LineChartDrawable.cs`) |
| **Modified Files** | 2 (MainPage.xaml, .cs) | 2 + 신규 폴더 구조 | 3 (csproj, xaml, xaml.cs) |
| **Complexity** | Low | High | Medium |
| **Maintainability** | Low (xaml.cs 비대) | High | High |
| **Effort** | Low | High | Medium |
| **Risk** | Low (코드 양 적음) | Medium (over-engineering, scope creep) | Low |
| **단위 테스트** | 어려움 (xaml.cs 결합) | 쉬움 | 쉬움 (snapshot 입력만) |
| **Recommendation** | hotfix 한정 | 후속 cycle에서 multi-series 확장 시 | **Default choice — cycle scope에 적합** |

**Selected**: **Option C — Pragmatic Balance** — **Rationale**: 본 cycle 목표는 "Skia trigger 제거 + 시각화 parity 유지"라는 좁고 명확한 범위다. Option B의 추상화는 현재 두 시리즈만 그리는 상황에서 over-engineering, Option A는 xaml.cs를 비대화하고 단위 테스트를 막는다. 단일 `LineChartDrawable` 재사용으로 시각화 parity + 테스트 가능성 + 차후 확장 여지(생성자 옵션 추가)를 모두 확보한다.

### 2.1 Component Diagram

```
┌────────────────────────────┐
│ DashboardViewModel         │  (FastPortDashboard.Core, 불변)
│  - ClientRttSeries         │
│  - ThroughputSeries        │
└─────────────┬──────────────┘
              │ CollectionChanged
              ▼
┌────────────────────────────┐
│ MainPage.xaml.cs           │  (snapshot 생성 + Invalidate)
│  - rttSnapshot: double[]   │
│  - thrSnapshot: double[]   │
└──────────┬─────────────────┘
           │ Drawable.Update(snapshot)
           ▼
┌────────────────────────────┐
│ LineChartDrawable          │  (Views/LineChartDrawable.cs, 신규)
│  IDrawable.Draw(canvas,    │
│                rect)       │
└──────────┬─────────────────┘
           │ ICanvas operations
           ▼
┌────────────────────────────┐
│ GraphicsView (MAUI)        │  (MainPage.xaml × 2)
└────────────────────────────┘
```

### 2.2 Data Flow

```
ViewModel Snapshot (CollectionChanged)
  → MainPage 변환 (TimedRttPoint.P95Ms / ThroughputPoint.Value → double[])
  → drawable.Values = snapshot
  → GraphicsView.Invalidate()
  → MAUI render loop → IDrawable.Draw(ICanvas, RectF)
  → 라인 + 마지막 값 라벨 그리기
```

### 2.3 Dependencies

| Component | Depends On | Purpose |
|-----------|-----------|---------|
| `LineChartDrawable` | `Microsoft.Maui.Graphics` (MAUI 기본) | `IDrawable`, `ICanvas`, `Color`, `PointF`, `RectF` |
| `MainPage.xaml.cs` | `LineChartDrawable`, ViewModel | snapshot 생성 + Invalidate |
| `MainPage.xaml` | `Microsoft.Maui.Controls.GraphicsView` (기본) | 차트 표면 |
| 제거: `Microcharts.Maui`, `SkiaSharp.Views.Maui.Controls` | — | 더 이상 참조 없음 |

---

## 3. Data Model

`FastPortDashboard.Core/ViewModels/TimedRttPoint.cs` (불변):

```csharp
public readonly record struct TimedRttPoint(
    double TimestampUnixMs,
    double P50Ms,
    double P95Ms,
    double P99Ms);
```

`LineChartDrawable` 내부 모델 (신규):

```csharp
public sealed class LineChartDrawable : IDrawable
{
    public IReadOnlyList<double> Values { get; set; } = Array.Empty<double>();
    public Color LineColor { get; set; } = Colors.DodgerBlue;
    public float LineWidth { get; set; } = 2f;
    public string ValueFormat { get; set; } = "F0"; // e.g. "F0" or "F1"
    public bool ShowLastValueLabel { get; set; } = true;

    public void Draw(ICanvas canvas, RectF dirtyRect) { /* §11.2 algorithm */ }
}
```

설계 의도:
- `Values`는 mutable property — 매 snapshot 변경 시 view 코드가 새 list 할당 후 `Invalidate()`.
- ctor 인자 없음 → XAML에서 `<LineChartDrawable .../>` 직접 사용 가능 (필요 시).
- Static 도우미 메서드 `static (double min, double max) ComputeRange(IReadOnlyList<double> values)` → 단위 테스트 대상.

---

## 4. API Specification

해당 없음 (UI cycle, 외부 HTTP API 변경 없음).

---

## 5. UI/UX Design

### 5.1 Screen Layout

기존과 동일. 차트 두 Frame만 내부 컴포넌트 교체.

```
┌────────────────────────────────────┐
│  Header (Title + Description)      │
├────────────────────────────────────┤
│  [ Path Entry / Connect Controls ] │
├────────────────────────────────────┤
│  [ KPI Grid 3x2 ]                  │
├────────────────────────────────────┤
│  [ Frame: "Client RTT P95 (ms)" ]  │
│    Label "최근 sample 수: N"         │
│    GraphicsView (160h)             │ ← was microcharts:ChartView
├────────────────────────────────────┤
│  [ Frame: "Server Throughput..." ] │
│    Label "최근 sample 수: N"         │
│    GraphicsView (160h)             │ ← was microcharts:ChartView
└────────────────────────────────────┘
```

### 5.2 User Flow

기존과 동일. Connect → Polling → snapshot 도착 → 차트 자동 업데이트.

### 5.3 Component List

| Component | Location | Responsibility |
|-----------|----------|----------------|
| `LineChartDrawable` | `FastPortDashboard.Maui/Views/LineChartDrawable.cs` | `IDrawable` — 라인 + 마지막 값 라벨 그리기 |
| `RttChartView` (`GraphicsView`) | `MainPage.xaml` | RTT P95 시리즈 표면 |
| `ThroughputChartView` (`GraphicsView`) | `MainPage.xaml` | Throughput 시리즈 표면 |
| `MainPage.UpdateRttChart` | `MainPage.xaml.cs` | snapshot 변환 + Invalidate |
| `MainPage.UpdateThroughputChart` | `MainPage.xaml.cs` | snapshot 변환 + Invalidate |

### 5.4 Page UI Checklist

#### Dashboard MainPage

- [ ] GraphicsView: `RttChartView` (HeightRequest=160) — RTT P95 라인 (주황 `#FF9800`)
- [ ] GraphicsView: `ThroughputChartView` (HeightRequest=160) — Throughput 라인 (초록 `#4CAF50`)
- [ ] Label: "Client RTT P95 (ms)" 헤더 (FontAttributes=Bold)
- [ ] Label: "Server Throughput (B/s)" 헤더 (FontAttributes=Bold)
- [ ] Label: "최근 sample 수: {Count}" 두 차트 모두 (FontSize=11, TextColor=Gray)
- [ ] 마지막 값 라벨: 차트 우상단에 마지막 sample 정수 표기 (LineColor와 동일)
- [ ] Frame: 두 차트 모두 Padding=12, CornerRadius=8, BorderColor=Gray, HeightRequest=220
- [ ] 차트 면이 빈 상태(Count=0)일 때 라인 미렌더, 라벨 미표시 (crash/예외 없이)

---

## 6. Error Handling

차트 렌더 단계의 예외 경로:

| Code | Cause | Handling |
|------|-------|----------|
| Empty `Values` | snapshot 비어있음 (초기 상태) | `Draw` 첫 줄에서 `if (Values.Count < 2) return;` |
| Single value | sample 1개만 도착 | 라인 그릴 수 없으므로 점 1개 + 라벨만 표시 |
| All identical values | min==max → 0 division 위험 | `range = max-min; if (range == 0) range = 1;` |
| `dirtyRect` 너비 ≤ 0 | layout 중 비정상 | early return |

UI 레벨에서는 기존 `ErrorMessage` label로 ViewModel 오류 전달. 차트 자체 오류는 throw 하지 않고 silent skip (UI 안정성 우선).

---

## 7. Security Considerations

해당 없음 (로컬 UI 전용, 외부 입력 없음).

---

## 8. Test Plan

### 8.1 Test Scope

| Type | Target | Tool | Phase |
|------|--------|------|-------|
| L0: Unit | `LineChartDrawable.ComputeRange` 등 순수 함수 | MSTest | Do |
| L1: Adapter/ViewModel 회귀 | 기존 unit + E2E 25개 | MSTest | Do |
| L2: Manual UI | macOS Catalyst Debug 실행 후 차트 렌더 + 무 crash | 수동 | Check |

### 8.2 L0 — Unit Test Scenarios (신규)

| # | Target | Description | Expected |
|---|--------|-------------|----------|
| U1 | `ComputeRange(Array.Empty<double>())` | 빈 배열 처리 | `(0, 1)` 또는 일관된 기본값 |
| U2 | `ComputeRange([5.0])` | 단일 값 | min==max==5 → range 보정 적용 |
| U3 | `ComputeRange([1,2,3,4,5])` | 정상 범위 | min=1, max=5 |
| U4 | `ComputeRange([3,3,3])` | 동일 값 | range 0 안전 처리 |

### 8.3 L1 — Regression Test (기존)

기존 25개 (Unit + E2E)가 변경 없이 통과해야 함. ViewModel/Adapter 표면을 건드리지 않으므로 사실상 build success가 곧 회귀 패스.

### 8.4 L2 — Manual UI Verification

| # | Step | Expected |
|---|------|----------|
| M1 | `dotnet build` net10.0-maccatalyst 성공 | warning 신규 0 |
| M2 | Catalyst Debug 실행 + `UseMock=true` + Connect | 10초+ crash 없음 |
| M3 | 위 상태에서 두 차트에 라인이 그려짐 | RTT 라인 = 주황, Throughput 라인 = 초록 |
| M4 | Disconnect 후 Connect 재시도 | 차트 갱신 정상, 누수 없음 |
| M5 | `~/Library/Logs/DiagnosticReports/` 신규 IPS 없음 | crash 0 evidence |

### 8.5 Seed Data Requirements

`MockPollingAdapter` (기존)이 30ms 간격 random walk으로 RTT P50/P95/P99 + throughput을 생성 — 추가 seed 불필요.

---

## 9. Clean Architecture

| Layer | Responsibility | Location |
|-------|---------------|----------|
| **Presentation** | XAML page + GraphicsView + drawable | `FastPortDashboard.Maui/MainPage.xaml`, `Views/LineChartDrawable.cs` |
| **Application** | `DashboardViewModel`, polling 흐름 | `FastPortDashboard.Core/ViewModels/` (불변) |
| **Domain** | `TimedRttPoint`, `ObservedMetricsSnapshot` | `FastPortDashboard.Core/` (불변) |
| **Infrastructure** | Polling adapter, file IO | `FastPortDashboard.Maui/Adapters/` (불변) |

본 cycle 영향은 Presentation layer에 한정.

---

## 10. Coding Convention Reference

| Item | Convention Applied |
|------|-------------------|
| 네임스페이스 | `FastPortDashboard.Maui.Views` (신규) |
| 파일명 | `PascalCase.cs` (`LineChartDrawable.cs`) |
| Color 정의 | `Microsoft.Maui.Graphics.Color` 정적 readonly로 `MainPage.xaml.cs` 상단에 통일 |
| Design Ref 주석 | 모든 신규/수정 멤버 상단에 `// Design Ref: §X — rationale` 부착 (PDCA 규약) |
| Disposability | drawable은 `IDisposable` 아님 (snapshot은 단순 reference 교체) |

---

## 11. Implementation Guide

### 11.1 File Structure

```
FastPortDashboard.Maui/
├── FastPortDashboard.Maui.csproj    (MODIFIED: Microcharts 제거)
├── MainPage.xaml                    (MODIFIED: GraphicsView × 2)
├── MainPage.xaml.cs                 (MODIFIED: snapshot 변환 + drawable 사용)
└── Views/
    └── LineChartDrawable.cs         (NEW)
```

### 11.2 Implementation Order

1. [ ] **csproj cleanup**: `Microcharts.Maui` 및 `SkiaSharp.Views.Maui.Controls` 참조 제거. `dotnet restore` 확인.
2. [ ] **LineChartDrawable.cs 작성**:
   - 시그니처: `public sealed class LineChartDrawable : IDrawable`
   - Property: `Values`, `LineColor`, `LineWidth`, `ValueFormat`, `ShowLastValueLabel`.
   - `Draw(ICanvas, RectF)` 알고리즘:
     ```
     if (Values.Count < 1 || dirtyRect.Width <= 0 || dirtyRect.Height <= 0) return;
     var (min, max) = ComputeRange(Values);
     var range = max - min; if (range <= 0) range = 1;
     var marginTop = 10f; var marginBottom = 6f;
     var plotHeight = dirtyRect.Height - marginTop - marginBottom;
     var n = Values.Count;
     var stepX = n > 1 ? dirtyRect.Width / (n - 1) : 0;
     // Build path
     var path = new PathF();
     for (int i = 0; i < n; i++) {
       float x = dirtyRect.X + i * stepX;
       float yNorm = (float)((Values[i] - min) / range);
       float y = dirtyRect.Y + marginTop + (1f - yNorm) * plotHeight;
       if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
     }
     canvas.StrokeColor = LineColor;
     canvas.StrokeSize = LineWidth;
     canvas.DrawPath(path);
     if (ShowLastValueLabel && n > 0) {
       var lastVal = Values[n - 1];
       canvas.FontColor = LineColor;
       canvas.FontSize = 11;
       canvas.DrawString(
         lastVal.ToString(ValueFormat, CultureInfo.InvariantCulture),
         dirtyRect.X + dirtyRect.Width - 60, dirtyRect.Y + 2, 58, 14,
         HorizontalAlignment.Right, VerticalAlignment.Top);
     }
     ```
   - Static helper: `internal static (double Min, double Max) ComputeRange(IReadOnlyList<double> values)`.
3. [ ] **MainPage.xaml 수정**:
   - `xmlns:microcharts`, `xmlns:skia` 제거.
   - 두 `<microcharts:ChartView .../>` → `<GraphicsView x:Name="RttChartView" HeightRequest="160" />`, `<GraphicsView x:Name="ThroughputChartView" HeightRequest="160" />`.
4. [ ] **MainPage.xaml.cs 수정**:
   - `using Microcharts;`, `using SkiaSharp;` 삭제.
   - `SKColor` 상수 → `Microsoft.Maui.Graphics.Color` 상수 (`Color.FromArgb("#FF9800")`, `Color.FromArgb("#4CAF50")`).
   - 두 `LineChartDrawable` private field 추가, ctor에서 인스턴스 생성 후 `RttChartView.Drawable = _rttDrawable; ThroughputChartView.Drawable = _throughputDrawable;`.
   - `UpdateRttChart`: `_rttDrawable.Values = _viewModel.ClientRttSeries.Select(p => p.P95Ms).ToArray(); RttChartView.Invalidate();`.
   - `UpdateThroughputChart`: 동일 패턴.
5. [ ] **Unit test 추가**: `tests-projects/FastPortDashboardTests/Views/LineChartDrawableTests.cs` — `ComputeRange` 4 케이스.
6. [ ] **Build verify**: `dotnet build` (net10.0-maccatalyst + net10.0-windows10) 성공.
7. [ ] **Manual UI**: M1~M5 (§8.4).

### 11.3 Session Guide

#### Module Map

| Module | Scope Key | Description | Estimated Turns |
|--------|-----------|-------------|:---------------:|
| Drawable + tests | `module-1` | `LineChartDrawable.cs` + `ComputeRange` 단위 테스트 | 6-8 |
| Page wiring | `module-2` | csproj cleanup + MainPage.xaml / .cs 교체 | 6-8 |
| Manual verify | `module-3` | macOS Catalyst Debug 실행 + 스크린샷/로그 | 4-6 |

#### Recommended Session Plan

| Session | Phase | Scope | Turns |
|---------|-------|-------|:-----:|
| 1 | Plan + Design | 전체 | 20-25 |
| 2 | Do | `--scope module-1,module-2` | 25-35 |
| 3 | Check + Manual Verify + Report | `module-3` 포함 | 20-30 |

본 cycle은 규모가 작으므로 Session 2에서 module-1+2 합치는 것이 효율적이다.

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-11 | Initial draft (Option C 선택, LineChartDrawable IDrawable 설계) | boinred |
