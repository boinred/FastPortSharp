---
template: plan
version: 1.3
feature: dashboard-chart-graphicsview-migration
date: 2026-05-11
author: boinred
project: FastPortSharp
---

# dashboard-chart-graphicsview-migration Planning Document

> **Summary**: SkiaSharp 기반 chart (Microcharts.Maui ChartView, SKCanvasView)를 Microsoft.Maui.Graphics + GraphicsView (IDrawable)로 교체하여 macOS 26 SwiftUI Observation SIGSEGV crash trigger를 제거한다.
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
| **Problem** | macOS 26 + .NET 10 MAUI Catalyst 환경에서 SkiaSharp 기반 chart view (`microcharts:ChartView`, raw `SKCanvasView`)를 화면에 올리면 SwiftUI Observation framework가 SIGSEGV로 즉시 죽음. 직전 cycle `dashboard-revert-skcanvasview-keep-data`에서도 회피 실패가 확인됨. |
| **Solution** | SkiaSharp 의존을 완전히 제거하고 Microsoft.Maui.Graphics + `GraphicsView` + `IDrawable.Draw(ICanvas, RectF)`로 RTT P95 라인 차트 1개 + Throughput 라인 차트 1개를 재구현. 데이터 레이어(`ClientRttSeries: TimedRttPoint`, `ThroughputSeries`)와 ViewModel은 그대로 보존. |
| **Function/UX Effect** | (1) macOS Catalyst Debug 실행 시 즉사 crash 사라짐. (2) Windows에서도 동일 차트 렌더링. (3) `Microcharts`와 `SkiaSharp.Views.Maui.Controls` NuGet 의존성 삭제로 빌드 용량↓·workload 표면↓. |
| **Core Value** | 대시보드 cycle 의 baseline 가치(실시간 RTT/Throughput 시각화)를 잃지 않으면서 macOS 26에서 **다시 실행 가능한 상태**로 복귀. SkiaSharp 미사용 → SwiftUI Observation crash 경로 자체 회피. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | SkiaSharp 기반 view가 macOS 26 SwiftUI Observation framework crash trigger임이 직전 cycle에서 100% 재현으로 확인됨. 차트가 없는 빌드는 정상 실행 → 차트 component 자체가 유일한 변수. |
| **WHO** | FastPortSharp 서버 메트릭을 로컬에서 관찰하는 개발자 (macOS Catalyst + Windows 사용). 현재 macOS 사용자는 차트 도입 이후 앱을 전혀 실행하지 못함. |
| **RISK** | (a) `Microsoft.Maui.Graphics`의 IDrawable.Draw 경로도 SwiftUI Observation 경로를 똑같이 trigger할 가능성. (b) 직접 작성한 line chart 가 미세한 좌표/스케일 버그를 가질 수 있음. |
| **SUCCESS** | (1) macOS Catalyst Debug 실행 후 `Connect` (UseMock=true) 시 10초 이상 무 crash + 차트 라인 그려짐. (2) 기존 25개 단위 + E2E 테스트 모두 그대로 통과. (3) `Microcharts.Maui` / `SkiaSharp.Views.Maui.Controls` 참조 0건. |
| **SCOPE** | RTT P95 1-line chart + Throughput 1-line chart만. P50/P99 동시 오버레이는 OOS (data layer는 유지). 축 라벨/그리드는 단순(좌·하단 텍스트 한 줄). |

---

## 1. Overview

### 1.1 Purpose

macOS 26 SwiftUI Observation framework crash로 차트 도입 이후 실행 자체가 불가능해진 `FastPortDashboard.Maui` 앱을 다시 실행 가능한 상태로 복귀시키되, 차트가 제공하던 실시간 시각화 가치는 잃지 않는다.

### 1.2 Background

- `dashboard-rtt-chart` cycle에서 `Microcharts.Maui` (SkiaSharp 기반) 도입 → macOS 26 Catalyst Release crash 첫 발생.
- `dashboard-multi-rtt-overlay`에서 raw `SKCanvasView`로 multi-line 구현 → crash 지속.
- `dashboard-revert-skcanvasview-keep-data`로 `microcharts:ChartView` 복귀했지만 동일 stack(`libswiftObservation.dylib` → SwiftUI `ViewGraph.updateOutputs`)으로 재현.
- 진단 cycle에서 **차트 두 Frame을 XAML에서 주석 처리**하면 crash 사라짐을 확인 → 차트 view = 유일한 trigger.
- 자세한 분석은 memory `maccatalyst-26-swiftui-observation-release-crash.md` 참조.

### 1.3 Related Documents

- 직전 cycle plan: `docs/archive/2026-05/dashboard-revert-skcanvasview-keep-data/`
- E2E pipeline: `docs/archive/2026-05/dashboard-e2e-mock-tests/`
- Memory: `~/.claude/projects/-Users-boinred-dev-githubs-FastPortSharp/memory/maccatalyst-26-swiftui-observation-release-crash.md`

---

## 2. Scope

### 2.1 In Scope

- [ ] `Microcharts.Maui` NuGet 참조 제거 (`FastPortDashboard.Maui.csproj`).
- [ ] `SkiaSharp.Views.Maui.Controls` 등 SkiaSharp 직·간접 의존 제거.
- [ ] `Microsoft.Maui.Graphics` (MAUI 기본 포함) 기반 `GraphicsView` + 자작 `IDrawable` line chart 컴포넌트 1세트.
- [ ] RTT P95 단일 라인 + Throughput 단일 라인 두 개 차트 (기존 시각화 parity).
- [ ] `MainPage.xaml` / `MainPage.xaml.cs` 의 차트 영역을 새 컴포넌트로 교체 + `using SkiaSharp` 제거.
- [ ] macOS Catalyst Debug + Windows 빌드 양쪽 수동 실행 확인.
- [ ] 기존 unit / E2E 테스트 회귀 없음 (data layer는 그대로).

### 2.2 Out of Scope

- P50/P99 동시 overlay (data layer는 보존, 시각화는 후속 cycle).
- Y축 자동 스케일 외의 고급 인터랙션 (zoom/pan/tap tooltip).
- 차트 색상 테마/다크 모드 토큰화.
- Release 빌드 macOS 실행 검증 (memory 기준 단기 회피 불가 → 별도 cycle).

---

## 3. Requirements

### 3.1 Functional Requirements

| ID | Requirement | Priority | Status |
|----|-------------|----------|--------|
| FR-01 | `LineChartDrawable : IDrawable`이 `IReadOnlyList<double>` 시퀀스를 받아 GraphicsView 캔버스에 line + 마지막 값 라벨을 그린다. | High | Pending |
| FR-02 | `RttChartView` / `ThroughputChartView` 두 `GraphicsView`가 `MainPage.xaml`에서 기존 두 ChartView를 정확히 대체한다. | High | Pending |
| FR-03 | `_viewModel.ClientRttSeries` / `ThroughputSeries`의 `CollectionChanged`에 hook하여 `GraphicsView.Invalidate()` 호출로 재렌더. | High | Pending |
| FR-04 | `Microcharts.Maui`, `SkiaSharp.*` 패키지 참조가 `FastPortDashboard.Maui.csproj`에서 0건. | High | Pending |
| FR-05 | macOS Catalyst Debug 실행 시 10초 이상 crash 없이 mock 데이터로 라인이 그려진다. | High | Pending |
| FR-06 | Windows 빌드/실행에서 동일 동작 확인. | Medium | Pending |
| FR-07 | 기존 25개 테스트 (unit + E2E) all green 유지. | High | Pending |

### 3.2 Non-Functional Requirements

| Category | Criteria | Measurement Method |
|----------|----------|-------------------|
| Stability | macOS Catalyst Debug 10초 무 crash | 수동 실행 + `~/Library/Logs/DiagnosticReports/` IPS 없음 확인 |
| Performance | 1초 polling, 60 sample window 기준 frame 누락/지연 체감 없음 | 수동 관찰 |
| Footprint | 빌드 산출물 크기 감소 (Skia native bundle 제거) | `bin/.../*.app` 크기 비교 (참고용) |
| Compatibility | net10.0-maccatalyst + net10.0-windows10 모두 build success | `dotnet build` |

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] FR-01~FR-07 모두 충족.
- [ ] `FastPortDashboard.Maui.csproj`에서 Skia 관련 `<PackageReference>` 0건.
- [ ] `git grep -i "skiasharp\|microcharts" FastPortDashboard.Maui/` 결과 0건 (Resources 메타데이터 제외).
- [ ] macOS Catalyst Debug 빌드 + 실행 → Connect (UseMock=true) → 10초 후 라인 그려짐 + crash 0건 (수동, evidence 스크린샷 또는 로그).
- [ ] `dotnet test` 기존 25개 테스트 모두 통과.
- [ ] 변경된 코드에 Design Ref 주석 (`§{section}`) 부착.

### 4.2 Quality Criteria

- [ ] 빌드 warning 신규 0건.
- [ ] 차트 컴포넌트 코드는 가능한 한 작게 (≤ 200 라인 / file).
- [ ] data → drawable 변환은 ViewModel collection을 직접 의존하지 않고 snapshot list로 받음 (테스트 친화).

---

## 5. Risks and Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| `GraphicsView` 자체도 SwiftUI Observation 경로 진입 → crash 재발 | High | Medium | Design 단계에서 최소 prototype부터 macOS 실행 검증. crash 재현 시 plan abort + memory 업데이트. |
| 라인 좌표/스케일 계산 버그로 시각적 오해 | Medium | Medium | snapshot list 기반 순수 함수로 drawable 분리 → 단위 테스트 추가 가능. |
| `ClientRttSeries` CollectionChanged → 메인 스레드 마샬링 누락 | Medium | Low | 기존 코드 패턴 따라 `Dispatcher` 사용 (현 코드도 직접 호출). |
| `Microsoft.Maui.Graphics` API surface 미숙으로 의외의 layout 이슈 | Low | Medium | HeightRequest 고정 (160) + 부모 Frame 유지로 layout 복잡도 최소화. |

---

## 6. Impact Analysis

### 6.1 Changed Resources

| Resource | Type | Change Description |
|----------|------|--------------------|
| `FastPortDashboard.Maui/FastPortDashboard.Maui.csproj` | csproj | `Microcharts.Maui` PackageReference 제거, Skia 의존 검토 |
| `FastPortDashboard.Maui/MainPage.xaml` | XAML | `microcharts:ChartView` 두 개 → `GraphicsView` 두 개로 교체, `xmlns:microcharts`/`xmlns:skia` 제거 |
| `FastPortDashboard.Maui/MainPage.xaml.cs` | C# | `using Microcharts/SkiaSharp` 제거, `UpdateRttChart`/`UpdateThroughputChart` 재작성 (drawable에 snapshot 전달) |
| `FastPortDashboard.Maui/Views/LineChartDrawable.cs` (신규) | C# | `IDrawable` 구현, 라인 + 마지막 값 라벨 |

### 6.2 Current Consumers

| Resource | Operation | Code Path | Impact |
|----------|-----------|-----------|--------|
| `_viewModel.ClientRttSeries` | READ | `MainPage.xaml.cs` `UpdateRttChart` | Refactor (Skia 표현 → drawable snapshot) |
| `_viewModel.ThroughputSeries` | READ | `MainPage.xaml.cs` `UpdateThroughputChart` | Refactor |
| `ClientRttSeries.CollectionChanged` | EVENT | `MainPage.xaml.cs` ctor | Same handler shape, body 재작성 |
| ViewModel public API | READ | unit + E2E 테스트 25개 | 변경 없음 → 회귀 무영향 기대 |

### 6.3 Verification

- [ ] ViewModel public surface 변경 없음 (테스트 회귀 방지).
- [ ] `csproj`에서 Microcharts 제거 후 `dotnet restore` clean.
- [ ] `git grep skiasharp` 결과 zero (Resources/icons 제외).

---

## 7. Architecture Considerations

### 7.1 Project Level Selection

| Level | Characteristics | Recommended For | Selected |
|-------|-----------------|-----------------|:--------:|
| **Starter** | Simple structure | Static sites | ☐ |
| **Dynamic** | Feature-based modules | Web apps, fullstack | ☑ |
| **Enterprise** | Strict layer separation | High-traffic / microservices | ☐ |

> .NET MAUI desktop dashboard로 PDCA cycle 단위 진화 (Dynamic 유지).

### 7.2 Key Architectural Decisions

| Decision | Options | Selected | Rationale |
|----------|---------|----------|-----------|
| Chart 렌더링 엔진 | SkiaSharp(Microcharts) / Maui.Graphics(IDrawable) / WebView+JS | **Microsoft.Maui.Graphics** | SkiaSharp가 macOS 26 SwiftUI Observation crash trigger로 확인. Maui.Graphics는 .NET native, 추가 native lib 없음 |
| Drawable 단위 | 1개 멀티시리즈 / 시리즈당 1 drawable | **시리즈당 1 drawable** | 단순/테스트 친화, 향후 stacked overlay 확장 시 인터페이스 분리 |
| 데이터 전달 | drawable이 ObservableCollection 직접 참조 / snapshot list 주입 | **snapshot list 주입** | 멀티스레드 안전 + 순수함수성 + 단위 테스트 가능 |
| 재렌더 트리거 | DataBinding / 명시적 `Invalidate()` | **명시적 Invalidate()** | 기존 CollectionChanged 패턴 재사용, 예측 가능 |
| Y축 스케일 | 고정 / 자동 (min~max) | **자동 (마진 10%)** | 범위 변동이 큰 RTT/Throughput 모두 커버 |

### 7.3 Clean Architecture Approach

```
FastPortDashboard.Maui/
├── Views/
│   └── LineChartDrawable.cs   (NEW: IDrawable, snapshot list → ICanvas)
├── MainPage.xaml              (GraphicsView × 2)
└── MainPage.xaml.cs           (snapshot 생성 + Invalidate)

FastPortDashboard.Core/        (불변 — ViewModel/TimedRttPoint 그대로)
```

---

## 8. Convention Prerequisites

### 8.1 Existing Project Conventions

- [x] `CLAUDE.md` (글로벌 한국어 응답 규칙)
- [x] `.editorconfig`
- [x] tests-projects/FastPortDashboardTests/ MSTest 컨벤션

### 8.2 Conventions to Define/Verify

| Category | Current State | To Define | Priority |
|----------|---------------|-----------|:--------:|
| Drawable 작성 패턴 | missing | `IDrawable` 구현은 stateless, snapshot list를 ctor 또는 property로 주입 | High |
| 차트 색상 상수 위치 | `MainPage.xaml.cs`에 SKColor 사용 중 | `Microsoft.Maui.Graphics.Color` 정적 readonly로 이전 | High |

### 8.3 Environment Variables Needed

해당 없음 (UI 기능, 환경 변수 변경 없음).

---

## 9. Next Steps

1. [ ] `/pdca design dashboard-chart-graphicsview-migration` — 3 옵션 (Minimal / Clean / Pragmatic) 비교 후 선택.
2. [ ] Design 단계에서 `LineChartDrawable` 인터페이스/스케일 알고리즘 확정.
3. [ ] `/pdca do …` — 구현 + macOS Catalyst Debug 수동 실행 검증.
4. [ ] `/pdca analyze` — Skia 잔재 grep + 테스트 회귀 확인.
5. [ ] `/pdca report` — Cycle 마무리, memory 업데이트.

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-11 | Initial draft (macOS 26 SwiftUI Observation crash 회피용 chart 재구현 계획) | boinred |
