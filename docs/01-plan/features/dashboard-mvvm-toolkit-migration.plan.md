# dashboard-mvvm-toolkit-migration Plan

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **PRD**: (lightweight refactor — PRD 생략, Plan에 motivation 통합)

---

## Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | `DashboardViewModel.cs`가 9개 `INotifyPropertyChanged` boilerplate property (private field + getter/setter + OnPropertyChanged) + 수동 `Command` instance로 ~190 lines 됨. 새 KPI 추가나 ViewModel 분리 시마다 동일 패턴 반복. |
| **Solution** | `CommunityToolkit.Mvvm` 도입 — `ObservableObject` 상속 + `[ObservableProperty]` + `[RelayCommand]` 소스 제너레이터로 boilerplate 자동 생성. ~190 → ~80 lines (~60% 축소). |
| **Function/UX/Effect** | 런타임 동작 동일. 향후 KPI/Command 추가 시 한 줄 (`[ObservableProperty] private long _newKpi;`)으로 끝. 단위 테스트 작성도 동일하게 동작. |
| **Core Value** | 다음 cycle (RTT chart, multi-run viewer 등)에서 ViewModel 확장 비용 ↓. MAUI 표준 패턴 준수로 향후 contributor onboarding ↓. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | dashboard Foundation 완료 후 향후 ViewModel 확장 가속화. Toolkit이 .NET MAUI 사실상 표준이라 follow-up cycle 비용 ↓. |
| **WHO** | boinred + 미래 contributor + AI agent. |
| **RISK** | (R-1) Toolkit source generator 동작 미세 차이 / (R-2) 런타임 동작 회귀 / (R-3) MAUI 빌드 회귀 |
| **SUCCESS** | DashboardViewModel ~60% line ↓ + 런타임 동작 동일 + dashboard 빌드 0/0 + 기존 sln 회귀 0 + macOS Catalyst app 실행 후 Mock Connect 정상 |
| **SCOPE** | `FastPortDashboard.Maui/ViewModels/DashboardViewModel.cs` rewrite + `FastPortDashboard.Maui.csproj` NuGet 추가. PollingState (enum) / TimedDoublePoint (record struct)는 ViewModel 아니므로 미변경. |

---

## 1. Overview

### 1.1 Motivation

직전 cycle (`maui-telemetry-dashboard-foundation`) 완료 후, `DashboardViewModel.cs`는 다음 패턴 반복:

```csharp
private long _currentSessions;
public long CurrentSessions
{
    get => _currentSessions;
    private set { if (_currentSessions != value) { _currentSessions = value; OnPropertyChanged(); } }
}
```

각 property 6 lines × 9 properties = ~54 lines + Commands `Command(execute, canExecute)` 인스턴스 + State change에서 수동 `ChangeCanExecute()` 호출.

`CommunityToolkit.Mvvm` 8.4+ 도입 시:

```csharp
[ObservableProperty]
private long _currentSessions;   // 1 line → property + getter/setter/notify 자동 생성
```

```csharp
[RelayCommand(CanExecute = nameof(CanConnect))]
private async Task ConnectAsync() { ... }  // ICommand + CanExecute linkage 자동
```

### 1.2 왜 일부만이 아니라 ViewModel 전체

DashboardViewModel은 6 KPI + 4 UI state property + 2 commands로 구성. boilerplate 절감 효과가 명확하고 일부만 migration하면 코드 일관성 ↓. **single file rewrite**로 처리.

### 1.3 Out of scope 명시

- `PollingState` — enum, ViewModel 아님
- `TimedDoublePoint` — `readonly record struct`, 이미 immutable / value type
- `IPollingAdapter`, `JsonlPollingAdapter`, `MockPollingAdapter` — adapter 계층, ViewModel 아님
- MainPage.xaml — XAML binding은 동일 property 이름 사용. binding 변경 0.

---

## 2. Scope

### 2.1 In Scope

| 영역 | 형태 |
|---|---|
| `FastPortDashboard.Maui.csproj` | NuGet `CommunityToolkit.Mvvm` 8.4+ 추가 |
| `FastPortDashboard.Maui/ViewModels/DashboardViewModel.cs` | Rewrite — `ObservableObject` 상속 + `[ObservableProperty]` × 9 + `[RelayCommand]` × 2 |
| `FastPortDashboard.Maui/MainPage.xaml.cs` | BindingContext 타입 그대로 (생성된 property 이름이 동일하므로 binding 무변경) |

### 2.2 Out of Scope

- PollingState / TimedDoublePoint
- Adapters
- MainPage.xaml binding 변경
- XAML control 변경 (`Frame` → `Border` 등은 별도 cycle)
- LiveCharts2 재도입 (`dashboard-livecharts-integration` 별도 cycle)
- Production 코드 (`LibCommons/`, `LibNetworks/` 등) 변경 0줄

---

## 3. Requirements

### 3.1 Functional

- **FR-1**: 9 properties 모두 `[ObservableProperty]`로 자동 생성. 외부에서 보이는 이름 동일 (예: `CurrentSessions`).
- **FR-2**: `ConnectCommand` / `DisconnectCommand` `[RelayCommand]`로 생성. CanExecute는 State property 기반.
- **FR-3**: State property 변경 시 Command CanExecute 자동 갱신 (`[RelayCommand(CanExecute = ...)]` + `NotifyCanExecuteChangedFor` 활용).
- **FR-4**: Mock 모드 Connect → KPI 갱신 정상 (수동 검증).
- **FR-5**: MainPage.xaml binding 무변경 (property 이름 보존).

### 3.2 Non-Functional

- **NFR-1**: `FastPortSharp.sln` 회귀 0줄 (별도 sln).
- **NFR-2**: 139 tests 회귀 0.
- **NFR-3**: Dashboard 빌드 (Release maccatalyst) 0/0.
- **NFR-4**: macOS Catalyst app 실행 후 Mock Connect 정상 (수동 1회).
- **NFR-5**: DashboardViewModel.cs line count ~190 → ~80 (~60% 축소).
- **NFR-6**: 단일 commit.

### 3.3 Compatibility

- `CommunityToolkit.Mvvm` 8.4.0+ (Microsoft 공식 maintain)
- .NET 10 호환 (toolkit은 netstandard2.0 + net6.0+, .NET 10 OK)
- MAUI XAML binding 호환

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] `CommunityToolkit.Mvvm` NuGet 추가 (csproj)
- [ ] `DashboardViewModel.cs` rewrite — `ObservableObject` 상속 + `[ObservableProperty]` × 9 + `[RelayCommand]` × 2
- [ ] Property 이름 보존 (XAML binding 호환)
- [ ] CanExecute linkage 작동 (State 변경 시 Command 활성/비활성)
- [ ] `dotnet build FastPortSharp.Dashboard.sln -c Release` 0/0
- [ ] `dotnet build FastPortSharp.sln -c Release` 회귀 0/0
- [ ] `dotnet test FastPortSharp.sln -c Release --no-build` 139/0/0
- [ ] macOS Catalyst Release 실행 → Mock Connect → KPI 갱신 (수동 1회)
- [ ] DashboardViewModel.cs line count ~80 (~60% 축소)
- [ ] 단일 commit

### 4.2 Quality Criteria

- [ ] 한국어 주석 컨벤션 유지
- [ ] 변경 file ≤ 2 (csproj + ViewModel)
- [ ] XAML 변경 0 (binding은 그대로)

---

## 5. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| (R-1) Toolkit source generator가 .NET 10 + MAUI Catalyst와 호환 미세 차이 | Low | Medium | Toolkit 8.4+ 안정 release. 빌드 0/0 확인 + 수동 실행 검증. |
| (R-2) `OnPropertyChanged([CallerMemberName])` semantic 차이 | Low | Medium | Toolkit이 자동 생성하는 setter는 동일 의미 (값 변경 시만 notify). 수동 검증으로 확인. |
| (R-3) Command CanExecute 자동 갱신 누락 | Medium | Medium | `[NotifyCanExecuteChangedFor]` attribute로 State property → Command linkage 명시. |
| (R-4) Mock Connect 동작 회귀 | Low | High | 수동 실행 검증 1회 (Foundation cycle과 동일 path). |
| (R-5) 단위 테스트 없어서 회귀 감지 어려움 | Medium | Low | Foundation cycle에서 deferred한 unit test도 같은 issue. 별도 cycle `dashboard-unit-tests` 후 안전망 강화. |

---

## 6. Impact Analysis

### 6.1 영향 받는 파일

| 파일 | 형태 | 예상 라인 |
|---|---|---|
| `FastPortDashboard.Maui/FastPortDashboard.Maui.csproj` | edit | +1 (PackageReference) |
| `FastPortDashboard.Maui/ViewModels/DashboardViewModel.cs` | rewrite | ~190 → ~80 (-110) |

### 6.2 영향 받지 않는 영역

- `FastPortSharp.sln` 및 13개 기존 프로젝트
- `tests-projects/` (test 프로젝트 5개)
- `tests/scaffold/`
- MainPage.xaml (binding 동일)
- MainPage.xaml.cs (`new DashboardViewModel()` 그대로)
- Adapters (IPollingAdapter / Mock / Jsonl)
- ViewModels/PollingState.cs (enum)
- ViewModels/TimedDoublePoint.cs (struct)

### 6.3 Performance Impact

- Source generator는 빌드 타임에 동작 → 런타임 성능 0 변화.
- 첫 빌드 시 source generator 로딩 ~1-2초 추가. 이후 incremental.

---

## 7. Architecture Considerations

### 7.1 Decision Confirmed (Plan Checkpoint)

| Decision | Choice | Rationale |
|---|---|---|
| Toolkit version | **`CommunityToolkit.Mvvm` 8.4+** (latest stable) | Microsoft 공식 maintain, MAUI 표준 |
| Migration 범위 | **DashboardViewModel.cs 전체** | 일관성 + boilerplate 효과 명확 |
| Commands 패턴 | **`[RelayCommand]` 소스 제너레이터** | CanExecute linkage 자동, MAUI 표준 |
| 단일 commit | Yes | 직전 cycle 패턴 |
| XAML 변경 | 0 | property 이름 보존 |

### 7.2 Open Decisions for Design Phase

- Toolkit이 자동 생성하는 property가 `public partial class DashboardViewModel : ObservableObject` 필요. 현재 `sealed class` → `partial sealed class` 또는 `partial class`로 변경.
- ConnectAsync에서 `_adapter`, `_cts` 같은 private field 유지 (Toolkit과 무관)
- `CanConnect` / `CanDisconnect` private method or property

---

## 8. Convention Prerequisites

- 한국어 주석 컨벤션 유지
- `_camelCase` 또는 `m_camelCase` 필드 prefix → Toolkit은 `_camelCase` 컨벤션 (잘 맞음)
- 단일 commit

---

## 9. Next Steps

1. `/pdca design dashboard-mvvm-toolkit-migration`
   - 3 architecture options:
     - **A**: Toolkit 100% (ObservableObject + ObservableProperty + RelayCommand)
     - **B**: 부분적용 (Property만 Toolkit, Commands는 수동)
     - **C**: 추가로 `ObservableValidator` 도입 (validation rule)
2. `/pdca do dashboard-mvvm-toolkit-migration` (단일 세션, ≤ 15 turn 추정)
3. `/pdca analyze` + `report` + `archive`

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial plan (CommunityToolkit.Mvvm 8.4+ 도입, DashboardViewModel.cs rewrite ~60% 축소, 단일 commit) | boinred |
