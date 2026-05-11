# extract-LibDashboardCore Analysis

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Check Complete
> **Plan**: `docs/01-plan/features/extract-LibDashboardCore.plan.md`
> **Design**: `docs/02-design/features/extract-LibDashboardCore.design.md`
> **Commit**: `319dc3b`

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Compile Include 운영 부담 + 중복 컴파일 제거 |
| **WHO** | boinred + 미래 contributor + AI agent |
| **RISK** | (R-1) Namespace 변경 / (R-2) Toolkit SG lib 호환 / (R-3) 순환 참조 / (R-4) XAML binding |
| **SUCCESS** | LibDashboardCore lib + Compile Include 제거 + 빌드/test 0/0/18 + 회귀 0 |
| **SCOPE** | `FastPortDashboard.Core/` 신규 + 6 git mv + 2 csproj + Dashboard sln. FastPortSharp.sln 무변경 |

---

## 1. Match Rate Summary

| Axis | Score | Notes |
|---|---|---|
| **Structural** | 100% | Lib + 6 파일 + 2 csproj + sln 모두 적용 |
| **Functional** | 95% | XAML 1줄 추가 (`;assembly=` 한정자) — Design 예측 미스 |
| **Contract (Build/Test)** | 100% | Dashboard 0/0, 18/0/0, 회귀 0/0, 139/0/0 |
| **Runtime** | 100% | 18 tests 실제 실행, 732ms |
| **Overall (runtime-weighted)** | **98.5%** | (Structural × 0.15) + (Functional × 0.25) + (Contract × 0.25) + (Runtime × 0.35) = 15 + 23.75 + 25 + 35 |

---

## 2. Plan Success Criteria Status

| # | Criterion | Status | Evidence |
|---|---|---|---|
| SC-1 | `FastPortDashboard.Core.csproj` 신규 | ✅ Met | net10.0 Class Library + Toolkit.Mvvm 8.4 + LibTestTelemetry |
| SC-2 | 6 파일 `git mv` (rename detection) | ✅ Met | `rename (100%)` × 6, content 변경 0 |
| SC-3 | Namespace 보존 (`FastPortDashboard.Maui.*`) | ✅ Met | 파일 내 namespace 변경 0, csproj `<RootNamespace>` 일치 |
| SC-4 | Maui csproj ProjectReference 정리 | ✅ Met | Toolkit + LibTestTelemetry 제거, LibDashboardCore 추가 |
| SC-5 | Test csproj Compile Include 6줄 제거 | ✅ Met | 모두 제거, LibDashboardCore ProjectReference로 대체 |
| SC-6 | Dashboard sln만 등록 | ✅ Met | FastPortSharp.sln git diff 0 |
| SC-7 | Dashboard 빌드 0/0 | ✅ Met | `경고 0개 오류 0개` (27.35s) |
| SC-8 | Dashboard test 18/0/0 | ✅ Met | `통과: 18, 실패: 0` (732ms) |
| SC-9 | FastPortSharp.sln 회귀 0 build | ✅ Met | 0/0 (3.50s) |
| SC-10 | 139 tests 회귀 0 | ✅ Met | `통과: 139, 실패: 0` |
| SC-11 | CI workflow 무변경 | ✅ Met | `.github/workflows/` git diff 0 |
| SC-12 | MainPage.xaml 변경 0 | ⚠️ Partial | `;assembly=FastPortDashboard.Core` 한정자 1줄 추가. Plan/Design 예측 미스. |
| SC-13 | 단일 commit | ✅ Met | `319dc3b` |

**Met**: 12/13 | **Partial**: 1 | **Pending**: 0 | **Not Met**: 0

---

## 3. Functional Deep-Dive

### 3.1 git mv Audit

```
rename {FastPortDashboard.Maui => FastPortDashboard.Core}/Adapters/IPollingAdapter.cs (100%)
rename {FastPortDashboard.Maui => FastPortDashboard.Core}/Adapters/JsonlPollingAdapter.cs (100%)
rename {FastPortDashboard.Maui => FastPortDashboard.Core}/Adapters/MockPollingAdapter.cs (100%)
rename {FastPortDashboard.Maui => FastPortDashboard.Core}/ViewModels/DashboardViewModel.cs (100%)
rename {FastPortDashboard.Maui => FastPortDashboard.Core}/ViewModels/PollingState.cs (100%)
rename {FastPortDashboard.Maui => FastPortDashboard.Core}/ViewModels/TimedDoublePoint.cs (100%)
```

→ 모든 6 파일 rename detection 100% (content 변경 0). `git log --follow` 가능.

### 3.2 csproj Diff

| 파일 | Before | After | Δ |
|---|---|---|---|
| `FastPortDashboard.Maui.csproj` | Toolkit + LibTestTelemetry + 4 comments | LibDashboardCore + 2 comments | -7 net |
| `FastPortDashboardTests.csproj` | Toolkit + LibTestTelemetry + 6 Compile Include + 8 comment | LibDashboardCore + 1 comment | -15 net |
| `FastPortDashboard.Core.csproj` | — | new | +20 |

### 3.3 Transitive Dependency Verification

| Consumer | Toolkit.Mvvm | LibTestTelemetry | 결과 |
|---|---|---|---|
| Maui app | transitive (via Core) | transitive (via Core) | 빌드 0/0 |
| Test project | transitive (via Core) | transitive (via Core) | 빌드 0/0 + 18 tests |

→ `<ProjectReference>`의 transitive package/project flow 정상 동작.

### 3.4 XAML Compiler 제약 발견

**Design 단계 가설** (Design §5 R-5):
> XAML binding은 namespace만 매칭. assembly는 transitive로 찾음. 정상 동작.

**실제 결과**: Maui XAML compiler (XamlC)는 `clr-namespace:` xmlns 사용 시 type이 현재 프로젝트 외부 assembly에 있으면 `;assembly=<AssemblyName>` 한정자를 명시 요구. transitive 해석 안 함.

**Fix**: `xmlns:vm="clr-namespace:FastPortDashboard.Maui.ViewModels;assembly=FastPortDashboard.Core"`

**Impact**: MainPage.xaml diff 1 line. Plan SC-12 "MainPage.xaml 변경 0" 기대 미달이나 의도(namespace 보존, 로직 변경 0)는 유지.

---

## 4. Decision Record Verification

| Decision | Followed? | Evidence |
|---|---|---|
| [Plan] Lib 위치: `FastPortDashboard.Core` (루트) | ✅ | 루트 디렉토리에 생성 |
| [Plan] sln 전략: Dashboard sln only | ✅ | FastPortSharp.sln diff 0 |
| [Plan] Production 변경 0 (로직) | ✅ | git mv content 변경 0, 모든 .cs 파일 100% rename |
| [Plan] `git mv` 사용 | ✅ | 6/6 rename detection |
| [Plan] 단일 commit | ✅ | `319dc3b` |
| [Design] Option A — namespace 보존 | ✅ | `FastPortDashboard.Maui.*` 그대로 |
| [Design] `<RootNamespace>FastPortDashboard.Maui</RootNamespace>` 명시 | ✅ | csproj line 14 |
| [Design] MainPage.xaml 변경 0 | ❌ | 1 line 추가 (`;assembly=` 한정자). Design 예측 미스. |

---

## 5. Gap List

### Severity: Critical
없음.

### Severity: Important

| # | Gap | Location | Recommendation |
|---|---|---|---|
| G-1 | MainPage.xaml `;assembly=` 한정자 1줄 추가 필요 (Design 예측 미스) | `MainPage.xaml:5` | **수정 불요**. 현재 fix가 최소 변경이며 정상 동작. Lesson: 다른 assembly의 namespace를 XAML clr-namespace로 참조 시 `;assembly=` 필수. |

### Severity: Minor

| # | Gap | Location | Recommendation |
|---|---|---|---|
| G-2 | 수동 macOS Catalyst 실행 검증 미수행 | runtime | 사용자 확인 시 보고. Static 빌드/test 검증으로 회귀 가능성 ↓. |

---

## 6. Runtime Verification

| Level | Status | Detail |
|---|---|---|
| Build Contract | ✅ Pass | Dashboard 0/0 + 회귀 sln 0/0 |
| Unit Tests (Dashboard) | ✅ Pass | 18/0/0 (732ms) |
| Regression Tests | ✅ Pass | 139/0/0 |
| Manual Catalyst | 🔲 Pending | 사용자 직접 확인 |

---

## 7. Conclusion

**Overall Match Rate: 98.5%** (runtime-weighted).

- ✅ 12/13 Plan SC 충족
- ✅ Dashboard 빌드 0/0 + 18 tests 100% pass
- ✅ FastPortSharp.sln 회귀 0
- ✅ 6 git mv 모두 rename detection 100% (history 유지)
- ✅ Production 로직 변경 0
- ✅ CI workflow 변경 0
- ⚠️ MainPage.xaml 1 line 변경 (Design 예측 미스, lesson 가치 있음)

**Recommendation**: 90% threshold 충족 + Critical Gap 0건 → `/pdca report`. Design lesson은 memory candidate (`maui-xaml-assembly-qualifier-gotcha`).

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial analysis (Match Rate 98.5%, 12/13 SC met, 1 partial — XAML assembly qualifier) | boinred |
