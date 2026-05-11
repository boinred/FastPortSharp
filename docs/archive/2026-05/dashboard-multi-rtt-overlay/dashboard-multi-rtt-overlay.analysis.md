# dashboard-multi-rtt-overlay Analysis

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Check Complete
> **Plan**: `docs/01-plan/features/dashboard-multi-rtt-overlay.plan.md`
> **Design**: `docs/02-design/features/dashboard-multi-rtt-overlay.design.md`
> **Commit**: `cbca03b`

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Tail latency (P95/P99) 분석은 게임서버 핵심. 단일 P95만으론 distribution 모름. |
| **WHO** | boinred + 미래 contributor + 게임서버 운영자. |
| **RISK** | (R-1) custom Skia / (R-2) 좌표 변환 / (R-3) test 회귀 / (R-4) Y축 스케일 / (R-6) macOS Release crash |
| **SUCCESS** | 3 line overlay + Mock 갱신 + 빌드 0/0 + 20 tests 회귀 0 + 회귀 sln 0 |
| **SCOPE** | Core (TimedRttPoint + ViewModel) + Maui (XAML + code-behind) + Tests 갱신 |

---

## 1. Match Rate Summary

| Axis | Score | Notes |
|---|---|---|
| **Structural** | 100% | 5 파일 변경 (Plan §6.1 정확히 매칭, TimedRttPoint 신규 + ViewModel + XAML + code-behind + tests) |
| **Functional** | 100% | TimedRttPoint struct + ApplyClientSnapshot P50/P95/P99 store + SKCanvasView direct draw + legend |
| **Contract (Build/Test)** | 100% | Dashboard 0/0, 20/0/0 회귀 0, 회귀 sln 0/0, 139/0/0 |
| **Runtime** | 100% | 20 tests 실행, 755ms |
| **Overall (runtime-weighted)** | **100%** | (Structural × 0.15) + (Functional × 0.25) + (Contract × 0.25) + (Runtime × 0.35) |

---

## 2. Plan Success Criteria Status

| # | Criterion | Status | Evidence |
|---|---|---|---|
| SC-1 | TimedRttPoint.cs 신규 | ✅ Met | `FastPortDashboard.Core/ViewModels/TimedRttPoint.cs` |
| SC-2 | ViewModel ClientRttSeries 타입 변경 + ApplyClientSnapshot 갱신 | ✅ Met | `ObservableCollection<TimedRttPoint>`, P50/P95/P99 store |
| SC-3 | XAML SKCanvasView 추가 + xmlns | ✅ Met | `xmlns:skia` + `<skia:SKCanvasView x:Name="RttCanvasView" PaintSurface="...">` |
| SC-4 | code-behind PaintSurface + 3-line draw + legend | ✅ Met | `OnRttCanvasPaintSurface` + `DrawLegend` ~95 lines |
| SC-5 | T-VM-11/T-VM-12 갱신 | ✅ Met | TimedRttPoint API 사용, P50/P95/P99 모두 검증 |
| SC-6 | Dashboard 빌드 0/0 | ✅ Met | 0 errors (2 SkiaSharp warnings 무해) |
| SC-7 | Dashboard test 20/0/0 | ✅ Met | 755ms |
| SC-8 | FastPortSharp.sln 회귀 0 | ✅ Met | 0/0 + 139/0/0 |
| SC-9 | macOS Catalyst Debug 실행 3-line overlay 갱신 (수동) | 🔲 Pending | 사용자 확인 |
| SC-10 | 단일 commit | ✅ Met | `cbca03b` |

**Met**: 9/10 | **Pending**: 1 | **Not Met**: 0

---

## 3. Functional Deep-Dive

### 3.1 Architecture Boundary 유지

| Layer | UI lib 의존 |
|---|---|
| `FastPortDashboard.Core` (TimedRttPoint, ViewModel) | **0** (도메인 데이터만, struct 단순) |
| `FastPortDashboard.Maui` (XAML + code-behind) | SkiaSharp.Views.Maui + SkiaSharp |
| `FastPortDashboardTests` | **0** (테스트는 ViewModel state만 검증) |

→ 직전 cycle (dashboard-rtt-chart, throughput-chart) 동일 패턴 유지. Core lib는 UI lib 의존 0.

### 3.2 Direct Skia Drawing 핵심 로직

```csharp
// Y축 자동 스케일 — max(P99) × 1.1
foreach (var p in series)
    if (p.P99Ms > maxY) maxY = (float)p.P99Ms;
maxY *= 1.1f;

// 3 line draw helper (closure)
void DrawLine(Func<TimedRttPoint, double> getValue, SKColor color)
{
    paint.Color = color;
    using var path = new SKPath();
    for (int i = 0; i < series.Count; i++)
    {
        float x = padLeft + i * xStep;
        float y = padTop + chartH - (float)(getValue(series[i]) / maxY) * chartH;
        if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
    }
    canvas.DrawPath(path, paint);
}

DrawLine(p => p.P50Ms, RttP50Color);  // blue
DrawLine(p => p.P95Ms, RttP95Color);  // orange
DrawLine(p => p.P99Ms, RttP99Color);  // red
```

→ P50/P95/P99 모두 동일 Y축 스케일 (max(P99) 기준)으로 항상 visible.

### 3.3 Variance Analysis (Plan/Design vs 실제)

| 영역 | Plan/Design 예상 | 실제 | Δ |
|---|---|---|---|
| 파일 변경 | 5 | 5 | 0 |
| TimedRttPoint.cs | ~6 | 8 | +2 (header comment) |
| ViewModel edit | ±5 | ±6 | 무해 |
| XAML edit | ±5 | ±5 | 0 |
| MainPage.xaml.cs edit | ±70 | ±85 (-20 + 105) | 약 +15 (DrawLegend 분리 + 주석) |
| Tests edit | ±15 | ±10 | -5 (간결) |

→ 전체 net ~100 lines 추정과 일치.

### 3.4 Color Scheme Coherence

| Series | Color | Hex | Material Sense |
|---|---|---|---|
| P50 (median) | blue | `#2196F3` | 정상 (Material Primary) |
| P95 (tail) | orange | `#FF9800` | 주의 (Material Warning) |
| P99 (extreme tail) | red | `#F44336` | 위험 (Material Error) |
| Throughput | green | `#4CAF50` | 정상 throughput |

→ 시각적 일관성: blue < orange < red gradient로 latency severity 직관적 표현.

---

## 4. Decision Record Verification

| Decision | Followed? | Evidence |
|---|---|---|
| [Plan] Overlay 방식: Custom SKCanvasView | ✅ | `OnRttCanvasPaintSurface` + 3 line direct draw |
| [Plan] Data 구조: TimedRttPoint struct | ✅ | record struct (P50/P95/P99) |
| [Plan] Color scheme (blue/orange/red) | ✅ | line 11-13 |
| [Plan] TimedDoublePoint 보존 | ✅ | Throughput series 그대로 사용 |
| [Plan] 단일 commit | ✅ | `cbca03b` |
| [Design] Option A — Custom SKCanvasView | ✅ | XAML SKCanvasView + PaintSurface 핸들러 |
| [Design] Y축 max(P99) × 1.1 | ✅ | `maxY *= 1.1f` |
| [Design] Legend 우상단 | ✅ | `legendX = info.Width - 76f` |
| [Design] InvalidateSurface on CollectionChanged | ✅ | line 23 |

---

## 5. Gap List

### Severity: Critical
없음.

### Severity: Important
없음.

### Severity: Minor

| # | Gap | Location | Recommendation |
|---|---|---|---|
| G-1 | SkiaSharp OpenGLES warning | Build output | 무해, SDK 자체 패턴 |
| G-2 | macOS Catalyst Debug 수동 시각 확인 pending | runtime | 사용자 확인 (memory: maccatalyst-26-swiftui-observation-release-crash으로 Release는 회피) |
| G-3 | Y축 라벨 / 그리드 부재 | code-behind | 별도 cycle (`dashboard-chart-axis-labels`) 가능 |
| G-4 | Legend interactivity (toggle) | code-behind | Out of scope |

---

## 6. Runtime Verification

| Level | Status | Detail |
|---|---|---|
| Build Contract | ✅ Pass | Dashboard 0/0 + 회귀 sln 0/0 |
| Unit Tests (Dashboard) | ✅ Pass | 20/0/0 (755ms) |
| Regression Tests | ✅ Pass | 139/0/0 |
| Manual Catalyst Debug | 🔲 Pending | 사용자 확인 |

---

## 7. Conclusion

**Overall Match Rate: 100%** (runtime-weighted).

- ✅ 9/10 Plan SC Met, 0 Critical/Important Gap
- ✅ Core lib UI lib 의존 0 (Boundary 유지)
- ✅ Dashboard 빌드 0/0, 20 tests 회귀 0
- ✅ FastPortSharp.sln 회귀 0
- ✅ Production 코드 (LibTestTelemetry 등) 변경 0
- ✅ Throughput chart 동작 무변경 (Microcharts ChartView 그대로)
- 🔲 macOS Catalyst Debug 수동 실행만 pending

**Recommendation**: 90% threshold 충족 + Critical/Important 0 → `/pdca report` 즉시 진행. Iterator 불필요.

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial analysis (Match Rate 100%, 9/10 SC met, 0 Critical/Important, custom Skia overlay 검증) | boinred |
