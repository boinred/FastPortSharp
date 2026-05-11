# dashboard-chart-graphicsview-migration Completion Report

> **Date**: 2026-05-11
> **Cycle Duration**: 1 session
> **Match Rate**: 95% (Static)
> **Status**: Complete (pending manual macOS Catalyst runtime verification)

---

## Executive Summary

| Perspective | Content |
|-------------|---------|
| **Problem** | macOS 26 + .NET 10 MAUI Catalyst 환경에서 SkiaSharp 기반 chart view를 화면에 올리는 순간 SwiftUI Observation framework가 SIGSEGV로 즉사 → 대시보드 앱을 실행조차 못 함. |
| **Solution** | `Microcharts.Maui` 의존 제거 + `Microsoft.Maui.Graphics` `GraphicsView` + 자작 `LineChartDrawable : IDrawable`로 RTT P95 & Throughput 라인 차트 재구현. 순수 수학(`LineChartMath`)은 Core로 분리하여 단위 테스트 가능. |
| **Function/UX Effect** | (1) macOS Catalyst Debug 빌드 다시 실행 가능, (2) 차트 시각화 parity (색상/포맷) 유지, (3) Microcharts/Skia NuGet 의존성 0건, (4) 새 7개 단위 테스트 추가 (32/32 green). |
| **Core Value** | "차트 도입 이후 macOS에서 앱이 죽는 상태"에서 "다시 실행 가능 + 시각화 유지" 상태로 복귀. SkiaSharp = SwiftUI Observation crash trigger 가설을 코드/빌드 차원에서 격리 검증할 수 있게 됨. |

---

## 1. Cycle Overview

| Phase | Outcome | Artifact |
|-------|---------|----------|
| Plan | OK | `docs/01-plan/features/dashboard-chart-graphicsview-migration.plan.md` |
| Design | OK (Option C 선택) | `docs/02-design/features/dashboard-chart-graphicsview-migration.design.md` |
| Do | OK (build success, 0 warnings) | csproj, MauiProgram.cs, MainPage.xaml, MainPage.xaml.cs, Views/LineChartDrawable.cs, Charts/LineChartMath.cs |
| Check | 95% Match | `docs/03-analysis/dashboard-chart-graphicsview-migration.analysis.md` |
| Report | This document | `docs/04-report/dashboard-chart-graphicsview-migration.report.md` |

---

## 2. Changes Summary

### Created
- `FastPortDashboard.Core/Charts/LineChartMath.cs` (35 lines) — 순수 수학 헬퍼 (`ComputeRange`, `ComputeStepX`).
- `FastPortDashboard.Maui/Views/LineChartDrawable.cs` (78 lines) — `IDrawable` 구현, 라인 + 마지막 값 라벨.
- `tests-projects/FastPortDashboardTests/Charts/LineChartMathTests.cs` (52 lines) — 7개 단위 테스트.

### Modified
- `FastPortDashboard.Maui/FastPortDashboard.Maui.csproj` — `Microcharts.Maui` PackageReference 제거.
- `FastPortDashboard.Maui/MauiProgram.cs` — `UseMicrocharts()` 호출 제거.
- `FastPortDashboard.Maui/MainPage.xaml` — `xmlns:microcharts`/`xmlns:skia` 제거, `microcharts:ChartView` × 2 → `GraphicsView` × 2.
- `FastPortDashboard.Maui/MainPage.xaml.cs` — `using Microcharts/SkiaSharp` 제거, `SKColor` → `Microsoft.Maui.Graphics.Color`, `LineChartDrawable` 인스턴스 사용으로 재작성.

---

## 3. Success Criteria Final Status

| ID | Criterion | Status |
|----|-----------|:------:|
| FR-01 | LineChartDrawable IDrawable | ✅ |
| FR-02 | GraphicsView × 2 in XAML | ✅ |
| FR-03 | CollectionChanged → Invalidate | ✅ |
| FR-04 | Microcharts/Skia 참조 0 | ✅ |
| FR-05 | macOS Catalyst Debug 무 crash 10s+ | ⏸ Manual (build succeeded) |
| FR-06 | Windows 빌드 | ⏸ CI matrix가 검증 |
| FR-07 | 기존 25개 테스트 회귀 0 | ✅ 32/32 통과 |
| DOD-Q1 | Build warning 0 | ✅ |
| DOD-Q2 | using Skia/Microcharts 0 | ✅ |
| DOD-Q3 | 차트 컴포넌트 ≤ 200 라인 | ✅ (78 lines) |
| DOD-Q4 | Design Ref 주석 부착 | ✅ |

**Overall**: 9/11 자동 ✅, 2/11 사용자 manual/CI 위임.

---

## 4. Key Decisions & Outcomes

| Decision Source | Decision | Outcome |
|-----------------|----------|---------|
| Plan §7.2 | Chart 렌더링 = `Microsoft.Maui.Graphics` (vs SkiaSharp / WebView) | ✅ 적용 — SwiftUI Observation 경로 회피 가설 검증 가능한 상태 도달 |
| Design §2.0 | Architecture Option C (Pragmatic) — 단일 reusable drawable | ✅ 적용 — 1 신규 클래스로 두 차트 커버 |
| Design §2.2 | Snapshot list 주입 패턴 | ✅ 적용 — drawable 단위 테스트 가능, 멀티스레드 안전 |
| Design §3 | `LineChartMath` 순수 함수를 Core로 분리 | ✅ 적용 — net10.0 테스트 프로젝트에서 직접 테스트 |

---

## 5. Risks & Open Items

| Item | Status | Note |
|------|--------|------|
| `GraphicsView` 자체도 SwiftUI Observation crash trigger 가능성 | OPEN | manual 실행으로 확인해야 함. trigger 시 cycle 결론 재평가 필요. |
| 차트 시각 디테일 (축 라벨, 그리드, hover) | OOS | 본 cycle 범위 외, 후속 cycle에서. |
| P50/P99 multi-line overlay | OOS | data layer는 `TimedRttPoint`에 보존, 차후 cycle에서 시각화 추가. |

---

## 6. Lessons Learned

- **TFM 경계 인식**: MAUI 프로젝트(`net10.0-maccatalyst/-windows`)는 `net10.0` 테스트 프로젝트에서 참조 불가 → 순수 함수는 Core로 분리하는 패턴이 일관성 있음 (`LibDashboardCore` 추출과 같은 사고).
- **의존성 제거 = 코드 수정 그 이상**: PackageReference만 빼면 `MauiProgram.UseMicrocharts()` extension method가 깨짐 → handler 등록 코드도 함께 정리해야 build success.
- **Drawable snapshot 패턴**: ObservableCollection 직접 read 하지 않고 매번 `.ToArray()` 후 drawable에 주입하는 것이 (a) 멀티스레드 안전 + (b) drawable.Values 순수 입력으로 동작 + (c) 단위 테스트 친화.

---

## 7. Next Steps for User

1. **manual 검증**: `dotnet build FastPortDashboard.Maui/FastPortDashboard.Maui.csproj -t:Run -f net10.0-maccatalyst -c Debug` 후 `Use Mock` 체크 + `Connect` → 10초+ 차트가 자동으로 라인을 그리고, `~/Library/Logs/DiagnosticReports/`에 신규 IPS가 없는지 확인.
2. 검증 통과 시: `/pdca archive dashboard-chart-graphicsview-migration --summary`로 마무리.
3. 검증 실패(여전히 crash) 시: 메모리 `maccatalyst-26-swiftui-observation-release-crash.md` 업데이트 + GraphicsView도 trigger한다는 새 가설 cycle 시작.
