# game-server-template-scaffold-scripts Planning Document

> **Summary**: 단일 명령으로 `FastPortGameServerTemplate`을 사용자 지정 이름으로 복제 + 엔진(LibCommons/LibNetworks) 동봉 + 토큰 일괄 치환 + (옵션) `git init` + `dotnet build` smoke까지 자동화하는 cross-platform 스크립트 한 쌍 (`scripts/scaffold-game-server.sh` + `.ps1`).
>
> **Project**: FastPortSharp
> **Version**: (.NET 10 / FastPortSharp.sln)
> **Author**: boinred
> **Date**: 2026-05-09
> **Status**: Draft
> **PRD**: `docs/00-pm/game-server-template-scaffold-scripts.prd.md`
> **선행 cycle**: `game-server-template-from-network-engine` (archived 2026-05)
> **Level**: Dynamic (cross-platform 셸 도구 + 기존 .NET 솔루션 통합)

---

## Executive Summary

| Perspective | Content |
|-------------|---------|
| **Problem** | 직전 cycle이 `FastPortGameServerTemplate`을 출시했지만, 새 게임 서버 시작 시 (1) clone (2) 폴더 rename (3) csproj 메타 (4) `.cs` namespace/using (5) `.proto` csharp_namespace (6) `.sln` 등록 (7) `git init` 7단계 수동 yak-shaving이 5분 echo 약속을 깬다. 게다가 "Use this template"는 monorepo 14개 프로젝트를 통째로 복제한다 — 사용자는 게임 서버 1개만 원했다. |
| **Solution** | `scripts/scaffold-game-server.sh` + `.ps1` 두 개의 cross-platform 스크립트. 인자 `<NewProjectName> <DestinationPath>` + 옵션 (`--no-git`/`--force`/`--dry-run`/`--skip-smoke`)으로 호출. 스크립트는 `FastPortGameServerTemplate/` + `LibCommons/` + `LibNetworks/`를 destination에 복사하고, `FastPortGameServerTemplate` 토큰을 새 이름으로 일괄 치환, 새 `.sln` 생성, `git init` + 초기 commit, 마지막에 `dotnet build` smoke 실행. 외부 런타임 의존 0. |
| **Function/UX Effect** | 7단계 → 1단계, wall-clock ≤ 60초. 결과물은 자기완결적 (engine source 동봉) — destination이 어디든 그대로 빌드/실행 가능. 3환경 CI matrix(Ubuntu/macOS/Windows-PS7)에서 byte-identical 검증. |
| **Core Value** | 직전 cycle이 *템플릿*을 만들었다면, 본 cycle은 *템플릿을 즉시 쓸 수 있게 만드는 도구*. "Use this template" 직후의 7단계 yak-shaving을 0단계로 압축. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 새 게임 서버 부트스트랩의 잔여 마찰점(rename yak-shaving)을 제거하여 "5분 echo" 약속을 실제로 지키고, "Use this template" 사용자에게 작은 자기완결 시드 프로젝트를 즉시 손에 쥐어준다. |
| **WHO** | Primary: 본인/내부 팀 (Solo Soyoung). Secondary: C# 인디 (Indie Ian) + 스튜디오 backend lead (Studio Sora). 직전 cycle의 ICP와 동일. |
| **RISK** | (R-A) regex/토큰 충돌로 broken csproj. (R-B) cross-platform parity 깨짐 (BOM/CRLF). (R-C) engine 동봉으로 인한 향후 sync 부담. (R-D) 본인 외 사용자 0명일 가능성. |
| **SUCCESS** | (1) `./scripts/scaffold-game-server.sh MyGame ../mygame-test` 실행 → 60초 내 echo-ready 자기완결 프로젝트 생성. (2) golden-file diff 5+ 케이스 통과. (3) Ubuntu+macOS+Win-PS7 3환경 byte-identical. (4) 신규 프로젝트가 `dotnet build` 통과. (5) FastPortSharp 본 솔루션 회귀 0. |
| **SCOPE** | v1 only: server template 복제 + engine 동봉 + 토큰 치환 + sln 생성 + `git init` + smoke build. **비범위**: SampleClient scaffolding, `dotnet new` 등록, NuGet 업로드, GUI, 다국어 UI. |

---

## 1. Overview

### 1.1 Purpose

직전 cycle(`game-server-template-from-network-engine`)의 산출물 위에, 새 프로젝트 생성을 자동화하는 cross-platform 셸 스크립트 한 쌍을 만든다. 사용자는 한 줄 명령으로 `FastPortGameServerTemplate`을 자신의 프로젝트 이름으로 복제하고 즉시 빌드 가능한 자기완결 프로젝트를 얻는다.

### 1.2 Background

- 직전 cycle 결정: GitHub Template Repository (이 레포 자체)를 1차 배포 채널. NuGet 업로드는 명시적 out of scope. `dotnet new` 등록도 보류.
- "Use this template" 흐름은 monorepo 14개 프로젝트를 통째로 복제하는 단점이 있음. scaffolding 스크립트로 사용자가 *원하는* 1개 프로젝트 + 엔진만 깔끔하게 추출 가능.
- 본인 dogfood 시점: 다음 toy 게임 서버를 시작할 때 본 스크립트로 부트스트랩하여 가설 B1 (renaming이 실제 마찰점이다) 즉시 검증.
- 사용자 결정: **engine(LibCommons + LibNetworks) 자체도 함께 복사** → 결과물이 자기완결적. ProjectReference 경로 재계산 불필요.

### 1.3 Related Documents

- PRD: `docs/00-pm/game-server-template-scaffold-scripts.prd.md`
- 선행 cycle: `docs/archive/2026-05/game-server-template-from-network-engine/`
- Architecture rules: `HANDOFF.md` "Important Architecture Decisions"

---

## 2. Scope

### 2.1 In Scope

- [ ] `scripts/scaffold-game-server.sh` (Bash 5+, macOS bash 3.2 호환 — `set -euo pipefail`).
- [ ] `scripts/scaffold-game-server.ps1` (PowerShell 7+, UTF-8 NoBOM 강제).
- [ ] CLI 인자: `<NewProjectName> <DestinationPath>` 필수 + 옵션 `--force` / `--no-git` / `--dry-run` / `--skip-smoke` / `-h|--help`.
- [ ] 입력 검증: 이름 정규식 `^[A-Z][A-Za-z0-9]{0,63}$` + 차단어 목록 거절.
- [ ] 복사 대상:
  - `FastPortGameServerTemplate/` → `<dest>/<NewName>/`
  - `LibCommons/` → `<dest>/LibCommons/`
  - `LibNetworks/` → `<dest>/LibNetworks/`
  - `bin/` `obj/` `*.user` 제외
- [ ] 토큰 치환 (`<NewName>` 결정론적, 단일 토큰 `FastPortGameServerTemplate` → `<NewName>`):
  - 폴더명 `FastPortGameServerTemplate/` → `<NewName>/`
  - csproj 파일명 `FastPortGameServerTemplate.csproj` → `<NewName>.csproj`
  - 모든 `.cs` 의 `namespace`/`using` 선언
  - `.proto` 의 `option csharp_namespace = "FastPortGameServerTemplate.Protocols";` → `<NewName>.Protocols`
  - csproj `<RootNamespace>` / `<AssemblyName>`
  - README/QUICKSTART 의 Markdown 본문 (예: `dotnet run --project FastPortGameServerTemplate` → `<NewName>`)
- [ ] `<dest>/<NewName>.sln` 신규 생성 — `<NewName>`, `LibCommons`, `LibNetworks` 3개 프로젝트 등록 (`dotnet new sln` + `dotnet sln add`).
- [ ] `<dest>/.gitignore` 생성 (FastPortSharp 본 레포 .gitignore 베이스 + 본 cycle 추가 패턴).
- [ ] `<dest>/README.md` 신규 생성 (간단한 onboarding — 빌드/실행/패킷 추가 가이드 1쪽).
- [ ] `--no-git` 미지정 시 `<dest>` 에서 `git init` + 초기 commit (`Initial scaffold from FastPortGameServerTemplate`).
- [ ] `--skip-smoke` 미지정 시 마지막에 `dotnet build <dest>/<NewName>.sln -c Release` 실행 (실패 시 exit code != 0).
- [ ] `--dry-run`: 실제 파일 변경 없이 계획만 stdout 출력 (생성/복사/치환 대상 목록).
- [ ] Cross-platform parity: UTF-8 NoBOM + LF, `.gitattributes` 에 `* text=auto eol=lf` 명시.
- [ ] Exit code 표준화: `0` 성공, `2` 입력 검증 실패, `3` destination 충돌 (`--force` 없이), `4` smoke build 실패, `5` 기타 IO/git 실패.
- [ ] Golden-file diff 테스트: `tests/scaffold/<case>/{input,expected}/` 5+ 케이스.
- [ ] GitHub Actions CI matrix: Ubuntu + macOS + Windows-PS7 3환경에서 동일 입력 → byte-identical 결과물 검증.
- [ ] 본 cycle 스크립트 결과물 자체에 대한 `README.md` (`scripts/README.md`).
- [ ] `HANDOFF.md` "Important Architecture Decisions"에 본 cycle 결정 추가.
- [ ] 본 repo `README.md` Game Server Template 섹션에 scaffold 한 줄 명령 추가.

### 2.2 Out of Scope

- SampleClient scaffolding (v1.2 roadmap).
- `dotnet new` 템플릿 등록 (직전 cycle 비범위 결정 유지).
- NuGet 업로드 (직전 cycle 비범위 결정 유지).
- GUI installer / Electron 등 wrapper (PRD §1.2 Solution 1.3).
- `--preserve-upstream-references` 같은 fine-grained replacement 옵션 (v1.1 roadmap).
- 다국어 UI / `--lang ko` 같은 i18n.
- engine 자동 sync (생성 후 사용자가 직접 관리).
- monorepo 내부 destination에서 본 레포 `FastPortSharp.sln` 자동 등록 (사용자 인자 디자인이 외부 destination에 최적화되어 있어 v1에서는 외부 destination만 우선. monorepo 내부 사용은 manual `dotnet sln add` 권장으로 README에 명시).

---

## 3. Requirements

### 3.1 Functional Requirements

| ID | Requirement | Priority | Status |
|----|-------------|----------|--------|
| FR-01 | `scripts/scaffold-game-server.sh` 동작 (mac/linux, bash, `set -euo pipefail`) | High | Pending |
| FR-02 | `scripts/scaffold-game-server.ps1` 동작 (PowerShell 7+, UTF-8 NoBOM) | High | Pending |
| FR-03 | CLI: 두 positional 인자 (`NewName`, `DestPath`) + 옵션 (`--force` / `--no-git` / `--dry-run` / `--skip-smoke` / `-h`) | High | Pending |
| FR-04 | 이름 정규식 `^[A-Z][A-Za-z0-9]{0,63}$` 검증 + 차단어 거절 (양쪽 스크립트 동일 목록) | High | Pending |
| FR-05 | 복사 대상: 템플릿 + LibCommons + LibNetworks (bin/obj 제외) | High | Pending |
| FR-06 | 토큰 치환: 폴더/파일명, csproj 메타, .cs namespace/using, .proto csharp_namespace, README/QUICKSTART 본문 | High | Pending |
| FR-07 | 새 `<dest>/<NewName>.sln` 생성 + 3개 프로젝트 등록 | High | Pending |
| FR-08 | `<dest>/.gitignore`, `<dest>/.gitattributes`, `<dest>/README.md` 생성 | High | Pending |
| FR-09 | `--no-git` 미지정 시 `git init` + 초기 commit | Medium | Pending |
| FR-10 | `--skip-smoke` 미지정 시 `dotnet build` smoke 통과 | High | Pending |
| FR-11 | `--dry-run`: 실제 변경 0, 계획만 출력 | Medium | Pending |
| FR-12 | Exit code 표준 (0/2/3/4/5) | Medium | Pending |
| FR-13 | Golden-file diff 테스트 5+ 케이스 (`tests/scaffold/`) | High | Pending |
| FR-14 | GitHub Actions CI matrix 3환경 (Ubuntu / macOS / Windows-PS7) byte-identical 검증 | Medium | Pending |
| FR-15 | `scripts/README.md` 작성 (사용법, 옵션, 예제, 트러블슈팅) | Medium | Pending |
| FR-16 | 본 repo `README.md` + `HANDOFF.md` 업데이트 (scaffold 명령 + Architecture Decision) | Medium | Pending |

### 3.2 Non-Functional Requirements

| Category | Criteria | Measurement |
|----------|----------|-------------|
| **Performance** | scaffold (`--skip-smoke` 포함) wall-clock ≤ 60초 (loopback, SSD) | `time` 측정 |
| **Cross-platform** | 3환경 (Ubuntu / macOS / Windows-PS7) byte-identical 결과물 (LF, UTF-8 NoBOM) | `sha256sum` 후 diff |
| **Determinism** | 동일 인자 → 동일 결과 (timestamp 외) | Golden-file diff |
| **Robustness** | 차단어/공백/한글/regex meta 입력 시 친절한 에러 메시지 + exit code 2 | negative test |
| **Idempotency** | `--force` 없이 동일 destination 재실행 → exit code 3, 변경 0 | re-run test |
| **Build correctness** | scaffold된 프로젝트 `dotnet build -c Release` 0 warning 0 error | smoke test |
| **본 레포 회귀 0** | `FastPortSharp.sln` 빌드 / 139 테스트 회귀 0 | 본 cycle 후 dotnet build/test |

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] `scripts/scaffold-game-server.sh` + `.ps1` 두 스크립트 작성 + 실행 권한
- [ ] Golden-file 테스트 5+ 케이스 모두 통과 (양쪽 스크립트)
- [ ] 3환경 CI matrix 통과 (Ubuntu / macOS / Windows-PS7)
- [ ] `./scripts/scaffold-game-server.sh MyLobbyServer ../mygame-test` 실행 결과
  - destination이 자기완결적 (engine 동봉)
  - `dotnet build -c Release` 통과
  - `git log` 에 초기 commit 존재
  - 60초 내 완료
- [ ] negative test (차단어, 한글, 공백, 빈 값) 모두 exit code 2 + 친절한 메시지
- [ ] `--dry-run` 결과가 실제 실행 결과와 일치 (실제 실행 시 변경 파일 목록과 dry-run 목록이 동일)
- [ ] `scripts/README.md` 작성
- [ ] 본 repo `README.md` Game Server Template 섹션에 한 줄 사용 예시 추가
- [ ] `HANDOFF.md` Architecture Decision 추가

### 4.2 Quality Criteria

- [ ] `dotnet build FastPortSharp.sln -c Release` 0 warning / 0 error
- [ ] `dotnet test FastPortSharp.sln -c Release --no-build` 139 / 139 회귀 0
- [ ] 셸 스크립트 lint: `shellcheck` 통과 (sh) + `Invoke-ScriptAnalyzer` 통과 (ps1) — 가능한 범위에서
- [ ] 모든 신규 텍스트 파일 LF + UTF-8 NoBOM (`.gitattributes` 강제)

---

## 5. Risks and Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| **R-A**: regex/토큰 치환 실수로 broken csproj/cs | High | Medium | 단일 토큰 `FastPortGameServerTemplate` (26자 unique compound) 만 치환. 차단어 목록으로 collision 회피. golden-file 테스트 5+ 케이스. `--skip-smoke` 미지정 시 dotnet build smoke로 즉시 검출. |
| **R-B**: cross-platform parity 깨짐 (BOM/CRLF/대소문자) | High | Medium | PS 스크립트는 모든 파일 쓰기에 `[System.IO.File]::WriteAllText(path, content, [System.Text.UTF8Encoding]::new($false))` 사용. `.gitattributes` 로 LF 강제. CI matrix에서 `sha256sum` 비교. |
| **R-C**: engine 동봉으로 향후 engine 업데이트 sync 부담 | Medium | High | scaffold 결과물은 *시드*임을 README에 명시. 사용자가 향후 engine 업데이트가 필요하면 (a) 새로 scaffold 후 game 코드만 가져오거나, (b) 직접 LibCommons/LibNetworks 폴더만 재복사. v1.1에서 `--engine-mode submodule|copy|reference` 옵션 후보. |
| **R-D**: 본인 외 사용자 0명 (PRD R-1 동일) | Medium | High | 본인 dogfooding으로 v1 가치는 보장 (다음 toy 게임 서버 부트스트랩 시 1초컷). 외부 채택은 직전 cycle과 동일 게이트로 30일 측정. |
| **R-E**: PowerShell 7 미설치 사용자 (Windows 11 default는 PS5.1) | Medium | Medium | `scripts/README.md` 에 PS 7 설치 안내 (`winget install Microsoft.PowerShell`). v1 비범위로 PS5.1 호환은 명시적으로 빼고, 추후 issue 발생 시 v1.1 검토. |
| **R-F**: 차단어 목록 누락 → corner case 통과 후 broken | Medium | Low | 양쪽 스크립트가 동일 차단어 배열을 공유 (스크립트 간 sync는 golden-file 테스트의 input 으로 자동 검출). 추후 발견 시 patch. |
| **R-G**: macOS bash 3.2 vs Linux bash 5+ 미묘한 비호환 | Medium | Medium | 스크립트는 bash 3.2 호환 subset만 사용 (`mapfile` 회피, `[[` 조건문 사용, `read -a` 회피). CI Ubuntu가 bash 5+이므로 macOS 3.2가 강한 제약. |

---

## 6. Impact Analysis

### 6.1 Changed Resources

| Resource | Type | Change |
|----------|------|--------|
| `scripts/scaffold-game-server.sh` | new file | mac/linux scaffold 스크립트 |
| `scripts/scaffold-game-server.ps1` | new file | Windows scaffold 스크립트 |
| `scripts/README.md` | new file | 사용법/옵션/예제 |
| `tests/scaffold/<case>/input,expected/` | new dirs | golden-file 테스트 데이터 5+ 케이스 |
| `tests/scaffold/run.sh` | new file | golden-file diff runner (mac/linux) |
| `tests/scaffold/run.ps1` | new file | golden-file diff runner (Windows) |
| `.github/workflows/scaffold.yml` | new file | 3환경 CI matrix |
| `.gitattributes` | possibly new | LF 강제 (없으면 신규, 있으면 검토) |
| `README.md` (repo root) | modify | Game Server Template 섹션에 scaffold 한 줄 예시 |
| `README.ko.md` (repo root) | modify | 동일 한국어 갱신 |
| `HANDOFF.md` | modify | Architecture Decision 1건 추가 + Roadmap §3 후속 cycle 명시 |

### 6.2 Current Consumers

본 cycle 변경은 **스크립트 + 테스트 + CI + 문서 추가**이며, 기존 .NET 솔루션 코드는 건드리지 않는다.

| Resource | Impact |
|----------|--------|
| `FastPortSharp.sln` | None (신규 sln은 destination에서만 생성) |
| `LibCommons.csproj` / `LibNetworks.csproj` | None (소스는 읽기 전용 복사 대상) |
| `FastPortGameServerTemplate/*` | None (소스는 읽기 전용 복사 대상) |
| 기존 11개 프로젝트 | None |
| 139 테스트 | None |

### 6.3 Verification

- [ ] `dotnet build FastPortSharp.sln -c Release` 0 warning / 0 error (본 cycle 변경 후)
- [ ] `dotnet test FastPortSharp.sln -c Release --no-build` 139 / 139 회귀 0
- [ ] scaffold된 destination에서 `dotnet build` 통과
- [ ] golden-file 테스트 5+ 케이스 통과
- [ ] 3환경 CI matrix 통과 (Ubuntu / macOS / Windows-PS7)

---

## 7. Architecture Considerations

### 7.1 Project Level

| Level | Selected | Rationale |
|-------|:-:|-----------|
| Starter | ☐ | 단일 도구지만 cross-platform parity와 테스트 인프라가 필요 |
| **Dynamic** | ☑ | 셸 + 테스트 인프라 + CI matrix가 모두 들어감. 적합한 layer. |
| Enterprise | ☐ | Microservices/k8s 불필요 |

### 7.2 Key Architectural Decisions

| Decision | Options | Selected | Rationale |
|----------|---------|----------|-----------|
| 스크립트 구현 언어 | bash 5 / bash 3.2 / Python / Node | **bash 3.2-호환 + PS7** | 외부 런타임 의존 0. macOS bash 3.2 강한 제약 수용. |
| destination이 외부일 때 engine 처리 | 상대경로 재계산 / 절대경로 / **동봉** / 사용자 옵션 | **동봉 (사용자 확정)** | 결과물이 자기완결적. 이식성 ↑. R-C로 sync 부담은 트레이드오프. |
| 스크립트 테스트 방식 | bats/Pester / **golden-file diff** / MSTest / 수동 | **golden-file diff (사용자 확정)** | 외부 의존 0. 셸로 직접 작성. 케이스 추가 비용 낮음. |
| `dotnet build` smoke 포함 여부 | 항상 / off만 / **--skip-smoke 옵션** / on-only | **--skip-smoke 옵션 (사용자 확정)** | 기본 ON으로 사용자 확신 보장, 옵션으로 빠른 dry-run 가능. |
| CI matrix | 4환경 (PS5.1 포함) / **3환경** / 2환경 / 0환경 | **3환경 (사용자 확정)** | PS5.1은 향후 issue 발생 시 검토. PS7은 modern Windows 표준. |
| 토큰 치환 전략 | 단일 토큰 / 다중 토큰 / regex template | **단일 토큰 `FastPortGameServerTemplate`** | 26자 unique compound. metacharacter 위험 없음. 1회 sed/Replace로 충분. |
| 이름 검증 정규식 | 느슨 / **`^[A-Z][A-Za-z0-9]{0,63}$`** / 엄격 ASCII | **PRD 결정 그대로** | C# identifier prefix 규칙 + 길이 제한. |
| sln 등록 | 본 레포 sln add / **destination에 신규 sln** / 안 함 | **destination 신규 sln** | destination이 외부라는 디폴트 가정과 정합. monorepo 내부 사용은 README 안내. |
| 결과물 README | 자동 생성 / 사용자 수동 | **자동 생성 (간단한 onboarding 1쪽)** | scaffold 직후 `cat README.md` 로 next step 보일 수 있게. |
| .gitignore / .gitattributes | 자동 / 수동 | **자동 생성** | 본 레포 .gitignore 베이스 + LF 강제. |

### 7.3 Folder Structure (본 레포)

```
FastPortSharp/  (repo root)
├── scripts/                                      ← NEW
│   ├── scaffold-game-server.sh                   (Bash 3.2-호환)
│   ├── scaffold-game-server.ps1                  (PowerShell 7+)
│   └── README.md                                 (사용법/옵션/예제/트러블슈팅)
│
├── tests/scaffold/                               ← NEW
│   ├── run.sh                                    (golden-file diff runner, bash)
│   ├── run.ps1                                   (golden-file diff runner, PS)
│   ├── _shared/
│   │   ├── name-validation.txt                   (positive + negative case 목록)
│   │   └── blocked-tokens.txt                    (차단어 목록 — 스크립트와 sync)
│   ├── case-01-simple/
│   │   ├── input/{name,dest}                     (CLI 인자)
│   │   ├── expected/                             (기대 결과물 트리)
│   │   └── README.md                             (이 케이스가 검증하는 것)
│   ├── case-02-blocked-name/                     (negative — 차단어 거절)
│   ├── case-03-regex-meta/                       (negative — `My$App` 등)
│   ├── case-04-existing-dest-no-force/           (negative — exit 3)
│   ├── case-05-existing-dest-with-force/         (positive — overwrite)
│   ├── case-06-dry-run/                          (positive — 변경 0)
│   └── case-07-no-git-no-smoke/                  (positive — 옵션 조합)
│
├── .github/workflows/scaffold.yml                ← NEW (3환경 matrix)
├── .gitattributes                                ← NEW or modify (LF 강제)
│
├── (기존 11개 프로젝트 그대로)
├── README.md                                     ← modify (scaffold 한 줄)
├── README.ko.md                                  ← modify (동일 한국어)
└── HANDOFF.md                                    ← modify (Architecture Decision)
```

### 7.4 Folder Structure (scaffold 결과물 — destination)

```
<dest>/                                            ← scaffold 후 자기완결
├── <NewName>.sln                                  (3 projects)
├── <NewName>/                                     (← FastPortGameServerTemplate 였던 것)
│   ├── <NewName>.csproj
│   ├── Program.cs (namespace 치환됨)
│   ├── appsettings.json
│   ├── README.md  / QUICKSTART.ko.md (본문 치환됨)
│   ├── Application/{...}.cs
│   ├── Sessions/{...}.cs
│   ├── Handlers/{...}.cs
│   ├── Telemetry/{...}.cs
│   ├── Configuration/GameServerOptions.cs
│   └── Protocols/Sample.proto (csharp_namespace 치환됨)
├── LibCommons/                                    (engine, 동봉)
├── LibNetworks/                                   (engine, 동봉)
├── README.md                                      (NEW — 자동 생성, 빌드/실행 1쪽)
├── .gitignore                                     (NEW — 본 레포 .gitignore 베이스)
└── .gitattributes                                 (NEW — LF 강제)

(.git/, --no-git 미지정 시 초기 commit 1개)
```

---

## 8. Convention Prerequisites

### 8.1 Existing Project Conventions

- [x] `AGENTS.md`, `HANDOFF.md`, `README.md` 존재
- [x] `.gitignore` 존재 (본 cycle은 `.bkit/`, `.DS_Store` 추가됨)
- [ ] `.gitattributes` — 본 cycle에서 신규 또는 검토 (LF + UTF-8)
- [ ] `shellcheck` / `Invoke-ScriptAnalyzer` — CI에 포함 권장 (선택)

### 8.2 Conventions to Define

| Category | Current | To Define | Priority |
|----------|---------|-----------|:-:|
| 셸 스크립트 스타일 | 없음 | `set -euo pipefail`, function 우선, 인자 명시적 처리, exit code 표준 | High |
| PS 스크립트 스타일 | 없음 | `Set-StrictMode -Version Latest`, `$ErrorActionPreference='Stop'`, advanced function 권장, UTF-8 NoBOM 강제 | High |
| 입력 검증 | 없음 | 양쪽 스크립트가 동일 정규식 + 동일 차단어 목록 사용. `tests/scaffold/_shared/blocked-tokens.txt` 가 single source of truth (스크립트 init 시 읽기 / 또는 빌드 시 임베드) | High |
| 토큰 치환 컨벤션 | 없음 | 1회 정확 치환 (`FastPortGameServerTemplate` → `<NewName>`). regex meta 사용 X. | High |

### 8.3 Environment Variables

해당 없음 — 모든 입력은 CLI 인자 / 옵션.

### 8.4 Pipeline Integration

본 프로젝트는 .NET 멀티-프로젝트이며 9-phase web pipeline은 적용하지 않음. PDCA cycle만 적용.

---

## 9. Next Steps

1. [ ] Design 단계: `/pdca design game-server-template-scaffold-scripts`
   - 3 architecture options 비교 (특히 토큰 치환 구현, 차단어 sync, golden-file 디렉토리 구조)
   - Module Map + Session Guide 생성
2. [ ] Do 단계: scope 분할 가능 — `scripts-sh` / `scripts-ps1` / `tests-golden-file` / `ci-workflow` / `docs-update`
3. [ ] Check 단계: 본 레포 회귀 + scaffold dogfood (실제 본인이 새 toy 프로젝트 1개 부트스트랩) + 3환경 CI 통과
4. [ ] Report 단계: 본 cycle 학습 (특히 R-C engine 동봉 트레이드오프 평가) + 차기 cycle 후보 우선순위 정리

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-09 | Initial draft (engine 동봉 / golden-file 테스트 / 3환경 CI / --skip-smoke 사용자 확정 반영) | boinred |
