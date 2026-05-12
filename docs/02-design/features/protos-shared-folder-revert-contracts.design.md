# protos-shared-folder-revert-contracts Design Document

> **Summary**: 2-Phase 실행 전략 — Phase 1 (source repo): Protos 폴더 생성 + PacketIds Template 복귀 + Contracts 제거 + csproj 갱신 + build/test 검증. Phase 2 (scaffold + docs): script 수정 + fixture 재생성 + README. 직전 `template-contracts-extraction` 패턴 답습.
>
> **Project**: FastPortSharp
> **Version**: 1.0
> **Author**: das_young
> **Date**: 2026-05-12
> **Status**: Draft
> **Planning Doc**: [protos-shared-folder-revert-contracts.plan.md](../../01-plan/features/protos-shared-folder-revert-contracts.plan.md)

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Contracts Class Library는 game proto sharing 단위로 과도. proto는 data, 각 consumer가 자체 cs 생성이 .NET 자연스러움. scaffold 출력 단순화. |
| **WHO** | Template 유지자, SampleClient, 향후 Dashboard, 외부 scaffold 사용자, CI. |
| **RISK** | `<Compile Include Link>` 외부 cs 인식, scaffold path depth, fixture 4 case 재생성, dashboard-echo-client-tab plan 가정 갱신. |
| **SUCCESS** | Contracts 제거, Protos 폴더 단순화, 176 tests pass, echo round-trip 정상, scaffold 7/7 sh+ps1, scaffold output build OK. |
| **SCOPE** | sln -1, Contracts/ 디렉터리 삭제, Template/SampleClient csproj, scaffold sh/ps1, fixture 4 case, README 6개, dashboard plan 갱신. |

---

## 1. Overview

### 1.1 Design Goals

- **데이터-as-folder**: proto는 wire data → csproj 없는 단순 폴더로 표현
- **Per-consumer 생성**: 각 csproj가 자체 `<Protobuf Include>` + 자체 Grpc.Tools
- **단일 source of truth**: PacketIds는 Template 소유, SampleClient는 link
- **Exe→Exe 안티패턴 영구 회피**: ProjectReference 없이 source level만 공유
- **외부 사용자 경험 단순화**: scaffold 출력 3 projects + 1 폴더 (vs 4 projects)

### 1.2 Design Principles

- Per-consumer `<Protobuf Include>` 패턴은 .NET protobuf 관용 (Grpc.Tools가 obj/ 안에 generate)
- proto 파일 내부 `option csharp_namespace = "FastPortGameServerTemplate.Protocols"`은 source repo와 scaffold output 모두 token replace로 자연 처리
- `<Compile Include Link>` 는 MSBuild 표준 — MAUI test 경험으로 검증됨 (memory: maui-test-project-tfm-gotcha)
- scaffold Protos copy는 LibCommons/LibNetworks와 동일 verbatim 패턴

---

## 2. Architecture Options

### 2.0 Architecture Comparison

| Criteria | Option A: Atomic | Option B: 2-Phase | Option C: 3-Phase |
|----------|:-:|:-:|:-:|
| Approach | 단일 commit (전체) | source repo first / scaffold+docs second | source / scaffold / docs separate |
| Commits | 1 | 2 | 3 |
| 중간 build/test 검증 | N/A | 각 phase 끝 | 각 phase 끝 |
| Review 단위 | ~25 files diff | ~15 + ~10 | ~10 + ~8 + ~6 |
| bisect | 불가 | phase 단위 | module 단위 |
| **Selected** | | ✅ | |

**Selected: Option B — 2-Phase**.
- Phase 1: source repo만 (sln/csproj/files) → build + 176 tests + echo round-trip 검증
- Phase 2: scaffold scripts + fixtures + docs → run.sh 7/7 + smoke build 검증
- `template-contracts-extraction` cycle의 동일 패턴, 안전성 입증됨.

### 2.1 Component Diagram (Before / After)

**Before** (현재 상태, archived `template-contracts-extraction` + `scaffold-fix` 결과):
```
template-projects/
├── FastPortGameServerTemplate.Contracts/      ← 본 cycle에서 제거
│   ├── Protocols/Sample.proto
│   ├── Handlers/PacketIds.cs
│   └── FastPortGameServerTemplate.Contracts.csproj  (net10.0 Class Lib)
├── FastPortGameServerTemplate/                 ← Contracts ProjectReference
└── FastPortGameServerTemplate.SampleClient/    ← Contracts ProjectReference

sln: 13 projects (Contracts 포함)
scaffold output: 4 projects (<NewName>.Contracts 포함)
```

**After**:
```
template-projects/
├── Protos/                                     ← NEW (단순 폴더)
│   └── Sample.proto                            ← Contracts에서 이동
├── FastPortGameServerTemplate/
│   ├── Handlers/PacketIds.cs                   ← Contracts에서 복귀
│   ├── ...
│   └── FastPortGameServerTemplate.csproj
│       + Grpc.Tools + Google.Protobuf.Tools
│       + <Protobuf Include="..\Protos\*.proto" .../>
│       - <ProjectReference Include="..\FastPortGameServerTemplate.Contracts\..."/>
└── FastPortGameServerTemplate.SampleClient/
    └── FastPortGameServerTemplate.SampleClient.csproj
        + Grpc.Tools + Google.Protobuf.Tools
        + <Protobuf Include="..\Protos\*.proto" .../>
        + <Compile Include="..\FastPortGameServerTemplate\Handlers\PacketIds.cs"
                    Link="Handlers\PacketIds.cs"/>
        - <ProjectReference Include="..\FastPortGameServerTemplate.Contracts\..."/>

sln: 12 projects (Contracts 제거)
scaffold output: 3 projects (<NewName>, LibCommons, LibNetworks) + Protos/ 폴더 verbatim
```

### 2.2 Build Flow (Each Consumer)

```
Each .csproj with <Protobuf Include="..\Protos\*.proto"/>:
  1. dotnet restore: Grpc.Tools 2.71.0 + Google.Protobuf.Tools 3.32.1 pull
  2. dotnet build:
     a. Grpc.Tools MSBuild task: ..\Protos\Sample.proto → obj/Debug/net10.0/Sample.cs
        (csharp_namespace = "FastPortGameServerTemplate.Protocols" from proto file)
     b. Generated cs compiled into <Consumer>.dll
     c. <Consumer>.dll exposes EchoRequest, EchoResponse types (Access="Public")
  3. Result: Template.dll, SampleClient.dll, (future) Dashboard.Maui.dll
     all contain their own EchoRequest/Response types from the same proto.
     Wire byte-format identical → TCP communication compatible.
     In-process type identity NOT shared (each dll has its own type) — irrelevant
     since these consumers are separate processes.
```

### 2.3 PacketIds Sharing (Source Level)

```
Template/Handlers/PacketIds.cs (single source of truth)
        │
        ├── compiled into Template.dll (direct file)
        └── <Compile Include Link> by SampleClient
            → compiled into SampleClient.dll (same source, different assembly)

Note: No assembly reference between Template and SampleClient.
Edit PacketIds.cs once, both dlls rebuild with updated values.
```

---

## 3. Data Model

해당 없음 — refactor.

```protobuf
// template-projects/Protos/Sample.proto (unchanged content, only relocated)
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
// template-projects/FastPortGameServerTemplate/Handlers/PacketIds.cs (unchanged content)
namespace FastPortGameServerTemplate.Handlers;

public static class PacketIds
{
    public const int EchoRequest = 1001;
    public const int EchoResponse = 1002;
}
```

---

## 4. API Specification

해당 없음.

---

## 5. UI/UX Design

해당 없음.

---

## 6. Error Handling

기존 동일. scaffold output의 build 실패 시 scaffold가 [12/12]에서 exit 4 — 이미 검증된 흐름.

---

## 7. Security Considerations

- proto wire 호환성 보존 (정의 변경 0)
- token replacement scope 한정 (Protos 폴더는 verbatim, token replace 안 함)
- generated cs는 obj/ 내부 — git에 commit 안 됨

---

## 8. Test Plan

### 8.1 Test Scope

| Type | Target | Tool | Phase |
|------|--------|------|-------|
| L1: Build | sln + 각 csproj | `dotnet build` | Phase 1 M6, Phase 2 M9 |
| L1: Tests | FastPortTests (139) + FastPortDashboardTests (37) | `dotnet test` | Phase 1 M6 |
| L1: Smoke | Template + SampleClient echo round-trip | dotnet run × 2 | Phase 1 M6 |
| L1: Golden | scaffold 7 cases × 2 flavors | `tests/scaffold/run.sh [--script ps1]` | Phase 2 M9 |
| L1: Scaffold smoke | dest scaffold + dotnet build | manual | Phase 2 M9 |
| L0: Diff | cs/proto content diff = 0 (rename only) | `git diff --stat` | Both |

### 8.2 Detailed Scenarios

| # | 검증 | 명령 | 통과 기준 |
|---|------|------|----------|
| T1 | Phase 1 sln build | `dotnet build FastPortSharp.sln -c Debug` | 0 error, 0 new warning |
| T2 | Phase 1 tests | `dotnet test FastPortSharp.sln --no-build` | 139 PASS |
| T3 | Phase 1 dashboard sln build | `dotnet build FastPortSharp.Dashboard.sln` | 0/0 |
| T4 | Phase 1 dashboard tests | `dotnet test tests-projects/FastPortDashboardTests/...` | 37 PASS |
| T5 | Phase 1 echo round-trip | Template + SampleClient dll 실행 | `RTT=XX.XXXms` 출력 |
| T6 | Phase 1 SampleClient bin 안티패턴 | `ls template-projects/.../SampleClient/bin/Debug/net10.0/` | Template.dll/exe 부재 |
| T7 | Phase 1 Contracts.dll 부재 | repo 전체 grep | 0 matches |
| T8 | Phase 2 scaffold sh suite | `bash tests/scaffold/run.sh` | 7/7 PASS |
| T9 | Phase 2 scaffold ps1 suite | `bash tests/scaffold/run.sh --script ps1` | 7/7 PASS |
| T10 | Phase 2 scaffold output build | scaffold + `dotnet build /tmp/Foo/Foo.sln` | 0/0 |
| T11 | Phase 2 scaffold output 안티패턴 | `<NewName>/bin/...` 검사 | 직접 ProjectReference 없음 |
| T12 | Cross-phase cs/proto diff | `git diff --stat HEAD~N HEAD -- '*.cs' '*.proto'` | 빈 출력 (rename only) |

### 8.3 Seed Data

해당 없음.

---

## 9. Clean Architecture

### 9.1 Layer Structure

| Layer | Location |
|-------|----------|
| Engine Core | `LibCommons/`, `LibNetworks/` |
| Engine Contracts | `Protocols/` (test contracts) |
| **Game Contracts (data)** | `template-projects/Protos/` ← NEW |
| Game Template Server | `template-projects/FastPortGameServerTemplate/` (own PacketIds.cs) |
| Game Template Sample Client | `template-projects/FastPortGameServerTemplate.SampleClient/` |
| Dashboard | `FastPortDashboard.Core/`, `FastPortDashboard.Maui/` |
| Test Suites | `tests-projects/` |

### 9.2 Dependency Rules

```
                                ┌────────────────────┐
                                │ template-projects/ │
                                │   Protos/          │  (data only — proto files)
                                │   Sample.proto     │
                                └────────────────────┘
                                          ▲
                                          │ <Protobuf Include="..\Protos\*.proto"/>
                                          │ (each consumer generates own cs)
        ┌─────────────────────────────────┼─────────────────────────────────┐
        │                                 │                                 │
┌───────┴──────────────────┐  ┌───────────┴───────────┐  ┌─────────────────┴────┐
│ Template                 │  │ SampleClient          │  │ (future) Dashboard   │
│   own PacketIds.cs       │  │   <Compile Link>      │  │   own gen + custom   │
│   own gen Sample.cs      │  │   <Compile> Template/ │  │   handling           │
│                          │  │     Handlers/         │  │                      │
│                          │  │     PacketIds.cs      │  │                      │
└──────────────────────────┘  └───────────────────────┘  └──────────────────────┘
```

규칙:
- Protos folder: data only, no csproj
- Each consumer: own `<Protobuf Include>` + own Grpc.Tools PackageReference
- PacketIds source: Template owns, others link via `<Compile Include Link>`
- No Exe→Exe ProjectReference (안티패턴 회피)

---

## 10. Coding Convention Reference

### 10.1 Per-Consumer `<Protobuf>` Convention

각 consumer csproj 패턴:
```xml
<ItemGroup>
  <PackageReference Include="Google.Protobuf" Version="3.32.1" />
  <PackageReference Include="Google.Protobuf.Tools" Version="3.32.1" />
  <PackageReference Include="Grpc.Tools" Version="2.71.0">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>

<ItemGroup>
  <Protobuf Include="..\Protos\*.proto"
            ProtoRoot="..\Protos"
            GrpcServices="None"
            Access="Public" />
</ItemGroup>
```

### 10.2 `<Compile Include Link>` Convention (PacketIds 공유)

SampleClient (그리고 향후 Dashboard):
```xml
<ItemGroup>
  <Compile Include="..\FastPortGameServerTemplate\Handlers\PacketIds.cs"
           Link="Handlers\PacketIds.cs" />
</ItemGroup>
```

- `Include` 경로: 실제 파일 위치 (relative)
- `Link` 경로: 이 csproj 안에서 보이는 가상 경로 (IDE solution explorer)
- 컴파일: source 파일 한 번 더 컴파일되어 SampleClient.dll에 PacketIds 타입 포함

### 10.3 scaffold Protos Copy Convention

LibCommons 패턴 답습:
- token replace 대상 아님 (proto + namespace 모두 verbatim)
- `<dest>/Protos/Sample.proto` 그대로
- `<NewName>/csproj` 의 `<Protobuf Include="..\Protos\*.proto"/>` 도 source repo와 동일 (Token replace는 namespace만, path는 동일)

단, proto 파일 내부 `option csharp_namespace = "FastPortGameServerTemplate.Protocols"`는 token replace로 `<NewName>.Protocols`가 됨 — 이 때문에 **Protos도 token replace 대상에 포함되어야** 함 (proto는 verbatim에서 예외).

**중요 결정**: Protos/Sample.proto 안의 `FastPortGameServerTemplate` 토큰도 token replace 적용 → `<NewName>.Protocols`로 변환. scaffold output의 Protos 폴더 자체는 LibCommons처럼 위치 verbatim, 단 파일 내용은 token replace.

---

## 11. Implementation Guide

### 11.1 Phase 1 — Source Repo (Commit 1)

**Goal**: 기능 동일, repo 구조 변경. echo round-trip + tests 회귀 검증.

**Steps**:

1. `mkdir template-projects/Protos`
2. `git mv template-projects/FastPortGameServerTemplate.Contracts/Protocols/Sample.proto template-projects/Protos/Sample.proto`
3. `git mv template-projects/FastPortGameServerTemplate.Contracts/Handlers/PacketIds.cs template-projects/FastPortGameServerTemplate/Handlers/PacketIds.cs`
4. `dotnet sln FastPortSharp.sln remove template-projects/FastPortGameServerTemplate.Contracts/FastPortGameServerTemplate.Contracts.csproj`
5. `git rm -r template-projects/FastPortGameServerTemplate.Contracts/`
6. Template csproj 수정:
   ```diff
       <PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.7" />
       ...
       <PackageReference Include="Google.Protobuf" Version="3.32.1" />
   +    <PackageReference Include="Google.Protobuf.Tools" Version="3.32.1" />
   +    <PackageReference Include="Grpc.Tools" Version="2.71.0">
   +      <PrivateAssets>all</PrivateAssets>
   +      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
   +    </PackageReference>
     </ItemGroup>
   +
   +  <ItemGroup>
   +    <Protobuf Include="..\Protos\*.proto"
   +              ProtoRoot="..\Protos"
   +              GrpcServices="None"
   +              Access="Public" />
   +  </ItemGroup>

     <ItemGroup>
       <ProjectReference Include="..\..\LibCommons\LibCommons.csproj" />
       <ProjectReference Include="..\..\LibNetworks\LibNetworks.csproj" />
   -    <ProjectReference Include="..\FastPortGameServerTemplate.Contracts\FastPortGameServerTemplate.Contracts.csproj" />
     </ItemGroup>
   ```
7. SampleClient csproj 수정:
   ```diff
       <PackageReference Include="Google.Protobuf" Version="3.32.1" />
   +    <PackageReference Include="Google.Protobuf.Tools" Version="3.32.1" />
   +    <PackageReference Include="Grpc.Tools" Version="2.71.0">
   +      <PrivateAssets>all</PrivateAssets>
   +      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
   +    </PackageReference>
     </ItemGroup>
   +
   +  <ItemGroup>
   +    <Protobuf Include="..\Protos\*.proto"
   +              ProtoRoot="..\Protos"
   +              GrpcServices="None"
   +              Access="Public" />
   +    <Compile Include="..\FastPortGameServerTemplate\Handlers\PacketIds.cs"
   +             Link="Handlers\PacketIds.cs" />
   +  </ItemGroup>

     <ItemGroup>
       <ProjectReference Include="..\..\LibCommons\LibCommons.csproj" />
       <ProjectReference Include="..\..\LibNetworks\LibNetworks.csproj" />
   -    <ProjectReference Include="..\FastPortGameServerTemplate.Contracts\FastPortGameServerTemplate.Contracts.csproj" />
     </ItemGroup>
   ```
8. **검증** (T1-T7): build × 2 sln, tests × 2 sln, echo round-trip, SampleClient bin 안티패턴, Contracts.dll 부재
9. **Commit 1**: `refactor: revert Contracts lib, introduce Protos shared folder`

### 11.2 Phase 2 — Scaffold + Fixtures + Docs (Commit 2)

**Goal**: scaffold가 새 구조 반영. fixture 4 case 갱신.

**Steps**:

1. `scripts/scaffold-game-server.sh` 수정:
   - `CONTRACTS_SRC` → `PROTOS_SRC` 변수 (`${REPO_ROOT}/template-projects/Protos`)
   - `copy_contracts()` → `copy_protos()` (verbatim, token replace 안 함)
   - `replace_tokens()` subtree array 단일화 (Contracts subtree 제거)
   - Contracts dir/csproj rename 로직 제거
   - **Protos 안의 .proto 파일도 token replace 대상** (csharp_namespace) — 별도 처리 추가
   - `generate_sln()` Contracts add 줄 제거 (3 projects)
   - path-fix 로직: `..\..\Protos` → `..\Protos` 추가 (LibCommons와 동일 패턴)
   - dry-run 로그 갱신 (Contracts 제거, Protos 추가, "3 projects")
2. `scripts/scaffold-game-server.ps1` 1:1 mirror
3. fixture 재생성:
   - `bash tests/scaffold/run.sh --update-golden case-01-simple` (sha256 + tree 자동)
   - case-01 files-present.txt manual: Contracts 엔트리 제거, `<NewName>/Handlers/PacketIds.cs` 복귀, `Protos/Sample.proto` 추가
   - case-05/07 files-present.txt: Contracts 엔트리 제거 (Protos/ 추가는 case마다 다름, 검증 시 확인)
4. **검증** (T8-T11): sh 7/7 + ps1 7/7 + scaffold output build
5. README 갱신:
   - `template-projects/FastPortGameServerTemplate/README.md` (proto 경로 `..\Protos\Sample.proto`)
   - `template-projects/FastPortGameServerTemplate/QUICKSTART.ko.md` (동일)
   - `README.md`, `README.ko.md` (리포 트리 갱신 — Contracts 제거, Protos 추가)
   - `scripts/README.md` (scaffold 출력 3 projects + Protos)
   - `docs/01-plan/features/dashboard-echo-client-tab.plan.md` (Predecessor 갱신 — Contracts → Protos)
6. **Commit 2**: `fix(scaffold): use Protos shared folder, remove Contracts handling`

### 11.3 Critical Edge Cases

**Protos token replacement (scaffold)**:
- LibCommons/LibNetworks는 token replace 대상 아님 (proto 안에 token 없음)
- 그러나 `Protos/Sample.proto` 안에 `option csharp_namespace = "FastPortGameServerTemplate.Protocols"` 가 있음
- scaffold가 새 게임 만들 때 namespace는 `<NewName>.Protocols`로 바뀌어야 함
- **결정**: Protos는 verbatim copy + Protos 안의 .proto 파일도 token replace subtree에 포함

scaffold script 패치:
```bash
# replace_tokens()
local subtrees=(
  "${DEST_PATH}/${TEMPLATE_TOKEN}"
  "${DEST_PATH}/Protos"            # ← NEW: Sample.proto의 csharp_namespace 갱신용
)
```

이 변경은 LibCommons와 다른 점이라 별도 주석 필요.

### 11.4 Session Guide

#### Module Map

| Module | Scope Key | 설명 | Estimated Turns |
|--------|-----------|------|:--:|
| M1 | `phase-1-protos` | Protos 폴더 생성 + Sample.proto 이동 | 2-3 |
| M2 | `phase-1-packetids` | PacketIds.cs Template/Handlers/ 복귀 | 1-2 |
| M3 | `phase-1-contracts-remove` | sln remove + Contracts/ 디렉터리 git rm -r | 2-3 |
| M4 | `phase-1-template-csproj` | Template csproj 갱신 | 3-5 |
| M5 | `phase-1-sampleclient-csproj` | SampleClient csproj 갱신 | 3-5 |
| M6 | `phase-1-verify` | build + tests + round-trip + 안티패턴 검증 | 5-8 |
| M7 | `phase-2-scaffold` | sh + ps1 수정 | 15-20 |
| M8 | `phase-2-fixtures` | golden + files-present | 5-8 |
| M9 | `phase-2-scaffold-verify` | run.sh sh+ps1 + smoke build | 5-8 |
| M10 | `phase-2-docs` | README 6개 + dashboard plan | 5-8 |

#### Recommended Session Plan

| Session | Phase | Scope | Turns |
|---------|-------|-------|:-----:|
| 1 (현재) | Plan + Design | 전체 | 35-45 |
| 2 | Do (Phase 1) | `--scope phase-1-*` (M1-M6) | 25-40 |
| 3 | Do (Phase 2) | `--scope phase-2-*` (M7-M10) | 35-50 |
| 4 | Check + Report + Archive | 전체 | 20-30 |

또는 한 세션에 M1-M10 + Check + Report + Archive (refactor 단순, ~80-120 turns).

### 11.5 Commit Strategy 상세

**Commit 1 message**:
```
refactor: revert Contracts lib, introduce Protos shared folder

Simplify proto sharing: replace FastPortGameServerTemplate.Contracts
(net10.0 Class Library) with template-projects/Protos/ (simple folder
containing only .proto files). Each consumer (Template, SampleClient)
now generates its own C# code via <Protobuf Include="..\Protos\*.proto"/>.

- Move Sample.proto: Contracts/Protocols/ -> Protos/ (git mv, content 0 diff)
- Move PacketIds.cs back: Contracts/Handlers/ -> Template/Handlers/
- Remove FastPortGameServerTemplate.Contracts/ directory + csproj
- sln: 13 -> 12 projects
- Template csproj: add Grpc.Tools + <Protobuf Include>, remove Contracts ref
- SampleClient csproj: add Grpc.Tools + <Protobuf Include>
  + <Compile Include="..\FastPortGameServerTemplate\Handlers\PacketIds.cs"
              Link="Handlers\PacketIds.cs" /> for source-level sharing
  (no Exe->Exe ProjectReference)

Verified:
- dotnet build FastPortSharp.sln: 0 error / 0 warning
- dotnet test: 139 (Engine) + 37 (Dashboard) = 176 PASS
- Echo round-trip: RTT measured
- SampleClient bin: no Template.dll (anti-pattern still resolved)

Predecessor cycles (archived, not reverted):
- template-contracts-extraction (b3e4e2c, ff105ea)
- template-contracts-scaffold-fix (241cef2)
```

**Commit 2 message**:
```
fix(scaffold): use Protos shared folder, remove Contracts handling

Adapt scaffold-game-server.{sh,ps1} to the new Protos folder model:
- PROTOS_SRC replaces CONTRACTS_SRC
- copy_protos() copies template-projects/Protos verbatim (like LibCommons)
- replace_tokens() subtree no longer includes <NewName>.Contracts/
  (but Protos/ subtree IS included for csharp_namespace token replacement)
- generate_sln() registers 3 projects (no <NewName>.Contracts)
- step counter [1/12]~[12/12] preserved

Fixtures regenerated:
- case-01-simple: sha256 + tree auto, files-present manual
- case-05-existing-dest-with-force: files-present (Contracts entry removed)
- case-07-no-git-no-smoke: files-present (Contracts entry removed)
- case-02/03/04/06: unaffected

Verified:
- bash tests/scaffold/run.sh: 7 PASS / 0 FAIL
- bash tests/scaffold/run.sh --script ps1: 7 PASS / 0 FAIL
- scaffold output dotnet build: 0/0

Docs updated:
- README.md, README.ko.md: repo tree (Contracts -> Protos)
- scripts/README.md: scaffold output structure (3 projects + Protos)
- template README/QUICKSTART: proto path -> ../Protos/Sample.proto
- dashboard-echo-client-tab.plan.md: Predecessor reference updated
```

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-12 | Initial draft. Option B (2-Phase) 채택. | das_young |
