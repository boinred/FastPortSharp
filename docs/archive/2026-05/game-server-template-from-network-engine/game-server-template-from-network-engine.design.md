# game-server-template-from-network-engine Design Document

> **Summary**: Pragmatic Balance — `LibCommons` / `LibNetworks` csproj NuGet 메타데이터를 정렬(`PackageId`/`RootNamespace`만)하고, `FastPortGameServerTemplate` 신규 프로젝트(Generic Host + Serilog + DI + Sessions + Handlers + 자체 `.proto` Grpc.Tools)를 추가한다. 코드 namespace는 alias로 유지, GitHub Template Repo는 FastPortSharp 자체로 표기.
>
> **Project**: FastPortSharp
> **Version**: (.NET 10 / FastPortCharp.sln)
> **Author**: boinred
> **Date**: 2026-05-09
> **Status**: Draft
> **Planning Doc**: [game-server-template-from-network-engine.plan.md](../../01-plan/features/game-server-template-from-network-engine.plan.md)
> **PRD**: [game-server-template-from-network-engine.prd.md](../../00-pm/game-server-template-from-network-engine.prd.md)

### Pipeline References

| Phase | Document | Status |
|-------|----------|--------|
| Phase 1-9 | (Web pipeline N/A — .NET multi-project) | N/A |

---

## Context Anchor

> Copied from Plan document. Ensures strategic context survives Design→Do handoff.

| Key | Value |
|-----|-------|
| **WHY** | C# 게임 서버 0→1 비용을 줄이고, 검증된 TCP 엔진의 재사용 경계와 게임 서버 부트스트랩 템플릿을 monorepo 안에서 dogfooding 가능한 형태로 마련한다. |
| **WHO** | Primary: 본인/내부 팀 (Solo Soyoung). Secondary(차기 cycle): C# 인디 게임 개발자 (Indie Ian). |
| **RISK** | (R-A) namespace 광범위 변경으로 컴파일 깨짐, (R-D) Protobuf gen 도구 OS/CI 매트릭스 차이, (R-F) dogfooder 1명 한정. |
| **SUCCESS** | (1) 신규 `FastPortGameServerTemplate` 빌드 + echo 5–10분 동작. (2) `FastPort.Common`/`FastPort.Networks` `PackageId` + 메타데이터. (3) GitHub Template Repo 노출 경로 결정/문서화. (4) 10K 벤치 baseline 동등성. |
| **SCOPE** | Phase 1 only: monorepo 내부 템플릿 + 엔진 csproj 메타데이터. NuGet publish/`dotnet new`/room/match/auth/heartbeat/UDP는 차기. |

---

## 1. Overview

### 1.1 Design Goals

1. **엔진 패키지 경계의 1차 형식화**: `LibCommons`/`LibNetworks` csproj에 NuGet publish-ready 메타데이터를 추가하되, 실제 publish는 차기 cycle.
2. **게임 서버 부트스트랩 코드의 살아 있는 dogfood**: `FastPortGameServerTemplate`이 `FastPortServer`와 별도로 존재하며, `LibCommons` + `LibNetworks`만 의존하여 "게임 서버 사용자 시점"을 monorepo 안에서 검증.
3. **변경 폭 최소화 + 차기 cycle 진입 비용 최소화**: 코드 namespace를 즉시 rename하지 않음 (R-A 회피), 다만 차기 cycle에서 NuGet publish + namespace 정렬을 한 번에 처리할 수 있도록 csproj/문서를 미리 정렬.
4. **Protocols/ 와 도구 통일**: 템플릿의 Protobuf gen은 기존 `Protocols/Protocols.csproj`와 동일한 `Grpc.Tools` + `Google.Protobuf.Tools`를 사용 (`GrpcServices="None"` — gRPC 아님, 단순 message gen).
5. **HANDOFF 일관성 유지**: `LibNetworks` protocol-neutral, `FastPortServer` = engine sample/host, smoke 코드는 `FastPortTestSmokeServer`에 머무름 — 본 cycle에서 흔들지 않음.

### 1.2 Design Principles

- **Pragmatic over Pure**: namespace alias 유지 (`<RootNamespace>` 정렬만), full rename은 차기 cycle.
- **Boundary by csproj, not by code rename**: 사용자 시점에서의 패키지 경계는 csproj 메타데이터로 충분히 표현.
- **Template = Consumer**: `FastPortGameServerTemplate`은 엔진의 internal 코드를 직접 참조하지 않고, 외부 사용자 관점의 API만 사용 (HANDOFF 보호).
- **Reversible decisions**: 본 cycle 결정은 후속 cycle에서 쉽게 변경 가능해야 함 (namespace, GitHub Template repo split, NuGet publish).

---

## 2. Architecture Options (Selected)

### 2.0 Architecture Comparison

| Criteria | Option A: Minimal | Option B: Clean | **Option C: Pragmatic** |
|----------|:-:|:-:|:-:|
| **Approach** | csproj 메타데이터만 | namespace 전체 rename + layered template | csproj 메타데이터 + alias-only namespace + Generic Host template |
| **New Files** | ~6 | ~18-22 | ~10-12 |
| **Modified Files** | 2 | 30+ | 4-5 |
| **Complexity** | Low | High | Medium |
| **Maintainability** | Medium | High | High |
| **Effort** | Low | High | Medium |
| **Risk** | Low | Medium (rename 누락) | Low |
| **Recommendation** | hotfix 수준 | 외부 OSS 본격화 시점에 | **Default — Phase 1 cycle 적합** |

**Selected**: **Option C — Pragmatic Balance**

**Rationale**:
- Plan §7.2 Architecture Decisions와 정합 (Phase 1 only, namespace는 alias-only로 R-A 회피).
- `Grpc.Tools` 도구 통일로 OS/CI 매트릭스 위험(R-D) 감소.
- 신규 파일 ~10-12 / 수정 파일 4-5 → 단일 PR 단위로 관리 가능.
- 차기 cycle (NuGet publish + namespace rename + `dotnet new` 등록)에서 본 cycle 산출물을 그대로 진화.

### 2.1 Component Diagram

```
                    ┌────────────────────────────────────────┐
                    │  FastPortCharp.sln (monorepo)         │
                    └────────────────────────────────────────┘
                                       │
   ┌──────────────────────┬────────────┴───────────────┬──────────────────────┐
   ▼                      ▼                            ▼                      ▼
┌──────────┐      ┌─────────────────┐         ┌───────────────────┐    ┌─────────────────────┐
│LibCommons│ ◀──  │ LibNetworks     │  ◀──    │ FastPortServer    │    │ FastPortGameServer  │
│(meta:    │      │ (meta:          │ project │ (engine host/     │    │  Template (NEW)      │
│ FastPort.│      │  FastPort.      │  ref    │  sample, unchanged)│    │ ── ProjectReference  │
│ Common)  │      │  Networks)      │         │                   │    │   to LibCommons +    │
└──────────┘      └─────────────────┘         └───────────────────┘    │   LibNetworks only   │
                          ▲                                            └─────────────────────┘
                          │ project ref
                          │
                  ┌─────────────────────┐
                  │ Protocols/          │  ← engine-internal sample, unchanged
                  │ (Grpc.Tools .proto) │
                  └─────────────────────┘

  ┌─────────────────────┐    ┌─────────────────────────┐    ┌───────────────────────────────┐
  │ FastPortClient      │    │ FastPortTestSmokeServer │    │ FastPortTestLoadRunner /      │
  │ (unchanged)         │    │ (unchanged)             │    │  Validation / FastPortTests   │
  └─────────────────────┘    └─────────────────────────┘    │  (unchanged)                  │
                                                            └───────────────────────────────┘

  ┌────────────────────┐
  │ LibTestTelemetry   │  ← test telemetry contracts, unchanged
  └────────────────────┘
```

핵심 포인트:
- `FastPortGameServerTemplate`은 `LibCommons` + `LibNetworks` **만** ProjectReference. `FastPortServer` 코드 직접 참조 X.
- `Protocols/` 는 손대지 않음. 템플릿은 자체 `Protocols/Sample.proto` 폴더를 가짐.
- 다른 모든 프로젝트(`FastPortServer`, `FastPortClient`, smoke/load/test)는 본 cycle에서 변경 없음 (csproj 메타데이터 변경에 따른 영향만 검증).

### 2.2 Data Flow (Template runtime)

```
appsettings.json
   │
   ▼
Program.cs (Generic Host)
   │  ── DI: SerilogLogger, IOptions<GameServerOptions>, IGameServerTelemetry (Null), GameSessionFactory
   ▼
GameServer (Hosted Service)
   │  ── new BaseListener (LibNetworks) bound to configured endpoint
   ▼
GameSession : BaseSession (LibNetworks)
   │  ── OnPacketReceived → IPacketDispatcher → handlers/EchoHandler.cs
   ▼
Sample.proto-generated message classes (Grpc.Tools)
```

### 2.3 Dependencies

| Component | Depends On | Purpose |
|-----------|-----------|---------|
| `LibCommons` (csproj meta only) | `Microsoft.Extensions.Logging.Abstractions` (existing) | unchanged runtime deps |
| `LibNetworks` (csproj meta only) | `LibCommons`, `Google.Protobuf` (existing) | unchanged runtime deps |
| `FastPortGameServerTemplate` (NEW) | `LibCommons`, `LibNetworks`, `Microsoft.Extensions.Hosting`, `Serilog.AspNetCore` 또는 `Serilog.Extensions.Hosting`, `Google.Protobuf`, `Grpc.Tools` | Generic Host + Serilog + Protobuf gen |

> 유의: `FastPortGameServerTemplate`은 `FastPortServer`/`FastPortClient`/`Protocols`/`LibTestTelemetry`/test 프로젝트들을 **참조하지 않는다**. (사용자 관점 dogfood 보장)

---

## 3. Data Model

본 cycle은 데이터 모델 자체보다 **csproj 메타데이터 스키마** 와 **템플릿 구성 파일 스키마** 가 핵심.

### 3.1 csproj NuGet Metadata Schema

```xml
<!-- LibCommons/LibCommons.csproj 변경 부분만 -->
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>

  <!-- NEW: NuGet metadata (publish는 차기 cycle) -->
  <PackageId>FastPort.Common</PackageId>
  <Version>0.1.0-preview</Version>
  <Authors>boinred</Authors>
  <Description>Common buffers, packet primitives, and ID utilities used by FastPort game server engine.</Description>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <PackageProjectUrl>https://github.com/boinred/FastPortSharp</PackageProjectUrl>
  <RepositoryUrl>https://github.com/boinred/FastPortSharp</RepositoryUrl>
  <RepositoryType>git</RepositoryType>
  <PackageTags>tcp;game-server;networking;dotnet10</PackageTags>
  <PackageReadmeFile>README.md</PackageReadmeFile>

  <!-- NEW: GeneratePackageOnBuild=false (publish는 차기 cycle에서 명시적으로) -->
  <GeneratePackageOnBuild>false</GeneratePackageOnBuild>

  <!-- 옵션: RootNamespace 정렬 (코드 namespace는 그대로, 단지 default 변경) -->
  <!-- 본 cycle: RootNamespace 변경하지 않음 (R-A 회피). 모든 .cs는 명시적 namespace 헤더 사용 가정 -->
</PropertyGroup>

<ItemGroup>
  <None Include="..\README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

`LibNetworks/LibNetworks.csproj` 도 동일 패턴으로 `PackageId=FastPort.Networks` + Description 차이.

> **결정**: 본 cycle에서 `<RootNamespace>` 는 변경하지 않는다. 이유: 코드의 namespace 헤더는 모두 `namespace LibCommons.X` / `namespace LibNetworks.X` 형식이며, `<RootNamespace>` 변경은 *새로 추가되는 파일의 default namespace*에만 영향. 일관성 깨짐을 방지하기 위해 코드 rename cycle에서 묶어서 처리.

### 3.2 Template appsettings Schema

```json
{
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      { "Name": "Console" }
    ]
  },
  "GameServer": {
    "ListenAddress": "0.0.0.0",
    "ListenPort": 7777,
    "MaxSessions": 1024
  }
}
```

C# 측 옵션 타입:

```csharp
public sealed class GameServerOptions
{
    public string ListenAddress { get; init; } = "0.0.0.0";
    public int ListenPort { get; init; } = 7777;
    public int MaxSessions { get; init; } = 1024;
}
```

### 3.3 Sample.proto

```proto
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

`Grpc.Tools`의 `<Protobuf>` MSBuild item으로 `dotnet build` 시 자동 생성, `GrpcServices="None"`.

---

## 4. API Specification

본 feature는 게임 서버 자체 API가 아닌 **템플릿이 노출하는 게임 서버 측 contract** 가 대상.

### 4.1 Public Contract (게임 서버 사용자 관점)

| 영역 | 형태 | 위치 | 설명 |
|------|------|------|------|
| Hosted entrypoint | `IHostedService` | `FastPortGameServerTemplate.GameServer` | Generic Host에 등록 |
| Session 확장점 | `class : BaseSession` | `FastPortGameServerTemplate.Sessions.GameSession` | `LibNetworks.BaseSession` 상속 sample |
| Packet handler 확장점 | `IPacketHandler` (template-local interface) | `FastPortGameServerTemplate.Handlers.IPacketHandler` | 사용자가 추가하는 핸들러 contract |
| Sample handler | `EchoHandler : IPacketHandler` | `FastPortGameServerTemplate.Handlers.EchoHandler` | echo 동작 1개 |
| Telemetry hook | `IGameServerTelemetry` (template-local interface) | `FastPortGameServerTemplate.Telemetry.IGameServerTelemetry` | concrete impl은 후속 cycle, 본 cycle은 `NullGameServerTelemetry` |
| Configuration | `GameServerOptions` | `FastPortGameServerTemplate.Configuration.GameServerOptions` | `appsettings.json` 바인딩 |

> **중요**: 본 cycle에서 `LibCommons`/`LibNetworks`의 *공개 API 형태는 바꾸지 않는다*. 메타데이터만.

### 4.2 Wire Protocol

- 기존 `LibNetworks.BaseSession`/`BaseListener`의 packet 송수신 메커니즘 그대로 사용.
- payload는 `EchoRequest`/`EchoResponse` Protobuf message.
- header/length-prefix는 `LibNetworks`가 제공하는 기존 packet framing을 그대로 사용 (변경 없음).

### 4.3 Error Responses

본 feature는 HTTP/REST 아님. Error는 (a) listen bind 실패 (시작 시 fast-fail), (b) packet parse 실패 (session 단위 disconnect + Serilog warning), (c) handler 예외 (catch + Serilog error + session 유지 또는 정책에 따라 disconnect) 형태.

---

## 5. UI/UX Design

본 feature는 UI 없음 (server-side template).

대신 **사용자 onboarding 흐름** 을 명시:

### 5.1 Onboarding Flow (Persona = Solo Soyoung, 본인)

```
1. (이미 repo clone 상태 가정) → cd FastPortGameServerTemplate
2. dotnet build  (root에서 dotnet build FastPortCharp.sln 도 OK)
3. dotnet run --project FastPortGameServerTemplate  → "Listening on 0.0.0.0:7777" 콘솔 로그
4. 별도 터미널: cd FastPortClient → dotnet run -- --host 127.0.0.1 --port 7777
5. EchoRequest 송신 → EchoResponse 수신 (RTT 콘솔 로그)
```

목표: 위 5 step이 5–10분 안에 완료됨.

### 5.2 Documentation Touchpoints

| 문서 | 역할 |
|------|------|
| `FastPortGameServerTemplate/README.md` | 영문, 5분 echo onboarding |
| `FastPortGameServerTemplate/QUICKSTART.ko.md` | 한국어, 5분 echo onboarding |
| `README.md` (repo root) | "Game Server Template" 섹션 1문단 추가 + 링크 |
| `HANDOFF.md` | "Important Architecture Decisions"에 본 cycle 결정 1개 항목 추가 |
| `docs/ENGINE_PACKAGES.md` (NEW, optional) | `FastPort.Common` / `FastPort.Networks` 의 향후 NuGet publish 정책 1페이지 |

---

## 6. Error Handling

| 상황 | 동작 | Logging |
|------|------|---------|
| `appsettings.json` 누락 | Generic Host default fallback (코드 default 적용) | Warning |
| `ListenPort` 바인딩 실패 | 즉시 종료 (process exit code != 0) | Error |
| Packet parse 실패 | 해당 session disconnect | Warning per session |
| Handler 예외 | session 유지 + 예외 로깅 (정책: 본 cycle은 유지, 후속 cycle에서 정책 옵션) | Error with stack trace |
| `Sample.proto` 컴파일 실패 | `dotnet build` 실패 | MSBuild error |

---

## 7. Security Considerations

본 cycle 범위에서:
- [ ] `appsettings.json`은 secret 포함 금지 (port/log level만)
- [ ] `0.0.0.0` 바인딩 default는 dogfood 편의용 — README에 "프로덕션은 reverse proxy/firewall 권장" 1문장
- [ ] `EchoRequest.message` 길이 제한은 `LibNetworks` 기존 packet size 제한에 위임 (별도 검증 X)
- [ ] auth/encryption 없음 (v1 비범위, PRD §5.2 Non-Goals)
- [ ] 외부 네트워크에 publish되는 NuGet 메타데이터 (`Authors`, `RepositoryUrl`)는 publish 차기 cycle에서 최종 검토

---

## 8. Test Plan

> 본 feature는 .NET 솔루션 — Playwright 적용 X. L1/L2/L3 매핑은 .NET 적합 형태로 변환.

### 8.1 Test Scope

| Type | 적용 형태 | Tool | Phase |
|------|-----------|------|-------|
| L1 (정적 boundary) | csproj 메타데이터 + boundary `rg` 검증 | bash + ripgrep | Check |
| L2 (단위/통합 빌드) | `dotnet build` Release / `dotnet test` 전체 | dotnet CLI | Check |
| L3 (시나리오) | 템플릿 실행 + `FastPortClient` echo + 10K 벤치 baseline | dotnet run + s5-random-10k | Check |

### 8.2 L1: Static Boundary Tests

| # | 검증 | 명령 | 기대 |
|---|------|------|------|
| 1 | `LibCommons.csproj` 의 `<PackageId>=FastPort.Common` | `grep -E 'PackageId>FastPort\.Common' LibCommons/LibCommons.csproj` | match 1+ |
| 2 | `LibNetworks.csproj` 의 `<PackageId>=FastPort.Networks` | 동일 | match 1+ |
| 3 | 두 csproj 모두 `<PackageLicenseExpression>MIT</PackageLicenseExpression>` 보유 | grep | 2 match |
| 4 | `LibNetworks` 가 `Protocols/` 의존 없음 | `rg "Protocols\." LibNetworks/` | 0 match |
| 5 | `LibCommons`/`LibNetworks` 가 `LibTestTelemetry` 의존 없음 | `rg "LibTestTelemetry" LibCommons/ LibNetworks/` | 0 match |
| 6 | `FastPortGameServerTemplate.csproj` 가 `FastPortServer`/`FastPortClient`/`Protocols`/`LibTestTelemetry` ProjectReference 없음 | grep `<ProjectReference` | LibCommons + LibNetworks만 |
| 7 | `FastPortCharp.sln` 에 `FastPortGameServerTemplate` 등록 | `grep FastPortGameServerTemplate FastPortCharp.sln` | match 1+ |
| 8 | 템플릿 자체 `.proto` 파일 존재 | `ls FastPortGameServerTemplate/Protocols/Sample.proto` | exists |

### 8.3 L2: Build & Test Scenarios

| # | 명령 | 기대 |
|---|------|------|
| 1 | `dotnet build FastPortCharp.sln -c Release` | 0 error, 0 warning |
| 2 | `dotnet test FastPortCharp.sln -c Release --no-build` | 모든 test pass (기존 test set 회귀 0) |
| 3 | `dotnet build FastPortGameServerTemplate -c Release` (개별) | `obj/Release/net10.0/Sample.cs` 등 protobuf 산출물 생성 |

### 8.4 L3: Runtime Scenario Tests

| # | 시나리오 | 단계 | 기대 |
|---|----------|------|------|
| 1 | 템플릿 실행 | `dotnet run --project FastPortGameServerTemplate` | 콘솔에 "Listening on 0.0.0.0:7777" 로그 |
| 2 | echo 송수신 | `FastPortClient`로 `EchoRequest("hello")` 송신 | `EchoResponse` 수신, RTT 정상 |
| 3 | 5분 dogfood | wall-clock 측정 (clone → 빌드 → echo) | ≤ 10분 |
| 4 | 10K 벤치 baseline | `s5-random-10k` 실행 (기존 `FastPortServer` 또는 동일 환경) | RTT P95 ≤ baseline + 5% (HANDOFF L70-90 기준) |
| 5 | telemetry hook null impl 동작 | 기본 실행 시 `NullGameServerTelemetry` 호출 — 예외 없이 무동작 | exception 0 |

### 8.5 Seed Data Requirements

해당 없음 (DB 미사용).

---

## 9. Clean Architecture (.NET 적용)

### 9.1 Layer Structure (FastPortGameServerTemplate 내부)

| Layer | Responsibility | Folder |
|-------|---------------|--------|
| **Hosting** | Program.cs, Generic Host wiring, DI registration | `/` (project root) |
| **Application** | `GameServer` (HostedService), packet dispatcher | `Application/` |
| **Domain** | `IPacketHandler`, `EchoHandler`, packet routing primitives | `Handlers/` |
| **Infrastructure** | `GameSession : BaseSession`, listener wiring, telemetry adapter | `Sessions/`, `Telemetry/` |
| **Configuration** | `GameServerOptions`, `appsettings.json` | `Configuration/`, root |
| **Protocols** | `Sample.proto` + Grpc.Tools 생성물 | `Protocols/` |

### 9.2 Dependency Rules

```
Hosting (Program.cs)
   │
   ▼
Application (GameServer/Dispatcher) ──→ Domain (IPacketHandler)
   │                                          ▲
   ▼                                          │
Infrastructure (GameSession, Telemetry) ──────┘
   │
   ▼
External: LibNetworks, LibCommons, Generated Protobuf
```

규칙:
- Domain (`IPacketHandler`, `EchoHandler`)는 `Microsoft.Extensions.*` 의존 X (순수 C#).
- Infrastructure는 `LibNetworks` 직접 의존, Application은 Infrastructure를 추상화 통해 사용.
- 템플릿이 `LibTestTelemetry` 참조 안 함 (engine boundary 보호).

### 9.3 File Import Rules

| From | Can Import | Cannot Import |
|------|-----------|---------------|
| Hosting | Application, Configuration, Infrastructure (DI 등록 목적) | Domain handlers 직접 |
| Application | Domain, Infrastructure abstractions | Hosting |
| Domain | nothing external (POCO + interfaces only) | Microsoft.Extensions.*, LibNetworks |
| Infrastructure | LibNetworks, LibCommons, Domain, Generated Protobuf | Application, Hosting |

### 9.4 Layer Assignment (구체)

| 파일 | Layer | 위치 |
|------|-------|------|
| `Program.cs` | Hosting | `FastPortGameServerTemplate/Program.cs` |
| `GameServer.cs` | Application | `FastPortGameServerTemplate/Application/GameServer.cs` |
| `PacketDispatcher.cs` | Application | `FastPortGameServerTemplate/Application/PacketDispatcher.cs` |
| `IPacketHandler.cs` | Domain | `FastPortGameServerTemplate/Handlers/IPacketHandler.cs` |
| `EchoHandler.cs` | Domain | `FastPortGameServerTemplate/Handlers/EchoHandler.cs` |
| `GameSession.cs` | Infrastructure | `FastPortGameServerTemplate/Sessions/GameSession.cs` |
| `GameSessionFactory.cs` | Infrastructure | `FastPortGameServerTemplate/Sessions/GameSessionFactory.cs` |
| `IGameServerTelemetry.cs` | Domain (interface) + Infrastructure (impl) | `FastPortGameServerTemplate/Telemetry/` |
| `NullGameServerTelemetry.cs` | Infrastructure | 동일 |
| `GameServerOptions.cs` | Configuration | `FastPortGameServerTemplate/Configuration/` |
| `Sample.proto` | Protocols | `FastPortGameServerTemplate/Protocols/Sample.proto` |
| `appsettings.json` | Configuration | `FastPortGameServerTemplate/appsettings.json` |
| `README.md` / `QUICKSTART.ko.md` | Docs | `FastPortGameServerTemplate/` |

---

## 10. Coding Convention Reference

### 10.1 .NET 측 Naming

| Target | Rule | Example |
|--------|------|---------|
| Class / Interface | PascalCase, Interface 는 `I` prefix | `GameServer`, `IPacketHandler` |
| Method | PascalCase | `OnPacketReceivedAsync` |
| Field (private) | `_camelCase` | `_dispatcher` |
| Property | PascalCase | `ListenPort` |
| Constants | PascalCase | `DefaultMaxSessions` |
| Filename | 1 클래스 = 1 파일, 클래스명과 일치 | `GameServer.cs` |
| Folder | PascalCase | `Application/`, `Sessions/` |
| Namespace | `FastPortGameServerTemplate.<Folder>` | `FastPortGameServerTemplate.Sessions` |

### 10.2 Async/Await

- I/O 메서드는 `Async` suffix.
- `ConfigureAwait(false)` 는 라이브러리(LibCommons/LibNetworks) 관행 따름 — 템플릿 application 코드는 디폴트.
- `CancellationToken` 은 `IHostedService.StartAsync` / `StopAsync` 시그니처 준수.

### 10.3 Logging

- 모든 로깅은 `Microsoft.Extensions.Logging.ILogger<T>` 통해. Serilog는 sink로만 등록.
- 로그 메시지 템플릿은 영문, 구조화 로깅 활용 (`logger.LogInformation("Listening on {Address}:{Port}", addr, port)`).

### 10.4 .csproj 메타데이터 컨벤션 (LibCommons/LibNetworks)

| 키 | 값 |
|---|---|
| `PackageId` | `FastPort.Common` / `FastPort.Networks` |
| `Version` | `0.1.0-preview` (publish 전까지 preview) |
| `PackageLicenseExpression` | `MIT` |
| `PackageProjectUrl` | `https://github.com/boinred/FastPortSharp` |
| `RepositoryUrl` | 동일 |
| `Authors` | `boinred` |
| `PackageTags` | `tcp;game-server;networking;dotnet10` |
| `GeneratePackageOnBuild` | `false` (publish는 차기 cycle에서 명시적) |

---

## 11. Implementation Guide

### 11.1 File Structure (구체)

```
FastPortSharp/                          (repo root)
├── FastPortCharp.sln                   ← MODIFY (add FastPortGameServerTemplate)
├── README.md                            ← MODIFY (template 섹션 1문단)
├── HANDOFF.md                           ← MODIFY (Important Architecture Decisions)
├── docs/
│   └── ENGINE_PACKAGES.md               ← NEW (optional, 1 page)
│
├── LibCommons/
│   └── LibCommons.csproj                ← MODIFY (NuGet metadata)
│
├── LibNetworks/
│   └── LibNetworks.csproj               ← MODIFY (NuGet metadata)
│
└── FastPortGameServerTemplate/          ← NEW project
    ├── FastPortGameServerTemplate.csproj
    ├── Program.cs
    ├── appsettings.json
    ├── README.md
    ├── QUICKSTART.ko.md
    │
    ├── Configuration/
    │   └── GameServerOptions.cs
    │
    ├── Application/
    │   ├── GameServer.cs                (IHostedService)
    │   └── PacketDispatcher.cs
    │
    ├── Handlers/
    │   ├── IPacketHandler.cs
    │   └── EchoHandler.cs
    │
    ├── Sessions/
    │   ├── GameSession.cs               (: BaseSession)
    │   └── GameSessionFactory.cs
    │
    ├── Telemetry/
    │   ├── IGameServerTelemetry.cs
    │   └── NullGameServerTelemetry.cs
    │
    └── Protocols/
        └── Sample.proto
```

### 11.2 Implementation Order

| 순서 | 작업 | 산출물 | 검증 |
|------|------|--------|------|
| 1 | `LibCommons.csproj` / `LibNetworks.csproj` NuGet 메타데이터 추가 | csproj 변경 | `dotnet build` 0 warning |
| 2 | `FastPortGameServerTemplate` 프로젝트 생성 (`dotnet new console` 후 csproj 수정) | csproj + Program.cs skeleton | `dotnet build FastPortGameServerTemplate` 통과 |
| 3 | sln 등록 (`dotnet sln FastPortCharp.sln add FastPortGameServerTemplate`) | sln 변경 | 솔루션 빌드 통과 |
| 4 | `GameServerOptions.cs` + `appsettings.json` | configuration | 바인딩 동작 |
| 5 | `Sample.proto` + `Grpc.Tools` ItemGroup 추가 | .proto + .csproj `<Protobuf>` | 빌드 시 `EchoRequest.cs` 등 자동 생성 |
| 6 | `IGameServerTelemetry` + `NullGameServerTelemetry` | 2 files | DI 등록 가능 |
| 7 | `GameSession : BaseSession` + `GameSessionFactory` | 2 files | listener에 주입 가능 |
| 8 | `IPacketHandler` + `EchoHandler` + `PacketDispatcher` | 3 files | echo dispatch 동작 |
| 9 | `GameServer : IHostedService` (BaseListener 시작/정지) | 1 file | StartAsync/StopAsync 동작 |
| 10 | `Program.cs` Generic Host wiring + Serilog | 1 file | `dotnet run` 시 listen 로그 출력 |
| 11 | `FastPortClient` 또는 임시 client로 echo 수동 검증 | (코드 변경 없거나 최소) | RTT 정상 |
| 12 | README + QUICKSTART.ko 작성 | 2 docs | 5분 onboarding 가능 |
| 13 | `HANDOFF.md` 갱신 + repo `README.md` 갱신 | docs | 합의된 architecture decisions 반영 |
| 14 | (선택) `docs/ENGINE_PACKAGES.md` 1page | 1 doc | NuGet publish 차기 cycle 입력 |
| 15 | GitHub Template Repo 노출 경로 결정 문서화 (FastPortSharp 자체 표기) | README + HANDOFF 1문단 | FR-09 충족 |
| 16 | 10K 벤치 baseline 동등성 1회 확인 (선택, Check 단계로 미뤄도 OK) | 측정 노트 | RTT P95 ≤ baseline + 5% |

### 11.3 Session Guide

> Auto-generated from §11.2. `--scope` 키로 세션을 분할해 실행 가능.

#### Module Map

| Module | Scope Key | Description | Estimated Turns |
|--------|-----------|-------------|:---------------:|
| Engine csproj 메타데이터 | `csproj-meta` | LibCommons/LibNetworks csproj 메타데이터 추가 (#1) | 8-12 |
| 템플릿 골격 + Configuration | `template-skeleton` | 프로젝트 생성, sln 등록, GameServerOptions, appsettings (#2-4) | 12-18 |
| Protobuf 통합 | `template-protobuf` | Sample.proto + Grpc.Tools (#5) | 8-12 |
| Telemetry hook + Sessions + Handlers | `template-runtime` | Telemetry/Sessions/Handlers/Dispatcher (#6-8) | 18-25 |
| Hosting + Serilog 통합 | `template-host` | GameServer + Program.cs + Serilog (#9-10) | 12-18 |
| Echo 수동 검증 | `template-echo-verify` | FastPortClient로 echo 1회 (#11) | 6-10 |
| Docs + HANDOFF + GitHub Template 결정 | `docs-handoff` | README/QUICKSTART/HANDOFF/optional ENGINE_PACKAGES + FR-09 결정 (#12-15) | 12-18 |
| (옵션) 10K 벤치 baseline 비교 | `bench-baseline` | s5-random-10k 1회 (#16) — Check 단계로 미뤄도 무방 | 8-12 |

#### Recommended Session Plan

| Session | Phase | Scope | Turns |
|---------|-------|-------|:-----:|
| Session 1 | Plan + Design | 전체 | 30-35 |
| Session 2 | Do | `--scope csproj-meta,template-skeleton` | 25-35 |
| Session 3 | Do | `--scope template-protobuf,template-runtime` | 30-40 |
| Session 4 | Do | `--scope template-host,template-echo-verify` | 25-30 |
| Session 5 | Do | `--scope docs-handoff` | 12-18 |
| Session 6 | Check + Report | `--scope bench-baseline` 포함 | 25-35 |

> 최소 모드: Session 2-5를 한 세션에 묶어도 무방 (turn 합 ≈ 90-120). 본인 dogfood이므로 사용자 재량.

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-09 | Initial draft (Option C — Pragmatic Balance, Plan/PRD 정합) | boinred |
