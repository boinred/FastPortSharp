# dashboard-chart-multi-rtt-overlay-v2 Completion Report

> **Date**: 2026-05-11
> **Match Rate**: 95% (Static)
> **Status**: Complete (pending manual macOS Catalyst verification)

---

## Executive Summary

| Perspective | Content |
|-------------|---------|
| **Problem** | 직전 cycle (`dashboard-chart-graphicsview-migration`)에서 SwiftUI Observation crash 회피 trade-off로 RTT가 P95 단일 라인으로 축소 → percentile 분포 가시성 상실. |
| **Solution** | 신규 `MultiLineChartDrawable : IDrawable` + `LineChartSeries` record 도입. `LineChartMath.ComputeRangeMulti` Core 헬퍼로 통합 Y축 스케일. 기존 single-line `LineChartDrawable`은 Throughput용으로 0줄 변경. |
| **Function/UX Effect** | RTT 차트에 P50(blue) / P95(orange) / P99(red) 3-line + 우상단 색상 legend 복원. Throughput 차트 변경 0. Build warning 0 + 37 tests pass (5 신규). |
| **Core Value** | percentile 가시성 복원 + SkiaSharp 회피책 무결성 유지 + Throughput 회귀 위험 0 (분리 신규 drawable 패턴). |

---

## 1. Changes Summary

### Created
- `FastPortDashboard.Maui/Views/LineChartSeries.cs` (8 lines) — record (Color, Values, Label, LineWidth).
- `FastPortDashboard.Maui/Views/MultiLineChartDrawable.cs` (99 lines) — N-series IDrawable + 우상단 legend.

### Modified
- `FastPortDashboard.Core/Charts/LineChartMath.cs` — `ComputeRangeMulti(IEnumerable<IReadOnlyList<double>>)` 추가 (+38 lines).
- `FastPortDashboard.Maui/MainPage.xaml.cs` — `_rttMultiDrawable` 사용, RTT 3-series snapshot 생성, P50/P95/P99 색상 상수 3개 추가. Throughput 경로 무변경.
- `tests-projects/FastPortDashboardTests/Charts/LineChartMathTests.cs` — `ComputeRangeMulti` 5 tests 추가.

### Untouched (parity 보장)
- `FastPortDashboard.Maui/Views/LineChartDrawable.cs` — 0줄 변경 (Throughput용).
- `FastPortDashboard.Core/ViewModels/*`, `FastPortDashboard.Maui/MainPage.xaml` — 변경 없음.

---

## 2. Success Criteria Final

| ID | Status |
|----|:------:|
| FR-01 3-line overlay | ✅ |
| FR-02 Material 색상 | ✅ |
| FR-03 통합 Y축 | ✅ |
| FR-04 Legend | ✅ |
| FR-05 Throughput parity | ✅ |
| FR-06 Skia 신규 0 | ✅ |
| FR-07 Tests 회귀 0 + 신규 | ✅ 37/37 (32+5) |
| FR-08 Manual macOS crash 0 | ⏸ Manual |
| DOD warning 0 | ✅ |
| DOD ≤ 250 lines | ✅ 138 lines |
| DOD Design Ref 주석 | ✅ |

10/11 자동 ✅, 1 manual.

---

## 3. Key Decisions & Outcomes

| Source | Decision | Outcome |
|--------|----------|---------|
| Design §2.0 | Option B (분리 신규 drawable) | ✅ Throughput 0줄 변경 보장 |
| Design §3.1 | `LineChartSeries` record | ✅ immutable + 매 update 새 인스턴스 |
| Design §3.3 | `ComputeRangeMulti` Core 분리 | ✅ net10.0 단위 테스트 가능 (5 tests) |
| Design §5.2 | Legend drawable 내부, 우상단 right-aligned | ✅ `DrawLegend` 구현 |

---

## 4. Lessons Learned

- **분리 신규 drawable의 회귀 안전성**: 직전 cycle의 검증된 `LineChartDrawable`을 손대지 않고 새 클래스로 multi-line 추가 → Throughput 시각 회귀 가능성을 코드 차원에서 0으로 보장 가능.
- **legend 폭 휴리스틱**: `ICanvas`에는 텍스트 측정 API가 일관되지 않음. 짧은 라벨에는 `length * charWidthApprox` 추정으로 충분, 긴 라벨에선 재검토 필요.
- **순수 함수 일관성**: `ComputeRangeMulti`도 single-version과 동일하게 Core에 배치 → 새 단위 테스트가 즉시 가능했고 신규 5 tests를 1분 안에 작성/통과.

---

## 5. Next Steps for User

1. **manual 검증**: macOS Catalyst Debug 실행 + `UseMock=true` + Connect → RTT 차트에 3-line + legend 확인, 10초+ crash 0.
2. 통과 시: `/pdca archive dashboard-chart-multi-rtt-overlay-v2 --summary`로 마무리.
3. 후속 cycle 후보: Y축 라벨/grid, log-scale 옵션, legend 시리즈 toggle, hover tooltip.
