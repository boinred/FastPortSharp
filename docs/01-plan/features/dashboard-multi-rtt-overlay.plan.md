# dashboard-multi-rtt-overlay Plan

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **PRD**: (lightweight, Plan에 통합)

---

## Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | RTT chart는 P95Ms 단일 line만 표시. ClientObservedMetricsSnapshot에는 P50/P95/P99 모두 있으나 시각화 부재 → tail latency 분석 어려움. |
| **Solution** | RTT chart의 `microcharts:ChartView`를 `SKCanvasView`로 교체. ViewModel은 `TimedRttPoint(ts, p50, p95, p99)` struct collection 유지, code-behind에서 SkiaSharp로 3 line color-coded 직접 draw + 우상단 legend. |
| **Function/UX/Effect** | Mock/JSONL polling 시 3 latency percentile (P50/P95/P99)이 한 chart에 overlay되어 갱신. P50=blue / P95=orange / P99=red 색상으로 latency budget 파악 ↑. |
| **Core Value** | Game server latency 분석의 핵심 (tail percentile)을 한 눈에 보기 가능. Microcharts 단일 series 한계를 우회하면서 LiveCharts2 (macOS crash) 회피. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Tail latency (P95/P99) 분석은 게임서버 성능의 핵심 지표. 단일 P95만으론 distribution 모름. |
| **WHO** | boinred + 미래 contributor + 게임서버 운영자. |
| **RISK** | (R-1) Microcharts 단일 series 한계로 custom Skia 코드 필요 / (R-2) SKCanvasView 직접 draw 시 좌표 변환 정확성 / (R-3) 기존 RTT test 회귀 (struct 변경) / (R-4) 자동 스케일링 (data range 변동) |
| **SUCCESS** | 3 line overlay (P50/P95/P99) + Mock 갱신 + Dashboard 빌드 0/0 + Tests 회귀 0 (struct migration 포함) + FastPortSharp.sln 회귀 0 + 수동 시각 확인 |
| **SCOPE** | `FastPortDashboard.Core/ViewModels/TimedDoublePoint.cs` (또는 신규 `TimedRttPoint.cs`) + `DashboardViewModel.cs` + `MainPage.xaml` + `MainPage.xaml.cs` + 2 tests. Throughput chart 무변경. |

---

## 1. Overview

### 1.1 Motivation

현재 dashboard:
- 6 KPI labels ✅
- Client RTT P95 Microcharts LineChart (blue) ✅
- Server Throughput Microcharts LineChart (green) ✅

본 cycle은 RTT chart를 **3-line overlay**로 확장:
- P50 (median): latency 일반 분포
- P95: tail (대다수 사용자 worst case)
- P99: extreme tail (worst 1%)

Game server 운영에서 P50 vs P95 vs P99 gap이 user experience 균질성을 직접 결정.

### 1.2 Microcharts 한계와 Custom Skia 선택 사유

Microcharts.Maui 1.0.1의 `LineChart`는 단일 `Entries[]`만 지원. 3-line overlay 불가.

| 대안 | 채택? |
|---|---|
| 3 stacked Microcharts (Frame × 3) | ❌ — cycle 명 "overlay"와 어긋남, 시각 비교 어려움 |
| **Custom SKCanvasView + 3-line Skia 직접 draw** | ✅ — true overlay, 색상 구분, ~100 lines |
| LiveCharts2 재도입 | ❌ — macOS 26 SwiftUI Observation crash history |

### 1.3 ViewModel 구조 변경

현재: `ClientRttSeries: ObservableCollection<TimedDoublePoint>` (timestamp + P95 값)

변경 후: `ClientRttSeries: ObservableCollection<TimedRttPoint>` (timestamp + P50 + P95 + P99)

이는 ViewModel public collection 타입 변경 → 기존 tests T-VM-11/T-VM-12의 `.Value` 접근 부분 update 필요.

`TimedDoublePoint`는 Throughput chart가 그대로 사용 → 보존.
신규 `TimedRttPoint(double TimestampUnixMs, double P50Ms, double P95Ms, double P99Ms)` readonly record struct 추가.

### 1.4 Out of scope

- Chart interaction (tooltip / zoom / pan)
- Y축 자동 동적 scale (간단 max-based만 적용)
- Legend interactivity (toggle)
- Throughput chart 변경
- LibTestTelemetry contract 변경 (P50/P95/P99 모두 이미 존재)
- 실시간 average line 추가

---

## 2. Scope

### 2.1 In Scope

| 영역 | 작업 |
|---|---|
| `FastPortDashboard.Core/ViewModels/TimedRttPoint.cs` | 신규 readonly record struct (timestamp + P50/P95/P99) |
| `FastPortDashboard.Core/ViewModels/DashboardViewModel.cs` | `ClientRttSeries` 타입 변경 (TimedDoublePoint → TimedRttPoint), `ApplyClientSnapshot` 갱신 |
| `FastPortDashboard.Maui/MainPage.xaml` | RTT Frame 내부 `microcharts:ChartView` → `skia:SKCanvasView` 교체 + xmlns 추가 |
| `FastPortDashboard.Maui/MainPage.xaml.cs` | `UpdateRttChart()` 제거 → `OnRttCanvasPaintSurface` 핸들러로 custom 3-line draw + legend |
| `tests-projects/FastPortDashboardTests/ViewModels/DashboardViewModelTests.cs` | T-VM-11/T-VM-12 갱신 (TimedRttPoint API 사용) |

### 2.2 Out of Scope

- `ThroughputSeries` (변경 0)
- `JsonlPollingAdapter`, `MockPollingAdapter` (이미 RTT P50/P95/P99 yield)
- Microcharts pkg 제거 (Throughput chart가 사용)
- LibTestTelemetry contract
- iOS/Android
- FastPortSharp.sln 변경

### 2.3 Key Constraint

- **Production code (LibTestTelemetry 등) 변경 0**
- **Throughput chart 동작 무변경**
- **신규 Microcharts dependency 0** (이미 transitive 사용 중)
- **macOS Catalyst Debug 실행 가능**

---

## 3. Requirements

### 3.1 Functional

- **FR-1**: `TimedRttPoint` struct: `(double TimestampUnixMs, double P50Ms, double P95Ms, double P99Ms)`
- **FR-2**: `DashboardViewModel.ClientRttSeries`: `ObservableCollection<TimedRttPoint>` (타입 변경)
- **FR-3**: `ApplyClientSnapshot`: client.RttP50Ms/P95Ms/P99Ms를 단일 point로 묶어 append
- **FR-4**: MaxChartPoints=600 trim 동일 적용
- **FR-5**: MainPage.xaml에 `<skia:SKCanvasView x:Name="RttCanvasView" PaintSurface="OnRttCanvasPaintSurface" HeightRequest="160" />`
- **FR-6**: `OnRttCanvasPaintSurface` 핸들러:
  - 3 line draw (P50 blue / P95 orange / P99 red)
  - 자동 Y축 스케일 (`max(P99 series)` 기준)
  - X축 (시간) 선형 분배 (sample index 기반)
  - 우상단 legend (color box + label)
  - Canvas size 변동 시 `Invalidate()` 호출로 redraw
- **FR-7**: `ClientRttSeries.CollectionChanged` → `RttCanvasView.InvalidateSurface()`로 redraw
- **FR-8**: T-VM-11 갱신: ClientObserved 적용 시 ClientRttSeries[0].P50Ms/P95Ms/P99Ms 모두 검증
- **FR-9**: T-VM-12 갱신: 600 trim 동일하게 검증 (`p.P95Ms` 비교)

### 3.2 Non-Functional

- **NFR-1**: Dashboard 빌드 0/0
- **NFR-2**: Dashboard test 20/0/0 (기존 18 + T-VM-11/12 갱신, count 동일)
- **NFR-3**: FastPortSharp.sln 회귀 0
- **NFR-4**: CI workflow 변경 0
- **NFR-5**: 단일 commit
- **NFR-6**: 한국어 주석

### 3.3 Compatibility

- Microcharts.Maui 1.0.1 (transitive SkiaSharp 활용)
- SkiaSharp.Views.Maui.Controls (Microcharts가 끌어옴)
- `SKCanvasView` PaintSurface event 표준

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] `TimedRttPoint.cs` 신규
- [ ] `DashboardViewModel.ClientRttSeries` 타입 변경 + `ApplyClientSnapshot` 갱신
- [ ] MainPage.xaml: SKCanvasView 추가 + xmlns
- [ ] MainPage.xaml.cs: PaintSurface 핸들러 + custom 3-line draw + legend
- [ ] T-VM-11/T-VM-12 갱신 (TimedRttPoint API)
- [ ] Dashboard 빌드 0/0
- [ ] Dashboard test 20/0/0
- [ ] FastPortSharp.sln 회귀 0
- [ ] (수동) macOS Catalyst Debug 실행 → Mock Connect → 3 line overlay 갱신 확인
- [ ] 단일 commit

### 4.2 Quality Criteria

- [ ] 색상 명시 상수 (RttP50Color, RttP95Color, RttP99Color)
- [ ] Y축 자동 스케일 (`p.P99Ms` 최대값 기반)
- [ ] Legend 우상단 (color box + "P50" / "P95" / "P99" 텍스트)
- [ ] 한국어 주석 컨벤션

---

## 5. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| (R-1) Custom Skia 코드 시간 ↑ | Medium | Medium | ~100 lines 예상. P95 LineChart code-behind 패턴 변형. |
| (R-2) 좌표 변환 (data → pixel) 정확성 | Medium | Low | 수동 검증으로 확인. line이 chart 범위 안에 들어가는지 visual check. |
| (R-3) Test struct 변경으로 회귀 | Low | Medium | T-VM-11/12 명시적 갱신. CollectionAssert로 P50/P95/P99 검증. |
| (R-4) 자동 스케일 시 P50 < P95 < P99 항상 보장? | Low | Low | RTT 통계상 P50 ≤ P95 ≤ P99 정렬되어 있음. `max(P99)` 사용 안전. |
| (R-5) Legend 텍스트가 영역 밖으로 나감 | Low | Low | Canvas width 검사 후 padding 적용. |
| (R-6) macOS Release crash 재현 | High | High | Memory: maccatalyst-26-swiftui-observation-release-crash 적용. Debug 실행만 검증. |

---

## 6. Impact Analysis

### 6.1 영향 받는 파일

| 파일 | 작업 | 예상 라인 |
|---|---|---|
| `FastPortDashboard.Core/ViewModels/TimedRttPoint.cs` | new | ~5 (1-line record struct + namespace + comments) |
| `FastPortDashboard.Core/ViewModels/DashboardViewModel.cs` | edit (series type + ApplyClient) | ±5 |
| `FastPortDashboard.Maui/MainPage.xaml` | edit (ChartView → SKCanvasView) | ±5 |
| `FastPortDashboard.Maui/MainPage.xaml.cs` | edit (UpdateRttChart → OnRttCanvasPaintSurface) | ±70 (custom Skia drawing) |
| `tests-projects/FastPortDashboardTests/ViewModels/DashboardViewModelTests.cs` | edit (T-VM-11/12 struct API) | ±15 |

총 5 파일, ~100 lines net.

### 6.2 영향 받지 않는 영역

- `Throughput chart` (Microcharts ChartView + UpdateThroughputChart 그대로)
- `JsonlPollingAdapter`, `MockPollingAdapter`
- `LibTestTelemetry`
- `FastPortSharp.sln` + 13개 production 프로젝트
- CI workflows
- `TimedDoublePoint` struct (Throughput series가 사용)

### 6.3 CI Impact

dashboard.yml은 Dashboard 변경 시 자동 trigger (직전 cycle에서 도입). macOS + Windows 자동 검증 실행될 예정.

---

## 7. Architecture Considerations

### 7.1 Decisions (Auto mode)

| Decision | Choice | Rationale |
|---|---|---|
| Overlay 방식 | **Custom SKCanvasView (3-line direct draw)** | Microcharts 단일 series 한계 우회, LiveCharts2 crash 회피 |
| Data 구조 | **TimedRttPoint struct (P50/P95/P99)** | 단일 collection으로 3 series 표현, ViewModel API 명확 |
| Color scheme | P50=blue `#2196F3` / P95=orange `#FF9800` / P99=red `#F44336` | Material Design 단계적 warning 색상 |
| Y축 스케일 | max(P99) 기반 자동 | 단순, P50/P95/P99 모두 항상 visible |
| Legend 위치 | 우상단 | Microcharts 패턴 동일 |
| Single commit | Yes | 일관성 |

### 7.2 Open Decisions for Design Phase

- **TimedDoublePoint 보존 vs 통합**: 보존 권장 (Throughput chart 영향 0)
- **PaintSurface invalidate timing**: CollectionChanged → InvalidateSurface immediate vs throttle
- **Legend 위치 동적 조정**: 우상단 고정 vs 사용자 toggle (out of scope)

---

## 8. Convention Prerequisites

- 한국어 주석
- 색상 상수 명시 (RttP50Color, RttP95Color, RttP99Color)
- 단일 commit
- macOS Release crash 회피 → Debug 빌드로만 수동 검증

---

## 9. Next Steps

1. `/pdca design dashboard-multi-rtt-overlay`
   - 3 option:
     - **A**: Custom SKCanvasView, 1 PaintSurface 핸들러 (Recommended)
     - **B**: 3 stacked Microcharts ChartView (cycle 명과 다름)
     - **C**: 별도 `MultiLineChartView` UserControl 추출 (overengineering)
2. `/pdca do dashboard-multi-rtt-overlay` (단일 세션, ≤ 12 turn 추정)
3. `analyze` → `report` → `archive`

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial plan (Auto mode: Custom SKCanvasView 3-line overlay, TimedRttPoint struct, ~100 lines) | boinred |
