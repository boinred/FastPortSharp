# extract-LibDashboardCore Plan

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **PRD**: (lightweight, Plan에 통합)

---

## Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | 직전 cycle (`dashboard-unit-tests`)에서 test project가 ViewModel/Adapter 6 파일을 `<Compile Include Link="_Source/...">`로 끌어 씀. 신규 파일 추가 시 csproj 수동 갱신 필요 + 동일 타입이 두 assembly에 중복 컴파일됨. |
| **Solution** | ViewModel/Adapter 6 파일을 `FastPortDashboard.Core` (pure `net10.0` Class Library)로 추출. `FastPortDashboard.Maui`와 `FastPortDashboardTests`가 ProjectReference로 사용. |
| **Function/UX/Effect** | 런타임 동작 동일. 테스트 csproj `<Compile Include>` 6줄 제거 + ProjectReference 1줄. 향후 ViewModel/Adapter 추가 시 csproj 갱신 0. |
| **Core Value** | Compile Include 운영 부담 제거 + 코드 single-source-of-truth + 향후 Dashboard cycle 확장 비용 ↓. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Compile Include 패턴은 운영 부담 (csproj 수동 sync) + 코드 중복 컴파일. 정식 lib 추출로 깔끔하게 정리. |
| **WHO** | boinred + 미래 contributor + AI agent. |
| **RISK** | (R-1) namespace 변경으로 인한 referencing 코드 회귀 / (R-2) Toolkit source generator가 lib에서 동작하는지 / (R-3) FastPortSharp.sln 회귀 / (R-4) MainPage.xaml binding namespace 일치 |
| **SUCCESS** | LibDashboardCore lib 신규 + Compile Include 제거 + Dashboard sln 빌드 0/0 + Dashboard test 18/0/0 + Maui app build 0/0 + FastPortSharp.sln 회귀 0 + 139 tests 회귀 0 |
| **SCOPE** | `FastPortDashboard.Core/` 신규 + `FastPortDashboard.Maui` 6 파일 삭제(+ProjectReference 추가) + `FastPortDashboardTests` csproj 정리 + `FastPortSharp.Dashboard.sln` update. FastPortSharp.sln 미변경. |

---

## 1. Overview

### 1.1 Motivation

직전 cycle 종료 시점 상태:
- `FastPortDashboard.Maui/ViewModels/*.cs` (3 파일) + `FastPortDashboard.Maui/Adapters/*.cs` (3 파일) — production 코드
- `tests-projects/FastPortDashboardTests/FastPortDashboardTests.csproj`에 `<Compile Include="..\..\FastPortDashboard.Maui\X.cs" Link="_Source\X.cs">` × 6

운영 부담:
- ViewModel/Adapter 추가 시 csproj 수동 갱신 필요 (잊으면 test에서 type unresolved)
- 같은 타입이 `FastPortDashboard.Maui.dll`과 `FastPortDashboardTests.dll`에 중복 컴파일
- Toolkit source generator도 두 assembly에 동일 generated source 발행 (사소하지만 빌드 시간 ↑)

해결책: 6 파일을 `FastPortDashboard.Core` (net10.0 Class Library)로 이동. 두 consumer (Maui app + test)는 ProjectReference로 참조.

### 1.2 Namespace 보존

기존 namespace는 `FastPortDashboard.Maui.ViewModels` / `FastPortDashboard.Maui.Adapters`. 이를 그대로 보존:
- ViewModel/Adapter 소스 파일은 namespace 변경 없이 그대로 이동
- 따라서 `MainPage.xaml`의 `xmlns:vm="clr-namespace:FastPortDashboard.Maui.ViewModels"`도 무변경
- 단점: 프로젝트 이름(`FastPortDashboard.Core`)과 namespace(`FastPortDashboard.Maui.*`)가 불일치 → 정상 패턴이며 .NET에서 흔함

또는 옵션 B: namespace를 `FastPortDashboard.Core.*`로 변경 (Design phase에서 결정).

### 1.3 Out of scope

- LibCharts2 재도입
- iOS/Android TFM
- Test 추가
- Production 코드 로직 변경 (단순 이동만)

---

## 2. Scope

### 2.1 In Scope

| 영역 | 작업 |
|---|---|
| `FastPortDashboard.Core/FastPortDashboard.Core.csproj` | 신규 (net10.0 Class Library, CommunityToolkit.Mvvm 8.4 + LibTestTelemetry ProjectReference) |
| `FastPortDashboard.Core/ViewModels/*.cs` (3 파일) | `FastPortDashboard.Maui/ViewModels/`에서 `git mv` 이동 |
| `FastPortDashboard.Core/Adapters/*.cs` (3 파일) | `FastPortDashboard.Maui/Adapters/`에서 `git mv` 이동 |
| `FastPortDashboard.Maui.csproj` | `CommunityToolkit.Mvvm` PackageReference 제거 (lib가 transitive 제공) + LibTestTelemetry ProjectReference 제거 (lib transitive) + LibDashboardCore ProjectReference 추가 |
| `FastPortDashboardTests/FastPortDashboardTests.csproj` | `<Compile Include>` 6줄 제거 + LibTestTelemetry ProjectReference 제거 (transitive) + LibDashboardCore ProjectReference 추가 |
| `FastPortSharp.Dashboard.sln` | LibDashboardCore project entry 추가 |
| `FastPortSharp.sln` | 변경 0 |

### 2.2 Out of Scope

- `FastPortSharp.sln` 변경 (CI matrix 영향 없음)
- 기존 13개 production 프로젝트 변경 0
- `.github/workflows/build.yml` 변경 0
- MainPage.xaml / MainPage.xaml.cs 코드 변경 (binding namespace 보존 시)

### 2.3 Key Constraint

- **Namespace 보존**: `FastPortDashboard.Maui.ViewModels` / `FastPortDashboard.Maui.Adapters` 그대로 유지하여 MainPage.xaml binding + MainPage.xaml.cs `using` 무변경. 프로젝트 이름과 namespace 불일치 (.NET에서 정상).
- **Production 동작 변경 0**: 코드는 이동만. 로직 변경 0줄.
- **Test 동작 변경 0**: 동일 18 tests pass.

---

## 3. Requirements

### 3.1 Functional

- **FR-1**: `FastPortDashboard.Core` lib 신규 — net10.0, Class Library, CommunityToolkit.Mvvm 8.4 + LibTestTelemetry 참조.
- **FR-2**: 6 파일 (`ViewModels/DashboardViewModel.cs`, `PollingState.cs`, `TimedDoublePoint.cs`, `Adapters/IPollingAdapter.cs`, `MockPollingAdapter.cs`, `JsonlPollingAdapter.cs`) `git mv`로 이동. namespace 보존.
- **FR-3**: `FastPortDashboard.Maui`가 `LibDashboardCore`를 ProjectReference로 사용. ViewModel/Adapter 타입 동일 namespace로 접근.
- **FR-4**: `FastPortDashboardTests` csproj에서 `<Compile Include>` 6줄 제거 + LibDashboardCore ProjectReference 추가. 18 tests 동일 통과.
- **FR-5**: MAUI Catalyst app 실행 시 Mock Connect 동작 무변경 (수동).
- **FR-6**: `MainPage.xaml` 변경 0 (namespace 보존).

### 3.2 Non-Functional

- **NFR-1**: `dotnet build FastPortSharp.Dashboard.sln -c Release` 0/0
- **NFR-2**: `dotnet test FastPortSharp.Dashboard.sln -c Release --no-build` 18/0/0
- **NFR-3**: `dotnet build FastPortSharp.sln -c Release` 회귀 0/0
- **NFR-4**: `dotnet test FastPortSharp.sln -c Release --no-build` 139/0/0 회귀 0
- **NFR-5**: CI workflow 변경 0
- **NFR-6**: 단일 commit
- **NFR-7**: `git mv` 사용으로 파일 history 유지

### 3.3 Compatibility

- Namespace 변경 0 (Option A, Plan 권장)
- `net10.0` lib는 maccatalyst/windows Maui에서 사용 가능
- Toolkit source generator는 lib 컴파일 단계에서 적용 → consumer는 generated property/command 사용

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] `FastPortDashboard.Core/FastPortDashboard.Core.csproj` 신규 (~20 lines)
- [ ] 6 파일 `git mv`로 이동 (file rename detection 활성)
- [ ] Namespace `FastPortDashboard.Maui.*` 보존
- [ ] `FastPortDashboard.Maui.csproj` ProjectReference 정리
- [ ] `FastPortDashboardTests.csproj` `<Compile Include>` 6줄 제거 + ProjectReference 추가
- [ ] `FastPortSharp.Dashboard.sln`에 LibDashboardCore 추가
- [ ] Dashboard sln 빌드 0/0
- [ ] Dashboard tests 18/0/0
- [ ] FastPortSharp.sln 회귀 0 (build + 139 tests)
- [ ] CI workflow 변경 0
- [ ] MainPage.xaml 변경 0
- [ ] 단일 commit

### 4.2 Quality Criteria

- [ ] `git log --follow` 가능 (file rename detection 작동)
- [ ] csproj 한국어 주석 유지
- [ ] LibDashboardCore에 production 로직 추가 0 (단순 이동)

---

## 5. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| (R-1) Namespace 변경 누락으로 XAML binding 깨짐 | Low | High | Namespace 보존 원칙 + 빌드 검증 + Mock Connect 수동 실행 |
| (R-2) Toolkit source generator가 net10.0 lib에서 정상 동작? | Low | Medium | Toolkit 8.4는 netstandard2.0 + net6.0+ 호환. 빌드 검증으로 조기 감지. |
| (R-3) FastPortDashboard.Maui ↔ LibDashboardCore 순환 참조 | Low | Medium | Lib는 LibTestTelemetry만 참조. Maui app이 Lib 참조. 단방향. |
| (R-4) Test project가 LibDashboardCore 참조 시 transitive로 Toolkit 가져옴 | Low | Low | 정상 동작. 명시적 PackageReference 추가 불필요. |
| (R-5) `git mv` rename detection 실패 (변경 > 50% 시) | Low | Low | 코드 로직 변경 0이라 rename detection 99% 성공. 실패 시 정상 동작 (history만 link 끊김). |
| (R-6) `dotnet sln add`가 sln 포맷 깨뜨림 | Low | Low | 직전 cycle에서 성공한 패턴 동일 적용. |

---

## 6. Impact Analysis

### 6.1 영향 받는 파일

| 파일 | 작업 | 예상 라인 |
|---|---|---|
| `FastPortDashboard.Core/FastPortDashboard.Core.csproj` | new | ~20 |
| `FastPortDashboard.Core/ViewModels/*.cs` (3 파일) | move (git mv) | 변경 0 |
| `FastPortDashboard.Core/Adapters/*.cs` (3 파일) | move (git mv) | 변경 0 |
| `FastPortDashboard.Maui/FastPortDashboard.Maui.csproj` | edit | ±3 lines |
| `tests-projects/FastPortDashboardTests/FastPortDashboardTests.csproj` | edit | ±8 lines (Compile Include 6 제거 + PackageReference + ProjectReference 정리) |
| `FastPortSharp.Dashboard.sln` | edit | +18 (LibDashboardCore entry) |

### 6.2 영향 받지 않는 영역

- `FastPortSharp.sln`
- 13개 기존 production 프로젝트
- `.github/workflows/`
- `MainPage.xaml`, `MainPage.xaml.cs`, `App.xaml.cs`, `MauiProgram.cs`
- ViewModel/Adapter 코드 로직 (이동만)
- 18 test 코드 (csproj만 갱신)
- 139 기존 tests

### 6.3 CI Impact

- `build.yml`은 `FastPortSharp.sln`만 빌드 → 변경 없음. CI 시간 0.
- 로컬에서 `dotnet test FastPortSharp.Dashboard.sln`로 검증.

---

## 7. Architecture Considerations

### 7.1 Decision Confirmed (Plan Checkpoint)

| Decision | Choice | Rationale |
|---|---|---|
| Lib 위치 | **`FastPortDashboard.Core` (루트)** | 사용자 확정. Dashboard 관련임을 이름으로 명시. FastPortDashboard.Maui 옆에 타입 위치. |
| sln 전략 | **Dashboard sln only** | 사용자 확정. 직전 cycle 일관성. build.yml 무영향. |
| TFM | net10.0 (pure) | maccatalyst 의존 0 |
| Namespace | 보존 (`FastPortDashboard.Maui.*`) | XAML binding 무변경, MainPage.xaml.cs `using` 무변경 |
| Single commit | Yes | 일관성 |

### 7.2 Open Decisions for Design Phase

- **Namespace rename 여부**: 보존(권장) vs `FastPortDashboard.Core.*`로 변경 (XAML/cs `using` 일괄 변경 필요).
- LibDashboardCore가 다시 `LibCommons` / `LibNetworks` 등을 참조해야 하는지 (현재는 LibTestTelemetry만)

---

## 8. Convention Prerequisites

- 한국어 주석
- `git mv` 사용 (file rename history 유지)
- 단일 commit

---

## 9. Next Steps

1. `/pdca design extract-LibDashboardCore`
   - 3 option:
     - **A**: Namespace 보존 (`FastPortDashboard.Maui.*` 그대로) — XAML 무변경 (Recommended)
     - **B**: Namespace rename (`FastPortDashboard.Core.*`) — XAML + MainPage.xaml.cs `using` 갱신 필요
     - **C**: Hybrid — file은 lib로 이동하고 namespace는 새것 + 기존 namespace alias 유지 (overengineering)
2. `/pdca do extract-LibDashboardCore` (단일 세션, ≤ 15 turn 추정)
3. `analyze` → `report` → `archive`

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial plan (FastPortDashboard.Core lib 신규, Dashboard sln only, namespace 보존 권장) | boinred |
