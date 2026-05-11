# dashboard-revert-skcanvasview-keep-data Analysis

> **Project**: FastPortSharp · **Date**: 2026-05-11 · **Commit**: `667ca17`

## Match Rate: 100% (static) — 단, 가정 무효화

| Axis | Score |
|---|---|
| Structural | 100% (xaml + xaml.cs 정확히 revert) |
| Functional | 100% (data layer 보존, UI 안전 복귀) |
| Contract | 100% (Dashboard 0/0, 20/0/0 회귀 0) |
| Runtime | N/A — **revert 가설 무효화** (crash 재현) |

## Plan SC

| # | Criterion | Status |
|---|---|---|
| SC-1 | SKCanvasView → ChartView 복귀 | ✅ |
| SC-2 | UpdateRttChart 복원 + OnRttCanvasPaintSurface 제거 | ✅ |
| SC-3 | TimedRttPoint + ViewModel 변경 0 | ✅ |
| SC-4 | Tests 변경 0, 20/0/0 회귀 0 | ✅ |
| SC-5 | Dashboard 빌드 0/0 | ✅ |
| SC-6 | FastPortSharp.sln 회귀 0 | ✅ |
| SC-7 | Debug crash 없음 | ❌ **Not Met — crash 재현** |
| SC-8 | 단일 commit | ✅ |

**핵심 발견**: Plan 가설 (SKCanvasView가 crash trigger)이 **반증**. 동일 macOS 26 SwiftUI Observation crash가 ChartView 복귀 후에도 재현. 이전 "Debug 정상" 관찰은 우연.

## Gap List

| Severity | Gap | Recommendation |
|---|---|---|
| Critical | SC-7 Debug crash 미해결 | Plan 가설 오류. Crash는 baseline UIKit-on-Catalyst 문제 (SKCanvasView와 무관). 다음 cycle `dashboard-e2e-mock-tests`로 verification 경로 전환. |
| Minor | xmlns:skia + P50/P99 color 상수 미사용 | 의도적 보존 (향후 안전한 multi-line 도입 시 재활용) |

## Conclusion

Code-level revert는 정확하게 수행됐고 test 회귀 0. 그러나 의도한 효과(crash 해결)는 달성 못함 — 가설이 틀린 cycle. **Lesson 가치는 있음**: macOS 26 SwiftUI Observation crash가 SKCanvasView/ChartView/특정 UI 라이브러리와 무관한 baseline issue임을 확정.

Critical Gap (SC-7)은 본 cycle scope로 해결 불가 → 다음 cycle (`dashboard-e2e-mock-tests`)에서 verification 경로 자체를 UI에서 headless로 전환.
