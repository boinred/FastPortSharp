# dashboard-revert-skcanvasview-keep-data Plan

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft

---

## Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | 직전 cycle `dashboard-multi-rtt-overlay`에서 추가한 `skia:SKCanvasView`가 macOS 26 SwiftUI Observation crash를 Debug 빌드에서도 trigger. 이전엔 Debug 정상이었으나 SKCanvasView 직접 사용 이후 즉시 SIGSEGV. |
| **Solution** | UI를 `microcharts:ChartView` (P95 단일 line) 복귀. ViewModel data layer (`TimedRttPoint` + ClientRttSeries struct collection)는 그대로 보존 → 향후 P50/P99 시각화는 별도 안전 cycle로. |
| **Function/UX/Effect** | Debug 실행 정상화. RTT chart는 P95만 visible. P50/P99 데이터는 ViewModel state에 살아있어 향후 활용 가능. |
| **Core Value** | Crash 회피 + 데이터 layer 진화 보존 (revert 후 다시 build-up 불필요). |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Debug crash 즉시 해결. UI는 안전한 Microcharts ChartView로 복귀. Data는 multi-percentile 유지. |
| **WHO** | boinred + 미래 contributor. |
| **RISK** | (R-1) Revert로 인한 unintended diff / (R-2) test 회귀 / (R-3) Throughput chart 영향 |
| **SUCCESS** | Debug 실행 정상 + RTT P95 chart 갱신 + 빌드 0/0 + 20 tests 회귀 0 |
| **SCOPE** | MainPage.xaml + MainPage.xaml.cs만. TimedRttPoint + ViewModel + Tests 변경 0 |

---

## 1. Scope

### 1.1 In Scope

| 영역 | 작업 |
|---|---|
| `MainPage.xaml` | `<skia:SKCanvasView>` → `<microcharts:ChartView x:Name="RttChartView">`. xmlns:skia는 제거 안 함 (향후 활용 가능) |
| `MainPage.xaml.cs` | `OnRttCanvasPaintSurface` + `DrawLegend` 제거. `UpdateRttChart()` 복원 (`p.P95Ms` 사용). RttP50/P95/P99Color 상수 P95만 남기고 정리 (또는 보존). `ClientRttSeries.CollectionChanged` → `UpdateRttChart()` (InvalidateSurface 아님) |

### 1.2 Out of Scope

- `TimedRttPoint.cs` (보존)
- `DashboardViewModel.ClientRttSeries` 타입 (`<TimedRttPoint>` 보존)
- `ApplyClientSnapshot` (P50/P95/P99 store 동작 보존)
- Tests (T-VM-11/12 그대로 통과 — `p.P95Ms` 접근 동일)
- Throughput chart
- FastPortSharp.sln

---

## 2. Success Criteria

- [ ] MainPage.xaml SKCanvasView → ChartView 복귀
- [ ] MainPage.xaml.cs UpdateRttChart 복원 + OnRttCanvasPaintSurface 제거
- [ ] TimedRttPoint + ViewModel 변경 0
- [ ] Tests 변경 0, 20/0/0 회귀 0
- [ ] Dashboard 빌드 0/0
- [ ] FastPortSharp.sln 회귀 0
- [ ] Debug 실행 시 crash 없음 (수동 확인)
- [ ] 단일 commit

---

## 3. Next Steps

1. Design (Option A — 직접 revert)
2. Do
3. Verify crash 해결
4. Analyze / Report / Archive
