# dashboard-unit-tests Design

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **Plan**: `docs/01-plan/features/dashboard-unit-tests.plan.md`

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Toolkit migration 안전망 확보 + Dashboard cycle에서 수동 실행 의존 제거. |
| **WHO** | boinred + 미래 contributor + AI agent. |
| **RISK** | (R-1) TFM 매핑 / (R-2) FastPortSharp.sln 회귀 / (R-3) Windows FileShare 호환 |
| **SUCCESS** | ≥ 15 tests / Dashboard sln 회귀 0 / FastPortSharp.sln 회귀 0 / CI 무변경 |
| **SCOPE** | `tests-projects/FastPortDashboardTests/` 신규 + Dashboard sln update. Production 코드 변경 0. |

---

## 1. Overview

`tests-projects/FastPortDashboardTests` MSTest 프로젝트 신규. `FastPortSharp.Dashboard.sln`에만 등록. DashboardViewModel(Toolkit setter / CanExecute / ApplySnapshot / StartAsync) + 2 Adapter(Mock / Jsonl) 핵심 경로 ~18 tests.

---

## 2. Architecture Decision

### 2.1 Options Compared

| Option | TFM | Production 변경 | Risk |
|---|---|---|---|
| **A — maccatalyst TFM 매칭 (선택)** | `net10.0-maccatalyst` (Windows에선 + `net10.0-windows10.0.19041.0`) | 0 | Low — Maui workload 필요 (이미 설치됨) |
| B — ViewModel/Adapter `LibDashboardCore`로 추출 후 `net10.0` test | `net10.0` | High (production refactor + ProjectReference 재배선) | Medium |
| C — `<Compile Include="..\..\FastPortDashboard.Maui\ViewModels\*.cs" />` 핵 | `net10.0` | 0, but 빌드 위생 ↓ | High (Toolkit source generator 이중 적용 가능성) |

### 2.2 Selected: Option A — maccatalyst TFM 매칭

**선택 근거**:
- Production 변경 0 원칙 준수
- 기존 MAUI 빌드 환경 그대로 활용 (workload 이미 설치됨)
- Toolkit source generator를 ProjectReference 통해 자연스럽게 활용
- B는 production refactor 비용이 unit test 도입 가치를 초과
- C는 SourceGenerator 이중 실행 가능성으로 빌드 위생 ↓

---

## 3. Detailed Design

### 3.1 Project Structure

```
tests-projects/FastPortDashboardTests/
├── FastPortDashboardTests.csproj
├── ViewModels/
│   └── DashboardViewModelTests.cs       (~10 tests)
└── Adapters/
    ├── MockPollingAdapterTests.cs       (~3 tests)
    └── JsonlPollingAdapterTests.cs      (~5 tests)
```

### 3.2 csproj Skeleton

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0-maccatalyst</TargetFrameworks>
    <TargetFrameworks Condition="$([MSBuild]::IsOSPlatform('windows'))">$(TargetFrameworks);net10.0-windows10.0.19041.0</TargetFrameworks>
    <UseMaui>true</UseMaui>
    <SingleProject>true</SingleProject>
    <SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'maccatalyst'">15.0</SupportedOSPlatformVersion>
    <SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows'">10.0.17763.0</SupportedOSPlatformVersion>
    <RunAOTCompilation Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'maccatalyst'">false</RunAOTCompilation>
    <MtouchInterpreter Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'maccatalyst'">all</MtouchInterpreter>
    <IsPackable>false</IsPackable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.Controls" Version="10.0.20" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="MSTest.TestAdapter" Version="3.6.4" />
    <PackageReference Include="MSTest.TestFramework" Version="3.6.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\FastPortDashboard.Maui\FastPortDashboard.Maui.csproj" />
    <ProjectReference Include="..\LibTestTelemetry\LibTestTelemetry.csproj" />
  </ItemGroup>
</Project>
```

### 3.3 DashboardViewModelTests (10 tests)

| # | Test | Verifies |
|---|---|---|
| T-VM-1 | `FilePath_Set_FiresPropertyChanged` | Toolkit setter notify |
| T-VM-2 | `UseMock_Set_FiresPropertyChanged` | Toolkit setter notify |
| T-VM-3 | `CurrentSessions_Set_FiresPropertyChanged` (via reflection or via ApplySnapshot) | KPI property notify |
| T-VM-4 | `State_ChangeFromIdleToPolling_FiresConnectAndDisconnectCanExecuteChanged` | NotifyCanExecuteChangedFor 2개 |
| T-VM-5 | `ConnectCommand_CanExecute_TrueWhenIdle_FalseWhenPolling` | CanExecute 분기 |
| T-VM-6 | `DisconnectCommand_CanExecute_FalseWhenIdle_TrueWhenPolling` | CanExecute 분기 |
| T-VM-7 | `ApplySnapshot_AssignsAllKpi` | 6 KPI + LastUpdate 매핑 |
| T-VM-8 | `ApplySnapshot_AppendsToThroughputSeries` | ThroughputSeries.Count +=1 |
| T-VM-9 | `ApplySnapshot_TrimsThroughputSeriesAt600` | 601 snapshot → Count == 600, oldest 제거 |
| T-VM-10 | `ConnectCommand_NoMockAndEmptyFilePath_SetsErrorAndStateError` | StartAsync 분기 (Error path) |

**Helper**:
```csharp
private static ObservedMetricsSnapshot MakeSnap(long sessions, long sentBytes, DateTimeOffset ts)
    => new() { ServerObserved = new ServerObservedMetricsSnapshot { CurrentSessions = sessions, TotalSentBytes = sentBytes, Timestamp = ts, ... } };
```

### 3.4 MockPollingAdapterTests (3 tests)

| # | Test | Verifies |
|---|---|---|
| T-MA-1 | `StreamAsync_YieldsSnapshotsAtInterval` | interval=50ms, 3 snapshots 받기까지 < 500ms |
| T-MA-2 | `StreamAsync_CancelledToken_TerminatesGracefully` | OperationCanceledException 또는 정상 종료 |
| T-MA-3 | `StreamAsync_DifferentSeed_DifferentValues` | seed 42 vs 100 첫 snapshot 다름 |

### 3.5 JsonlPollingAdapterTests (5 tests)

| # | Test | Verifies |
|---|---|---|
| T-JA-1 | `Stream_3Lines_Yields3Snapshots` | tmp file에 3 line 추가 → 3 snapshot |
| T-JA-2 | `Stream_AppendNewLines_YieldsOnlyNew` | 3 line yield 후 2 line 추가 → 새 2개만 |
| T-JA-3 | `Stream_FileTruncated_RestartsFromBeginning` | 3 line → truncate → 1 line → 1 snapshot (offset 리셋 검증) |
| T-JA-4 | `Stream_MalformedLine_SkipsLine` | invalid JSON line → skip, valid만 yield |
| T-JA-5 | `Stream_FileShareReadWrite_ConcurrentWriteOk` | producer가 FileShare.ReadWrite로 동시 write 중에도 reader IOException 발생 0 |

**Helper**:
```csharp
private static string WriteJsonl(string path, params ObservedMetricsSnapshot[] snaps)
{
    using var sw = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write,
        FileShare.ReadWrite | FileShare.Delete));
    foreach (var s in snaps) sw.WriteLine(JsonSerializer.Serialize(s, ObservedMetricsJson.SerializerOptions));
    return path;
}
```

### 3.6 Test Infrastructure

- **Interval**: Mock + Jsonl adapter 모두 `interval: TimeSpan.FromMilliseconds(50)` 주입 → 테스트 시간 ≤ 1s/test.
- **Timeout**: 각 polling test에 `CancellationTokenSource(TimeSpan.FromSeconds(5))` guard.
- **Tmp file**: `Path.Combine(Path.GetTempPath(), $"fp-test-{Guid.NewGuid():N}.jsonl")` + `try/finally` cleanup.
- **State change observation**: `INotifyPropertyChanged.PropertyChanged` 직접 subscribe; `ICommand.CanExecuteChanged`도 동일.

---

## 4. Risks and Mitigation

| Risk | Mitigation |
|---|---|
| (R-1) maccatalyst TFM에서 MSTest discovery 실패 | 기존 dashboard sln이 maccatalyst로 빌드되므로 동일 환경. 빌드 검증으로 조기 감지. |
| (R-2) FastPortSharp.sln 회귀 | sln 변경 없음. 신규 csproj는 Dashboard sln에만 추가. |
| (R-3) Windows FileShare 호환 | `FileShare.ReadWrite | FileShare.Delete` 명시 (테스트 + memory lesson 반영). |
| (R-4) MAUI Init이 unit test 실행 시 필요 | MSTest는 Maui platform 초기화 없이 BCL만 사용. ViewModel/Adapter는 plain class. 검증: 첫 test 빌드 후 실행. |
| (R-5) maccatalyst test의 Windows runner 실행 | Windows에서는 `net10.0-windows10.0.19041.0` TFM으로 빌드. 다만 현재 cycle에서는 macOS 로컬에서만 검증 (CI 미연동). |

---

## 5. Implementation Guide

### 5.1 Implementation Order

1. csproj 생성 (`tests-projects/FastPortDashboardTests/FastPortDashboardTests.csproj`)
2. `FastPortSharp.Dashboard.sln`에 project 추가 (`dotnet sln add`)
3. `dotnet restore FastPortSharp.Dashboard.sln` 확인
4. `ViewModels/DashboardViewModelTests.cs` 작성 (T-VM-1 ~ T-VM-10)
5. `Adapters/MockPollingAdapterTests.cs` 작성 (T-MA-1 ~ T-MA-3)
6. `Adapters/JsonlPollingAdapterTests.cs` 작성 (T-JA-1 ~ T-JA-5)
7. `dotnet build FastPortSharp.Dashboard.sln -c Release` 0/0
8. `dotnet test FastPortSharp.Dashboard.sln -c Release --no-build` ≥ 15 tests pass
9. `dotnet build FastPortSharp.sln -c Release` 회귀 0/0
10. `dotnet test FastPortSharp.sln -c Release --no-build` 139/0/0 회귀
11. 단일 commit

### 5.2 Session Guide (Module Map)

| Module Key | Description | Estimated turns |
|---|---|---|
| `module-1-proj` | csproj + sln add + restore | 2 |
| `module-2-vm` | DashboardViewModelTests (10 tests) | 6-8 |
| `module-3-mock` | MockPollingAdapterTests (3 tests) | 2 |
| `module-4-jsonl` | JsonlPollingAdapterTests (5 tests) | 4-5 |
| `module-5-verify` | Build + test (both sln) + commit | 2 |

**Recommended**: 한 세션에 모두 (≤ 20 turn). `--scope` 분할 불필요.

---

## 6. Test Plan

| Level | Test | Pass Criteria |
|---|---|---|
| Build | `dotnet build FastPortSharp.Dashboard.sln -c Release` | 0 errors |
| Unit | `dotnet test FastPortSharp.Dashboard.sln -c Release --no-build` | ≥ 15 passed, 0 failed |
| Regression Build | `dotnet build FastPortSharp.sln -c Release` | 0 errors |
| Regression Test | `dotnet test FastPortSharp.sln -c Release --no-build` | 139 passed, 0 failed |
| File count | `ls tests-projects/FastPortDashboardTests/` | 1 csproj + 3 cs files (이상) |

---

## 7. Out of Scope

- Production code refactor
- LiveCharts2 재도입
- iOS/Android TFM
- CI workflow 변경
- Maui UI binding render 테스트
- Integration test (JSONL ↔ 실제 producer)

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial design (Option A — maccatalyst TFM 매칭, MSTest, ~18 tests) | boinred |
