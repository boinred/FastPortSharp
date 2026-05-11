# dashboard-unit-tests Plan

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **PRD**: (lightweight, Plan에 통합)

---

## Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | Dashboard 코드(`DashboardViewModel`, `MockPollingAdapter`, `JsonlPollingAdapter`)에 단위 테스트 0건. 직전 cycle `dashboard-mvvm-toolkit-migration`에서 SC-8(macOS 수동 실행)가 유일한 런타임 안전망이었고, Toolkit 소스 제너레이터로 인한 회귀를 자동 감지할 수단 부재. |
| **Solution** | `tests-projects/FastPortDashboardTests` MSTest 프로젝트 신규 추가, `FastPortSharp.Dashboard.sln`에만 등록. ViewModel(Toolkit setter / CanExecute / ApplySnapshot / StartAsync 흐름) + Adapter(Mock/Jsonl) 핵심 경로 ~15–20 tests. |
| **Function/UX/Effect** | `dotnet test FastPortSharp.Dashboard.sln`로 회귀 자동 감지. CI는 미변경(별도 sln 격리 유지). 향후 ViewModel 확장 시 안전망 확보. |
| **Core Value** | Toolkit migration 안정성 확정 + 향후 Dashboard cycle (rtt-chart, multi-run viewer)에서 “수동 실행 의존” 제거. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Toolkit migration으로 setter/CanExecute가 source-generated. 수동 검증만으로는 미세 회귀 (e.g., NotifyCanExecuteChangedFor 누락) 발견 어려움. |
| **WHO** | boinred + 미래 contributor + AI agent. |
| **RISK** | (R-1) MAUI 의존성으로 인한 build/test 환경 복잡도 / (R-2) FastPortSharp.sln 회귀 / (R-3) JsonlPollingAdapter 실파일 I/O 테스트의 Windows FileShare 호환 |
| **SUCCESS** | ≥ 15 tests / Dashboard sln 회귀 0 / FastPortSharp.sln 회귀 0 / CI 무변경 / 단일 commit |
| **SCOPE** | `tests-projects/FastPortDashboardTests/` 신규 + `FastPortSharp.Dashboard.sln` update. Production 코드 변경 0. |

---

## 1. Overview

### 1.1 Motivation

- 직전 cycle SC-8 `macOS Catalyst Release 수동 실행` 외에는 Dashboard 런타임 검증 경로 없음.
- Toolkit `[ObservableProperty]` setter는 source-generated → 컴파일은 통과하지만 의미 회귀 (e.g., setter 누락, CanExecute 미연결) 발견 어려움.
- ApplySnapshot의 ThroughputSeries `MaxChartPoints=600` 경계, JSONL polling의 incremental offset 동작은 manual로 검증 어려움.

### 1.2 Coverage Goal

| 영역 | 대상 |
|---|---|
| ViewModel — Toolkit setter | `CurrentSessions/TotalAcceptedSessions/...` 값 set 시 `PropertyChanged` event 발생 |
| ViewModel — CanExecute | `State` 변경 시 `ConnectCommand.CanExecute`/`DisconnectCommand.CanExecute` 자동 갱신 |
| ViewModel — ApplySnapshot | snapshot → KPI 일괄 매핑 + ThroughputSeries 추가 + MaxChartPoints 600 trimming |
| ViewModel — StartAsync | Mock 경로 + FilePath 누락 시 Error 분기 + OperationCanceled → Disconnected |
| MockPollingAdapter | 1초 간격으로 snapshot yield, cancel 시 종료 |
| JsonlPollingAdapter | offset 누적, 일부분만 새 line 추가 시 새것만 yield, truncation 시 offset 리셋, FileShare.ReadWrite 사용 |

### 1.3 Out of scope

- XAML 렌더링 / UI thread 테스트 (Maui.Controls.Test 미도입)
- LiveCharts2 (commented out 상태)
- iOS/Android 빌드
- Production 코드 변경

---

## 2. Scope

### 2.1 In Scope

| 영역 | 형태 |
|---|---|
| `tests-projects/FastPortDashboardTests/FastPortDashboardTests.csproj` | 신규 (MSTest, `net10.0`, ProjectReference: FastPortDashboard.Maui) |
| `tests-projects/FastPortDashboardTests/ViewModels/DashboardViewModelTests.cs` | 신규 (~10 tests) |
| `tests-projects/FastPortDashboardTests/Adapters/MockPollingAdapterTests.cs` | 신규 (~3 tests) |
| `tests-projects/FastPortDashboardTests/Adapters/JsonlPollingAdapterTests.cs` | 신규 (~5 tests, tmp file I/O) |
| `FastPortSharp.Dashboard.sln` | edit (+1 project ref) |

### 2.2 Out of Scope

- `FastPortSharp.sln` 변경 (CI build matrix 영향 없음)
- Production 코드 (`FastPortDashboard.Maui/` 내 변경 0)
- `.github/workflows/build.yml` 변경

### 2.3 Key Constraint

ViewModel 테스트는 `net10.0-maccatalyst` TFM에 의존하지 않도록 설계. Test 프로젝트는 `net10.0`(pure)만 타깃팅. 가능한 이유: Toolkit 소스 제너레이터로 생성된 코드는 .NET BCL만 의존. MAUI Controls 의존이 있는 경우(`Command` 등) → 직접 호출 회피.

→ **검증 필요**: `FastPortDashboard.Maui.csproj`를 net10.0 test project가 직접 참조 가능한지. 불가하면 `InternalsVisibleTo` + 코드 일부 추출 방안 검토. **Design phase에서 결정**.

---

## 3. Requirements

### 3.1 Functional

- **FR-1**: ViewModel KPI property 11개에 대해 `PropertyChanged` event handler에서 적절 PropertyName 수신 확인 (대표 3개 sampling)
- **FR-2**: `State` setter → `ConnectCommand.CanExecuteChanged` + `DisconnectCommand.CanExecuteChanged` 둘 다 fire
- **FR-3**: `ApplySnapshot` 호출 → 6 KPI + LastUpdate + ThroughputSeries.Count 증가
- **FR-4**: ThroughputSeries에 601개 추가 시 Count == 600, 가장 오래된 point 제거
- **FR-5**: `StartAsync` UseMock=false + FilePath 빈 문자열 → `ErrorMessage` set + State=Error
- **FR-6**: `MockPollingAdapter.StreamAsync` cancel 시 OperationCanceledException 정상 propagation
- **FR-7**: `JsonlPollingAdapter`: 임시파일에 3 line 추가 → 3 snapshot yield. 파일이 partially 새 line 추가되면 새것만 yield (offset 누적). 파일 truncate → offset 리셋 후 처음부터 다시 yield.

### 3.2 Non-Functional

- **NFR-1**: `dotnet build FastPortSharp.Dashboard.sln -c Release` 0/0
- **NFR-2**: `dotnet test FastPortSharp.Dashboard.sln -c Release --no-build` 신규 tests ≥ 15 passed, 0 failed
- **NFR-3**: `dotnet build FastPortSharp.sln -c Release` 회귀 0 (변경 없음 예상)
- **NFR-4**: `dotnet test FastPortSharp.sln -c Release --no-build` 139/0/0 회귀 0
- **NFR-5**: 테스트 실행 시간 ≤ 10초 (JsonlPollingAdapter file polling test의 interval 단축 필요할 수 있음)
- **NFR-6**: 단일 commit
- **NFR-7**: CI workflow 변경 0

### 3.3 Compatibility

- MSTest framework (기존 5개 test 프로젝트와 동일)
- `.NET 10`
- `FileShare.ReadWrite | FileShare.Delete` 사용 (Windows FileShare 메모리 lesson 적용)

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] `tests-projects/FastPortDashboardTests/` 프로젝트 신규 (csproj + 3 test 파일)
- [ ] `FastPortSharp.Dashboard.sln`에 추가, `FastPortSharp.sln`에는 미추가
- [ ] ≥ 15 tests pass (target ~18)
- [ ] Dashboard sln 빌드 0/0
- [ ] Dashboard sln test 신규 tests 모두 pass
- [ ] FastPortSharp.sln 회귀 0 (build + 139 tests)
- [ ] `.github/workflows/build.yml` 변경 0 검증
- [ ] 단일 commit

### 4.2 Quality Criteria

- [ ] 한국어 주석 컨벤션 유지
- [ ] AAA 패턴 (Arrange / Act / Assert)
- [ ] 임시파일은 `Path.GetTempFileName()` + `try/finally`로 정리
- [ ] `FileShare.ReadWrite | FileShare.Delete` 명시 (memory lesson 반영)

---

## 5. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| (R-1) `FastPortDashboard.Maui` (net10.0-maccatalyst TFM)를 `net10.0` test project가 직접 참조 불가 | Medium | High | Design phase에서 결정: ① Test project를 `net10.0-maccatalyst`로 맞추거나 ② Multi-targeting `<TargetFrameworks>net10.0-maccatalyst</TargetFrameworks>` (제한적). 또는 ③ ViewModels/Adapters를 별도 lib (`LibDashboardCore`)로 추출 후 net10.0 빌드. **Plan 단계에서는 ①을 default로 가정**. |
| (R-2) `FastPortSharp.sln` 회귀 | Low | High | sln 변경 미실시. 빌드/테스트 격리 확인. |
| (R-3) JsonlPollingAdapter file polling test의 Windows FileShare 호환 | Medium | Medium | `FileShare.ReadWrite | FileShare.Delete` 명시 (memory: fileshare-windows-gotcha). |
| (R-4) MSTest discovery in MAUI workload | Low | Medium | `Microsoft.NET.Test.Sdk` + `MSTest.TestFramework` + `MSTest.TestAdapter` 표준 조합. 기존 5 test 프로젝트와 동일. |
| (R-5) JsonlPollingAdapter 폴링 interval로 인한 테스트 지연 | Medium | Low | Adapter 생성 시 interval 주입 가능하면 짧게 (e.g., 50ms). 불가하면 default 1s 사용하고 timeout 5s. **Design phase에서 결정**. |

---

## 6. Impact Analysis

### 6.1 영향 받는 파일

| 파일 | 형태 | 예상 라인 |
|---|---|---|
| `tests-projects/FastPortDashboardTests/FastPortDashboardTests.csproj` | new | ~25 |
| `tests-projects/FastPortDashboardTests/ViewModels/DashboardViewModelTests.cs` | new | ~150 |
| `tests-projects/FastPortDashboardTests/Adapters/MockPollingAdapterTests.cs` | new | ~50 |
| `tests-projects/FastPortDashboardTests/Adapters/JsonlPollingAdapterTests.cs` | new | ~100 |
| `FastPortSharp.Dashboard.sln` | edit | +20 (project + config) |

### 6.2 영향 받지 않는 영역

- `FastPortSharp.sln`
- Production 코드 (`FastPortDashboard.Maui/`, `LibCommons/`, `LibNetworks/` 등)
- `.github/workflows/`
- 기존 139 tests

### 6.3 CI Impact

- `build.yml`은 `FastPortSharp.sln`만 빌드 → 신규 test project 미실행. CI 시간 변화 0.
- 로컬에서 `dotnet test FastPortSharp.Dashboard.sln`로 명시 실행 필요. (향후 Dashboard CI 추가 cycle에서 통합)

---

## 7. Architecture Considerations

### 7.1 Decision Confirmed (Plan Checkpoint)

| Decision | Choice | Rationale |
|---|---|---|
| Test project 위치 | `tests-projects/FastPortDashboardTests` (별도 sln) | 직전 cycle 격리 정책 일관성, build.yml 미영향 |
| Test 범위 | ViewModel + Adapters (Mock + Jsonl) | Toolkit 안전망 확보 + polling 핵심 경로 검증 |
| Test framework | MSTest | 기존 5 test 프로젝트와 동일 |
| Production 코드 변경 | 0 | 안전망 추가에 집중 |
| Single commit | Yes | 일관성 |

### 7.2 Open Decisions for Design Phase

- TFM 매핑 (R-1): `net10.0` only vs `net10.0-maccatalyst` only vs multi.
- JsonlPollingAdapter polling interval test injection 가능 여부 → Adapter 코드 변경 0 원칙과 충돌 시 어떻게?

---

## 8. Convention Prerequisites

- 한국어 주석
- AAA 패턴
- 임시파일 정리
- `FileShare.ReadWrite | FileShare.Delete`
- 단일 commit

---

## 9. Next Steps

1. `/pdca design dashboard-unit-tests`
   - 3 option:
     - **A**: TFM `net10.0-maccatalyst` only (가장 안전, MAUI workload 필요)
     - **B**: TFM `net10.0` only + ViewModel/Adapter 추출(`LibDashboardCore`) (production 변경 ↑)
     - **C**: Test 시 ViewModel 코드를 컴파일타임 link (`<Compile Include="..\..\FastPortDashboard.Maui\ViewModels\*.cs" />`) (Hacky)
   - **Recommended**: A
2. `/pdca do dashboard-unit-tests` (단일 세션, ≤ 20 turn 추정)
3. `analyze` → `report` → `archive`

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial plan (별도 sln test project + ViewModel + Adapters 범위) | boinred |
