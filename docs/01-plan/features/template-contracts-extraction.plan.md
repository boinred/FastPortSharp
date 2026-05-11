# template-contracts-extraction Planning Document

> **Summary**: (1) `FastPortGameServerTemplate` (Exe)에 묶여 있는 proto + PacketIds를 신규 `FastPortGameServerTemplate.Contracts` (net10.0 Class Library)로 분리하고, (2) Template 관련 3개 프로젝트를 `template-projects/` 폴더로 묶어 `tests-projects/` 와 일관된 구조를 만든다. 다중 소비자(SampleClient, Dashboard, 향후 게임 클라이언트)가 안티패턴 없이 재사용 + 리포 구조 일관성 확보.
>
> **Project**: FastPortSharp
> **Version**: 1.0
> **Author**: das_young
> **Date**: 2026-05-11
> **Status**: Draft

---

## Executive Summary

| Perspective | Content |
|-------------|---------|
| **Problem** | (a) `FastPortGameServerTemplate`가 `<OutputType>Exe</OutputType>`인데 그 안에 `Sample.proto` + `PacketIds`가 들어있어, SampleClient는 이미 Exe→Exe ProjectReference 안티패턴으로 동작 중이고 Dashboard 같은 신규 소비자는 추가가 막혀 있다. (b) Template 관련 프로젝트가 리포 루트에 평탄하게 흩어져 있어 `tests-projects/` 같은 그룹화 패턴과 일관성이 깨져 있다. |
| **Solution** | (a) `FastPortGameServerTemplate.Contracts` (net10.0 Class Library) 신설 → proto + PacketIds 이동 → Template/SampleClient는 Contracts를 ProjectReference. 기존 `Protocols/` 라이브러리 패턴 답습. (b) Template 3개 프로젝트(`Contracts`/`Server`/`SampleClient`)를 `template-projects/` 하위로 이동 → `tests-projects/` 와 동일 명명 규약. |
| **Function/UX Effect** | 기능 변화 0 (pure refactor). Template 서버 + SampleClient echo round-trip 동작 동일. 빌드 후 SampleClient bin에서 서버 어셈블리 사라짐 (안티패턴 해소). 리포 트리에서 template 영역이 한눈에 보임. |
| **Core Value** | (1) `dashboard-echo-client-tab` cycle unblock, (2) 게임 사용자가 자신의 proto를 추가할 표준 위치 제공, (3) SampleClient 기존 Exe 참조 안티패턴 해소, (4) `tests-projects/` ↔ `template-projects/` 대칭으로 리포 구조 가독성 향상. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | (a) Exe 프로젝트에 proto 타입을 가둔 구조가 다중 소비자 재사용을 막고 SampleClient는 Exe→Exe ProjectReference 안티패턴으로 우회 중. (b) Template 관련 프로젝트가 리포 루트 평탄 구조로 흩어져 `tests-projects/` 그룹화 패턴과 비대칭. |
| **WHO** | 직접: FastPortGameServerTemplate 유지 개발자(=본인), FastPortDashboard.Maui (Echo Client 탭 cycle 차기 진행), 향후 외부 게임 사용자(scaffold 스크립트로 새 프로젝트 생성). 간접: SampleClient 사용자, CI/CD 파이프라인. |
| **RISK** | (a) namespace 변경 시 downstream cs 전수 수정 → **namespace 보존**으로 cs 코드 0 변경 보장. (b) 폴더 이동 후 scaffold script `TEMPLATE_SRC` 경로 + GitHub Actions workflow `paths:` filter + ProjectReference 상대경로 미갱신 시 빌드/CI 깨짐 → Do 단계에서 영향 파일 전수 grep + 단일 PR로 동기화. |
| **SUCCESS** | (1) `dotnet build FastPortSharp.sln` 성공, (2) Template 서버 + SampleClient echo round-trip 정상 (RTT ms 로그), (3) Contracts lib 외부에서 `EchoRequest/EchoResponse/PacketIds` 사용 가능, (4) `template-projects/` 하위 3개 프로젝트 모두 IDE/CLI 양쪽에서 빌드, (5) scaffold-game-server 스크립트 dry-run 통과. |
| **SCOPE** | 신규 csproj 1개, sln 1개 수정, 기존 csproj 2개 수정 (path depth + ProjectReference), 파일 이동 2개 (proto, PacketIds.cs), **폴더 이동 3개** (Template/Server/Contracts/SampleClient → `template-projects/`), scaffold script 2개 (`.sh`/`.ps1`) path 변수 갱신, workflow yml 1개 `paths:` filter 갱신, README path 표기 갱신. cs 코드 수정 0. |

---

## 1. Overview

### 1.1 Purpose

`FastPortGameServerTemplate`에 포함된 proto/PacketIds를 외부 라이브러리로 분리하여, Echo 프로토콜을 사용하는 모든 소비자(서버, SampleClient, Dashboard, 향후 게임 클라이언트)가 동일한 contract 어셈블리를 참조하도록 한다.

### 1.2 Background

`FastPortGameServerTemplate.csproj:9`는 `<OutputType>Exe</OutputType>`이고, `Protocols/Sample.proto`와 `Handlers/PacketIds.cs`가 그 내부에 있다. `FastPortGameServerTemplate.SampleClient.csproj:34`는 echo 타입 재사용을 위해 Template Exe를 ProjectReference 하는 .NET 안티패턴으로 동작 중:

- 빌드 시 SampleClient bin에 Template Exe 어셈블리 + 무관한 Hosting/Telemetry 의존성 복사
- Template 서버 코드 한 줄 수정 → SampleClient 재컴파일
- 신규 소비자(`FastPortDashboard.Maui`, 추후 외부 게임 client)는 이 안티패턴을 따라 하기도 어려움 (Exe→GUI 참조는 더 문제)

이미 `Protocols/` (top-level Class Library, net10.0)이 FastPortServer/FastPortClient에서 사용되는 좋은 선례가 있으므로, Template 측도 동일 패턴을 따른다.

### 1.3 Related Documents

- Downstream cycle: `docs/01-plan/features/dashboard-echo-client-tab.plan.md` (본 cycle이 해당 cycle의 Risk #1 unblocker)
- Reference: `Protocols/Protocols.csproj` (선례 패턴)

---

## 2. Scope

### 2.1 In Scope

**Part A — Contracts 추출**
- [ ] 신규 프로젝트 `FastPortGameServerTemplate.Contracts/` 생성 (net10.0 Class Library, 단 최종 위치는 `template-projects/` 하위)
- [ ] `FastPortGameServerTemplate/Protocols/Sample.proto` → `template-projects/FastPortGameServerTemplate.Contracts/Protocols/Sample.proto` 이동 (내용 무변경)
- [ ] `FastPortGameServerTemplate/Handlers/PacketIds.cs` → `template-projects/FastPortGameServerTemplate.Contracts/Handlers/PacketIds.cs` 이동 (namespace 무변경)
- [ ] `FastPortGameServerTemplate.csproj`: `<Protobuf>` ItemGroup 제거, `<ProjectReference Contracts>` 추가, `Google.Protobuf` PackageReference 유지 (직접 인스턴스화), `Grpc.Tools` 제거
- [ ] `FastPortGameServerTemplate.SampleClient.csproj`: `<ProjectReference FastPortGameServerTemplate>` 제거, `<ProjectReference Contracts>` 추가

**Part B — 폴더 재구조화 (`template-projects/`)**
- [ ] `git mv FastPortGameServerTemplate template-projects/FastPortGameServerTemplate` (history 보존)
- [ ] `git mv FastPortGameServerTemplate.SampleClient template-projects/FastPortGameServerTemplate.SampleClient`
- [ ] `template-projects/FastPortGameServerTemplate/FastPortGameServerTemplate.csproj`: ProjectReference 상대경로 `..\LibCommons` → `..\..\LibCommons`, `..\LibNetworks` → `..\..\LibNetworks`, Contracts 참조는 `..\FastPortGameServerTemplate.Contracts` (동일 깊이)
- [ ] `template-projects/FastPortGameServerTemplate.SampleClient/FastPortGameServerTemplate.SampleClient.csproj`: ProjectReference 상대경로 동일 패턴으로 갱신
- [ ] `FastPortSharp.sln`: 3개 Project 경로를 `template-projects\...`로 갱신 + Contracts 신규 등록

**Part C — 부수 갱신**
- [ ] `scripts/scaffold-game-server.sh:48`: `TEMPLATE_SRC="${REPO_ROOT}/${TEMPLATE_TOKEN}"` → `TEMPLATE_SRC="${REPO_ROOT}/template-projects/${TEMPLATE_TOKEN}"` (또는 신규 var로 분리)
- [ ] `scripts/scaffold-game-server.ps1`: 동등 변경 (PowerShell `$Script:TemplateSrc` 경로)
- [ ] `.github/workflows/scaffold.yml`: `paths:` filter `'FastPortGameServerTemplate/**'` → `'template-projects/**'` (또는 더 정밀하게 `'template-projects/FastPortGameServerTemplate/**'`)
- [ ] `.github/workflows/build.yml`, `dashboard.yml`: hardcoded path 확인 후 필요 시 갱신
- [ ] `scripts/README.md`: 경로 표기 갱신
- [ ] `README.md` / `README.ko.md`: 트리 다이어그램 갱신
- [ ] `FastPortGameServerTemplate/README.md` 와 `QUICKSTART.ko.md`: 폴더 구조 다이어그램 갱신
- [ ] `FastPortDashboard.Maui/README.md`: Template 참조 부분 갱신

**Part D — 검증**
- [ ] `dotnet build FastPortSharp.sln` 0 error
- [ ] Template 서버 + SampleClient echo round-trip 회귀 검증 (RTT 로그)
- [ ] `scripts/scaffold-game-server.sh --dry-run` 또는 임시 디렉터리 scaffold 1회 성공
- [ ] `git diff --stat -- '*.cs'` 결과 비어 있음 (cs 코드 변경 0 검증)

### 2.2 Out of Scope

- Dashboard Maui 측 Contracts 참조 (그 작업은 `dashboard-echo-client-tab` cycle에서 처리)
- proto 메시지 추가/변경 (pure refactor, 형식 변경 0)
- PacketIds 값 재할당 (`EchoRequest=1001`, `EchoResponse=1002` 그대로)
- namespace 변경 (`csharp_namespace`, `FastPortGameServerTemplate.Handlers` 그대로)
- gRPC 서비스 추가 (`GrpcServices="None"` 그대로)
- LibCommons/LibNetworks 의존 추가 (Contracts는 순수 proto + 정적 클래스만)
- scaffold script가 생성하는 출력 구조 변경 (scaffold가 새 프로젝트 만들 때 `template-projects/`-style 그룹화로 떨굴지는 별도 결정 — 본 cycle은 **source path만** 갱신)
- `tests-projects/` 폴더 명명 통일 (현재 `tests-projects/` 그대로 유지 — `tests/` 폴더와 별개)
- Solution Folder 그룹화 (sln의 `Project("{2150E333-...}")` solution folder 트리는 IDE 작업 영역 — 본 cycle은 csproj 경로만 갱신)

---

## 3. Requirements

### 3.1 Functional Requirements

| ID | Requirement | Priority | Status |
|----|-------------|----------|--------|
| FR-01 | `FastPortGameServerTemplate.Contracts` (net10.0 Class Library) 신규 프로젝트가 sln에 포함되고 단독 빌드 가능 | High | Pending |
| FR-02 | `Sample.proto`로부터 `EchoRequest`/`EchoResponse` C# 클래스가 Contracts 어셈블리에 생성되며 namespace는 `FastPortGameServerTemplate.Protocols` 유지 | High | Pending |
| FR-03 | `PacketIds.EchoRequest=1001`, `PacketIds.EchoResponse=1002` 가 Contracts 어셈블리의 `FastPortGameServerTemplate.Handlers` namespace에 위치 | High | Pending |
| FR-04 | `FastPortGameServerTemplate` (Exe)는 더 이상 `<Protobuf>` Item을 갖지 않고 Contracts ProjectReference로 동일 타입 사용 | High | Pending |
| FR-05 | `FastPortGameServerTemplate.SampleClient`는 Template Exe ProjectReference 제거, Contracts만 참조 | High | Pending |
| FR-06 | 전체 sln `dotnet build` 성공 (Dashboard 빌드는 본 cycle 영향 범위 외 — 동일 결과) | High | Pending |
| FR-07 | Template 서버 + SampleClient 동시 실행 시 echo round-trip 정상 동작 (RTT 로그 출력) | High | Pending |
| FR-08 | Template 관련 3개 프로젝트(`FastPortGameServerTemplate`, `FastPortGameServerTemplate.Contracts`, `FastPortGameServerTemplate.SampleClient`)가 모두 `template-projects/` 하위에 위치 | High | Pending |
| FR-09 | `scripts/scaffold-game-server.sh` 와 `.ps1` 이 새 경로(`template-projects/...`)에서 source를 찾아 동작 | High | Pending |
| FR-10 | `.github/workflows/scaffold.yml` `paths:` filter가 새 경로 반영 | Medium | Pending |

### 3.2 Non-Functional Requirements

| Category | Criteria | Measurement Method |
|----------|----------|-------------------|
| Compatibility | downstream cs 파일 수정 0 (using/namespace 변경 없음) | `git diff --stat -- '*.cs'` 결과가 비어있음 |
| Build Time | 분리 후 SampleClient incremental build 시간 동일 또는 감소 (Template 서버 재컴파일 전파 차단) | `dotnet build` 2회 측정 (clean → no-op) |
| Refactor Safety | proto 바이너리 wire 호환성 보존 (Sample.proto 내용 변경 0) | `git diff Sample.proto` 결과가 비어있음 |
| Solution Structure | Contracts lib는 `LibCommons`/`LibNetworks`/`FastPortServer`/`FastPortClient` 의존 없음 (game contract만) | `dotnet list FastPortGameServerTemplate.Contracts.csproj reference` |

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] FR-01 ~ FR-07 모두 충족
- [ ] `dotnet build FastPortSharp.sln` 0 error / 0 new warning
- [ ] Template 서버 실행 후 SampleClient 실행 → echo round-trip 로그 정상 출력
- [ ] `git diff` 상 cs 파일 변경 0 (proto/cs 이동 + csproj/sln 만 변경)
- [ ] Template `README.md` 의 폴더 구조 그림이 현실과 일치

### 4.2 Quality Criteria

- [ ] Contracts csproj가 server/client 어셈블리 어느 쪽에도 의존하지 않음 (단방향 의존성)
- [ ] Contracts bin에서 EchoRequest 타입이 public access로 노출됨 (`Access="Public"` 유지)
- [ ] SampleClient bin 디렉터리에 FastPortGameServerTemplate.dll/exe 사라짐 (안티패턴 해소 확인)

---

## 5. Risks and Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| `Grpc.Tools` 가 Class Library에서 동작 안 함 | High | Low | 이미 `Protocols/Protocols.csproj`가 동일 패턴(net10.0 Class Library + Grpc.Tools)으로 동작 중. 패키지 버전(`Google.Protobuf 3.32.1`, `Grpc.Tools 2.71.0`) 동일 사용. |
| Solution build 순서 / Solution Folder 어긋남 | Low | Low | `dotnet sln add` 사용하면 ProjectReference 기반으로 자동 ordering. Solution Folder는 sln 파일 수동 정리 또는 IDE에서 후처리. |
| `<RootNamespace>` 차이로 인한 generated proto code namespace mismatch | Medium | Low | proto 파일 내부 `option csharp_namespace = "FastPortGameServerTemplate.Protocols";` 명시적 지정이 RootNamespace보다 우선 → 안전. |
| `FastPortGameServerTemplate.csproj`에서 `Google.Protobuf` PackageReference를 같이 제거하면 직접 인스턴스화 코드 컴파일 실패 | Medium | Medium | Template 서버는 여전히 `new EchoResponse { ... }` 등 직접 사용 → `Google.Protobuf` PackageReference는 **유지**, `Grpc.Tools`만 제거 (또는 둘 다 유지하고 `<Protobuf>` 항목만 제거). 단순 안전책: Protobuf+Grpc.Tools 모두 유지하고 `<Protobuf Include>` 항목만 비움. |
| SampleClient의 기존 `<ProjectReference Template>` 제거 시 다른 implicit 의존(LibCommons/LibNetworks transitive) 끊김 | Low | Low | SampleClient.csproj는 이미 `<ProjectReference LibCommons>`/`<ProjectReference LibNetworks>` 명시적 보유 (line 30-31 동등 위치). transitive 의존 없음. |
| 외부 git clone 사용자가 옛 경로(`FastPortGameServerTemplate/Protocols/`) reference 보유 | Low | Medium | 본 cycle은 internal refactor. README 갱신만으로 처리. |
| `git mv` 후 ProjectReference 상대경로 (`..\LibCommons` 등) 미갱신으로 빌드 실패 | High | Medium | Plan 2.1에서 `..\..\LibCommons` 명시. Do 단계에서 grep으로 전수 검사: `grep -nE "Include=\"\.\.\\\\?" template-projects/**/*.csproj`. 빌드 1회로 즉시 검출됨. |
| `scaffold-game-server` 의 SOURCE 경로 미갱신으로 외부 사용자가 scaffold 실행 시 실패 | High | Low | FR-09로 명시. Plan에 `TEMPLATE_SRC` 라인 번호 기록(`scripts/scaffold-game-server.sh:48`). |
| GitHub Actions `paths:` filter 미갱신으로 CI trigger 누락 (template 변경 시 빌드 skip) | Medium | Medium | FR-10. workflow yml 3개 모두 grep 후 일괄 갱신. 첫 PR 시 actions 트리거 확인. |
| `git mv` 시 cross-platform CRLF/LF 이슈로 history detection 실패 | Low | Low | macOS Darwin 환경 사용 + `core.autocrlf=input` 기본. `git log --follow` 로 검증. |

---

## 6. Impact Analysis

### 6.1 Changed Resources

| Resource | Type | Change Description |
|----------|------|--------------------|
| `template-projects/FastPortGameServerTemplate.Contracts/FastPortGameServerTemplate.Contracts.csproj` | New csproj | net10.0 Class Library 신규 생성 (Google.Protobuf + Grpc.Tools, `<Protobuf Include="Protocols\**\*.proto" GrpcServices="None" Access="Public" />`) |
| `template-projects/FastPortGameServerTemplate.Contracts/Protocols/Sample.proto` | File moved | `FastPortGameServerTemplate/Protocols/Sample.proto`에서 이동, 내용 무변경 |
| `template-projects/FastPortGameServerTemplate.Contracts/Handlers/PacketIds.cs` | File moved | `FastPortGameServerTemplate/Handlers/PacketIds.cs`에서 이동, namespace 무변경 |
| `FastPortGameServerTemplate/` 전체 디렉터리 | Directory moved | `template-projects/FastPortGameServerTemplate/` 로 `git mv` (history 보존) |
| `FastPortGameServerTemplate.SampleClient/` 전체 디렉터리 | Directory moved | `template-projects/FastPortGameServerTemplate.SampleClient/` 로 `git mv` |
| `template-projects/FastPortGameServerTemplate/FastPortGameServerTemplate.csproj` | Modified | (1) `<Protobuf Include>` 항목 제거 + `<ProjectReference Contracts>` 추가 + `Grpc.Tools` 제거. (2) ProjectReference 상대경로 `..\LibCommons` → `..\..\LibCommons`, `..\LibNetworks` → `..\..\LibNetworks` (depth +1). Contracts 참조는 `..\FastPortGameServerTemplate.Contracts` (동일 깊이). |
| `template-projects/FastPortGameServerTemplate.SampleClient/FastPortGameServerTemplate.SampleClient.csproj` | Modified | (1) `<ProjectReference FastPortGameServerTemplate>` 제거 + `<ProjectReference Contracts>` 추가. (2) `..\LibCommons` → `..\..\LibCommons`, `..\LibNetworks` → `..\..\LibNetworks`. |
| `FastPortSharp.sln` | Modified | (1) 3개 Project 경로를 `template-projects\...` 로 갱신, (2) Contracts 프로젝트 신규 등록(`Project(...)`, GUID, Configuration mapping). |
| `scripts/scaffold-game-server.sh` | Modified | line 48 `TEMPLATE_SRC="${REPO_ROOT}/${TEMPLATE_TOKEN}"` → `TEMPLATE_SRC="${REPO_ROOT}/template-projects/${TEMPLATE_TOKEN}"`. 출력 destination은 변경하지 않음 (외부 사용자 영향 0). |
| `scripts/scaffold-game-server.ps1` | Modified | 동등 변경 (PowerShell 변수) |
| `scripts/README.md` | Modified | 경로 표기 갱신 |
| `.github/workflows/scaffold.yml` | Modified | `paths:` filter `'FastPortGameServerTemplate/**'` → `'template-projects/FastPortGameServerTemplate/**'` (line 13, 22) |
| `.github/workflows/build.yml` | Verify | hardcoded path 없으면 변경 없음, 있으면 갱신 |
| `.github/workflows/dashboard.yml` | Verify | 동일 |
| `README.md` / `README.ko.md` | Modified | 리포 트리 다이어그램 갱신 |
| `template-projects/FastPortGameServerTemplate/README.md` | Modified | 폴더 구조 다이어그램 갱신 (Protocols → Contracts로 이동) |
| `template-projects/FastPortGameServerTemplate/QUICKSTART.ko.md` | Modified | 동일 |
| `FastPortDashboard.Maui/README.md` | Modified | Template 경로 표기 갱신 |

### 6.2 Current Consumers

| Resource | Operation | Code Path | Impact |
|----------|-----------|-----------|--------|
| `EchoRequest` / `EchoResponse` | 인스턴스화 | `FastPortGameServerTemplate.Application/...` (서버 Echo handler) | None — namespace 보존 |
| `EchoRequest` / `EchoResponse` | 인스턴스화 | `FastPortGameServerTemplate.SampleClient/Sessions/SampleClientSession.cs:41,49` | None — namespace 보존 |
| `PacketIds.EchoRequest` / `PacketIds.EchoResponse` | static read | `FastPortGameServerTemplate.SampleClient/Sessions/SampleClientSession.cs:42,55,57` | None — namespace 보존 |
| `PacketIds.EchoRequest` / `PacketIds.EchoResponse` | static read | `FastPortGameServerTemplate/Application/` (서버 handler dispatch) | None — namespace 보존 |
| Dashboard Maui (예정) | 신규 참조 | `FastPortDashboard.Maui` (Echo Client 탭 cycle) | New consumer — Contracts ProjectReference 한 줄로 해결 |

### 6.3 Verification

- [ ] downstream cs 전수 검사: `grep -rn "PacketIds\|EchoRequest\|EchoResponse" --include="*.cs"` 결과 모두 namespace mismatch 없음
- [ ] `dotnet build FastPortSharp.sln` 성공
- [ ] Template + SampleClient round-trip echo 동작 verify (RTT 로그)
- [ ] `dotnet list FastPortGameServerTemplate.SampleClient.csproj reference` 출력에 Template csproj 없음 확인

---

## 7. Architecture Considerations

### 7.1 Project Level Selection

본 cycle은 .NET solution 구조 refactor — bkit Level 매트릭스(웹 중심)는 직접 적용되지 않음. 가까운 매핑:

| Level | 적용 여부 |
|-------|----------|
| Starter | ❌ (단순 정적 사이트가 아님) |
| Dynamic | ❌ (BaaS 없음) |
| Enterprise | ☑ Engine-level boundary 보호 + monorepo project split — Enterprise 패턴 적용 |

### 7.2 Key Architectural Decisions

| Decision | Options | Selected | Rationale |
|----------|---------|----------|-----------|
| Contract 분리 위치 | (a) `FastPortGameServerTemplate.Contracts` 신규 lib, (b) 기존 top-level `Protocols/` lib에 Sample.proto 추가, (c) `<Compile Include Link>` 우회 | **(a) Contracts 신규 lib** | (b)는 Protocols가 *engine-test* contract 영역 — game-specific proto 섞이면 layer 오염. (c)는 anti-pattern. (a)는 template 영역에 게임 contract 표준 위치 제공 + 외부 사용자도 동일 패턴 확장 가능. |
| Namespace 정책 | (a) namespace 보존 (`FastPortGameServerTemplate.Protocols/Handlers`), (b) 신규 namespace로 마이그레이션 (`...Contracts.Protocols`) | **(a) 보존** | downstream cs 0 변경 = 최소 침습 refactor. 추후 namespace 정리 필요 시 별도 cycle. |
| `Google.Protobuf` / `Grpc.Tools` 위치 | (a) Contracts만, (b) Contracts + Template 양쪽, (c) Template 그대로 | **(a) Contracts만** | Template은 Contracts ProjectReference로 transitive 획득. `Grpc.Tools`는 빌드타임 도구 — Contracts에서 한 번만 실행. `Google.Protobuf`는 runtime — transitive 노출. |
| Output assembly name | (a) `FastPortGameServerTemplate.Contracts`, (b) `FastPort.Sample.Contracts` | **(a)** | 기존 명명 규칙(`FastPortGameServerTemplate.SampleClient`) 일관. |
| sln Solution Folder | (a) flat (other projects와 같은 레벨), (b) `Template/` 하위 그룹화 | **(a) flat** | 본 cycle 범위 최소화. 그룹화는 future cycle 또는 IDE 작업. |

### 7.3 Clean Architecture Approach

```
Selected Level: Enterprise (engine boundary protection)

Repo Layout (After):
/
├── FastPortServer/                              (변경 없음)
├── FastPortClient/                              (변경 없음)
├── LibCommons/  LibNetworks/  Protocols/        (변경 없음 — engine)
├── FastPortDashboard.Core/  FastPortDashboard.Maui/  (변경 없음)
├── tests-projects/                              (기존 패턴 — test 그룹)
│   ├── FastPortTests/
│   ├── FastPortTestSmokeServer/
│   └── ...
├── template-projects/                           ← 신규 (대칭 그룹)
│   ├── FastPortGameServerTemplate/              ← 이동
│   ├── FastPortGameServerTemplate.Contracts/    ← 신규
│   └── FastPortGameServerTemplate.SampleClient/ ← 이동
└── scripts/  docs/  ...

Project Dependency Graph (After):
┌─────────────────────────────────────────────────────────────┐
│ template-projects/FastPortGameServerTemplate.Contracts      │ ← 새 layer
│   (net10.0 Class Library)                                   │
│   - Protocols/Sample.proto                                  │
│   - Handlers/PacketIds.cs                                   │
│   - depends: Google.Protobuf, Grpc.Tools (build-only)       │
│   - NO LibCommons, NO LibNetworks                           │
└─────────────────────────────────────────────────────────────┘
            ↑                            ↑                ↑
            │                            │                │
┌───────────┴────────────┐  ┌────────────┴───────────┐  ┌─┴──────────────────┐
│ template-projects/     │  │ template-projects/     │  │ FastPortDashboard. │
│   FastPortGameServer-  │  │   FastPortGameServer-  │  │   Maui             │
│   Template (Exe)       │  │   Template.SampleClient│  │   (next cycle)     │
│ + LibCommons (../../)  │  │   (Exe)                │  │                    │
│ + LibNetworks (../../) │  │ + LibCommons (../../)  │  │                    │
└────────────────────────┘  │ + LibNetworks (../../) │  └────────────────────┘
                            └────────────────────────┘
```

핵심:
- Contracts는 **leaf node** (외부 두 NuGet만 의존). 누구나 안전하게 참조.
- `template-projects/` ↔ `tests-projects/` **대칭 그룹화** — 리포 루트 가독성 향상.
- ProjectReference 상대경로는 `..\..\` 패턴으로 통일 (`tests-projects/`와 동일 depth).

---

## 8. Convention Prerequisites

### 8.1 Existing Project Conventions

- ☑ `CLAUDE.md`, `AGENTS.md` 존재 (engine boundary 정책 문서화됨)
- ☑ `Protocols/Protocols.csproj` — 동일 패턴 선례
- ☑ `.editorconfig` / C# nullable + ImplicitUsings 정책 일관

### 8.2 Conventions to Define/Verify

| Category | Current State | To Define | Priority |
|----------|---------------|-----------|:--------:|
| **Naming** | `<ProjectName>.<SubArea>` (e.g., `FastPortGameServerTemplate.SampleClient`) | 신규: `FastPortGameServerTemplate.Contracts` — 동일 패턴 | High |
| **Folder structure** | 기존 Template은 `Protocols/`, `Handlers/` 하위 — Contracts에서도 동일 구조 유지 | `Contracts/Protocols/`, `Contracts/Handlers/` | High |
| **csproj structure** | `Protocols/Protocols.csproj` 패턴 (Google.Protobuf + Grpc.Tools + `<Protobuf Include>`) | 동일 적용 | High |
| **Namespace 규약** | proto파일은 `option csharp_namespace` 명시, cs 파일은 폴더 기반 namespace | namespace 그대로 유지 (변경 없음) | High |

### 8.3 Environment Variables Needed

해당 없음 (build-only structural refactor).

### 8.4 Pipeline Integration

해당 없음 (9-phase web pipeline은 .NET solution refactor에 적용되지 않음).

---

## 9. Next Steps

1. [ ] Design 단계: 단일 옵션이 이미 명확하므로 Design 문서는 간략 — 모듈별 파일/csproj/sln/script/workflow diff 명세 위주
2. [ ] Do 단계: 모듈 분할
   - **M0** — 사전 grep & 영향 파일 inventory 확정
   - **M1** — `template-projects/` 폴더 생성 + `git mv FastPortGameServerTemplate*` 2개 이동
   - **M2** — Contracts 프로젝트 신설 (`template-projects/FastPortGameServerTemplate.Contracts/`)
   - **M3** — Sample.proto + PacketIds.cs를 Contracts로 이동 (`git mv`)
   - **M4** — Template/SampleClient csproj 갱신 (Protobuf 제거 + Contracts 추가 + 상대경로 depth +1)
   - **M5** — sln 갱신 (3개 path + Contracts 신규)
   - **M6** — scaffold 스크립트 2개 + workflow yml 1개 갱신
   - **M7** — README 일괄 갱신
   - **M8** — `dotnet build` + echo round-trip + scaffold dry-run 검증
3. [ ] Check 단계: build 결과 + round-trip 결과 + `git diff --stat -- '*.cs'` 0 확인 + scaffold dry-run pass
4. [ ] Archive 후 → `dashboard-echo-client-tab` cycle Design 재개 (Contracts ProjectReference 한 줄로 unblock)

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-11 | Initial draft | das_young |
