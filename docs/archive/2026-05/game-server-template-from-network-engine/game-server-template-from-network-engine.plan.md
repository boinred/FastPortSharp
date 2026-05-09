# game-server-template-from-network-engine Planning Document

> **Summary**: `LibCommons` + `LibNetworks`를 재사용 가능한 네트워크 엔진으로 정리하고, `FastPortGameServerTemplate` 신규 프로젝트를 추가하여 monorepo 내부 dogfooding + GitHub Template Repo로 노출한다 (Phase 1 only).
>
> **Project**: FastPortSharp
> **Version**: (.NET 10 / FastPortCharp.sln)
> **Author**: boinred
> **Date**: 2026-05-09
> **Status**: Draft
> **PRD**: `docs/00-pm/game-server-template-from-network-engine.prd.md`
> **Level**: Dynamic (multi-project .NET solution, internal team usage)

---

## Executive Summary

| Perspective | Content |
|-------------|---------|
| **Problem** | C# 게임 서버 0→1 부트스트랩 비용. FastPortSharp는 10K 세션 검증된 TCP 엔진(`LibCommons` + `LibNetworks`)을 갖고 있지만, 신규 프로젝트가 재사용할 "엔진 경계 + 게임 서버 스타터" 형태가 없다. |
| **Solution** | 본 cycle은 **Phase 1만** 수행: 엔진 패키지 경계를 `FastPort.Common` + `FastPort.Networks` 둘로 정리(코드는 monorepo 유지, NuGet 발행은 다음 cycle), `FastPortGameServerTemplate` 신규 프로젝트를 추가하여 dogfooding 가능한 게임 서버 부트스트랩 코드 베이스를 만든다. |
| **Function/UX Effect** | 솔루션에서 `FastPortGameServerTemplate`를 복제/참조하면 Generic Host + DI + Serilog + sample game session/handler + 자체 `.proto` 폴더가 갖춰진 게임 서버를 5–10분 안에 echo까지 구동 가능. |
| **Core Value** | "엔진/템플릿 경계가 명확한 monorepo" 1차 확보. 본인/내부 팀이 차기 게임 서버 프로젝트를 즉시 시작할 수 있고, 다음 cycle(NuGet/dotnet new) 진입을 위한 기반을 마련한다. |

---

## Context Anchor

> Auto-generated from Executive Summary. Propagated to Design/Do documents for context continuity.

| Key | Value |
|-----|-------|
| **WHY** | C# 게임 서버 0→1 비용을 줄이고, 검증된 TCP 엔진의 재사용 경계와 게임 서버 부트스트랩 템플릿을 monorepo 안에서 dogfooding 가능한 형태로 마련한다. |
| **WHO** | Primary: 본인/내부 팀 (Persona 1 — Solo Soyoung). Secondary(차기 cycle): C# 인디 게임 개발자 (Persona 2 — Indie Ian). |
| **RISK** | (R-2) 엔진/템플릿 분리가 호환 매트릭스 폭발로 이어짐. (R-3) Phase 1만으로는 외부 dogfood가 어려워 dogfooding이 본인 1명에 한정될 수 있음. |
| **SUCCESS** | (1) `FastPortGameServerTemplate` 신규 프로젝트가 `FastPortCharp.sln`에서 빌드 통과 + echo 송수신 5–10분 내 동작. (2) 엔진 패키지 경계(`FastPort.Common` / `FastPort.Networks`) docs/CHANGELOG에 명시. (3) GitHub Template Repo 또는 monorepo subtree 형태로 외부 노출 경로 1개 결정/문서화. (4) 10K 벤치 baseline 동등성 유지. |
| **SCOPE** | Phase 1 only: monorepo 내부 템플릿 프로젝트 + 엔진 namespace/csproj 경계 정리 + GitHub Template Repo 구성 결정. NuGet publish, `dotnet new` 등록, room/match/auth/heartbeat/UDP는 모두 차기 cycle/roadmap. |

---

## 1. Overview

### 1.1 Purpose

C# .NET 게임 서버 신규 프로젝트의 0→1 부트스트랩 비용을 줄이기 위해, 본 PDCA cycle은 다음 두 가지를 한다.

1. 기존 `LibCommons` + `LibNetworks`의 **재사용 패키지 경계**를 결정하고 csproj/namespace/문서를 그 경계에 맞춘다 (코드는 monorepo 유지, NuGet 발행은 차기 cycle).
2. 신규 `FastPortGameServerTemplate` 프로젝트를 추가하여, 엔진 경계 위에서 게임 서버를 5–10분 안에 부트스트랩할 수 있는 **"살아 있는 템플릿"** 을 monorepo 안에 둔다. 이를 GitHub Template Repository로도 노출 가능한 구조로 정리한다.

본 cycle은 PRD §6.1의 **Phased Path Phase 1만** 수행하며, **Phase 2(NuGet preview + `dotnet new`)는 후속 cycle**로 분리한다.

### 1.2 Background

- 기존 자산: `LibCommons` (buffers/packet/IDs), `LibNetworks` (listener/session, protocol-neutral), `FastPortServer` (engine sample/host), `FastPortTestSmokeServer` + `FastPortTestLoadRunner`/`Validation` (test 인프라), `LibTestTelemetry` (test telemetry contracts), `Protocols/` (engine-internal sample protocol).
- 최근 telemetry 분리(`extract-telemetry-contracts-from-network-core`, `remove-server-telemetry-from-network-base-classes`)로 `LibNetworks`의 protocol-neutral 성격이 정리되어 패키지화 사전 조건이 충족된 상태다.
- HANDOFF.md L278-281의 architecture decision (LibNetworks protocol-neutral, FastPortServer = engine host, smoke 코드는 SmokeServer로) 와 모순되지 않게 유지해야 한다.

### 1.3 Related Documents

- PRD: `docs/00-pm/game-server-template-from-network-engine.prd.md`
- Architecture rules: `HANDOFF.md`, `AGENTS.md`, `README.md`
- Prior feature (telemetry separation): `docs/01-plan/features/remove-server-telemetry-from-network-base-classes.plan.md`

---

## 2. Scope

### 2.1 In Scope

- [ ] 엔진 패키지 경계 결정: `FastPort.Common` (← `LibCommons`) + `FastPort.Networks` (← `LibNetworks`) 둘로 분리. csproj `<RootNamespace>` / `<AssemblyName>` / `<PackageId>` 메타데이터 정렬 (실제 NuGet publish는 본 cycle 외).
- [ ] `LibCommons.csproj` / `LibNetworks.csproj` 의 `<PackageId>`, `<Description>`, `<Authors>`, `<RepositoryUrl>` 등 NuGet 친화 메타데이터 채움 (publish 자체는 X).
- [ ] `FastPortGameServerTemplate` 신규 프로젝트 생성, `FastPortCharp.sln`에 등록.
  - `Program.cs` (Generic Host) + sample `GameServer` / `GameSession` / `PacketHandler`
  - `appsettings.json` (listen port, log level)
  - `Serilog` 기본 구성
  - 자체 `Protocols/` 폴더 (`Sample.proto`) + MSBuild gen target (생성기 동작은 v1 필수)
  - 기본 텔레메트리 hook 자리만 마련 (concrete impl은 후속)
- [ ] `FastPortClient` 또는 별도 sample client로 echo 송수신 시나리오 1개 (template 결과물에 docs/QUICKSTART 포함).
- [ ] `docs/CHANGELOG.md` 또는 README 갱신: 엔진 패키지 경계 + 템플릿 프로젝트 추가 명시.
- [ ] GitHub Template Repository 노출 경로 결정: (a) FastPortSharp 자체를 Template Repo로 표기 vs (b) 별도 repo로 split. 결정 + 1차 문서.
- [ ] HANDOFF.md "Important Architecture Decisions" 섹션에 본 cycle 결정 반영.
- [ ] CI: 신규 프로젝트가 `dotnet build FastPortCharp.sln -c Release` / `dotnet test ... --no-build` 통과.

### 2.2 Out of Scope

- NuGet.org publish (차기 cycle: `engine-publish-to-nuget`).
- `dotnet new` 템플릿 등록 (`*.template.config/template.json`) — Phase 2.
- room / matchmaking / authentication / heartbeat / tick / UDP / Unity SDK / MAUI dashboard — 전부 roadmap.
- `FastPortServer` 자체의 큰 리팩토링 (engine sample/host 역할 유지).
- `Protocols/` 프로젝트 이동/재구성 (현 상태 유지, 템플릿은 자체 `.proto` 폴더 생성).
- 10K 벤치 시나리오 자체 변경 (baseline 동등성만 확인).

---

## 3. Requirements

### 3.1 Functional Requirements

| ID | Requirement | Priority | Status |
|----|-------------|----------|--------|
| FR-01 | `LibCommons.csproj`의 `<PackageId>=FastPort.Common`, `<RootNamespace>=FastPort.Common` (또는 alias 유지)로 정렬 + NuGet 메타데이터 채움 | High | Pending |
| FR-02 | `LibNetworks.csproj`의 `<PackageId>=FastPort.Networks`, `<RootNamespace>=FastPort.Networks` (또는 alias 유지)로 정렬 + NuGet 메타데이터 채움 | High | Pending |
| FR-03 | namespace 변경이 발생하는 경우, 기존 코드(특히 `FastPortServer`/`FastPortClient`/`FastPortTestSmokeServer`/`FastPortTests`)의 `using` 갱신 — 전체 솔루션 빌드/테스트 통과 | High | Pending |
| FR-04 | `FastPortGameServerTemplate` 프로젝트 생성: `Program.cs`, `GameServer.cs`, `GameSession.cs`, sample `PacketHandler` | High | Pending |
| FR-05 | 템플릿에 `appsettings.json` (listen port, log level) + Serilog DI 등록 | High | Pending |
| FR-06 | 템플릿에 자체 `Protocols/Sample.proto` + MSBuild gen target → `dotnet build` 시 .cs 자동 생성 | High | Pending |
| FR-07 | 템플릿 + `FastPortClient` 또는 sample client로 echo 송수신 시나리오 동작 (수동 검증) | High | Pending |
| FR-08 | 템플릿 README/QUICKSTART (영문 + 한국어): "5분 내 echo" 절차 | Medium | Pending |
| FR-09 | GitHub Template Repository 노출 경로 결정 (a/b) + 결정 사유 문서화 | Medium | Pending |
| FR-10 | HANDOFF.md "Important Architecture Decisions" 섹션 갱신 (엔진 경계/템플릿 위치) | Medium | Pending |
| FR-11 | 기본 텔레메트리 hook 자리 마련: 템플릿이 `LibTestTelemetry`를 강제 참조하지 않도록 추상화 위치만 잡기 | Low | Pending |

### 3.2 Non-Functional Requirements

| Category | Criteria | Measurement Method |
|----------|----------|-------------------|
| Build | `dotnet build FastPortCharp.sln -c Release` warning 0, error 0 | CLI |
| Test | `dotnet test FastPortCharp.sln -c Release --no-build` 100% pass | CLI |
| Performance | 10K 세션 벤치(`s5-random-10k`) RTT P95 ≤ baseline + 5% | `FastPortTestLoadRunner` baseline 비교 (HANDOFF L70-90) |
| Architecture | `LibNetworks`의 protocol-neutral 성질 유지 (`Protocols/` 또는 게임 도메인 타입 import 없음) | `rg "Protocols\." LibNetworks` → 0 매치 |
| Architecture | `LibNetworks` / `LibCommons`가 `LibTestTelemetry` 참조하지 않음 | `rg "LibTestTelemetry" LibNetworks LibCommons` → 0 매치 |
| Compatibility | 기존 `FastPortServer` / `FastPortClient` / 테스트 프로젝트 모두 정상 빌드/실행 | CI |
| Bootstrap UX | 신규 사용자(또는 본인)가 README만 보고 5–10분 내 echo 동작 | wall-clock 측정, 1회 dogfood |

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] FR-01 ~ FR-10 (P0/P1) 모두 완료
- [ ] `dotnet build FastPortCharp.sln -c Release` 통과 (0 warning, 0 error)
- [ ] `dotnet test FastPortCharp.sln -c Release --no-build` 전 항목 pass
- [ ] `FastPortGameServerTemplate` 단독 실행으로 echo 송수신 동작 (수동 1회)
- [ ] `s5-random-10k` 벤치 RTT P95 ≤ baseline + 5%
- [ ] HANDOFF.md / README.md 갱신
- [ ] GitHub Template Repository 경로 결정 문서화 (FR-09)
- [ ] PR/commit set 깨끗하게 정리

### 4.2 Quality Criteria

- [ ] `LibNetworks` source에 `Protocols.` 의존 0건
- [ ] `LibNetworks` / `LibCommons`가 `LibTestTelemetry` 의존 0건
- [ ] 신규 프로젝트가 `FastPortServer` 코드를 그대로 복사하지 않음 (필요 시 reference만)
- [ ] 신규 프로젝트의 `appsettings.json`에 비밀/환경별 값 포함하지 않음
- [ ] 영문 README + 한국어 QUICKSTART 1개

---

## 5. Risks and Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| **R-A**: `<RootNamespace>` 변경으로 컴파일 깨짐 광범위 발생 | High | Medium | 1) `<PackageId>`만 바꾸고 `<RootNamespace>`는 기존 namespace alias 유지하는 옵션 우선. 2) namespace 변경이 필요하면 `global using`/per-file refactor를 한 PR 안에서 일괄 처리. 3) Design 단계에서 alias-only vs full-rename 두 옵션 비교. |
| **R-B**: 템플릿이 `FastPortServer` 코드와 중복 → drift 발생 | Medium | Medium | 템플릿은 `LibCommons` + `LibNetworks`만 ProjectReference로 의존. `FastPortServer`는 reference 안 함. README/HANDOFF에 "두 산출물의 역할 차이" 1문단 명시. |
| **R-C**: 10K 벤치 회귀 (csproj 메타데이터 변경/Configuration 변동) | High | Low | 본 cycle 변경은 메타데이터/신규 프로젝트 위주 → 엔진 코드 변경 없음 보장. analyze 단계에서 baseline 비교 1회 실행. |
| **R-D**: Protobuf MSBuild gen target가 IDE/CI에서 동작 차이 | Medium | Medium | Design에서 gen 도구 선택 (Google.Protobuf.Tools / protoc-gen-csharp / Grpc.Tools 중) + Linux/macOS/Windows 동작 매트릭스 명시. 본 cycle에서는 1개 OS(macOS) + GitHub Actions 1개 검증. |
| **R-E**: GitHub Template Repo 노출 경로 결정 보류 → cycle 미완성 | Medium | Low | 결정만 내리고 실제 split은 차기 cycle로 분리해도 OK. FR-09는 "결정 + 1차 문서"까지만. |
| **R-F**: dogfooding 사용자가 본인 1명뿐 → 검증 한계 | Medium | High | 본 cycle은 Phase 1로 한정, 외부 검증은 차기 cycle (Phase 2/3) 게이트로 미룸. PRD R-1과 정합. |

---

## 6. Impact Analysis

> **Purpose**: csproj 메타데이터 / namespace / 신규 프로젝트 추가가 기존 consumer를 깨지 않는지 사전 점검.

### 6.1 Changed Resources

| Resource | Type | Change Description |
|----------|------|--------------------|
| `LibCommons/LibCommons.csproj` | csproj | `<PackageId>=FastPort.Common`, `<Description>`, `<Authors>` 등 NuGet 메타데이터 추가 |
| `LibNetworks/LibNetworks.csproj` | csproj | `<PackageId>=FastPort.Networks`, NuGet 메타데이터 추가 |
| `LibCommons/**.cs` (선택적) | namespace | full rename 채택 시 namespace 변경 — Design에서 alias vs rename 결정 |
| `LibNetworks/**.cs` (선택적) | namespace | full rename 채택 시 namespace 변경 |
| `FastPortGameServerTemplate/**` | new project | Generic Host + sample game session/handler + Protocols + appsettings + Serilog |
| `FastPortCharp.sln` | sln | 신규 프로젝트 등록 |
| `HANDOFF.md` | docs | "Important Architecture Decisions" + 템플릿 위치 추가 |
| `README.md` | docs | 템플릿 사용법 1문단, GitHub Template Repo 경로 |

### 6.2 Current Consumers

| Resource | Operation | Code Path | Impact |
|----------|-----------|-----------|--------|
| `LibCommons` | reference | `LibNetworks/LibNetworks.csproj` ProjectReference | None (csproj 메타데이터 변경만이면 영향 없음) |
| `LibCommons` | reference | `FastPortServer/FastPortServer.csproj` | None / Needs verification (namespace 변경 시) |
| `LibCommons` | reference | `FastPortClient/FastPortClient.csproj` | Same |
| `LibCommons` | reference | `FastPortTestSmokeServer/FastPortTestSmokeServer.csproj` | Same |
| `LibCommons` | reference | `FastPortTestLoadRunner/*.csproj` | Same |
| `LibCommons` | reference | `FastPortTestLoadValidation/*.csproj` | Same |
| `LibCommons` | reference | `FastPortTests/FastPortTests.csproj` | Same |
| `LibCommons` | reference | `LibTestTelemetry/LibTestTelemetry.csproj` | Same |
| `LibNetworks` | reference | 위 동일한 다수 소비자 | namespace 변경 시 `using` 일괄 갱신 |
| `Protocols/` | reference | `FastPortServer`, `FastPortClient`, `FastPortTests` | 본 cycle에서는 손대지 않음 |
| `FastPortServer` | reference | 없음 (템플릿이 `FastPortServer`를 reference하지 않음) | 무관 |

### 6.3 Verification

- [ ] 모든 ProjectReference 소비자가 메타데이터/네임스페이스 변경 후에도 빌드 통과
- [ ] `LibTestTelemetry`가 `LibCommons`/`LibNetworks` 변경에 영향 받지 않음 (telemetry separation 유지)
- [ ] `FastPortTestSmokeServer` / `FastPortTestLoadRunner` 의 telemetry export 동작 회귀 없음
- [ ] 벤치 baseline 동등성

---

## 7. Architecture Considerations

### 7.1 Project Level Selection

| Level | Characteristics | Recommended For | Selected |
|-------|-----------------|-----------------|:--------:|
| **Starter** | 단일 프로젝트, 단순 구조 | Static sites, CLI utilities | ☐ |
| **Dynamic** | Multi-project .NET solution, 재사용 패키지 경계 | C# multi-project 라이브러리 + 템플릿 | ☑ |
| **Enterprise** | 엄격한 layer separation, microservices | High-traffic distributed systems | ☐ |

**Rationale**: FastPortSharp는 이미 multi-project solution이고, 본 cycle은 패키지 경계 정리 + 신규 프로젝트 추가 수준. Enterprise 레벨의 layered architecture 도입은 불필요.

### 7.2 Key Architectural Decisions

| Decision | Options | Selected | Rationale |
|----------|---------|----------|-----------|
| Engine package split | (a) 단일 `FastPort.Networks` (b) `FastPort.Common` + `FastPort.Networks` | **(b) 분리** | LibCommons가 buffers/IDs 등 범용 유틸이라 단독 가치 가능. 사용자 확정. |
| Template 위치 | (a) FastPortServer 개조 (b) 신규 `FastPortGameServerTemplate` (c) `samples/` 폴더 | **(b) 신규 프로젝트** | HANDOFF의 `FastPortServer = engine host` 결정과 충돌 회피. 사용자 확정. |
| v1 범위 | Phase 1 / 1+2 / 1+2+stable | **Phase 1 only** | 작고 빠른 이터레이션, NuGet publish 부담 회피, dogfooding 우선. 사용자 확정. |
| Protocols 처리 | (a) Protocols/ 유지, 템플릿 자체 .proto (b) 별도 NuGet 분리 (c) 손대지 않음 | **(a)** | Protocols/는 engine-internal sample 유지, 템플릿은 자체 `.proto` 폴더로 독립성 확보. 사용자 확정. |
| Distribution form (Phase 1) | GitHub Template Repo 단독 / 별도 repo split / monorepo subtree | **TBD (Design 단계 결정)** | FR-09. 본 cycle 안에서는 결정 + 1차 문서까지만. |
| Namespace 처리 | (a) `<PackageId>`만 변경, namespace 유지 (b) namespace까지 `FastPort.*`로 rename | **TBD (Design 단계 결정, R-A 회피 고려)** | alias-only가 위험 적음. Design에서 trade-off 비교. |
| Protobuf gen 도구 | Google.Protobuf.Tools / Grpc.Tools / protoc 직접 | **TBD (Design)** | OS/CI 매트릭스 + 기존 `Protocols/` 가 쓰는 도구 일치 여부 확인 후 Design에서 결정. |
| Serilog 구성 | code-only / appsettings 기반 / 둘 다 | **appsettings 기반** | 사용자 친화 + 템플릿 답게 직관적. |
| Telemetry 추상화 | LibTestTelemetry 직접 의존 / 인터페이스만 노출 | **인터페이스만 노출 (concrete는 후속)** | LibCommons/LibNetworks가 LibTestTelemetry를 참조하지 않는 boundary 유지. |

### 7.3 Clean Architecture Approach

```
Selected Level: Dynamic (multi-project .NET solution)

Folder Structure (변경 후):
FastPortCharp.sln
├─ LibCommons/                      ← PackageId=FastPort.Common (publish는 차기)
├─ LibNetworks/                     ← PackageId=FastPort.Networks (publish는 차기)
├─ LibTestTelemetry/                ← unchanged
├─ Protocols/                       ← engine-internal sample, unchanged
├─ FastPortServer/                  ← engine host/sample, unchanged
├─ FastPortClient/                  ← unchanged
├─ FastPortTestSmokeServer/         ← unchanged
├─ FastPortTestLoadRunner/          ← unchanged
├─ FastPortTestLoadValidation/      ← unchanged
├─ FastPortTests/                   ← unchanged
└─ FastPortGameServerTemplate/      ← NEW
   ├─ FastPortGameServerTemplate.csproj
   ├─ Program.cs                     (Generic Host)
   ├─ GameServer.cs / Sessions/
   ├─ Protocols/Sample.proto         (자체 .proto)
   ├─ appsettings.json
   ├─ Serilog 구성
   └─ README.md / QUICKSTART.md
```

---

## 8. Convention Prerequisites

### 8.1 Existing Project Conventions

- [x] `AGENTS.md` 존재 (프로젝트 협업 규칙)
- [x] `HANDOFF.md` 존재 (architecture decisions, baseline 수치)
- [x] `README.md` / `README.ko.md` 존재
- [ ] `CONVENTIONS.md` 별도 — N/A (HANDOFF/AGENTS로 충당)
- [x] `.gitignore`
- [ ] `.editorconfig` — 본 cycle에서 추가 검토 (필수 아님)
- [x] `FastPortCharp.sln` Release/Debug Configuration

### 8.2 Conventions to Define/Verify

| Category | Current State | To Define | Priority |
|----------|---------------|-----------|:--------:|
| **csproj NuGet 메타데이터** | missing for LibCommons/LibNetworks | `<PackageId>`, `<Description>`, `<Authors>`, `<RepositoryUrl>`, `<PackageLicenseExpression>=MIT`, `<PackageReadmeFile>` | High |
| **namespace 정책** | 현재 `LibCommons`/`LibNetworks` 그대로 | alias-only vs `FastPort.*` rename — Design에서 결정 | High |
| **신규 프로젝트 명명** | template 미존재 | `FastPortGameServerTemplate` (csproj name + folder) | High |
| **Protobuf gen 도구 컨벤션** | `Protocols/` 의 현 도구 그대로 | 템플릿도 동일 도구로 통일 | Medium |
| **README 다국어** | 영문 + 한국어 main README 있음 | 템플릿도 동일 정책 | Medium |
| **HANDOFF 갱신 정책** | 새 architecture decision은 HANDOFF에 기재 (기존 관행) | 본 cycle 결정도 동일 | High |

### 8.3 Environment Variables Needed

본 cycle은 빌드/메타데이터 + 신규 프로젝트 추가 위주이며 신규 환경변수 도입 없음. 템플릿 결과물은 `appsettings.json`을 통해 listen port / log level만 노출.

| Variable | Purpose | Scope | To Be Created |
|----------|---------|-------|:-------------:|
| (없음) | — | — | ☐ |

### 8.4 Pipeline Integration

본 프로젝트는 .NET 멀티-프로젝트이며 9-phase web pipeline은 적용하지 않음. PDCA cycle만 적용.

---

## 9. Next Steps

1. [ ] Design 단계: `/pdca design game-server-template-from-network-engine`
   - Architecture Option A/B/C 비교 (특히 namespace 처리, Protobuf gen 도구, GitHub Template 노출 경로)
   - Module Map + Session Guide 생성
2. [ ] Do 단계: scope를 `csproj-meta` / `template-skeleton` / `template-protobuf` / `docs-handoff` 등으로 분할 가능
3. [ ] Check 단계: 빌드/테스트/벤치 baseline + `rg`로 boundary 검증
4. [ ] Report 단계: 본 cycle 결정 사항을 HANDOFF + 차기 cycle (NuGet publish) 의 입력으로 정리

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-09 | Initial draft (PRD Phase 1 scope, user-confirmed decisions reflected) | boinred |
