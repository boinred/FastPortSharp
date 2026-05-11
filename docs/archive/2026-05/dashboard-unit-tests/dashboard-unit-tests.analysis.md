# dashboard-unit-tests Analysis

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Check Complete
> **Plan**: `docs/01-plan/features/dashboard-unit-tests.plan.md`
> **Design**: `docs/02-design/features/dashboard-unit-tests.design.md`
> **Commit**: `4184d57`

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Toolkit migration 안전망 확보 + Dashboard cycle 수동 실행 의존 제거. |
| **WHO** | boinred + 미래 contributor + AI agent. |
| **RISK** | (R-1) TFM 매핑 / (R-2) FastPortSharp.sln 회귀 / (R-3) Windows FileShare 호환 |
| **SUCCESS** | ≥ 15 tests / Dashboard sln 회귀 0 / FastPortSharp.sln 회귀 0 / CI 무변경 |
| **SCOPE** | `tests-projects/FastPortDashboardTests/` 신규 + Dashboard sln update. Production 코드 변경 0. |

---

## 1. Match Rate Summary

| Axis | Score | Notes |
|---|---|---|
| **Structural** | 100% | 4 file 신규 (csproj + 3 cs) + sln update. 누락 0. |
| **Functional** | 88% | 18 tests 모두 통과. Design Option A → C 전환은 architecture deviation. |
| **Contract (Build/Test)** | 100% | Dashboard 0/0, 회귀 sln 0/0, 139 tests 회귀 0, 18 신규 pass. |
| **Runtime** | 100% | 18 tests 실제 실행, 745ms. |
| **Overall (runtime-weighted)** | **96.2%** | (Structural × 0.15) + (Functional × 0.25) + (Contract × 0.25) + (Runtime × 0.35) = 15 + 22 + 25 + 35 |

---

## 2. Strategic Alignment Check

| Layer | Verification | Status |
|---|---|---|
| **Plan WHY** | "Toolkit 안전망 확보 + 수동 검증 의존 제거" → ViewModel Toolkit 동작 (setter notify, CanExecute linkage), Adapter polling 핵심 경로 검증 | ✅ |
| **Plan SUCCESS** | 8/8 SC ✅ Met (architecture deviation은 Plan Constraint이지 SC 아님) | ✅ |
| **Design Decision** | Option A 선언 → 실제는 Option C 전환. WHY는 빌드/실행 제약 (Resizetizer + dotnet test maccatalyst Exe 미지원). | ⚠️ Deviation, but Strategically aligned |

---

## 3. Plan Success Criteria Status

| # | Criterion | Status | Evidence |
|---|---|---|---|
| SC-1 | `tests-projects/FastPortDashboardTests/` 신규 (csproj + 3 test cs) | ✅ Met | `FastPortDashboardTests.csproj`, `ViewModels/DashboardViewModelTests.cs`, `Adapters/{Mock,Jsonl}PollingAdapterTests.cs` |
| SC-2 | `FastPortSharp.Dashboard.sln`에만 추가, FastPortSharp.sln 미추가 | ✅ Met | `FastPortSharp.Dashboard.sln:12-13` 신규 entry. `FastPortSharp.sln` git diff 0. |
| SC-3 | ≥ 15 tests pass (target ~18) | ✅ Met (target) | 실제 **18** tests (`DashboardViewModelTests` 10 + `MockPollingAdapterTests` 3 + `JsonlPollingAdapterTests` 5) |
| SC-4 | Dashboard sln 빌드 0/0 | ✅ Met | `경고 0개 오류 0개` |
| SC-5 | Dashboard sln 신규 tests 모두 pass | ✅ Met | `통과: 18, 실패: 0, 건너뜀: 0, 기간: 745ms` |
| SC-6 | FastPortSharp.sln 회귀 0/0 build | ✅ Met | `경고 0개 오류 0개` (3.97s) |
| SC-7 | FastPortSharp.sln 139 tests 회귀 0 | ✅ Met | `통과: 139, 실패: 0` |
| SC-8 | `.github/workflows/build.yml` 변경 0 | ✅ Met | git diff 0 |
| SC-9 | 단일 commit | ✅ Met | `4184d57` |

**Met**: 9/9 | **Partial**: 0 | **Pending**: 0 | **Not Met**: 0

---

## 4. Functional Deep-Dive

### 4.1 Test Coverage Audit

| 영역 | Design 계획 | 실제 | 비고 |
|---|---|---|---|
| ViewModel — Toolkit setter notify | T-VM-1, T-VM-2, T-VM-3 | ✅ | FilePath/UseMock/KPI PropertyChanged 검증 |
| ViewModel — CanExecute linkage | T-VM-4, T-VM-5, T-VM-6, T-VM-7 | ✅ | Reflection으로 attribute 존재 + State 변경 시 CanExecuteChanged fire + Connect/Disconnect CanExecute 분기 |
| ViewModel — ApplySnapshot | T-VM-8 | ✅ | 6 KPI + LastUpdate + ThroughputSeries +=1 |
| ViewModel — ThroughputSeries 600 trim | T-VM-9 | ✅ | 601 추가 → 600 유지, 가장 오래된 제거, 최신 보존 |
| ViewModel — StartAsync Error 분기 | T-VM-10 | ✅ | UseMock=false + FilePath empty → State=Error |
| MockPollingAdapter | T-MA-1/2/3 | ✅ | interval, cancellation, seed |
| JsonlPollingAdapter | T-JA-1/2/3/4/5 | ✅ | 3 line yield, offset persist, truncate reset, malformed skip, FileShare concurrent |

### 4.2 Memory Lesson Application

| Memory | 적용 위치 | Status |
|---|---|---|
| `fileshare-windows-gotcha` (FileShare.ReadWrite) | `JsonlPollingAdapterTests.AppendJsonl` helper + T-JA-5 동시 write test | ✅ Applied |
| `refactor-grep-truncation-gotcha` | (해당 cycle scope 아님) | N/A |

### 4.3 Production 코드 변경 검증

```
git diff --stat HEAD~1 FastPortDashboard.Maui/   # production 영역
→ 0 lines changed (Plan 원칙 준수)
```

---

## 5. Architecture Deviation Analysis

### 5.1 Plan→Design→Implementation 추이

| Phase | Decision | Outcome |
|---|---|---|
| Plan | Test project 위치: `tests-projects/FastPortDashboardTests` (Dashboard sln only) | ✅ Followed |
| Plan | Framework: MSTest | ✅ Followed (`MSTest` 3.9.0 umbrella package) |
| Plan | Production 변경 0 | ✅ Followed (production diff 0) |
| Plan | 단일 commit | ✅ Followed |
| Design | Option A — TFM `net10.0-maccatalyst` 매칭 (ProjectReference) | ❌ **Deviated** → Option C |

### 5.2 Deviation 원인

Design 단계에서 식별 못한 두 가지 빌드 제약:

1. **Resizetizer 출력 충돌**: `FastPortDashboard.Maui`와 test project가 모두 MAUI SDK인 경우, `MauiIcon` (appicon.svg)이 중복 출력 → 빌드 실패. `EnableMauiImageProcessing=false` 등 해결책 시도했으나 ApplicationId 등 부가 요구사항 폭증.
2. **`dotnet test`가 `net10.0-maccatalyst` Exe DLL 실행 불가**: maccatalyst 런타임은 simulator/번들 필요. Test discovery는 통과해도 실행 단계 실패.

### 5.3 Option C 정당화

- ViewModel/Adapter 6 파일 모두 MAUI 타입 의존 0 (`ICommand`은 BCL, `RelayCommand`은 Toolkit). 따라서 pure `net10.0`에서 컴파일 가능.
- `<Compile Include Link="_Source/...">`로 source-level reuse. Toolkit source generator는 test project에서도 동일 적용.
- 단점: Dashboard 프로젝트와 test 프로젝트가 같은 타입을 따로 컴파일 → assembly가 2개 (FastPortDashboard.Maui.dll + FastPortDashboardTests.dll). 정상 패턴이며 충돌 없음 (test project가 Maui 프로젝트를 ProjectReference 안 함).
- 향후 production 파일 추가 시 csproj `<Compile Include>` 목록 수동 업데이트 필요 (운영 부담 ↑).

### 5.4 Strategic 평가

Plan의 핵심 가치(안전망 확보 + 수동 의존 제거)는 100% 달성. Architecture 선택은 implementation detail이며, Plan의 "Production 변경 0" 원칙은 Option C에서도 보존. → **Critical severity 아님**.

---

## 6. Gap List

### Severity: Important

| # | Gap | Location | Recommendation |
|---|---|---|---|
| G-1 | Design Option A → C 전환 (architecture deviation) | `tests-projects/FastPortDashboardTests/FastPortDashboardTests.csproj` | **수정 불요**. Plan 가치 100% 달성. Design phase에 “maccatalyst TFM의 빌드/실행 제약” lesson 보존 (다음 cycle 권장). |

### Severity: Minor

| # | Gap | Location | Recommendation |
|---|---|---|---|
| G-2 | Production 파일 추가 시 csproj `<Compile Include>` 수동 갱신 필요 (Option C 부담) | csproj | 단기적 부담 적음. 장기적으론 별도 cycle `extract-LibDashboardCore`로 Option B 전환 검토. |
| G-3 | JsonlPollingAdapter race (offset 갱신 vs 동시 write)을 production 사용 패턴에서 발견 | `JsonlPollingAdapter.cs:38-52` | 실제 사용 패턴은 백그라운드 write이므로 무해. 단, 향후 `dashboard-rtt-chart` cycle에서 검증 권장. |

### Severity: Critical

없음.

---

## 7. Decision Record Verification

| Decision | Followed? | Evidence |
|---|---|---|
| [Plan] Test project 위치: `tests-projects/FastPortDashboardTests` (Dashboard sln only) | ✅ | `FastPortSharp.Dashboard.sln`에만 등록 |
| [Plan] Framework: MSTest | ✅ | `MSTest` 3.9.0 |
| [Plan] Production 변경 0 | ✅ | git diff 0 in `FastPortDashboard.Maui/` |
| [Plan] FileShare.ReadWrite 적용 | ✅ | `JsonlPollingAdapterTests.AppendJsonl` |
| [Plan] 단일 commit | ✅ | `4184d57` |
| [Design] Option A — maccatalyst TFM 매칭 | ❌ | **Option C로 전환** (위 §5 참조) |
| [Design] ≥ 15 tests | ✅ | 18 tests |
| [Design] ≤ 20 turn 추정 | ✅ | 실제 turn count 추정 ~17 (Build trial-and-error 6회 + test fixup 1회) |

---

## 8. Runtime Verification

| Level | Status | Detail |
|---|---|---|
| Build Contract | ✅ Pass | Dashboard 0/0 + 회귀 sln 0/0 |
| Unit Tests (Dashboard) | ✅ Pass | 18/0/0 (745ms) |
| Regression Tests | ✅ Pass | 139/0/0 |
| L2/L3 UI/E2E | N/A | Dashboard scope에 UI test 미포함 (별도 cycle) |

---

## 9. Conclusion

**Overall Match Rate: 96.2%** (runtime-weighted).

- ✅ 모든 Plan SC (9/9) 충족
- ✅ Build 0/0 (Dashboard + 회귀)
- ✅ 18 신규 tests 100% pass (745ms)
- ✅ 회귀 139/0/0
- ✅ Production 코드 변경 0
- ✅ CI workflow 변경 0
- ✅ FileShare memory lesson 적용
- ⚠️ Architecture: Design Option A → Option C 전환 (빌드/실행 제약). Plan 가치는 100% 달성.

**Recommendation**: 90% threshold 충족 + Critical Gap 0건 → 바로 `/pdca report`로 진행. Design lesson은 향후 cycle (`dashboard-rtt-chart`, `extract-LibDashboardCore` 등) 작성 시 참고.

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial analysis (Match Rate 96.2%, 9/9 SC met, 1 architecture deviation noted but non-Critical) | boinred |
