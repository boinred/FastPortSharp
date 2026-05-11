# template-contracts-extraction Design Document

> **Summary**: 2-phase pragmatic refactor — Phase 1: 3개 template 프로젝트를 `template-projects/` 그룹화 + 경로/스크립트/workflow 동기화. Phase 2: `FastPortGameServerTemplate.Contracts` Class Library 신설 + proto/PacketIds 이동.
>
> **Project**: FastPortSharp
> **Version**: 1.0
> **Author**: das_young
> **Date**: 2026-05-12
> **Status**: Draft
> **Planning Doc**: [template-contracts-extraction.plan.md](../../01-plan/features/template-contracts-extraction.plan.md)

---

## Context Anchor

> Copied from Plan document.

| Key | Value |
|-----|-------|
| **WHY** | (a) Exe 프로젝트에 proto 타입을 가둔 구조가 다중 소비자 재사용을 막고 SampleClient는 Exe→Exe ProjectReference 안티패턴으로 우회 중. (b) Template 관련 프로젝트가 리포 루트 평탄 구조로 흩어져 `tests-projects/` 그룹화 패턴과 비대칭. |
| **WHO** | 직접: FastPortGameServerTemplate 유지 개발자, FastPortDashboard.Maui (Echo Client 탭 cycle 차기 진행), 향후 외부 게임 사용자. 간접: SampleClient 사용자, CI/CD 파이프라인. |
| **RISK** | (a) namespace 변경 시 downstream cs 전수 수정 → namespace 보존으로 cs 코드 0 변경 보장. (b) 폴더 이동 후 scaffold script `TEMPLATE_SRC` + workflow `paths:` filter + ProjectReference 상대경로 미갱신 시 빌드/CI 깨짐 → Do 단계 grep 전수 + 2-phase 분리로 위험 격리. |
| **SUCCESS** | (1) `dotnet build` 성공, (2) Template + SampleClient echo round-trip 정상, (3) Contracts lib 외부에서 `EchoRequest/EchoResponse/PacketIds` 사용 가능, (4) `template-projects/` 하위 3개 프로젝트 빌드, (5) scaffold-game-server 스크립트 dry-run 통과. |
| **SCOPE** | 신규 csproj 1, sln 1 수정, 기존 csproj 2 수정, 파일 이동 2, 폴더 이동 3, scaffold script 2, workflow yml 1, README 5. cs 코드 수정 0. |

---

## 1. Overview

### 1.1 Design Goals

- **외부 인터페이스 보존**: namespace (`FastPortGameServerTemplate.Protocols`, `FastPortGameServerTemplate.Handlers`)와 PacketIds 값(1001/1002)을 그대로 유지하여 downstream cs 변경 0
- **단방향 의존**: Contracts → (no internal dep). Template/SampleClient → Contracts. 향후 Dashboard → Contracts. 의존 그래프에 cycle 없음.
- **위험 격리**: Phase 1(폴더 이동)와 Phase 2(라이브러리 추출)을 분리하여 각 단계 독립 검증 + 문제 발생 시 단일 phase rollback
- **리포 구조 일관성**: `tests-projects/` ↔ `template-projects/` 대칭

### 1.2 Design Principles

- **Pure refactor**: 어떤 기능 변화도 도입하지 않음. proto wire 호환성 100% 보존.
- **History preservation**: `git mv` 사용으로 `git log --follow` 추적 가능.
- **No anti-pattern propagation**: SampleClient의 Exe→Exe ProjectReference를 제거하고 Class Library 참조로 정상화.
- **Source-only changes**: cs 파일 0 변경. 모든 변경은 csproj/sln/scripts/yml/md.

---

## 2. Architecture Options

### 2.0 Architecture Comparison

| Criteria | Option A: Minimal | Option B: Clean | Option C: Pragmatic |
|----------|:-:|:-:|:-:|
| **Approach** | Single atomic commit | 8 per-module commits | 2-phase commits |
| **New Files** | 1 csproj + 0 cs | 1 csproj + 0 cs | 1 csproj + 0 cs |
| **Modified Files** | ~13 (in one diff) | ~13 (split 8 ways) | ~13 (split 2 ways) |
| **Complexity** | Low (single op) | High (8 build cycles) | Medium |
| **Maintainability** | Low (large diff review) | High (bisect-friendly) | High (clear phase boundary) |
| **Effort** | Low | High | Medium |
| **Risk** | Medium (single failure point) | Low (granular) | **Low (phase isolation)** |
| **Recommendation** | Hotfixes only | Long-term repos | **Default for structural refactor** |

**Selected**: **Option C — 2-Phase Pragmatic** — Rationale: 폴더 이동과 라이브러리 추출이 개념적으로 분리된 변경이라 각각 독립 commit이 자연스럽고, 각 phase 후 build+round-trip 검증을 끼워 위험을 격리할 수 있다. 단일 commit(A)보다 review/bisect 우수, per-module(B)보다 commit 노이즈 낮음.

### 2.1 Component Diagram (Before / After)

**Before**:
```
/  (repo root)
├── FastPortGameServerTemplate/             ← Exe, Protocols/Sample.proto + Handlers/PacketIds.cs 보유
│   ├── Protocols/Sample.proto
│   └── Handlers/PacketIds.cs
├── FastPortGameServerTemplate.SampleClient/ ← Exe→Exe ProjectReference 안티패턴
└── (LibCommons, LibNetworks, FastPortServer, FastPortClient, ...)
```

**After**:
```
/  (repo root)
├── template-projects/                       ← 신규 그룹 폴더
│   ├── FastPortGameServerTemplate/          ← git mv
│   ├── FastPortGameServerTemplate.Contracts/ ← 신규 Class Library
│   │   ├── Protocols/Sample.proto           ← git mv from Template
│   │   ├── Handlers/PacketIds.cs            ← git mv from Template
│   │   └── FastPortGameServerTemplate.Contracts.csproj
│   └── FastPortGameServerTemplate.SampleClient/ ← git mv
└── (LibCommons, LibNetworks, ... 모두 변경 없음)
```

### 2.2 Dependency Graph (After)

```
                  ┌─────────────────────────────────────────────┐
                  │ template-projects/                          │
                  │   FastPortGameServerTemplate.Contracts      │
                  │   (net10.0 Class Library, leaf)             │
                  │   • Google.Protobuf 3.32.1                  │
                  │   • Grpc.Tools 2.71.0 (build-only)          │
                  └─────────────────────────────────────────────┘
                                ▲                ▲
                                │                │
        ┌───────────────────────┘                └─────────────────────┐
        │                                                              │
┌───────┴──────────────────┐                          ┌────────────────┴────────┐
│ template-projects/       │                          │ template-projects/      │
│   FastPortGameServer-    │                          │   FastPortGameServer-   │
│   Template (Exe)         │                          │   Template.SampleClient │
│   • LibCommons (..\..)   │                          │   (Exe)                 │
│   • LibNetworks (..\..)  │                          │   • LibCommons (..\..)  │
│   • Google.Protobuf      │                          │   • LibNetworks (..\..) │
│   • Microsoft.Extensions │                          │   • Google.Protobuf     │
│     .Hosting             │                          │   • Serilog 일체        │
│   • Serilog 일체         │                          └─────────────────────────┘
└──────────────────────────┘
                                ▲
                                │
                  (future) FastPortDashboard.Maui — 다음 cycle에서 Contracts 참조 추가
```

### 2.3 Dependencies Table

| Component | Depends On | Purpose |
|-----------|-----------|---------|
| `FastPortGameServerTemplate.Contracts` | `Google.Protobuf`, `Grpc.Tools` | proto → C# 코드 생성 + 런타임 직렬화 |
| `FastPortGameServerTemplate` | Contracts, `LibCommons`, `LibNetworks`, `Microsoft.Extensions.Hosting`, Serilog stack | echo 서버 host |
| `FastPortGameServerTemplate.SampleClient` | Contracts, `LibCommons`, `LibNetworks`, `Microsoft.Extensions.Hosting`, Serilog stack | echo client smoke test |
| `FastPortDashboard.Maui` (future) | Contracts | Echo Client 탭 |

---

## 3. Data Model

해당 없음 — pure refactor. proto 메시지 정의는 무변경:

```protobuf
// template-projects/FastPortGameServerTemplate.Contracts/Protocols/Sample.proto
// (내용 무변경)
syntax = "proto3";
package fastport.sample;
option csharp_namespace = "FastPortGameServerTemplate.Protocols";

message EchoRequest {
  string message = 1;
}

message EchoResponse {
  string message = 1;
  int64 server_unix_ms = 2;
}
```

```csharp
// template-projects/FastPortGameServerTemplate.Contracts/Handlers/PacketIds.cs
// (내용 무변경)
namespace FastPortGameServerTemplate.Handlers;

public static class PacketIds
{
    public const int EchoRequest = 1001;
    public const int EchoResponse = 1002;
}
```

---

## 4. API Specification

해당 없음 — internal refactor. wire-level Echo 프로토콜은 PacketIds + proto 정의로 정의되어 있고 변경 없음.

---

## 5. UI/UX Design

해당 없음 — refactor, UI 변경 0.

---

## 6. Error Handling

해당 없음 — runtime 에러 처리 변경 없음. build error만 본 cycle의 관심사이며, FR-06/검증 모듈에서 처리.

---

## 7. Security Considerations

- [ ] proto wire 호환성 보존 (외부 클라이언트가 있을 경우 깨짐 방지) — 메시지 정의 무변경으로 보장됨
- [ ] 새 Contracts lib에 민감 정보 포함 없음 — proto + PacketIds (1001/1002 상수)만
- [ ] sln/csproj GUID 충돌 없음 — Contracts는 신규 GUID 발급

---

## 8. Test Plan

### 8.1 Test Scope

본 cycle은 refactor라 L2/L3 UI/E2E 테스트 N/A. 회귀 검증 중심:

| Type | Target | Tool | Phase |
|------|--------|------|-------|
| L1: Build | sln 전체 + 개별 csproj | `dotnet build` | Do M5, M8 |
| L1: Smoke | Template 서버 + SampleClient echo round-trip | `dotnet run` 2개 (서버 백그라운드 + 클라이언트) | Do M8 |
| L1: Script | scaffold-game-server dry-run | `bash` / `pwsh` | Do M8 |
| L0: Diff | cs 코드 변경 0 검증 | `git diff --stat -- '*.cs'` | Do M8 / Check |

### 8.2 Detailed Test Scenarios

| # | 검증 | 명령 | 통과 기준 |
|---|------|------|----------|
| T1 | Phase 1 끝 — 폴더 이동 + 경로 갱신만 적용된 상태 | `dotnet build FastPortSharp.sln` | 0 error, 0 new warning |
| T2 | Phase 1 끝 — echo round-trip 회귀 | 서버 실행 + SampleClient 실행 | SampleClient 로그에 `RTT=...ms` 출력 |
| T3 | Phase 2 끝 — Contracts 추출 적용된 상태 | `dotnet build FastPortSharp.sln` | 0 error, 0 new warning |
| T4 | Phase 2 끝 — echo round-trip 회귀 | 동일 | 동일 |
| T5 | Phase 2 끝 — Contracts 어셈블리 자체 빌드 | `dotnet build template-projects/FastPortGameServerTemplate.Contracts/FastPortGameServerTemplate.Contracts.csproj` | success, `bin/Debug/net10.0/FastPortGameServerTemplate.Contracts.dll` 생성 |
| T6 | Contracts dll에 EchoRequest 타입 존재 | `dotnet-ildasm` 또는 `ikdasm` 또는 PowerShell `[Reflection.Assembly]::LoadFrom(...).GetTypes()` | `FastPortGameServerTemplate.Protocols.EchoRequest` 발견 |
| T7 | SampleClient bin에 Template 어셈블리 없음 | `ls template-projects/FastPortGameServerTemplate.SampleClient/bin/Debug/net10.0/` | `FastPortGameServerTemplate.dll/exe` 부재 (안티패턴 해소 확인) |
| T8 | scaffold-game-server.sh dry-run | `bash scripts/scaffold-game-server.sh --dry-run /tmp/scaffold-test FooGame` | exit code 0, "[DRY-RUN]" 로그 출력 |
| T9 | cs 파일 변경 0 | `git diff --stat HEAD~2 HEAD -- '*.cs'` | 빈 출력 |
| T10 | GitHub Actions trigger | PR 푸시 후 actions UI 확인 | scaffold/build workflow 모두 trigger |

### 8.3 Seed Data Requirements

해당 없음 — Echo 프로토콜은 stateless, seed 불필요.

---

## 9. Clean Architecture

### 9.1 Layer Structure (.NET Solution 관점)

| Layer | Responsibility | Location |
|-------|---------------|----------|
| **Engine Core** | TCP framing, session lifecycle, common utilities | `LibCommons/`, `LibNetworks/` |
| **Engine Contracts** | Engine test 통신 contract | `Protocols/` |
| **Game Template Contracts** | 게임 sample contract (proto + PacketIds) | `template-projects/FastPortGameServerTemplate.Contracts/` ← 신규 |
| **Game Template Server** | Echo 서버 host + handler dispatch | `template-projects/FastPortGameServerTemplate/` |
| **Game Template Sample Client** | Echo round-trip smoke test client | `template-projects/FastPortGameServerTemplate.SampleClient/` |
| **Dashboard** | MAUI 기반 관측/모니터링 | `FastPortDashboard.Core/`, `FastPortDashboard.Maui/` |
| **Engine Test Suites** | engine 단위/통합/load test | `tests-projects/` |

### 9.2 Dependency Rules

```
┌──────────────────────────────────────────────────────────────────────┐
│                       Dependency Direction                            │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│   Engine Core (LibCommons, LibNetworks)                              │
│         ▲                                                            │
│         │                                                            │
│   Engine Contracts (Protocols/)                                      │
│         ▲                                                            │
│         │                                                            │
│   Game Template Contracts ← 신규 layer                               │
│         ▲                                                            │
│         │                                                            │
│   Game Template Server / SampleClient / (future) Dashboard           │
│                                                                      │
│   규칙:                                                              │
│   - Contracts는 leaf node, internal dependency 0 (외부 NuGet만)      │
│   - Server/SampleClient/Dashboard는 동등한 consumer                  │
│   - 새 게임 proto 추가 시 Contracts에 추가, 외부 publish 가능         │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

### 9.3 File Layout Rules

| From | Can Reference | Cannot Reference |
|------|--------------|-----------------|
| `Contracts` | Google.Protobuf, Grpc.Tools만 | LibCommons, LibNetworks, Server, SampleClient |
| `Template` (Exe) | Contracts, LibCommons, LibNetworks | SampleClient, Dashboard |
| `SampleClient` (Exe) | Contracts, LibCommons, LibNetworks | Template (← 본 cycle에서 끊김), Server, Dashboard |
| `Dashboard.Maui` (future) | Contracts, LibCommons | LibNetworks (TCP layer는 Echo session에서 격리), Server |

### 9.4 Component Layer Assignment

| Component | Layer | Location (After) |
|-----------|-------|------------------|
| `EchoRequest`, `EchoResponse` (generated) | Game Template Contracts | `template-projects/FastPortGameServerTemplate.Contracts/bin/.../FastPortGameServerTemplate.Contracts.dll` |
| `PacketIds` | Game Template Contracts | `template-projects/FastPortGameServerTemplate.Contracts/Handlers/PacketIds.cs` |
| Echo handler (server) | Game Template Server | `template-projects/FastPortGameServerTemplate/Application/...` (변경 없음) |
| `SampleClientSession` | Game Template Sample Client | `template-projects/FastPortGameServerTemplate.SampleClient/Sessions/SampleClientSession.cs` (변경 없음) |

---

## 10. Coding Convention Reference

### 10.1 .NET Naming Conventions

| Target | Rule | Example |
|--------|------|---------|
| Project name | `<Vendor>.<Area>.<Sub>` | `FastPortGameServerTemplate.Contracts` |
| Assembly | `<RootNamespace>` 와 동일 | `FastPortGameServerTemplate.Contracts.dll` |
| Folder grouping | `tests-projects/`, `template-projects/` (kebab + plural) | (본 cycle에서 도입) |
| ProjectReference 상대경로 | `..\..\Foo\Foo.csproj` (template-projects 깊이 기준) | `..\..\LibCommons\LibCommons.csproj` |
| Proto namespace | `option csharp_namespace = "<Vendor>.<Area>.Protocols"` 명시 | `FastPortGameServerTemplate.Protocols` |
| `PacketIds` class | `static class` in `<Vendor>.<Area>.Handlers` namespace, int 상수 | `FastPortGameServerTemplate.Handlers.PacketIds` |

### 10.2 csproj File Structure Convention

Contracts csproj는 `Protocols/Protocols.csproj` 패턴 답습:
- net10.0 Class Library (`Microsoft.NET.Sdk` SDK)
- `Google.Protobuf` + `Google.Protobuf.Tools` + `Grpc.Tools` PackageReference
- `<Protobuf Include="Protocols\**\*.proto" ProtoRoot="Protocols" GrpcServices="None" Access="Public" />`
- `Grpc.Tools` 는 `<PrivateAssets>all</PrivateAssets>` (transitive 차단)
- `<ImplicitUsings>enable</ImplicitUsings>` + `<Nullable>enable</Nullable>`

### 10.3 This Cycle's Conventions

| Item | Convention Applied |
|------|-------------------|
| 폴더 명명 | `template-projects/` (kebab + plural, `tests-projects/` 대칭) |
| 프로젝트 명명 | `FastPortGameServerTemplate.Contracts` (기존 `.SampleClient` 패턴) |
| 파일 이동 | `git mv` (history 보존) |
| csproj 상대경로 | `..\..\` (depth +1 일관) |
| Namespace | **변경 없음** (cs 코드 0 변경 보장) |
| Commit 메시지 | Phase 1: `refactor: move template projects under template-projects/`, Phase 2: `refactor: extract proto + PacketIds into FastPortGameServerTemplate.Contracts` |

---

## 11. Implementation Guide

### 11.1 File Structure (After Phase 2)

```
/
├── template-projects/                                  (신규)
│   ├── FastPortGameServerTemplate/                     (이동)
│   │   ├── Application/                                (변경 없음)
│   │   ├── Configuration/
│   │   ├── Handlers/                                   (PacketIds.cs는 Contracts로 이동)
│   │   ├── Sessions/
│   │   ├── Telemetry/
│   │   ├── Protocols/                                  (Sample.proto는 Contracts로 이동 → 폴더 삭제)
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── README.md
│   │   ├── QUICKSTART.ko.md
│   │   └── FastPortGameServerTemplate.csproj           (수정)
│   ├── FastPortGameServerTemplate.Contracts/           (신규)
│   │   ├── Protocols/
│   │   │   └── Sample.proto                            (이동)
│   │   ├── Handlers/
│   │   │   └── PacketIds.cs                            (이동)
│   │   └── FastPortGameServerTemplate.Contracts.csproj (신규)
│   └── FastPortGameServerTemplate.SampleClient/        (이동)
│       ├── Sessions/                                   (변경 없음)
│       ├── Program.cs
│       ├── EchoSignal.cs
│       ├── SampleClient*.cs
│       ├── appsettings.json
│       └── FastPortGameServerTemplate.SampleClient.csproj  (수정)
├── (FastPortServer, FastPortClient, LibCommons, LibNetworks, Protocols, FastPortDashboard.*, tests-projects, scripts, docs, .github 모두 변경 없음 — 단, scripts/ 와 .github/ 안 일부 파일은 path 갱신)
└── FastPortSharp.sln                                   (수정)
```

### 11.2 Phase 1 — Folder Restructure (Commit 1)

**Goal**: 기능 변화 0. 모든 파일 이동 + 경로 갱신만. echo round-trip 동작 동일.

**Steps**:

1. `mkdir template-projects`
2. `git mv FastPortGameServerTemplate template-projects/FastPortGameServerTemplate`
3. `git mv FastPortGameServerTemplate.SampleClient template-projects/FastPortGameServerTemplate.SampleClient`
4. `template-projects/FastPortGameServerTemplate/FastPortGameServerTemplate.csproj` 수정:
   ```diff
   -    <ProjectReference Include="..\LibCommons\LibCommons.csproj" />
   -    <ProjectReference Include="..\LibNetworks\LibNetworks.csproj" />
   +    <ProjectReference Include="..\..\LibCommons\LibCommons.csproj" />
   +    <ProjectReference Include="..\..\LibNetworks\LibNetworks.csproj" />
   ```
5. `template-projects/FastPortGameServerTemplate.SampleClient/FastPortGameServerTemplate.SampleClient.csproj` 수정:
   ```diff
   -    <ProjectReference Include="..\LibCommons\LibCommons.csproj" />
   -    <ProjectReference Include="..\LibNetworks\LibNetworks.csproj" />
   -    <ProjectReference Include="..\FastPortGameServerTemplate\FastPortGameServerTemplate.csproj" />
   +    <ProjectReference Include="..\..\LibCommons\LibCommons.csproj" />
   +    <ProjectReference Include="..\..\LibNetworks\LibNetworks.csproj" />
   +    <ProjectReference Include="..\FastPortGameServerTemplate\FastPortGameServerTemplate.csproj" />
   ```
   (※ Template→SampleClient 참조는 동일 depth라 `..\` 그대로 유지. Phase 2에서 Contracts 참조로 바뀜.)
6. `FastPortSharp.sln` 수정 (line 40, 42):
   ```diff
   -Project("{FAE...}") = "FastPortGameServerTemplate", "FastPortGameServerTemplate\FastPortGameServerTemplate.csproj", "{192...}"
   +Project("{FAE...}") = "FastPortGameServerTemplate", "template-projects\FastPortGameServerTemplate\FastPortGameServerTemplate.csproj", "{192...}"
   -Project("{FAE...}") = "FastPortGameServerTemplate.SampleClient", "FastPortGameServerTemplate.SampleClient\FastPortGameServerTemplate.SampleClient.csproj", "{592...}"
   +Project("{FAE...}") = "FastPortGameServerTemplate.SampleClient", "template-projects\FastPortGameServerTemplate.SampleClient\FastPortGameServerTemplate.SampleClient.csproj", "{592...}"
   ```
7. `scripts/scaffold-game-server.sh` line 48:
   ```diff
   -readonly TEMPLATE_SRC="${REPO_ROOT}/${TEMPLATE_TOKEN}"
   +readonly TEMPLATE_SRC="${REPO_ROOT}/template-projects/${TEMPLATE_TOKEN}"
   ```
8. `scripts/scaffold-game-server.ps1` 동등 변경 (PowerShell 변수 검색 후 수정)
9. `.github/workflows/scaffold.yml` line 13, 22:
   ```diff
   -      - 'FastPortGameServerTemplate/**'
   +      - 'template-projects/FastPortGameServerTemplate/**'
   ```
10. `.github/workflows/build.yml`, `dashboard.yml` 확인 후 hardcoded path 발견 시 갱신 (대부분 sln-based 빌드라 변경 불필요할 가능성 높음)
11. README/QUICKSTART path 표기 갱신 (5개 파일)
12. **검증**: `dotnet build FastPortSharp.sln` + Template 서버 실행 + SampleClient 실행 → RTT 로그 확인
13. **commit**: `refactor: move template projects under template-projects/ (mirror tests-projects pattern)`

### 11.3 Phase 2 — Contracts Extraction (Commit 2)

**Goal**: `FastPortGameServerTemplate.Contracts` 신설 + proto/PacketIds 이동 + Template/SampleClient의 Protobuf 책임 분리.

**Steps**:

1. `mkdir -p template-projects/FastPortGameServerTemplate.Contracts/Protocols`
2. `mkdir -p template-projects/FastPortGameServerTemplate.Contracts/Handlers`
3. **`template-projects/FastPortGameServerTemplate.Contracts/FastPortGameServerTemplate.Contracts.csproj` 신규 작성**:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">

     <!--
       Design Ref: §2.3, §9 — Template proto contract library.
       Leaf node (no internal dep). Consumed by Template, SampleClient, and future Dashboard.
     -->
     <PropertyGroup>
       <TargetFramework>net10.0</TargetFramework>
       <ImplicitUsings>enable</ImplicitUsings>
       <Nullable>enable</Nullable>
       <RootNamespace>FastPortGameServerTemplate</RootNamespace>
       <AssemblyName>FastPortGameServerTemplate.Contracts</AssemblyName>
     </PropertyGroup>

     <ItemGroup>
       <PackageReference Include="Google.Protobuf" Version="3.32.1" />
       <PackageReference Include="Google.Protobuf.Tools" Version="3.32.1" />
       <PackageReference Include="Grpc.Tools" Version="2.71.0">
         <PrivateAssets>all</PrivateAssets>
         <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       </PackageReference>
     </ItemGroup>

     <ItemGroup>
       <Protobuf Include="Protocols\**\*.proto"
                 ProtoRoot="Protocols"
                 GrpcServices="None"
                 Access="Public" />
     </ItemGroup>

   </Project>
   ```
   ※ `RootNamespace`는 `FastPortGameServerTemplate` (PacketIds.cs의 namespace prefix와 일치) — Sample.proto 내부의 `option csharp_namespace` 명시가 우선이라 EchoRequest namespace는 그대로 `FastPortGameServerTemplate.Protocols`.
4. `git mv template-projects/FastPortGameServerTemplate/Protocols/Sample.proto template-projects/FastPortGameServerTemplate.Contracts/Protocols/Sample.proto`
5. `git mv template-projects/FastPortGameServerTemplate/Handlers/PacketIds.cs template-projects/FastPortGameServerTemplate.Contracts/Handlers/PacketIds.cs`
6. Empty 폴더(`template-projects/FastPortGameServerTemplate/Protocols/`)는 git이 자동 정리. `Handlers/` 폴더는 다른 cs 파일 있는지 확인 — 있으면 유지, 없으면 삭제.
7. `template-projects/FastPortGameServerTemplate/FastPortGameServerTemplate.csproj` 수정:
   ```diff
       <PackageReference Include="Google.Protobuf" Version="3.32.1" />
   -    <PackageReference Include="Google.Protobuf.Tools" Version="3.32.1" />
   -    <PackageReference Include="Grpc.Tools" Version="2.71.0">
   -      <PrivateAssets>all</PrivateAssets>
   -      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
   -    </PackageReference>
     </ItemGroup>

     <ItemGroup>
   -    <Protobuf Include="Protocols\**\*.proto"
   -              ProtoRoot="Protocols"
   -              GrpcServices="None"
   -              Access="Public" />
   -  </ItemGroup>
   -
   -  <ItemGroup>
       <ProjectReference Include="..\..\LibCommons\LibCommons.csproj" />
       <ProjectReference Include="..\..\LibNetworks\LibNetworks.csproj" />
   +    <ProjectReference Include="..\FastPortGameServerTemplate.Contracts\FastPortGameServerTemplate.Contracts.csproj" />
     </ItemGroup>
   ```
   ※ `Google.Protobuf` PackageReference는 **유지** (Application/ 내부에서 `new EchoResponse {...}` 직접 사용).
8. `template-projects/FastPortGameServerTemplate.SampleClient/FastPortGameServerTemplate.SampleClient.csproj` 수정:
   ```diff
       <ProjectReference Include="..\..\LibCommons\LibCommons.csproj" />
       <ProjectReference Include="..\..\LibNetworks\LibNetworks.csproj" />
   -    <!-- Reuse the generated Sample.proto types (EchoRequest/EchoResponse) and PacketIds. -->
   -    <ProjectReference Include="..\FastPortGameServerTemplate\FastPortGameServerTemplate.csproj" />
   +    <ProjectReference Include="..\FastPortGameServerTemplate.Contracts\FastPortGameServerTemplate.Contracts.csproj" />
   ```
9. `FastPortSharp.sln`에 Contracts 신규 등록:
   - 신규 GUID 발급 (예: `dotnet sln add` 자동 부여)
   - 또는 수동: `Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "FastPortGameServerTemplate.Contracts", "template-projects\FastPortGameServerTemplate.Contracts\FastPortGameServerTemplate.Contracts.csproj", "{NEW-GUID}"`
   - Configuration mapping: Debug|Any CPU, Release|Any CPU, ActiveCfg + Build.0 각 2줄 추가
   - 권장: CLI 사용 — `dotnet sln FastPortSharp.sln add template-projects/FastPortGameServerTemplate.Contracts/FastPortGameServerTemplate.Contracts.csproj` (자동으로 모든 Configuration mapping + GUID 생성)
10. Template/SampleClient README 폴더 구조 다이어그램 갱신 (Protocols/ 폴더가 Contracts로 이동했음을 반영)
11. **검증**: `dotnet build FastPortSharp.sln` + Template 서버 실행 + SampleClient 실행 → RTT 로그 + SampleClient bin에 Template.dll 없음 확인
12. **commit**: `refactor: extract proto + PacketIds into FastPortGameServerTemplate.Contracts library`

### 11.4 Session Guide

#### Module Map

| Module | Scope Key | Description | Phase | Estimated Turns |
|--------|-----------|-------------|:-----:|:---------------:|
| M0 | `inventory` | 변경 영향 파일 grep 전수 확인 (read-only) | Pre-Phase 1 | 3-5 |
| M1 | `phase-1-folder` | Phase 1 전체: 폴더 이동 + sln/csproj path + scripts + workflow + README | Commit 1 | 15-25 |
| M2 | `phase-1-verify` | Phase 1 build + round-trip 검증 | Commit 1 | 5-10 |
| M3 | `phase-2-contracts` | Phase 2 전체: Contracts csproj 신설 + 파일 이동 + Template/SampleClient csproj 갱신 + sln 등록 | Commit 2 | 15-25 |
| M4 | `phase-2-verify` | Phase 2 build + round-trip + dll 존재 + bin 안티패턴 해소 검증 | Commit 2 | 5-10 |
| M5 | `final-check` | scaffold dry-run + cs diff 0 + GitHub Actions trigger 확인 | Check phase | 5-10 |

#### Recommended Session Plan

| Session | Phase | Scope | Turns |
|---------|-------|-------|:-----:|
| Session 1 (현재) | Plan + Design | 전체 | 35-45 |
| Session 2 | Do | `--scope phase-1-folder,phase-1-verify` (M0+M1+M2) | 25-40 |
| Session 3 | Do | `--scope phase-2-contracts,phase-2-verify` (M3+M4) | 25-40 |
| Session 4 | Check + Report + Archive | `--scope final-check` (M5) + 보고서 | 20-30 |

> 또는 Session 2/3을 합쳐 1 세션에 처리하는 것도 가능 (refactor 작업이 단순, 추정 50-70 turns).

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-12 | Initial draft. Option C (2-Phase Pragmatic) 채택. | das_young |
