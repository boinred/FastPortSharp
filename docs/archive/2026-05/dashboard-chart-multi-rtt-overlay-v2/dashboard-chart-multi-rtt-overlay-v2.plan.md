---
template: plan
version: 1.3
feature: dashboard-chart-multi-rtt-overlay-v2
date: 2026-05-11
author: boinred
project: FastPortSharp
---

# dashboard-chart-multi-rtt-overlay-v2 Planning Document

> **Summary**: 직전 cycle에서 SkiaSharp 제거 후 RTT 차트를 P95 단일 라인으로 축소했던 것을, `Microsoft.Maui.Graphics` 기반 multi-series IDrawable로 P50/P95/P99 3-line overlay로 복원한다.
>
> **Project**: FastPortSharp
> **Version**: 0.1.0
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft

---

## Executive Summary

| Perspective | Content |
|-------------|---------|
| **Problem** | `dashboard-chart-graphicsview-migration`에서 SwiftUI Observation crash 회피를 위해 SkiaSharp을 제거하면서 RTT 차트가 P95 단일 라인으로 줄었다. 사용자 가시성 측면에서 percentile 분포(P50 median ↔ P99 tail)를 한 화면에서 비교하던 가치가 사라짐. data layer(`TimedRttPoint.P50/P95/P99`)는 이미 살아있음. |
| **Solution** | 현 `LineChartDrawable`을 series 1개로 보존하되, 새로 `MultiLineChartDrawable : IDrawable`을 도입하여 `IReadOnlyList<LineChartSeries>` 입력을 공통 좌표계로 한 번에 그린다. RTT 차트만 multi-line, Throughput 차트는 단일 라인으로 그대로. SkiaSharp 미사용 (regression 금지). |
| **Function/UX Effect** | RTT 차트에 P50(파랑)/P95(주황)/P99(빨강) 3-line + 우상단 단순 legend가 다시 표시됨. Throughput 시각화는 변동 없음. Y축 범위는 모든 시리즈의 통합 min/max로 자동 스케일. |
| **Core Value** | 직전 cycle에서 잃었던 percentile 분포 가시성 회복 + SkiaSharp 회피책 무결성 유지. data layer는 이미 갖춰져 있어 작업 범위가 좁다 (drawable 1개 + xaml.cs wiring). |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | percentile 분포(P50 vs P99 spread) 가시성은 latency 회귀 진단에 핵심. 직전 cycle의 P95-only 축소는 임시 trade-off. |
| **WHO** | FastPort 메트릭을 macOS Catalyst + Windows에서 관찰하는 개발자. percentile 비교를 통해 tail latency 회귀를 빠르게 감지. |
| **RISK** | (a) multi-series drawable이 SwiftUI Observation crash를 다시 trigger할 가능성(단일 GraphicsView 사용이라 가능성 낮으나 확인 필요), (b) 시리즈 색상/legend 가독성. |
| **SUCCESS** | RTT 차트에 3-line 시각화 + Throughput parity 유지 + 32+ tests green + macOS Catalyst Debug 무 crash + SkiaSharp 잔재 0건. |
| **SCOPE** | RTT 차트 한정 multi-line. Throughput은 손대지 않음. Hover/legend interaction, 축 라벨, grid 라인은 OOS (별도 cycle). |

---

## 1. Overview

### 1.1 Purpose

직전 cycle (`dashboard-chart-graphicsview-migration`)에서 SwiftUI Observation crash 회피를 위해 P95 단일 라인으로 축소한 RTT 차트를, 원래 cycle (`dashboard-multi-rtt-overlay`, SkiaSharp 사용)이 제공하던 P50/P95/P99 3-line overlay로 복원한다. 이번엔 `Microsoft.Maui.Graphics` 기반.

### 1.2 Background

- `dashboard-multi-rtt-overlay` cycle (archived): `SKCanvasView` direct draw로 3-line overlay 구현 → crash trigger.
- `dashboard-chart-graphicsview-migration` cycle (archived 2026-05-11): SkiaSharp 완전 제거 + GraphicsView/IDrawable 도입, 단 시각화는 P95 단일 라인으로 축소 (single-series LineChartDrawable).
- data layer는 그대로: `TimedRttPoint(TimestampUnixMs, P50Ms, P95Ms, P99Ms)`가 `ClientRttSeries`에 들어옴. `ApplyClientSnapshot`이 3 percentile 모두 보존.
- 따라서 본 cycle은 drawable 확장(또는 신규)만으로 가치 복원 가능.

### 1.3 Related Documents

- `docs/archive/2026-05/dashboard-chart-graphicsview-migration/` — SkiaSharp 회피책 검증된 cycle.
- `docs/archive/2026-05/dashboard-multi-rtt-overlay/` — 원조 3-line overlay (SkiaSharp 기반, 참고용).
- Memory: `~/.claude/projects/-Users-boinred-dev-githubs-FastPortSharp/memory/maccatalyst-26-swiftui-observation-release-crash.md`.

---

## 2. Scope

### 2.1 In Scope

- [ ] 신규 `MultiLineChartDrawable : IDrawable` 또는 기존 `LineChartDrawable`의 multi-series 확장 (Design에서 결정).
- [ ] `LineChartSeries` (record) — 색상 + 값 시퀀스 + 라벨을 가진 단위.
- [ ] `RttChartView.Drawable`을 multi-series drawable로 교체.
- [ ] `UpdateRttChart`가 ClientRttSeries → 3 series(P50/P95/P99)로 snapshot 변환.
- [ ] 통합 Y축 자동 스케일 (모든 시리즈 min/max).
- [ ] 우상단 단순 legend: "P50 P95 P99" 색상 막대 + 라벨.
- [ ] Throughput 차트는 손대지 않음.
- [ ] 신규 `MultiLineChartMath` 또는 `LineChartMath` 확장에 대한 단위 테스트.
- [ ] 32개 기존 테스트 회귀 0.
- [ ] macOS Catalyst Debug 실행 시 차트 정상 + crash 0건 (수동 검증).

### 2.2 Out of Scope

- Hover/tooltip interaction.
- Y축 텍스트 라벨, grid 라인.
- legend의 시리즈 toggle (show/hide).
- Throughput multi-series 확장.
- SkiaSharp 재도입 (금지).

---

## 3. Requirements

### 3.1 Functional Requirements

| ID | Requirement | Priority | Status |
|----|-------------|----------|--------|
| FR-01 | RTT 차트에 P50/P95/P99 3-line이 동일 좌표계에 overlay되어 표시된다. | High | Pending |
| FR-02 | 색상: P50 `#2196F3` blue, P95 `#FF9800` orange, P99 `#F44336` red (이전 cycle Material 유지). | High | Pending |
| FR-03 | Y축 스케일은 3 series 통합 min/max로 자동 (단일 series scale 금지). | High | Pending |
| FR-04 | RTT 차트 우상단에 simple legend (3개 색상 점/막대 + "P50 P95 P99" 텍스트) 표시. | Medium | Pending |
| FR-05 | Throughput 차트 동작·시각 변경 0. | High | Pending |
| FR-06 | SkiaSharp/Microcharts using·PackageReference 신규 0건. | High | Pending |
| FR-07 | 기존 32 테스트 회귀 0 + 신규 multi-line math 테스트 추가. | High | Pending |
| FR-08 | macOS Catalyst Debug 10초+ 무 crash + 3-line 정상 렌더 (수동). | High | Pending |

### 3.2 Non-Functional Requirements

| Category | Criteria | Measurement Method |
|----------|----------|-------------------|
| Stability | macOS Catalyst Debug 무 crash (`~/Library/Logs/DiagnosticReports/` clean) | 수동 + IPS 확인 |
| Performance | 1초 polling × 60 sample window 기준 frame drop 체감 없음 | 수동 관찰 |
| Footprint | drawable + math 파일 합산 ≤ 250 라인 | `wc -l` |
| Compatibility | net10.0-maccatalyst + net10.0-windows10 build success | `dotnet build` |

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] FR-01~FR-08 모두 충족.
- [ ] `MultiLineChartDrawable` (또는 확장된 `LineChartDrawable`) 구현 + Design Ref 주석.
- [ ] `git grep -i "skiasharp\|microcharts" FastPortDashboard.Maui/ FastPortDashboard.Core/` 신규 0건 (기존 주석 외).
- [ ] `dotnet test` ≥ 32 + 신규 추가 테스트 모두 통과.
- [ ] macOS Catalyst Debug 실행 evidence (사용자 confirm).

### 4.2 Quality Criteria

- [ ] 빌드 warning 신규 0건.
- [ ] 변경된 코드에 `// Design Ref: §X` 주석.
- [ ] Y축 스케일 함수 (`ComputeRange` 멀티 시리즈)는 순수 함수 + Core에 위치 + 단위 테스트.
- [ ] 시리즈 0개 / 비어있는 시리즈 / 일부 비어있는 시리즈 입력에 대해 예외 없이 처리.

---

## 5. Risks and Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Multi-series drawable이 SwiftUI Observation crash 재유발 | High | Low | 단일 `GraphicsView` + `IDrawable` 패턴 유지(직전 cycle에서 검증됨). manual verify. |
| 3-line이 시각적으로 너무 빽빽 (가독성↓) | Medium | Medium | LineWidth 차등(P50=1.5, P95=2, P99=1.5) + 색상 contrast 검증. |
| Y축 통합 스케일이 outlier P99 spike에 끌려 P50/P95가 평탄해 보임 | Medium | Medium | 본 cycle scope에선 raw scale 우선. 후속 cycle에서 log-scale/clipping 옵션 고려. |
| 시리즈 길이가 다른 경우 (P50만 늦게 들어오는 등) | Low | Low | x축은 series.Count 기반 stepX (각 series 독립), 또는 max(count) 기준. data layer는 사실상 동시 update이므로 risk 낮음. |

---

## 6. Impact Analysis

### 6.1 Changed Resources

| Resource | Type | Change Description |
|----------|------|--------------------|
| `FastPortDashboard.Maui/Views/LineChartDrawable.cs` | C# | Single-series는 그대로 보존 (Throughput용). |
| `FastPortDashboard.Maui/Views/MultiLineChartDrawable.cs` (신규) | C# | N-series IDrawable. |
| `FastPortDashboard.Core/Charts/LineChartMath.cs` | C# | `ComputeRangeMulti(IEnumerable<IReadOnlyList<double>>)` 추가 (또는 별도 `MultiLineChartMath`). |
| `FastPortDashboard.Maui/MainPage.xaml.cs` | C# | `_rttDrawable`을 multi-series drawable로 교체, `UpdateRttChart` 3-series 변환 + legend. |
| `tests-projects/FastPortDashboardTests/Charts/LineChartMathTests.cs` | C# | `ComputeRangeMulti` 단위 테스트 추가. |

### 6.2 Current Consumers

| Resource | Operation | Code Path | Impact |
|----------|-----------|-----------|--------|
| `ClientRttSeries` (TimedRttPoint) | READ | `MainPage.UpdateRttChart` | 변환 로직 확장 (P50/P95/P99 모두 추출) |
| `LineChartDrawable` | USE | `MainPage` Throughput용 | 변경 0 (single-series 유지) |
| `LineChartMath.ComputeRange` | USE | `LineChartDrawable.Draw` | 변경 0 |
| `MultiLineChartDrawable` | USE | `MainPage` RTT용 | 신규 |

### 6.3 Verification

- [ ] Throughput 시각/동작 변경 0건 (시각 회귀 방지).
- [ ] LineChartDrawable 단일-시리즈 동작 변경 0건.
- [ ] ClientRttSeries `TimedRttPoint` 시그너처 변경 0건.

---

## 7. Architecture Considerations

### 7.1 Project Level Selection

| Level | Selected |
|-------|:--------:|
| Starter | ☐ |
| **Dynamic** | ☑ |
| Enterprise | ☐ |

### 7.2 Key Architectural Decisions

| Decision | Options | Tentative | Rationale |
|----------|---------|-----------|-----------|
| 단일/멀티 drawable 통합 여부 | `LineChartDrawable` 확장(Series property) / 별도 `MultiLineChartDrawable` | Design에서 결정 | 가독성/회귀 위험 균형. 신규 drawable은 single-line 회귀 0 보장. |
| Series 모델 | tuple / record / class | record `LineChartSeries(Color, IReadOnlyList<double>, string Label, float LineWidth)` | 불변 + 패턴 매칭 + 단순 |
| Y축 스케일 | 시리즈별 / 통합 | 통합 | overlay 의미상 통합 필수 |
| Legend 구현 | 별도 GraphicsView / drawable 내부 / XAML Label | drawable 내부 | 차트와 좌표계 공유, 단순 |

### 7.3 Clean Architecture Approach

```
FastPortDashboard.Core/Charts/
├── LineChartMath.cs        (확장: ComputeRangeMulti)
FastPortDashboard.Maui/Views/
├── LineChartDrawable.cs    (불변, Throughput 용 single-series)
├── MultiLineChartDrawable.cs   (NEW, RTT 용 N-series)
└── LineChartSeries.cs      (NEW record, MultiLine 입력 모델)
```

---

## 8. Convention Prerequisites

### 8.1 Existing Project Conventions

- [x] 직전 cycle의 `// Design Ref: §X` 주석 패턴.
- [x] 순수 함수 → Core, MAUI 의존 → Maui 분리.
- [x] MSTest 컨벤션.

### 8.2 Conventions to Define/Verify

| Category | Current State | To Define | Priority |
|----------|---------------|-----------|:--------:|
| Series 색상 상수 위치 | `MainPage.xaml.cs` 단일 RTT/Throughput 두 개 | RTT 3-percentile 색상 + Throughput 색상 한 곳에 통합 (`MainPage.xaml.cs` 상단) | High |
| Legend 텍스트 포맷 | 없음 | `P50` / `P95` / `P99` 영문 + 시리즈와 동일 색상 | Medium |

### 8.3 Environment Variables Needed

해당 없음.

---

## 9. Next Steps

1. [ ] `/pdca design dashboard-chart-multi-rtt-overlay-v2` — 단일 drawable 확장 vs 분리 (Option A/B/C) 결정.
2. [ ] Design에서 `LineChartSeries` 시그너처 + legend layout 확정.
3. [ ] `/pdca do …` — drawable + math + xaml.cs wiring + 테스트.
4. [ ] `/pdca analyze` — Throughput 회귀 검증 + manual macOS Catalyst Debug.
5. [ ] `/pdca report` + archive.

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-11 | Initial draft (SkiaSharp-free P50/P95/P99 multi-line overlay 복원 계획) | boinred |
