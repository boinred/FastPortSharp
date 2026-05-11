# extract-LibDashboardCore Completion Report

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: ✅ Completed
> **Match Rate**: 98.5% (runtime-weighted)
> **Commit**: `319dc3b`

---

## Executive Summary

| 관점 | Planned | Delivered |
|---|---|---|
| **Problem** | Compile Include 6줄 우회 → 신규 파일 추가 시 csproj 수동 sync + 중복 컴파일 부담 | ✅ 동일 |
| **Solution** | ViewModel/Adapter 6 파일을 `FastPortDashboard.Core` (net10.0 Class Library)로 추출 | ✅ Lib 신규 + 6 git mv (rename detection 100%) |
| **Function/UX/Effect** | 런타임 동작 동일, namespace 보존, Compile Include 6줄 제거 | ✅ Toolkit + LibTestTelemetry transitive, 18 tests 동일 통과, XAML 1줄 추가 |
| **Core Value** | Compile Include 운영 부담 제거 + single-source-of-truth + Dashboard 확장 비용 ↓ | ✅ 향후 ViewModel/Adapter 추가 시 csproj sync 0 |

### Value Delivered

| Metric | Before | After |
|---|---|---|
| Compile Include 줄 (test csproj) | 6 | **0** |
| ViewModel/Adapter assembly 컴파일 위치 | 2 (Maui + Test 각각) | **1 (Core)** |
| 신규 파일 추가 시 csproj sync | 수동 1회 | **자동 (ProjectReference)** |
| Production 코드 로직 변경 | — | **0줄** |
| `git log --follow` | 작동 (Compile Include 패턴 의존) | 작동 (rename detection) |
| Dashboard 빌드 | 0/0 | 0/0 |
| Dashboard test | 18/0/0 | 18/0/0 (732ms) |
| FastPortSharp.sln 회귀 | 139/0/0 | 139/0/0 |
| CI workflow 변경 | — | **0줄** |

---

## 1. Key Decisions & Outcomes

| Phase | Decision | Outcome |
|---|---|---|
| **[Plan]** Lib 위치: `FastPortDashboard.Core` (루트) | ✅ Followed | 루트에 net10.0 Class Library 생성 |
| **[Plan]** sln 전략: Dashboard sln only | ✅ Followed | FastPortSharp.sln git diff 0 |
| **[Plan]** Production 코드 로직 변경 0 | ✅ Followed | 6 파일 git mv content 변경 0 |
| **[Plan]** `git mv` 사용 | ✅ Followed | 6/6 rename detection 100% |
| **[Plan]** 단일 commit | ✅ Followed | `319dc3b` |
| **[Design]** Option A — namespace 보존 | ✅ Followed | `FastPortDashboard.Maui.*` 그대로 |
| **[Design]** csproj `<RootNamespace>FastPortDashboard.Maui</RootNamespace>` | ✅ Followed | line 14 |
| **[Design]** MainPage.xaml 변경 0 | ⚠️ **Deviated** (1 line) | XAML compiler가 `clr-namespace:` 사용 시 다른 assembly의 type은 `;assembly=<AssemblyName>` 명시 요구. transitive 해석 안 함. 1줄 추가로 정상 동작. |

---

## 2. Success Criteria Final Status

| # | Criterion | Status | Evidence |
|---|---|---|---|
| SC-1 | Lib csproj 신규 | ✅ Met | `FastPortDashboard.Core/FastPortDashboard.Core.csproj` |
| SC-2 | 6 git mv (rename 100%) | ✅ Met | `rename ... (100%)` × 6 |
| SC-3 | Namespace 보존 | ✅ Met | `FastPortDashboard.Maui.*` |
| SC-4 | Maui csproj 정리 | ✅ Met | Toolkit + LibTestTelemetry 제거, LibDashboardCore 추가 |
| SC-5 | Test csproj Compile Include 제거 | ✅ Met | 6줄 제거, ProjectReference 1줄 |
| SC-6 | Dashboard sln only | ✅ Met | FastPortSharp.sln 무변경 |
| SC-7 | Dashboard 빌드 0/0 | ✅ Met | 27.35s |
| SC-8 | Dashboard test 18/0/0 | ✅ Met | 732ms |
| SC-9 | FastPortSharp.sln 회귀 build | ✅ Met | 3.50s, 0/0 |
| SC-10 | 139 tests 회귀 0 | ✅ Met | 통과: 139 |
| SC-11 | CI workflow 무변경 | ✅ Met | `.github/workflows/` diff 0 |
| SC-12 | MainPage.xaml 변경 0 | ⚠️ Partial | 1줄 추가 (`;assembly=` 한정자) — Design 예측 미스 |
| SC-13 | 단일 commit | ✅ Met | `319dc3b` |

**Overall**: 12/13 ✅ Met, 1 ⚠️ Partial, 0 ❌ Not Met

---

## 3. PDCA Cycle Summary

| Phase | Output | Notes |
|---|---|---|
| Plan | `docs/01-plan/features/extract-LibDashboardCore.plan.md` | 사용자 결정: 루트 + Dashboard sln only |
| Design | `docs/02-design/features/extract-LibDashboardCore.design.md` | Option A — namespace 보존 |
| Do | commit `319dc3b` | 6 git mv + csproj 정리 + XAML 1줄 fix |
| Check | `docs/03-analysis/extract-LibDashboardCore.analysis.md` | 98.5% Match Rate, 0 Critical |
| Report | (this document) | — |

---

## 4. Implementation Highlights

### 4.1 git mv Audit (rename detection 100%)

```
rename {FastPortDashboard.Maui => FastPortDashboard.Core}/Adapters/IPollingAdapter.cs (100%)
rename {FastPortDashboard.Maui => FastPortDashboard.Core}/Adapters/JsonlPollingAdapter.cs (100%)
rename {FastPortDashboard.Maui => FastPortDashboard.Core}/Adapters/MockPollingAdapter.cs (100%)
rename {FastPortDashboard.Maui => FastPortDashboard.Core}/ViewModels/DashboardViewModel.cs (100%)
rename {FastPortDashboard.Maui => FastPortDashboard.Core}/ViewModels/PollingState.cs (100%)
rename {FastPortDashboard.Maui => FastPortDashboard.Core}/ViewModels/TimedDoublePoint.cs (100%)
```

→ `git log --follow` 6 파일 모두 가능.

### 4.2 csproj Diff Summary

| 파일 | Before | After | Δ |
|---|---|---|---|
| FastPortDashboard.Core.csproj | — | new | +20 |
| FastPortDashboard.Maui.csproj | 13 lines | 6 lines | -7 |
| FastPortDashboardTests.csproj | 22 lines | 7 lines | -15 |
| **Total** | 35 | 33 | **-2 (총 줄 수 ↓)** |

(Compile Include 6줄 + Toolkit + LibTestTelemetry 중복 제거로 순감)

### 4.3 XAML Lesson Discovered

**Hypothesis** (Design §5 R-5):
> XAML binding은 namespace만 매칭. assembly는 transitive로 찾음.

**Reality**: Maui XamlC는 `clr-namespace:` xmlns 사용 시 type이 외부 assembly에 있으면 `;assembly=<AssemblyName>` 명시 요구. transitive 해석 안 함.

**Fix**: `xmlns:vm="clr-namespace:FastPortDashboard.Maui.ViewModels;assembly=FastPortDashboard.Core"` (1줄 변경)

---

## 5. Lessons Learned

1. **XAML `clr-namespace` cross-assembly 규칙**: type이 외부 assembly에 있을 때 `;assembly=` 한정자 필수. Maui XamlC는 transitive 해석 안 함. → Memory candidate: `maui-xaml-assembly-qualifier-gotcha`.
2. **`<RootNamespace>` 활용**: 프로젝트명 ≠ namespace인 경우 csproj `<RootNamespace>`를 명시하면 IDE 자동 namespace도 일치하여 미래 파일 추가 시 일관성 ↑.
3. **`git mv` rename detection**: 코드 content 변경 0이면 100% rename detection. `git log --follow` history 유지. Refactoring cycle의 표준 패턴.
4. **Transitive ProjectReference 작동**: ProjectReference는 자동으로 PackageReference + 하위 ProjectReference를 transitive 전달. Consumer가 명시 reference 추가 불필요.

---

## 6. Follow-up Recommendations

| Cycle | Purpose | Priority |
|---|---|---|
| `dashboard-rtt-chart` | RTT chart 추가 (ThroughputSeries 활용) + LiveCharts2 재도입 검토 | Medium |
| `dashboard-jsonl-offset-fix` | JsonlPollingAdapter offset race 정식 수정 | Low |
| `dashboard-ci-add` | Dashboard sln 별도 CI workflow | Low |

---

## 7. New Memory Lesson Candidate

> **maui-xaml-assembly-qualifier-gotcha**: Maui XAML compiler가 `xmlns="clr-namespace:..."`로 다른 assembly의 type을 참조할 땐 `;assembly=<AssemblyName>` 한정자 필수. ProjectReference로 transitive 해도 XAML compiler는 단일 assembly로만 해석. extract-LibDashboardCore cycle (2026-05-11)에서 발견.

→ archive 단계에서 memory 저장 예정.

---

## 8. Archive Note

이 cycle의 모든 PDCA 문서는 `/pdca archive extract-LibDashboardCore` 실행 시 `docs/archive/2026-05/extract-LibDashboardCore/`로 이동됩니다.

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Completion report (Match Rate 98.5%, 12/13 SC met, 1 XAML deviation, single commit 319dc3b) | boinred |
