# protos-shared-folder-revert-contracts Planning Document

> **Summary**: 직전 두 cycle (`template-contracts-extraction`, `template-contracts-scaffold-fix`)이 도입한 `FastPortGameServerTemplate.Contracts` 라이브러리를 제거하고, `.proto` 파일만 담는 `template-projects/Protos/` 폴더로 단순화한다. 각 consumer (Template / SampleClient / 향후 Dashboard)가 자체 `<Protobuf Include="..\Protos\*.proto"/>`로 C# 코드를 자체 어셈블리에 생성. `PacketIds.cs`는 Template 소유 + SampleClient는 `<Compile Include Link>`로 소스 공유.
>
> **Project**: FastPortSharp
> **Version**: 1.0
> **Author**: das_young
> **Date**: 2026-05-12
> **Status**: Draft
> **Predecessor Cycles** (partial revert):
> - `template-contracts-extraction` (archived, commits `b3e4e2c`, `ff105ea`)
> - `template-contracts-scaffold-fix` (archived, commit `241cef2`)

---

## Executive Summary

| Perspective | Content |
|-------------|---------|
| **Problem** | Contracts Class Library는 sharing 매커니즘으로는 과도. proto는 wire 정의 (data)에 가까운데 별도 .NET 어셈블리로 만들면 모든 consumer가 같은 어셈블리 의존 → 단순 단일 게임 서버에 비해 indirection 추가. `LibCommons/LibNetworks` 패턴과 달리 게임별로 진화하는 contracts. |
| **Solution** | `Protos/` 폴더 (csproj 없음, .proto만)로 단순화. 각 consumer가 자체 `<Protobuf Include>`로 C# 생성. PacketIds.cs는 Template 소유 + SampleClient는 `<Compile Link>`로 공유 (Exe→Exe 안티패턴 회피 유지). |
| **Function/UX Effect** | sln 프로젝트 수 13 → 12. scaffold 출력 4 projects → 3 projects + Protos 폴더 (LibCommons 패턴). 빌드 결과 모든 검증 동일 (echo round-trip, tests). |
| **Core Value** | (1) 게임 contract = data 인식 강화, (2) scaffold 출력 단순화 (LibCommons처럼 verbatim), (3) Dashboard 등 신규 consumer는 `<Protobuf Include>` 한 줄로 통합 (ProjectReference 의존 어셈블리 0). |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | Contracts lib은 sharing 단위로 과도하다. proto는 data, 각 consumer가 자체 cs 생성하는 것이 .NET 생태계에서 더 자연스러움. scaffold 출력 단순화 + 향후 consumer 추가 시 ProjectReference 의존 제거. |
| **WHO** | Template 유지자, SampleClient, 향후 Dashboard (Echo Client 탭), 외부 게임 사용자 (scaffold 출력 받는 사람), CI. |
| **RISK** | (a) Exe→Exe ProjectReference 안티패턴 재발 — `<Compile Link>` 패턴으로 회피. (b) scaffold 출력 path depth 조정 (현재 `..\..\` → `..\..\` 또는 `..\`) 재검토 필요. (c) fixture 4 case (01/05/07 + golden) 재생성. (d) Dashboard `dashboard-echo-client-tab` cycle은 `<Protobuf Include>` 모델로 unblock 방식 전환. |
| **SUCCESS** | (1) `Contracts/` 디렉터리 + csproj 제거, (2) `template-projects/Protos/Sample.proto` 1개 파일만, (3) Template + SampleClient 자체 build → EchoRequest/Response 어셈블리 안 노출, (4) echo round-trip 정상 RTT 측정, (5) scaffold 출력 3 projects + Protos 폴더, (6) 모든 case PASS, (7) full sln build 0/0 + 139 + 37 tests pass. |
| **SCOPE** | sln -1 project, Contracts/ 디렉터리 제거, Template/SampleClient csproj 각각 수정, scaffold sh/ps1 수정 (copy_contracts → copy_protos, replace 로직), fixture 4 case 재생성, README 갱신. |

---

## 1. Overview

### 1.1 Purpose

이전 두 cycle은 SampleClient의 Exe→Exe ProjectReference 안티패턴을 해소하는 과정에서 `FastPortGameServerTemplate.Contracts` Class Library를 도입했다. 그러나 다음 cycle (Dashboard Echo Client 탭) 작업을 앞두고 검토한 결과:

- `Contracts` 라이브러리는 sharing 단위로 **과도**하다 (proto는 data에 가깝고, 각 consumer가 자체 cs 생성하는 것이 더 자연스러움).
- `LibCommons`/`LibNetworks` 처럼 verbatim 공유되는 엔진 인프라와 달리, 게임 contract는 게임별로 진화한다.
- scaffold 출력에 `<NewName>.Contracts` 프로젝트가 추가되어 onboarding 복잡도 증가.

본 cycle은 다음을 수행한다:
- `Contracts` 라이브러리 제거 (proto + PacketIds → 새 위치).
- `template-projects/Protos/` (단순 폴더, .proto만).
- 각 consumer가 자체 `<Protobuf Include="..\Protos\*.proto"/>` 보유.
- PacketIds.cs는 Template 소유 + SampleClient는 `<Compile Include Link>`.

### 1.2 Background

직전 두 cycle 결과 (archived):
```
template-projects/
├── FastPortGameServerTemplate.Contracts/      ← 본 cycle에서 제거
│   ├── Protocols/Sample.proto
│   ├── Handlers/PacketIds.cs
│   └── FastPortGameServerTemplate.Contracts.csproj (net10.0 Class Lib)
├── FastPortGameServerTemplate/
└── FastPortGameServerTemplate.SampleClient/
```

각 consumer는 `<ProjectReference Include="..\FastPortGameServerTemplate.Contracts\..."/>` 으로 의존 → Class Library가 생성한 `EchoRequest/EchoResponse` 타입 사용.

### 1.3 Related Documents

- `docs/archive/2026-05/template-contracts-extraction/` (이전 Plan/Design/Report)
- `docs/archive/2026-05/template-contracts-scaffold-fix/` (이전 Plan/Design/Report)
- `docs/01-plan/features/dashboard-echo-client-tab.plan.md` (이번 cycle 후 unblock 방식 전환 예정)

---

## 2. Scope

### 2.1 In Scope

**Part A — Source Repo 구조 변경**
- [ ] `mkdir template-projects/Protos`
- [ ] `git mv template-projects/FastPortGameServerTemplate.Contracts/Protocols/Sample.proto template-projects/Protos/Sample.proto`
- [ ] `git mv template-projects/FastPortGameServerTemplate.Contracts/Handlers/PacketIds.cs template-projects/FastPortGameServerTemplate/Handlers/PacketIds.cs` (Template 소유 복귀)
- [ ] `template-projects/FastPortGameServerTemplate.Contracts/` 디렉터리 + csproj 제거
- [ ] `FastPortSharp.sln`에서 Contracts 프로젝트 등록 제거 (`dotnet sln remove`)

**Part B — Template csproj**
- [ ] `Google.Protobuf.Tools` + `Grpc.Tools` PackageReference 복원 (Contracts에서 회수)
- [ ] `<Protobuf Include="..\Protos\*.proto" ProtoRoot="..\Protos" GrpcServices="None" Access="Public"/>` 추가
- [ ] `<ProjectReference Include="..\FastPortGameServerTemplate.Contracts\..."/>` 제거

**Part C — SampleClient csproj**
- [ ] `Google.Protobuf.Tools` + `Grpc.Tools` PackageReference 추가
- [ ] `<Protobuf Include="..\Protos\*.proto" ProtoRoot="..\Protos" GrpcServices="None" Access="Public"/>` 추가
- [ ] `<Compile Include="..\FastPortGameServerTemplate\Handlers\PacketIds.cs" Link="Handlers\PacketIds.cs"/>` 추가 (소스 공유 — Exe 참조 아님)
- [ ] `<ProjectReference Include="..\FastPortGameServerTemplate.Contracts\..."/>` 제거

**Part D — scaffold script (sh + ps1)**
- [ ] `CONTRACTS_SRC` → `PROTOS_SRC` 변수 변경 (또는 추가, Contracts 변수 제거)
- [ ] `copy_contracts()` → `copy_protos()` (verbatim 복사, token replace 안 함 — LibCommons 패턴)
- [ ] `replace_tokens()` 의 Contracts subtree iteration 제거 (Protos는 token replace 대상 아님)
- [ ] Contracts 디렉터리/csproj rename 로직 제거
- [ ] `generate_sln()` 의 Contracts `dotnet sln add` 제거 → 3 projects 복귀
- [ ] csproj path depth 조정 로직 유지 (`..\..\LibCommons` → `..\LibCommons`)
- [ ] `..\..\Protos` → `..\Protos` 도 동일 패턴으로 추가 (Template/SampleClient의 `<Protobuf Include>` 경로)
  - 단, Template의 `<Protobuf>` path는 source repo (`..\Protos`) 와 scaffold output (`..\Protos`) 동일 깊이 — 추가 조정 불필요할 가능성 (depth 분석 필요)
- [ ] Dry-run 로그 갱신 (Contracts 라인 제거, Protos 라인 추가, 3 projects)
- [ ] step counter `[1/12]~[12/12]` 유지

**Part E — fixture 재생성**
- [ ] `tests/scaffold/case-01-simple/expected/`: `--update-golden` 자동 + files-present.txt manual (Contracts 엔트리 제거, PacketIds.cs를 `<NewName>/Handlers/` 로 복귀, Protos/Sample.proto 추가)
- [ ] `tests/scaffold/case-05-existing-dest-with-force/expected/files-present.txt`: Contracts 엔트리 제거
- [ ] `tests/scaffold/case-07-no-git-no-smoke/expected/files-present.txt`: Contracts 엔트리 제거
- [ ] case-02/03/04/06: 영향 없음 검증

**Part F — docs / README**
- [ ] `template-projects/FastPortGameServerTemplate/README.md`: proto 경로 안내 (`../Protos/Sample.proto`)
- [ ] `template-projects/FastPortGameServerTemplate/QUICKSTART.ko.md`: 동일
- [ ] `README.md`, `README.ko.md`: 리포 트리 다이어그램 갱신 (Contracts 제거, Protos 추가)
- [ ] `scripts/README.md`: scaffold 출력 구조 갱신 (3 projects + Protos 폴더)

**Part G — 검증**
- [ ] `dotnet build FastPortSharp.sln` 0 error / 0 warning
- [ ] `dotnet test FastPortSharp.sln` 139 PASS
- [ ] `dotnet build FastPortSharp.Dashboard.sln` 0/0
- [ ] FastPortDashboardTests 37 PASS
- [ ] echo round-trip RTT 측정 (server + SampleClient)
- [ ] `bash tests/scaffold/run.sh` 7/7 PASS
- [ ] `bash tests/scaffold/run.sh --script ps1` 7/7 PASS
- [ ] 임시 dest scaffold + `dotnet build /tmp/foo/Foo.sln` 성공
- [ ] cs / proto 파일 변경량 검증: PacketIds.cs는 이동만 (rename, content diff = 0), Sample.proto도 이동만

### 2.2 Out of Scope

- `Protocols/` 엔진 test class library (`Protocols/Protos/tests.proto` 등) 변경 — 엔진 영역 별개
- `Dashboard.Maui` Echo Client 탭 구현 — 다음 cycle에서 신규 방식 (`<Protobuf Include>`)으로 진행
- `FastPortServer/FastPortClient` 변경 — 엔진 영역 별개
- proto 메시지/PacketIds 값 변경 (wire 호환성 보존)
- scaffold 출력 구조의 추가 그룹화 (Protos 같은 레벨 + LibCommons 같은 레벨, 평탄 유지)
- `template-projects/` 그룹 폴더 이름 변경 (`template-contracts-extraction`에서 결정한 그대로)

---

## 3. Requirements

### 3.1 Functional Requirements

| ID | Requirement | Priority | Status |
|----|-------------|----------|--------|
| FR-01 | `template-projects/FastPortGameServerTemplate.Contracts/` 디렉터리 + csproj 제거 | High | Pending |
| FR-02 | `template-projects/Protos/Sample.proto` 위치, csproj 없음 | High | Pending |
| FR-03 | `template-projects/FastPortGameServerTemplate/Handlers/PacketIds.cs` 복귀 (Template 소유) | High | Pending |
| FR-04 | Template csproj가 `<Protobuf Include="..\Protos\*.proto">` 보유, Grpc.Tools 자체 PackageReference | High | Pending |
| FR-05 | SampleClient csproj가 `<Protobuf Include="..\Protos\*.proto">` + `<Compile Include Link>` PacketIds 보유 | High | Pending |
| FR-06 | SampleClient ↔ Template 어셈블리 ProjectReference 없음 (Exe→Exe 안티패턴 회피 유지) | High | Pending |
| FR-07 | FastPortSharp.sln 12 프로젝트 (Contracts 제거 후) | High | Pending |
| FR-08 | scaffold sh/ps1 출력에 Contracts 사라지고 Protos 폴더 verbatim 등장 | High | Pending |
| FR-09 | scaffold 출력 sln에 3 projects (`<NewName>`, LibCommons, LibNetworks), Protos는 폴더만 | High | Pending |
| FR-10 | scaffold 출력 csproj가 `<Protobuf Include="..\Protos\*.proto">` 포함 | High | Pending |
| FR-11 | scaffold 출력 `dotnet build <NewName>.sln` 성공 | High | Pending |
| FR-12 | `tests/scaffold/run.sh` 전체 7 case sh + ps1 양쪽 PASS | High | Pending |

### 3.2 Non-Functional Requirements

| Category | Criteria | Measurement |
|----------|----------|-------------|
| Wire 호환성 | proto 메시지 정의 변경 0 (EchoRequest/Response 필드 동일) | `git diff Sample.proto` 빈 출력 (이동만, 내용 무변경) |
| Test 회귀 | FastPortTests 139 + FastPortDashboardTests 37 = 176 모두 pass | `dotnet test` 양쪽 sln |
| Echo round-trip | RTT 측정 (이전 cycle 대비 변동 ±5ms 이내) | manual run |
| Cross-OS scaffold | sh/ps1 byte-identical 유지 | `run.sh --script ps1` PASS |
| Step counter | `[1/12]~[12/12]` 보존 | stdout-contains fixture |

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] FR-01~FR-12 모두 충족
- [ ] 두 sln 모두 build 0/0
- [ ] 176 tests 모두 pass
- [ ] echo round-trip RTT 측정 (성공 로그 출력)
- [ ] scaffold suite 7/7 양쪽 PASS
- [ ] scaffold 출력 manual build 성공
- [ ] cs/proto content diff = 0 (이동만, 내용 무변경) — `git log --follow` 추적 가능
- [ ] commit message에 직전 두 cycle 인용 + 본 cycle 동기 명시

### 4.2 Quality Criteria

- [ ] SampleClient bin 디렉터리에 Template.dll/exe 부재 (안티패턴 회귀 확인)
- [ ] Contracts.dll 더 이상 어디서도 생성되지 않음 (artifacts 확인)
- [ ] `template-projects/Protos/` 폴더에 .proto 파일만 (.cs / .csproj 없음)

---

## 5. Risks and Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| `<Compile Include Link>`에서 BSD `..` 경로 해석 안 되거나 MSBuild가 외부 cs 인식 못 함 | High | Low | MAUI test project gotcha 메모리에서 동일 패턴 검증됨. SampleClient csproj에서 절대 fallback 없이 `..\FastPortGameServerTemplate\Handlers\PacketIds.cs` 명시. 빌드 1회로 즉시 검출. |
| 각 consumer가 자체 EchoRequest/Response 타입 생성 → 어셈블리 간 타입 불일치 | Medium | Low | TCP wire 통신은 byte 수준이라 어셈블리별 타입은 무관. 직접 instance 교환은 안 함 (각 process 별도). 단, 같은 process에서 두 consumer 동거 시 cast 불가 (현재 시나리오에 없음). |
| scaffold 출력 `..\Protos` path가 정확히 어디로 resolve 되는지 (`<dest>/<NewName>/csproj` 기준 `..\Protos` = `<dest>/Protos`) | Medium | Low | 동일 깊이라 `..\Protos` 그대로 작동. 단, source repo는 `template-projects/<project>/csproj` 기준이라 `..\Protos` 역시 동일하게 `template-projects/Protos`로 resolve. 깊이 분석으로 path-fix 불필요 가능성 높음. |
| fixture 4 case 재생성 누락 | High | Medium | 직전 cycle (`scaffold-fix`) 절차 동일 — `--update-golden` + manual edit. checklist로 case-01/05/07 + 02/03/04/06 영향 0 확인. |
| `dashboard-echo-client-tab` Plan 문서가 Contracts 참조 가정 | Medium | High | Plan §1.3 (`Predecessor Cycles`)에 명시 + Echo Client cycle Design 시작 시 `<Protobuf Include>` 모델로 전환 (한 줄 add). dashboard plan은 archived 안 되었으므로 같이 수정 가능. |
| Grpc.Tools 양쪽 csproj에 추가 시 build 시간 증가 (각자 generation) | Low | Medium | Sample.proto는 작음 (~10 lines), 생성 cs도 작음. Template + SampleClient 두 번 실행해도 빌드 시간 차이 무시 가능. |
| git history에서 PacketIds.cs/Sample.proto rename detection 깨짐 (2회 이동) | Low | Low | `git mv` 후 `git log --follow` 확인. 회복 불가 시 history 단절 acceptable (cs content 변경 0이라 blame은 commit 메시지 추적). |

---

## 6. Impact Analysis

### 6.1 Changed Resources

| Resource | Type | Change |
|----------|------|--------|
| `template-projects/Protos/` | New dir | mkdir + Sample.proto 이동 |
| `template-projects/Protos/Sample.proto` | File moved | Contracts/Protocols/ → Protos/ (rename) |
| `template-projects/FastPortGameServerTemplate/Handlers/PacketIds.cs` | File moved (back) | Contracts/Handlers/ → Template/Handlers/ (rename, content 동일) |
| `template-projects/FastPortGameServerTemplate.Contracts/` | Removed | 전체 디렉터리 + csproj 제거 (`git rm -r`) |
| `template-projects/FastPortGameServerTemplate/FastPortGameServerTemplate.csproj` | Modified | `<Protobuf Include>` 복귀 + Grpc.Tools 복귀, Contracts ProjectReference 제거 |
| `template-projects/FastPortGameServerTemplate.SampleClient/FastPortGameServerTemplate.SampleClient.csproj` | Modified | `<Protobuf Include>` 추가 + Grpc.Tools 추가 + `<Compile Include Link>` PacketIds, Contracts ProjectReference 제거 |
| `FastPortSharp.sln` | Modified | Contracts 프로젝트 + config mapping 제거 (`dotnet sln remove`) |
| `scripts/scaffold-game-server.sh` | Modified | CONTRACTS_SRC → PROTOS_SRC, copy_contracts → copy_protos (verbatim), replace_tokens subtree 단일화, generate_sln 3 projects |
| `scripts/scaffold-game-server.ps1` | Modified | sh 1:1 mirror |
| `scripts/README.md` | Modified | scaffold 출력 구조 안내 (3 projects + Protos) |
| `tests/scaffold/case-01-simple/expected/{sha256,tree,files-present}.txt` | Regenerated | `--update-golden` + manual files-present (Contracts 제거, PacketIds 복귀, Protos 추가) |
| `tests/scaffold/case-05-existing-dest-with-force/expected/files-present.txt` | Modified | Contracts 엔트리 제거 |
| `tests/scaffold/case-07-no-git-no-smoke/expected/files-present.txt` | Modified | 동일 |
| `template-projects/FastPortGameServerTemplate/README.md` | Modified | proto 경로 안내 `..\Protos\Sample.proto` |
| `template-projects/FastPortGameServerTemplate/QUICKSTART.ko.md` | Modified | 동일 |
| `README.md`, `README.ko.md` | Modified | 리포 트리 다이어그램 |
| `docs/01-plan/features/dashboard-echo-client-tab.plan.md` | Modified | Predecessor 가정 갱신 (Contracts → Protos shared folder) |

### 6.2 Current Consumers

| Resource | Operation | Path | Impact |
|----------|-----------|------|--------|
| `EchoRequest`/`EchoResponse` (Template 측) | Application/EchoHandler 등에서 인스턴스화 | Template/Application/*.cs | None — 같은 namespace `FastPortGameServerTemplate.Protocols` 생성됨 |
| `EchoRequest`/`EchoResponse` (SampleClient 측) | Sessions/SampleClientSession.cs | 동일 namespace 생성, byte 호환 | None |
| `PacketIds.EchoRequest`/`.EchoResponse` | Template + SampleClient 양쪽 | `FastPortGameServerTemplate.Handlers.PacketIds.*` (namespace 보존) | None — `<Compile Include Link>` 로 동일 namespace 컴파일 |
| Scaffold CLI 사용자 | `bash scripts/scaffold-game-server.sh Foo /tmp/foo` | 출력 구조 단순화 (3 projects + Protos 폴더) | Improvement |
| CI scaffold workflow | `.github/workflows/scaffold.yml` | fixture 변경 후 PASS 필요 | M5-M6에서 검증 |

### 6.3 Verification

- [ ] `find template-projects/Protos -type f` → Sample.proto 1개만
- [ ] `find template-projects -name "Contracts" -o -name "Contracts.csproj"` → 빈 출력
- [ ] `grep -r "FastPortGameServerTemplate.Contracts" --include="*.csproj"` → 0 matches
- [ ] `ls template-projects/FastPortGameServerTemplate.SampleClient/bin/Debug/net10.0/` → Template.dll/exe 부재
- [ ] `dotnet sln FastPortSharp.sln list` → 12 projects (Contracts 부재)

---

## 7. Architecture Considerations

### 7.1 Project Level

해당 없음 — .NET solution refactor.

### 7.2 Key Architectural Decisions

| Decision | Options | Selected | Rationale |
|----------|---------|----------|-----------|
| proto 위치 | (a) `Protocols/` (engine과 합침), (b) `template-projects/Protos/` 신규 폴더, (c) Contracts lib 유지 | **(b)** | engine `Protocols/`는 test 영역 — game contract 섞이면 layer 오염. 신규 폴더가 template-projects/ 내 scope 명확. |
| PacketIds.cs 소유 | (a) Template 소유 + SampleClient `<Compile Link>`, (b) Protos/ 폴더 안에 cs 같이, (c) 각자 복제, (d) thin Contracts.csproj | **(a)** | "Protos는 proto만" 사용자 요구 + Exe→Exe ProjectReference 회피 + 단일 source of truth + minor follow-up 수정 시 한 곳만. |
| Grpc.Tools 위치 | (a) Template/SampleClient 각자 PackageReference, (b) Directory.Build.props로 일괄 | **(a)** | minimal change. Directory.Build.props 도입은 별도 cycle. |
| scaffold 출력 Protos | (a) LibCommons 패턴 (verbatim copy, fixed name), (b) token rename | **(a) verbatim** | proto는 data, 게임 이름과 무관. LibCommons/LibNetworks와 동일 패턴 일관성. |
| Contracts archived 처리 | (a) revert로 같이 사라짐, (b) 그대로 history 보존 | **(b) history 보존** | archive는 immutable. 본 cycle은 새 cycle로 진행 (revert 아니라 design pivot). |

### 7.3 Repo Layout (After)

```
template-projects/
├── Protos/                                ← NEW (simple folder, no csproj)
│   └── Sample.proto
├── FastPortGameServerTemplate/
│   ├── Handlers/
│   │   ├── EchoHandler.cs
│   │   ├── IPacketHandler.cs
│   │   └── PacketIds.cs                   ← BACK (Template 소유)
│   ├── ...
│   └── FastPortGameServerTemplate.csproj
│       ├── + Google.Protobuf
│       ├── + Google.Protobuf.Tools
│       ├── + Grpc.Tools
│       └── + <Protobuf Include="..\Protos\*.proto"/>
└── FastPortGameServerTemplate.SampleClient/
    ├── ...
    └── FastPortGameServerTemplate.SampleClient.csproj
        ├── + Google.Protobuf
        ├── + Google.Protobuf.Tools
        ├── + Grpc.Tools
        ├── + <Protobuf Include="..\Protos\*.proto"/>
        └── + <Compile Include="..\FastPortGameServerTemplate\Handlers\PacketIds.cs"
                       Link="Handlers\PacketIds.cs"/>
```

```
scaffold output (After):
<dest>/
├── <NewName>/                              (token-renamed)
│   ├── Handlers/PacketIds.cs               (← Template에서 copy)
│   ├── ...
│   └── <NewName>.csproj
│       └── <Protobuf Include="..\Protos\*.proto"/>
├── Protos/                                  (verbatim, like LibCommons)
│   └── Sample.proto
├── LibCommons/  LibNetworks/
└── <NewName>.sln  (3 projects)
```

---

## 8. Convention Prerequisites

### 8.1 Existing

- ☑ scaffold script byte-identical 정책 (HANDOFF.md:282)
- ☑ `tests/scaffold/run.sh --update-golden` 도구
- ☑ MAUI test project `<Compile Include Link>` 패턴 (memory: maui-test-project-tfm-gotcha)
- ☑ `template-projects/` 그룹 폴더 패턴

### 8.2 To Define

해당 없음 — 기존 convention 활용.

---

## 9. Next Steps

1. [ ] Design 단계: 짧게 — 정확한 diff 위치 + 검증 매트릭스 명세
2. [ ] Do 단계: 모듈
   - **M1** — `Protos/` 폴더 생성 + Sample.proto 이동
   - **M2** — PacketIds.cs Template/Handlers/ 복귀
   - **M3** — Contracts/ 디렉터리 + csproj 삭제, sln에서 제거
   - **M4** — Template csproj: Grpc.Tools 복귀 + `<Protobuf Include>` + Contracts ref 제거
   - **M5** — SampleClient csproj: Grpc.Tools 추가 + `<Protobuf Include>` + `<Compile Include Link>` PacketIds + Contracts ref 제거
   - **M6** — sln build + test 검증 + echo round-trip
   - **M7** — scaffold sh + ps1 수정 (CONTRACTS_SRC → PROTOS_SRC, copy_protos, sln 3 projects)
   - **M8** — fixture 재생성 (case-01 auto + case-05/07 manual)
   - **M9** — scaffold suite + smoke build 검증
   - **M10** — docs 갱신 (READMEs, scripts/README, dashboard plan)
3. [ ] Check: build + tests + scaffold + cs diff 검증
4. [ ] Archive

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-12 | Initial draft. 직전 두 cycle의 Contracts lib을 Protos shared folder로 단순화. | das_young |
