# extract-LibDashboardCore Design

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-11
> **Status**: Draft
> **Plan**: `docs/01-plan/features/extract-LibDashboardCore.plan.md`

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Compile Include 패턴 운영 부담 + 중복 컴파일 제거. |
| **WHO** | boinred + 미래 contributor + AI agent. |
| **RISK** | (R-1) Namespace 변경 누락 / (R-2) Toolkit SG lib 호환 / (R-3) Maui ↔ Lib 순환 / (R-4) XAML binding namespace |
| **SUCCESS** | LibDashboardCore lib + Compile Include 제거 + Dashboard 빌드/test 0/0/18 + Maui app 빌드 0/0 + FastPortSharp.sln 회귀 0 |
| **SCOPE** | `FastPortDashboard.Core/` 신규 + 6 파일 git mv + 2 csproj edit + Dashboard sln update. FastPortSharp.sln 무변경. |

---

## 1. Overview

ViewModel(3) + Adapter(3) = 6 production 파일을 `FastPortDashboard.Maui/` → `FastPortDashboard.Core/` (net10.0 Class Library)로 이동. 기존 두 consumer (Maui app + Test project)는 ProjectReference로 통합. 코드 로직 변경 0, namespace 보존, MainPage.xaml 무변경.

---

## 2. Architecture Decision

### 2.1 Options Compared

| Option | Namespace | XAML 변경 | 명확성 | Risk |
|---|---|---|---|---|
| **A — Namespace 보존 (선택)** | `FastPortDashboard.Maui.{ViewModels,Adapters}` 그대로 | 0 | 프로젝트명 ≠ namespace (정상 .NET 패턴) | Low |
| B — Namespace rename | `FastPortDashboard.Core.{ViewModels,Adapters}` | XAML xmlns + cs using 갱신 | 프로젝트명 = namespace | Medium |
| C — Hybrid (alias) | 새 + 기존 namespace 동시 | XAML 무변경 + extern alias | overengineering | High |

### 2.2 Selected: Option A — Namespace 보존 (Plan §7.1 권장)

**선택 근거**:
- MainPage.xaml `xmlns:vm="clr-namespace:FastPortDashboard.Maui.ViewModels"` 무변경
- MainPage.xaml.cs `using FastPortDashboard.Maui.ViewModels;` 무변경
- Test 파일 `using FastPortDashboard.Maui.{ViewModels,Adapters};` 무변경
- 코드 변경 면적 ↓ → diff 깔끔 + 회귀 risk ↓
- .NET 생태계에선 프로젝트명 ≠ namespace는 흔함 (예: `Microsoft.AspNetCore.App` 내 `Microsoft.Extensions.*` namespaces)
- 향후 namespace 정합이 필요해지면 별도 cycle로 분리 가능

---

## 3. Detailed Design

### 3.1 Project Structure (After)

```
FastPortSharp/
├── FastPortDashboard.Core/              # 신규 (net10.0 Class Library)
│   ├── FastPortDashboard.Core.csproj
│   ├── ViewModels/
│   │   ├── DashboardViewModel.cs        # ← Maui/ViewModels/에서 이동
│   │   ├── PollingState.cs              # ← (동상)
│   │   └── TimedDoublePoint.cs          # ← (동상)
│   └── Adapters/
│       ├── IPollingAdapter.cs           # ← Maui/Adapters/에서 이동
│       ├── MockPollingAdapter.cs        # ← (동상)
│       └── JsonlPollingAdapter.cs       # ← (동상)
├── FastPortDashboard.Maui/              # ViewModel/Adapter 파일 제거
│   ├── FastPortDashboard.Maui.csproj    # CommunityToolkit.Mvvm 제거 (transitive) + LibDashboardCore 추가
│   ├── App.xaml(.cs)
│   ├── MainPage.xaml(.cs)               # 변경 0
│   └── MauiProgram.cs
└── tests-projects/
    └── FastPortDashboardTests/
        ├── FastPortDashboardTests.csproj  # Compile Include 6줄 제거 + LibDashboardCore 추가
        ├── ViewModels/DashboardViewModelTests.cs    # 변경 0
        └── Adapters/{Mock,Jsonl}PollingAdapterTests.cs  # 변경 0
```

### 3.2 FastPortDashboard.Core.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>FastPortDashboard.Maui</RootNamespace>
    <!-- Namespace 보존 정책: 프로젝트명 ≠ root namespace. Design Ref: §2.2. -->
  </PropertyGroup>

  <ItemGroup>
    <!-- ObservableObject/ObservableProperty/RelayCommand 소스 제너레이터. -->
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
  </ItemGroup>

  <ItemGroup>
    <!-- ObservedMetricsSnapshot 데이터 contract. -->
    <ProjectReference Include="..\tests-projects\LibTestTelemetry\LibTestTelemetry.csproj" />
  </ItemGroup>
</Project>
```

**Note**: `<RootNamespace>FastPortDashboard.Maui</RootNamespace>` 설정으로 새 파일 추가 시 IDE가 자동 namespace 생성도 보존. 단, 기존 6 파일은 이미 `namespace FastPortDashboard.Maui.*`를 명시하므로 file content 변경 0.

### 3.3 FastPortDashboard.Maui.csproj Diff

| Before | After |
|---|---|
| `<PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />` | (제거 — transitive via LibDashboardCore) |
| `<ProjectReference Include="..\tests-projects\LibTestTelemetry\LibTestTelemetry.csproj" />` | (제거 — transitive via LibDashboardCore) |
| — | `<ProjectReference Include="..\FastPortDashboard.Core\FastPortDashboard.Core.csproj" />` |

XAML/cs 코드 변경 0.

### 3.4 FastPortDashboardTests.csproj Diff

| Before | After |
|---|---|
| `<Compile Include="..\..\FastPortDashboard.Maui\ViewModels\DashboardViewModel.cs" Link="_Source\..." />` × 6 | (제거) |
| `<PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />` | (제거 — transitive via LibDashboardCore) |
| `<ProjectReference Include="..\LibTestTelemetry\LibTestTelemetry.csproj" />` | (제거 — transitive) |
| — | `<ProjectReference Include="..\..\FastPortDashboard.Core\FastPortDashboard.Core.csproj" />` |

Test 파일 코드 변경 0 (using 그대로).

### 3.5 sln Update

```bash
dotnet sln FastPortSharp.Dashboard.sln add FastPortDashboard.Core/FastPortDashboard.Core.csproj
```

FastPortSharp.sln (메인) 미변경.

### 3.6 git mv Strategy

```bash
mkdir -p FastPortDashboard.Core/ViewModels FastPortDashboard.Core/Adapters
git mv FastPortDashboard.Maui/ViewModels/DashboardViewModel.cs FastPortDashboard.Core/ViewModels/
git mv FastPortDashboard.Maui/ViewModels/PollingState.cs FastPortDashboard.Core/ViewModels/
git mv FastPortDashboard.Maui/ViewModels/TimedDoublePoint.cs FastPortDashboard.Core/ViewModels/
git mv FastPortDashboard.Maui/Adapters/IPollingAdapter.cs FastPortDashboard.Core/Adapters/
git mv FastPortDashboard.Maui/Adapters/MockPollingAdapter.cs FastPortDashboard.Core/Adapters/
git mv FastPortDashboard.Maui/Adapters/JsonlPollingAdapter.cs FastPortDashboard.Core/Adapters/
```

각 파일 content 변경 0 → rename detection 100% 성공 → `git log --follow` 가능.

---

## 4. Risks and Mitigation

| Risk | Mitigation |
|---|---|
| (R-1) Namespace 변경 누락 | 보존 정책. csproj `<RootNamespace>` 일치. 빌드 검증. |
| (R-2) Toolkit SG가 lib에서 동작? | 8.4 stable에서 ObservableObject + ObservableProperty + RelayCommand 모두 lib 컴파일 단계 적용. 빌드 검증. |
| (R-3) 순환 참조 | Lib는 LibTestTelemetry만 참조 (downward). Maui app + Test project가 Lib 참조 (upward). 단방향. |
| (R-4) XAML binding 깨짐 | namespace 보존 → MainPage.xaml diff 0. 수동 실행 검증. |
| (R-5) MainPage.xaml `x:DataType="vm:DashboardViewModel"` resolve 실패 | `xmlns:vm="clr-namespace:FastPortDashboard.Maui.ViewModels"`은 namespace만 매칭. assembly는 transitive로 찾음. 정상 동작. |
| (R-6) Test transitive Toolkit 미작동 | ProjectReference는 transitive PackageReference 자동 전달. 추가 작업 0. |

---

## 5. Implementation Guide

### 5.1 Implementation Order

1. `FastPortDashboard.Core` 디렉토리 + `FastPortDashboard.Core.csproj` 생성
2. `git mv`로 6 파일 이동 (`ViewModels/*.cs` × 3 + `Adapters/*.cs` × 3)
3. `FastPortDashboard.Maui.csproj` 편집:
   - `<PackageReference Include="CommunityToolkit.Mvvm" .../>` 제거
   - `<ProjectReference Include="..\tests-projects\LibTestTelemetry\..." />` 제거
   - `<ProjectReference Include="..\FastPortDashboard.Core\FastPortDashboard.Core.csproj" />` 추가
4. `FastPortDashboardTests.csproj` 편집:
   - `<Compile Include>` 6줄 제거
   - `<PackageReference Include="CommunityToolkit.Mvvm" .../>` 제거
   - `<ProjectReference Include="..\LibTestTelemetry\LibTestTelemetry.csproj" />` 제거
   - `<ProjectReference Include="..\..\FastPortDashboard.Core\FastPortDashboard.Core.csproj" />` 추가
5. `dotnet sln FastPortSharp.Dashboard.sln add FastPortDashboard.Core/FastPortDashboard.Core.csproj`
6. `dotnet build FastPortSharp.Dashboard.sln -c Release` 0/0
7. `dotnet test FastPortSharp.Dashboard.sln -c Release --no-build` 18/0/0
8. `dotnet build FastPortSharp.sln -c Release` 회귀 0/0
9. `dotnet test FastPortSharp.sln -c Release --no-build` 139/0/0
10. (수동) macOS Catalyst Release 실행 + Mock Connect 검증
11. 단일 commit

### 5.2 Session Guide (Module Map)

| Module Key | Description | Turns |
|---|---|---|
| `module-1-lib` | csproj 생성 + git mv 6 파일 | 3 |
| `module-2-maui` | Maui csproj 편집 | 1 |
| `module-3-test` | Test csproj 편집 | 1 |
| `module-4-verify` | 빌드 + test 양쪽 sln | 2 |
| `module-5-commit` | 단일 commit | 1 |

**Recommended**: 한 세션 ≤ 10 turn. `--scope` 분할 불필요.

---

## 6. Test Plan

| Level | Test | Pass Criteria |
|---|---|---|
| Build | `dotnet build FastPortSharp.Dashboard.sln -c Release` | 0 errors |
| Unit | `dotnet test FastPortSharp.Dashboard.sln -c Release --no-build` | 18 passed, 0 failed |
| Regression Build | `dotnet build FastPortSharp.sln -c Release` | 0 errors |
| Regression Test | `dotnet test FastPortSharp.sln -c Release --no-build` | 139 passed, 0 failed |
| File location | `ls FastPortDashboard.Core/{ViewModels,Adapters}/*.cs` | 3 + 3 files |
| Removal | `! test -f FastPortDashboard.Maui/ViewModels/DashboardViewModel.cs` | 원본 위치 부재 |
| Manual | macOS Catalyst app + Mock Connect | KPI 갱신 정상 |

---

## 7. Out of Scope

- Production 로직 변경
- Namespace rename
- iOS/Android TFM
- 신규 test 추가
- LiveCharts2 재도입
- FastPortSharp.sln 변경

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-11 | Initial design (Option A — namespace 보존, 6 git mv + 2 csproj edit) | boinred |
