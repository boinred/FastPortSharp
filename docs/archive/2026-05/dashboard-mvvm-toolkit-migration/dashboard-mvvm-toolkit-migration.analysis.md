# dashboard-mvvm-toolkit-migration Analysis

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Check Complete
> **Plan**: `docs/01-plan/features/dashboard-mvvm-toolkit-migration.plan.md`
> **Design**: `docs/02-design/features/dashboard-mvvm-toolkit-migration.design.md`
> **Commit**: `a82c25c`

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | DashboardViewModel boilerplate (~190 lines) 축소로 향후 ViewModel 확장 비용 ↓. MAUI 표준 패턴 준수. |
| **WHO** | boinred + 미래 contributor + AI agent. |
| **RISK** | (R-1) Toolkit + .NET 10 MAUI Catalyst 호환 / (R-2) Setter semantic 차이 / (R-3) CanExecute 자동 갱신 누락 |
| **SUCCESS** | DashboardViewModel ~60% line ↓ + 런타임 동일 + Dashboard 빌드 0/0 + 기존 sln 회귀 0 + Mock Connect 정상 |
| **SCOPE** | `FastPortDashboard.Maui.csproj` + `ViewModels/DashboardViewModel.cs` 만 |

---

## 1. Match Rate Summary

| Axis | Score | Notes |
|---|---|---|
| **Structural** | 100% | 변경 대상 2 file 모두 적용. 신규 file 없음. |
| **Functional** | 90% | LOC 축소 목표 일부 미달 (118 vs 목표 ~80). Boilerplate 제거 자체는 완전. |
| **Contract (Build/Test)** | 100% | Dashboard 0/0, 회귀 sln 0/0, 139 tests 회귀 0. |
| **Runtime** | N/A | Dashboard 단위 테스트 미존재 (Foundation cycle에서 deferred). 수동 실행 검증만 가능. |
| **Overall (static-only)** | **96%** | (Structural × 0.2) + (Functional × 0.4) + (Contract × 0.4) = 20 + 36 + 40 |

---

## 2. Strategic Alignment Check

| Layer | Verification | Status |
|---|---|---|
| **PRD** | (PRD 생략, Plan에 통합) — Dashboard ViewModel 확장 비용 절감 목적 | ✅ |
| **Plan WHY** | "boilerplate 축소 + MAUI 표준 패턴 준수" → ObservableObject + ObservableProperty + RelayCommand 100% 적용 | ✅ |
| **Plan SUCCESS** | 6개 SC 중 5 ✅ + 1 ⚠️ (LOC 목표 미달) + 1 🔲 (수동 실행 미검증) | ⚠️ Partial |
| **Design Decision** | Option A (Toolkit 100%) 선택 → 정확히 구현 | ✅ |

---

## 3. Plan Success Criteria Status

| # | Criterion | Status | Evidence |
|---|---|---|---|
| SC-1 | `CommunityToolkit.Mvvm` NuGet 추가 | ✅ Met | `FastPortDashboard.Maui.csproj:60` `<PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />` |
| SC-2 | `DashboardViewModel.cs` rewrite — `ObservableObject` + `[ObservableProperty]` × 9 + `[RelayCommand]` × 2 | ✅ Met (overshoot) | 실제 `[ObservableProperty]` × **11** (9 Plan + 2 추가: useMock, filePath), `[RelayCommand]` × 2. `DashboardViewModel.cs:11,21-36,44-48`. |
| SC-3 | Property 이름 보존 (XAML binding 호환) | ✅ Met | XAML 13 binding 모두 생성 property와 매칭 (`MainPage.xaml:25,32,38,42,43,47,56,60,64,68,72,76,86`). XAML 변경 0. |
| SC-4 | CanExecute linkage 작동 (State 변경 시 Command 활성/비활성) | ✅ Met | `[NotifyCanExecuteChangedFor(nameof(ConnectCommand))]` + `[NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]` (`DashboardViewModel.cs:35-36`). Manual `ChangeCanExecute()` 0건 잔존. |
| SC-5 | `dotnet build FastPortSharp.Dashboard.sln -c Release` 0/0 | ✅ Met | 빌드 결과 `경고 0개 오류 0개` (29.40s). |
| SC-6 | `dotnet build FastPortSharp.sln -c Release` 회귀 0/0 | ✅ Met | 빌드 결과 `경고 0개 오류 0개` (3.52s). |
| SC-7 | `dotnet test FastPortSharp.sln -c Release --no-build` 139/0/0 | ✅ Met | `통과: 139, 실패: 0, 건너뜀: 0`. |
| SC-8 | macOS Catalyst Release 실행 → Mock Connect 정상 | 🔲 Pending | 수동 검증 필요 (사용자). Static 검증으로 회귀 가능성 ↓. |
| SC-9 | DashboardViewModel.cs LOC ~80 (~60% 축소) | ⚠️ Partial | 실제 118 (vs 193 → -75, ~39%). Boilerplate(`[ObservableProperty]` + RelayCommand) 자체는 ~80% 축소했으나 비즈니스 로직(StartAsync ~30 + ApplySnapshot ~20)이 보존되어 총 LOC 영향 ↓. Plan이 비즈니스 로직 LOC를 과소추정. |
| SC-10 | 단일 commit | ✅ Met | `a82c25c` 단일 commit. |

**Met**: 8/10 | **Partial**: 1 | **Pending**: 1 | **Not Met**: 0

---

## 4. Functional Deep-Dive

### 4.1 Removal Audit (boilerplate 잔존 0)

| Pattern | Count Before | Count After |
|---|---|---|
| `INotifyPropertyChanged` | 1 (interface) | 0 ✅ |
| `OnPropertyChanged(` 호출 | 11 | 0 ✅ |
| `PropertyChanged?.Invoke` | 1 | 0 ✅ |
| `[CallerMemberName]` | 1 | 0 ✅ |
| `((Command)...).ChangeCanExecute()` | 2 | 0 ✅ |
| `new Command(execute:` | 2 | 0 ✅ |

### 4.2 Toolkit Marker Audit (생성기 적용 확인)

| Marker | Plan Target | Actual |
|---|---|---|
| `ObservableObject` 상속 | 1 | 1 ✅ |
| `partial class` | 1 | 1 ✅ |
| `[ObservableProperty]` | ≥ 9 | **11** ✅ (UseMock, FilePath까지 포함) |
| `[RelayCommand]` | 2 | 2 ✅ |
| `[NotifyCanExecuteChangedFor]` | 2 | 2 ✅ |

### 4.3 Behavior Preservation

| Behavior | Before | After | Status |
|---|---|---|---|
| State change → Connect/Disconnect Command CanExecute 갱신 | manual `ChangeCanExecute()` 2회 호출 | `[NotifyCanExecuteChangedFor]` 2개 attribute | ✅ 의미 동일 |
| Property change → UI binding 갱신 | manual `OnPropertyChanged()` | Toolkit 생성 setter `OnPropertyChanged(args)` | ✅ 의미 동일 |
| Equality check (불필요한 notify 회피) | `if (_field != value)` | Toolkit setter: `EqualityComparer<T>.Default.Equals(...)` | ✅ 의미 동일 |
| ApplySnapshot 데이터 흐름 | unchanged | unchanged | ✅ |
| StartAsync / Stop 로직 | unchanged | unchanged | ✅ |
| ThroughputSeries 관리 (MaxChartPoints 600) | unchanged | unchanged | ✅ |

---

## 5. Decision Record Verification

| Decision | Followed? | Evidence |
|---|---|---|
| [Plan] Toolkit version: `CommunityToolkit.Mvvm` 8.4.0 | ✅ | csproj: `Version="8.4.0"` |
| [Plan] 범위: DashboardViewModel.cs 전체 | ✅ | 단일 ViewModel rewrite, 다른 ViewModels 변경 0 |
| [Plan] Commands: `[RelayCommand]` 소스 제너레이터 | ✅ | ConnectAsync / Disconnect 모두 `[RelayCommand]` attribute |
| [Plan] 단일 commit | ✅ | `a82c25c` (4 files changed: csproj + ViewModel + Plan + Design) |
| [Plan] XAML 변경 0 | ✅ | `MainPage.xaml` git diff 0 |
| [Design] Option A (Toolkit 100%) | ✅ | Property/Command 모두 Toolkit 패턴, Option B/C 흔적 0 |
| [Design] `[NotifyCanExecuteChangedFor]` State field에 명시 | ✅ | `_state` field에 attribute 2개 적용 |

---

## 6. Gap List

### Severity: Important

| # | Gap | Location | Rationale | Recommendation |
|---|---|---|---|---|
| G-1 | LOC 목표 미달 (118 vs ~80, Plan SC-9) | `DashboardViewModel.cs` | Plan이 StartAsync/ApplySnapshot 등 비즈니스 로직 LOC를 과소추정. Toolkit migration 자체 효과는 완전. | **수정 불요** — Plan 추정치 오류이며 실제 boilerplate 축소 효과는 기대대로. 향후 Plan 작성 시 비즈니스 로직 LOC를 미리 별도 측정. |

### Severity: Minor

| # | Gap | Location | Rationale | Recommendation |
|---|---|---|---|---|
| G-2 | 수동 실행 미검증 (Plan SC-8) | macOS Catalyst Release runtime | Dashboard 단위 테스트 부재로 자동 검증 경로 없음 (Foundation cycle에서 deferred) | 사용자 수동 실행 1회 확인. 별도 cycle `dashboard-unit-tests`로 안전망 강화 권장. |

### Severity: Critical

없음.

---

## 7. Runtime Verification

| Level | Status | Reason |
|---|---|---|
| L1 (API) | N/A | Dashboard는 MAUI 클라이언트, server API 없음 |
| L2 (UI Action) | N/A | Playwright 미적용 (.NET MAUI 환경) |
| L3 (E2E) | Pending (manual) | macOS Catalyst Release 직접 실행 — Mock Connect → KPI 갱신 확인 |
| Build Contract | ✅ Pass | Dashboard 0/0 + 회귀 sln 0/0 |
| Regression Test | ✅ Pass | 139/0/0 |

---

## 8. Conclusion

**Overall Match Rate: 96%** (static-only formula). 

- ✅ 모든 Toolkit pattern 적용 (ObservableObject + ObservableProperty × 11 + RelayCommand × 2 + NotifyCanExecuteChangedFor × 2)
- ✅ Manual INPC/Command boilerplate 0건 잔존
- ✅ Build 0/0 (Dashboard + 회귀)
- ✅ 139 tests 회귀 0
- ✅ XAML binding 무변경 (13 binding 모두 매칭)
- ✅ 단일 commit, 변경 file ≤ 2
- ⚠️ LOC 목표 ~80 미달 (실제 118, ~39% 축소). Plan 추정 오류, boilerplate 자체 축소 효과는 기대대로 ~80%
- 🔲 macOS Catalyst 수동 실행 검증 pending

**Recommendation**: 90% threshold 충족 → 바로 `/pdca report`로 진행. LOC Gap은 수정 대상 아님 (Plan 추정 오류).

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial analysis (Match Rate 96%, 8/10 SC met, 1 partial, 1 pending) | boinred |
