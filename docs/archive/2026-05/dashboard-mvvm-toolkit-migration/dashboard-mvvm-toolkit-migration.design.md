# dashboard-mvvm-toolkit-migration Design

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **Plan**: `docs/01-plan/features/dashboard-mvvm-toolkit-migration.plan.md`

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

## 1. Overview

### 1.1 Purpose

DashboardViewModel을 `CommunityToolkit.Mvvm` 소스 제너레이터로 migration하여 boilerplate 제거.

### 1.2 Goal

| 항목 | Before | After (목표) |
|---|---|---|
| DashboardViewModel.cs LOC | ~190 | ~80 |
| Property 정의 | manual getter/setter + OnPropertyChanged | `[ObservableProperty] private T _field;` |
| Command 정의 | `new Command(execute, canExecute) + ChangeCanExecute()` | `[RelayCommand(CanExecute = ...)]` |
| Class 선언 | `internal sealed class : INotifyPropertyChanged` | `internal sealed partial class : ObservableObject` |
| INPC 코드 | manual `event PropertyChangedEventHandler + OnPropertyChanged()` | source-generated |

### 1.3 Non-Goal

- XAML / UI binding 변경
- Adapters 변경
- PollingState / TimedDoublePoint 변경
- LiveCharts2 재도입
- 단위 테스트 추가

---

## 2. Architecture Decision

### 2.1 Options Compared

| Option | Approach | LOC | Risk | Effort |
|---|---|---|---|---|
| **A — Toolkit 100% (선택)** | `ObservableObject` + `[ObservableProperty]` × 9 + `[RelayCommand]` × 2 + `[NotifyCanExecuteChangedFor]` | ~80 | Low | 1 session |
| B — Property만 Toolkit | `[ObservableProperty]`만 사용, Commands는 수동 `Command` 유지 | ~110 | Low | 1 session |
| C — Toolkit + ObservableValidator | A + validation attribute (`[Required]` 등) | ~95 | Medium | 1.5 session |

### 2.2 Selected: Option A (Toolkit 100%) — Plan §7.1에서 사용자 확정

**선택 근거**:
- Boilerplate 절감 효과 최대 (~190 → ~80)
- MAUI 표준 패턴 100% 준수
- 일관성 ↑ (Property/Command 모두 동일한 소스 제너레이터 패러다임)
- Option C의 Validation은 현재 KPI 표시에 불필요 (FilePath validation은 향후 cycle에서)

---

## 3. Detailed Design

### 3.1 NuGet Dependency

```xml
<!-- FastPortDashboard.Maui.csproj <ItemGroup> -->
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
```

- 위치: `Microsoft.Maui.Controls` PackageReference 옆.
- 버전: 8.4.0 (latest stable as of 2026-05).

### 3.2 DashboardViewModel.cs Structure (After)

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastPortDashboard.Maui.Adapters;
using LibTestTelemetry;

namespace FastPortDashboard.Maui.ViewModels;

internal sealed partial class DashboardViewModel : ObservableObject
{
    // ─── 의존성 / 내부 상태 (Toolkit 미적용) ─────────────────────
    private IPollingAdapter? _adapter;
    private CancellationTokenSource? _cts;
    private Task? _pumpTask;

    // ─── UI Input (편집 가능) ─────────────────────────────────
    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private bool _useMock = true;

    // ─── State (CanExecute trigger) ───────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private PollingState _state = PollingState.Idle;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    // ─── KPI ──────────────────────────────────────────────────
    [ObservableProperty] private long _currentSessions;
    [ObservableProperty] private long _totalAcceptedSessions;
    [ObservableProperty] private long _totalSentBytes;
    [ObservableProperty] private long _pendingSendRequests;
    [ObservableProperty] private long _sendBufferBytes;
    [ObservableProperty] private DateTime _lastUpdate;

    public ObservableCollection<TimedDoublePoint> ThroughputSeries { get; } = new();

    // ─── Commands ─────────────────────────────────────────────
    private bool CanConnect() => State == PollingState.Idle || State == PollingState.Error;
    private bool CanDisconnect() => State == PollingState.Polling;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync() { /* StartAsync 호출 */ }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect() { /* Stop 호출 */ }

    // ─── Lifecycle / Pump ─────────────────────────────────────
    private async Task StartAsync() { ... }
    private void Stop() { ... }
    private async Task PumpAsync(CancellationToken ct) { ... }
    private void ApplySnapshot(ServerObservedMetricsSnapshot snap) { ... }
}
```

### 3.3 Key Translation Rules

| Before | After |
|---|---|
| `private long _currentSessions; public long CurrentSessions { get; private set; }` (~6 lines) | `[ObservableProperty] private long _currentSessions;` (1 line) |
| `ConnectCommand = new Command(async () => await ConnectAsync(), () => CanConnect())` | `[RelayCommand(CanExecute = nameof(CanConnect))] private async Task ConnectAsync()` |
| `((Command)ConnectCommand).ChangeCanExecute(); ((Command)DisconnectCommand).ChangeCanExecute();` (State setter 안) | `[NotifyCanExecuteChangedFor(nameof(ConnectCommand))]` + `[NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]` on `_state` field |
| `public event PropertyChangedEventHandler? PropertyChanged;` + `OnPropertyChanged([CallerMemberName])` | `ObservableObject` 상속으로 자동 제공 |

### 3.4 XAML Binding Compatibility

생성된 property 이름은 field name(`_camelCase`)에서 prefix `_` 제거 + 첫 글자 대문자화:
- `_currentSessions` → `CurrentSessions` ✅
- `_filePath` → `FilePath` ✅
- `_state` → `State` ✅

→ MainPage.xaml의 `{Binding CurrentSessions}` 등 모든 binding 무변경.

Command 이름은 method name에서 suffix `Async` 제거 + `Command` 추가:
- `ConnectAsync` → `ConnectCommand` ✅
- `Disconnect` → `DisconnectCommand` ✅

→ XAML의 `Command="{Binding ConnectCommand}"` 무변경.

### 3.5 Threading

기존 코드의 `MainThread.BeginInvokeOnMainThread(...)` 사용은 그대로 유지 (Pump → UI 갱신). Toolkit의 setter는 caller thread에서 동작하므로, ApplySnapshot이 UI thread에서 호출되는 한 안전.

---

## 4. Risks and Mitigation

| Risk | Mitigation |
|---|---|
| (R-1) Toolkit + .NET 10 MAUI Catalyst 호환 | Toolkit 8.4 안정 release, .NET 6+ 지원. 빌드 검증. |
| (R-2) Setter semantic 차이 | Toolkit 생성 setter는 `EqualityComparer<T>.Default.Equals` 후 notify — 기존 수동 `if (_field != value)`와 동일 의미. |
| (R-3) CanExecute 자동 갱신 누락 | `[NotifyCanExecuteChangedFor(nameof(...Command))]` × 2 명시. State setter가 자동으로 ChangeCanExecute 호출. |
| (R-4) Source generator 빌드 실패 | 첫 빌드에서 generated source 확인 (`obj/Debug/.../Generated/` 안에 `*.g.cs` 파일). |

---

## 5. Implementation Guide

### 5.1 File-Level Changes

| 파일 | 작업 | 예상 라인 |
|---|---|---|
| `FastPortDashboard.Maui/FastPortDashboard.Maui.csproj` | edit (+1 PackageReference) | +1 |
| `FastPortDashboard.Maui/ViewModels/DashboardViewModel.cs` | rewrite | ~190 → ~80 (-110) |

### 5.2 Implementation Order

1. csproj에 `<PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />` 추가
2. `dotnet restore FastPortSharp.Dashboard.sln`로 패키지 fetch 확인
3. DashboardViewModel.cs:
   1. `using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input;` 추가
   2. class 선언: `internal sealed class : INotifyPropertyChanged` → `internal sealed partial class : ObservableObject`
   3. 9 properties → `[ObservableProperty]` field로 변환
   4. State field에 `[NotifyCanExecuteChangedFor(nameof(ConnectCommand))]` × 2 추가
   5. Commands: `ICommand ConnectCommand = new Command(...)` 제거 → `ConnectAsync` 메서드에 `[RelayCommand(CanExecute = nameof(CanConnect))]`
   6. 수동 INPC 코드 (`event PropertyChanged`, `OnPropertyChanged`) 제거
   7. State setter 안의 `ChangeCanExecute()` 호출 제거 (attribute가 대체)
4. `dotnet build FastPortSharp.Dashboard.sln -c Release` 0/0 확인
5. `dotnet build FastPortSharp.sln -c Release` 회귀 0/0 확인
6. `dotnet test FastPortSharp.sln -c Release --no-build` 139/0/0 확인
7. (수동) macOS Catalyst Release 실행 → "Use Mock" 체크 후 Connect → KPI 갱신 확인
8. 단일 commit

### 5.3 Session Guide (Module Map)

| Module Key | Description | Estimated turns |
|---|---|---|
| `module-1-pkg` | csproj NuGet 추가 + restore | 1 |
| `module-2-vm` | DashboardViewModel.cs rewrite | 5-7 |
| `module-3-verify` | Build + test + 수동 실행 | 2-3 |

**Recommended Session Plan**: 한 세션에 모두 실행 (총 ≤ 12 turn 예상). `--scope` 분할 불필요.

---

## 6. Test Plan

| Level | Test | Pass Criteria |
|---|---|---|
| Build | `dotnet build FastPortSharp.Dashboard.sln -c Release` | 0 errors, 0 warnings |
| Regression Build | `dotnet build FastPortSharp.sln -c Release` | 0 errors |
| Regression Test | `dotnet test FastPortSharp.sln -c Release --no-build` | 139 passed, 0 failed |
| Manual | macOS Catalyst app 실행 + Mock Connect | KPI 갱신, State `Idle→Polling`, Connect btn disable / Disconnect btn enable |
| LOC | `wc -l ViewModels/DashboardViewModel.cs` | ≤ 100 lines |

---

## 7. Architecture Considerations

### 7.1 Partial Class

Toolkit 소스 제너레이터는 partial class에 generated source를 emit. 따라서 `internal sealed partial class DashboardViewModel : ObservableObject` 선언 필수.

### 7.2 Field Naming Convention

Toolkit은 `_camelCase` 또는 `m_camelCase` field에서 PascalCase property 생성. 기존 코드는 이미 `_camelCase`이므로 호환.

### 7.3 Generated Source 확인 위치

빌드 후 `FastPortDashboard.Maui/obj/Debug/net10.0-maccatalyst/generated/CommunityToolkit.Mvvm.SourceGenerators/...` 에 `*.g.cs` 파일이 생성됨. 디버깅 시 참조.

---

## 8. Out of Scope

- XAML Hot Reload 검증
- iOS/Android 빌드
- LiveCharts2 재도입
- 단위 테스트 추가 (별도 cycle `dashboard-unit-tests`)
- ObservableValidator / Validation rule
- DI container 도입 (현재 `new DashboardViewModel()` 그대로)

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial design (Option A — Toolkit 100%) | boinred |
