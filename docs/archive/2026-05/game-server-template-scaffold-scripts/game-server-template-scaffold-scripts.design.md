# game-server-template-scaffold-scripts Design Document

> **Summary**: Pragmatic Balance — bash + PowerShell 두 모놀리식 스크립트가 자기완결적이되, **block tokens** 만 `tests/scaffold/_shared/blocked-tokens.txt` 단일 source of truth에서 읽어 drift 0. 치환은 BSD/GNU 양쪽 동작하는 `sed -i.bak ... && rm .bak` + PS `[System.IO.File]::WriteAllText` UTF-8 NoBOM. 결과물은 자기완결적 (engine 동봉).
>
> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-09
> **Status**: Draft
> **Planning Doc**: [game-server-template-scaffold-scripts.plan.md](../../01-plan/features/game-server-template-scaffold-scripts.plan.md)
> **PRD**: [game-server-template-scaffold-scripts.prd.md](../../00-pm/game-server-template-scaffold-scripts.prd.md)

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 새 게임 서버 부트스트랩의 잔여 마찰점(rename yak-shaving)을 제거하여 "5분 echo" 약속을 실제로 지키고, "Use this template" 사용자에게 작은 자기완결 시드 프로젝트를 즉시 손에 쥐어준다. |
| **WHO** | Primary: 본인/내부 팀. Secondary: C# 인디 + 스튜디오 backend lead. |
| **RISK** | (R-A) regex/토큰 충돌. (R-B) cross-platform parity 깨짐. (R-C) engine 동봉으로 sync 부담. (R-D) 사용자 0명. (R-G) bash 3.2 vs 5+ 미묘한 비호환. |
| **SUCCESS** | scaffold 60초 내 echo-ready 자기완결 프로젝트 + golden-file 5+ 케이스 + 3환경 byte-identical + dotnet build 통과 + 본 레포 회귀 0. |
| **SCOPE** | v1 only: server template + engine 동봉 + 토큰 치환 + sln 생성 + git init + smoke build. SampleClient/`dotnet new`/NuGet/GUI 비범위. |

---

## 1. Overview

### 1.1 Design Goals

1. **결정론적 치환**: 동일 입력 → 동일 결과물 (timestamp 외). 양쪽 스크립트가 byte-identical 산출물 생성.
2. **Drift-proof block list**: 양쪽 스크립트가 동일한 차단어 / 동일한 정규식을 사용하도록 단일 spec 파일을 두고 둘 다 읽기.
3. **자기완결성**: 결과물이 destination에서 별도 의존 없이 `dotnet build` 통과.
4. **bash 3.2 호환**: macOS 시스템 bash가 3.2 (LTS). `mapfile` / `readarray` / `[[ -v ]]` 회피.
5. **외부 런타임 의존 0**: bash + standard POSIX utils (sed, awk, mv, cp, find, grep), PowerShell 7+의 native API만.
6. **친절한 실패**: 입력 검증 실패 시 무엇이 잘못됐는지 + 어떻게 고치는지 안내.

### 1.2 Design Principles

- **Pragmatic > Pure**: drift가 가장 큰 위험인 *블록 토큰*만 외부화. 나머지(검증 정규식, 치환 토큰)는 인라인. 어차피 양쪽 모두 변경 시 동일 파일 두 군데 수정 필요한 항목.
- **Fail loud**: `set -euo pipefail` (bash) + `Set-StrictMode -Version Latest` (PS) + `$ErrorActionPreference = 'Stop'`.
- **Idempotent dry-run**: `--dry-run`은 실제 변경 0이지만 *모든 검증* 통과해야 stdout 출력.
- **Surface area minimisation**: 단일 토큰 `FastPortGameServerTemplate` (26자) — 하나만 정확히 치환.

---

## 2. Architecture Options (Selected)

### 2.0 Architecture Comparison

| Criteria | Option A: Minimal | Option B: Clean | **Option C: Pragmatic** |
|----------|:-:|:-:|:-:|
| **Approach** | 양쪽 스크립트 완전 자기완결, 블록리스트도 하드코딩 | lib.sh / lib.ps1 추상화 + 함수 호출 | shared spec 1개 (블록 토큰) + 모놀리식 스크립트 |
| **New Files** | ~10 | ~20+ | ~14 |
| **Drift Risk** | High | Low | Low (shared spec 1개 항목) |
| **Lines per script** | ~280 | ~150 + lib | ~250 |
| **Complexity** | Low | High | Medium |
| **Maintainability** | Medium | High | High |
| **Effort** | Low | High | Medium |
| **Risk** | Drift | Over-engineering | Balanced |
| **Recommendation** | hotfix | 큰 surface area | **Default — 본 cycle 적합** |

**Selected**: **Option C — Pragmatic Balance**

**Rationale**:
- Plan §7.2 결정 (engine 동봉 / golden-file / --skip-smoke / 3환경 CI)와 정합.
- drift가 가장 큰 위험인 블록 토큰만 외부화 → `tests/scaffold/_shared/blocked-tokens.txt` 가 단일 source of truth. 양쪽 스크립트와 모든 negative test case가 같은 파일을 읽음.
- 정규식 / 치환 토큰은 양쪽 스크립트에 동일 상수로 인라인 — 26자 unique compound token이라 변경 빈도가 사실상 0.
- 250줄 / 14파일 — 단일 PR 범위.

### 2.1 Component Diagram

```
┌──────────────────────────────────────────────────────────────────────┐
│  scripts/                                                             │
│  ├── scaffold-game-server.sh         (Bash 3.2-호환, ~250 lines)      │
│  ├── scaffold-game-server.ps1        (PowerShell 7+, ~250 lines)      │
│  └── README.md                        (사용법/옵션/예제/트러블슈팅)    │
└──────────────────────────────────────────────────────────────────────┘
                       │                          │
                       │   reads (single source)  │
                       ▼                          ▼
┌──────────────────────────────────────────────────────────────────────┐
│  tests/scaffold/_shared/                                              │
│  ├── blocked-tokens.txt   (1 token / line, line-based parser)         │
│  └── name-validation.txt  (positive + negative cases for self-check)  │
└──────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────┐
│  tests/scaffold/                                                      │
│  ├── run.sh / run.ps1                (golden-file diff runner)        │
│  ├── case-01-simple/                                                  │
│  │   ├── input/{name,dest,flags}     (CLI invocation)                 │
│  │   └── expected/{tree,sha256.txt}  (expected output: file tree +    │
│  │                                    per-file sha256 hash)           │
│  ├── case-02-blocked-name/                                            │
│  ├── case-03-regex-meta/                                              │
│  ├── case-04-existing-dest-no-force/                                  │
│  ├── case-05-existing-dest-with-force/                                │
│  ├── case-06-dry-run/                                                 │
│  └── case-07-no-git-no-smoke/                                         │
└──────────────────────────────────────────────────────────────────────┘
                                                │
                                                ▼
┌──────────────────────────────────────────────────────────────────────┐
│  scaffold execution flow (양쪽 스크립트 동일 12 steps)                 │
│   1. Parse args + flags                                               │
│   2. Validate name (regex + blocked-tokens.txt)                       │
│   3. Resolve & validate destination (--force, idempotency)            │
│   4. (dry-run) print plan, exit 0                                     │
│   5. Copy FastPortGameServerTemplate/ → <dest>/<NewName>/             │
│      (excluding bin/, obj/, *.user)                                   │
│   6. Copy LibCommons/ → <dest>/LibCommons/                            │
│   7. Copy LibNetworks/ → <dest>/LibNetworks/                          │
│   8. Token replacement: FastPortGameServerTemplate → <NewName>        │
│      (folder rename + file rename + content replace in .cs/.csproj/   │
│       .proto/.json/.md, UTF-8 NoBOM, LF preserved)                    │
│   9. Generate <dest>/.gitignore + .gitattributes + README.md          │
│   10. Generate <dest>/<NewName>.sln (dotnet new sln + sln add x3)     │
│   11. (--no-git false) git init + initial commit                      │
│   12. (--skip-smoke false) dotnet build <NewName>.sln -c Release      │
└──────────────────────────────────────────────────────────────────────┘
```

### 2.2 Replacement Algorithm

```
INPUT:  FastPortGameServerTemplate/<file>
OUTPUT: <dest>/<NewName>/<file>  (+ folder/file name replaced)

For each text file (whitelist by ext: cs csproj proto json md sln yml):
  read file → string-replace "FastPortGameServerTemplate" with <NewName>
            → write back as UTF-8 NoBOM, LF line endings

For each folder/file name containing "FastPortGameServerTemplate":
  rename to <NewName>-substituted form

Binary files (none expected — verify with file extension whitelist):
  copy as-is
```

`bash` 구현:
```bash
sed -i.bak "s/FastPortGameServerTemplate/${NEW_NAME}/g" "$f"
rm -f "$f.bak"
```
- BSD sed (macOS): `-i ''` 필요. GNU sed: `-i` 단독 OK.
- 양쪽에서 동작하는 호환 패턴: `-i.bak` (extension 명시) → 결과 .bak 파일 삭제. macOS 12+, Ubuntu 22.04+, Alpine 모두 OK.
- 본문에 regex meta는 검증 단계에서 차단되므로 escape 불필요 (sed 입력 토큰 = `FastPortGameServerTemplate`만, 출력 = 사용자 지정 PascalCase ASCII).

`PowerShell` 구현:
```powershell
$content = [System.IO.File]::ReadAllText($filePath)
$content = $content -replace 'FastPortGameServerTemplate', $NewName
[System.IO.File]::WriteAllText($filePath, $content, [System.Text.UTF8Encoding]::new($false))
```
- `[System.Text.UTF8Encoding]::new($false)` → BOM 없음 강제.
- LF 라인 엔딩은 source 파일이 이미 LF이고 PS가 라인 엔딩 변환 없이 read/write → preserve.

### 2.3 Dependencies (런타임)

| Dependency | Version | Required by |
|------------|---------|-------------|
| `bash` | 3.2+ (macOS LTS) | `.sh` |
| POSIX `sed` (BSD or GNU) | any | `.sh` |
| POSIX `cp`, `mv`, `find`, `grep`, `mkdir` | any | `.sh` |
| `git` | 2.0+ | optional (`--no-git`로 건너뜀) |
| `dotnet` | 10.0+ | optional (`--skip-smoke`로 건너뜀, sln add 시 필요) |
| PowerShell | 7.0+ | `.ps1` |
| `git` | 2.0+ | PS, optional |
| `dotnet` | 10.0+ | PS, optional |

> 본 레포 빌드 도구 외 신규 의존 0.

---

## 3. Data Model

### 3.1 CLI Schema

```
scaffold-game-server.sh|.ps1 <NewProjectName> <DestinationPath> [OPTIONS]

Positional:
  NewProjectName     C# identifier prefix, 1-64 chars, ^[A-Z][A-Za-z0-9]{0,63}$
                     not in tests/scaffold/_shared/blocked-tokens.txt
  DestinationPath    Absolute or relative target directory.
                     If exists and not empty: refuse unless --force.

Options:
  --force            Overwrite existing destination (irreversibly).
  --no-git           Skip 'git init' + initial commit.
  --dry-run          Print planned actions; no filesystem changes.
  --skip-smoke       Skip 'dotnet build' verification.
  -h, --help         Print usage.

Exit codes:
  0  success
  2  input validation failed (bad name / blocked / bad path)
  3  destination conflict (use --force)
  4  smoke build failed
  5  IO / git / dotnet error
```

### 3.2 Shared Spec Files

`tests/scaffold/_shared/blocked-tokens.txt`:

```
# Lines starting with # are comments.
# Each non-empty, non-comment line is a single blocked token (case-sensitive).
Application
Configuration
Handlers
Sessions
Telemetry
Protocols
LibCommons
LibNetworks
FastPortServer
FastPortClient
FastPortGameServerTemplate
```

`tests/scaffold/_shared/name-validation.txt` (self-check):

```
# Each line: STATUS<TAB>NAME<TAB>EXPECTED_REASON
# STATUS = OK | REJECT_REGEX | REJECT_BLOCKED
OK	MyLobbyServer	matches regex, not blocked
OK	A	1 char minimum
OK	Game1	digits allowed after first char
REJECT_REGEX		empty
REJECT_REGEX	myGame	must start with uppercase
REJECT_REGEX	1Game	must start with letter
REJECT_REGEX	My-Game	hyphen not allowed
REJECT_REGEX	My$Game	special char rejected
REJECT_REGEX	한글이름	non-ASCII rejected
REJECT_BLOCKED	Application	in blocked list
REJECT_BLOCKED	LibCommons	in blocked list
REJECT_BLOCKED	FastPortServer	in blocked list
```

### 3.3 Generated `<dest>/<NewName>.sln`

- Format: VS solution file format 12.00.
- Generated via `dotnet new sln -n <NewName>` then `dotnet sln add <NewName>/<NewName>.csproj`, `dotnet sln add LibCommons/LibCommons.csproj`, `dotnet sln add LibNetworks/LibNetworks.csproj`.
- Cross-platform: dotnet CLI handles GUID generation deterministically per project path (timestamp 외 byte-identical 측면에서 확인 필요 → §8 test plan).

### 3.4 Generated `<dest>/.gitignore`

본 레포 `.gitignore` 사본 + scaffold-specific 줄 추가:
```
# Created by scaffold-game-server (FastPortSharp template)
bin/
obj/
.vs/
*.log
.DS_Store
**/.DS_Store

# IDE / OS
*.user
*.suo
```

### 3.5 Generated `<dest>/.gitattributes`

```
* text=auto eol=lf

*.csproj eol=lf
*.cs     eol=lf
*.proto  eol=lf
*.sln    eol=crlf
*.sh     text eol=lf
*.ps1    text eol=lf

*.png    binary
*.jpg    binary
```
> `.sln`은 VS 호환을 위해 CRLF 유지 (`dotnet new sln`이 CRLF로 생성).

### 3.6 Generated `<dest>/README.md`

```markdown
# <NewName>

A game server scaffolded from the FastPortSharp template
(https://github.com/boinred/FastPortSharp).

## Build & Run

```bash
dotnet build <NewName>.sln -c Release
dotnet run --project <NewName> -c Release
```

The server listens on `0.0.0.0:7777` by default. Edit
`<NewName>/appsettings.json` to change.

## Layout

- `<NewName>/`    — your game server (start here)
- `LibCommons/`   — engine: buffers, packet primitives (read-only baseline)
- `LibNetworks/`  — engine: TCP listener / session (read-only baseline)

## Adding packets

See `<NewName>/README.md` and `<NewName>/QUICKSTART.ko.md` for the
template's packet/handler customisation guide.

## License

MIT (inherits from the upstream FastPortSharp template).
```

---

## 4. API Specification

본 feature는 HTTP API가 아닌 **CLI contract**가 대상.

### 4.1 Entry Point Contract

| Aspect | Bash | PowerShell |
|--------|------|------------|
| File | `scripts/scaffold-game-server.sh` | `scripts/scaffold-game-server.ps1` |
| Shebang / declaration | `#!/usr/bin/env bash` + `set -euo pipefail` | `#Requires -Version 7.0` + `Set-StrictMode -Version Latest` + `$ErrorActionPreference = 'Stop'` |
| Args | positional 2 + flags | positional 2 + `[switch]` params |
| stdout (success) | step-by-step log + "Done." | identical |
| stdout (--dry-run) | `[DRY-RUN]` prefix per planned action | identical |
| stderr (failure) | `error: <message>\nhint: <fix>` | identical |
| Exit code | 0/2/3/4/5 | 0/2/3/4/5 (PS `exit` statement) |

### 4.2 Internal Steps (양쪽 동일 12-step 시퀀스)

| # | Step | Inputs | Outputs |
|---|------|--------|---------|
| 1 | parse args | argv | NEW_NAME, DEST, FORCE, NO_GIT, DRY_RUN, SKIP_SMOKE |
| 2 | validate name | NEW_NAME + blocked-tokens.txt | OK / exit 2 |
| 3 | validate dest | DEST, FORCE | OK / exit 3 |
| 4 | (dry-run) plan + exit | (state) | stdout + exit 0 |
| 5 | copy template | repo root → DEST | files |
| 6 | copy LibCommons | repo root → DEST | files |
| 7 | copy LibNetworks | repo root → DEST | files |
| 8 | token replacement | DEST tree | renamed/replaced |
| 9 | generate aux files | DEST | .gitignore, .gitattributes, README.md |
| 10 | generate sln | DEST | NewName.sln |
| 11 | git init (cond) | DEST | .git/ + initial commit |
| 12 | smoke build (cond) | DEST/NewName.sln | dotnet build pass |

---

## 5. UI/UX Design

CLI 도구 — UI 없음. 대신 **stdout/stderr UX 명세**:

### 5.1 Success Path Output

```
$ ./scripts/scaffold-game-server.sh MyLobbyServer ../my-lobby

[1/12] Parsing arguments...                      OK (NewName=MyLobbyServer, Dest=../my-lobby)
[2/12] Validating project name...                OK
[3/12] Resolving destination...                  OK (created: ../my-lobby)
[5/12] Copying FastPortGameServerTemplate...     OK (32 files)
[6/12] Copying LibCommons...                     OK (12 files)
[7/12] Copying LibNetworks...                    OK (18 files)
[8/12] Replacing tokens...                       OK (47 files touched)
[9/12] Generating .gitignore, .gitattributes, README.md... OK
[10/12] Creating MyLobbyServer.sln...            OK
[11/12] git init + initial commit...             OK (commit 8d4a2f1)
[12/12] dotnet build smoke...                    OK (0 warnings, 0 errors, 14.2s)

Done. wall-clock: 18.7s

Next steps:
  cd ../my-lobby
  dotnet run --project MyLobbyServer -c Release
```

### 5.2 Validation Failure Output

```
$ ./scripts/scaffold-game-server.sh "myGame" ../somewhere

[1/12] Parsing arguments...                      OK
[2/12] Validating project name...                FAIL

error: name "myGame" does not match required pattern.
hint:  must match ^[A-Z][A-Za-z0-9]{0,63}$ (PascalCase, ASCII, 1-64 chars,
       starts with uppercase letter)
exit 2
```

### 5.3 Blocked Token Output

```
$ ./scripts/scaffold-game-server.sh Application ../somewhere

[2/12] Validating project name...                FAIL

error: name "Application" is in the blocked tokens list.
hint:  this name conflicts with an internal folder/namespace token.
       see tests/scaffold/_shared/blocked-tokens.txt for the full list.
exit 2
```

### 5.4 Destination Conflict Output

```
$ ./scripts/scaffold-game-server.sh Foo ../existing-dir

[3/12] Resolving destination...                  FAIL

error: destination "../existing-dir" already exists and is not empty.
hint:  use --force to overwrite (irreversible), or pick a different path.
exit 3
```

### 5.5 Smoke Build Failure

```
[12/12] dotnet build smoke...                    FAIL

error: 'dotnet build ../my-lobby/MyLobbyServer.sln -c Release' failed.
hint:  this usually means a token was missed during replacement.
       run with --dry-run to inspect, or file an issue.
exit 4

(scaffold succeeded but build failed; destination is left in place
 for inspection — delete manually if you want to retry)
```

---

## 6. Error Handling

### 6.1 Error Code → Message → Action

| Exit | Cause | Message | Recovery |
|------|-------|---------|----------|
| 0 | success | "Done." | — |
| 2 | name regex fail | "name does not match pattern" | use ^[A-Z][A-Za-z0-9]{0,63}$ |
| 2 | name blocked | "name is in blocked list" | pick another name |
| 2 | bad dest path | "destination path invalid" | check spelling |
| 3 | dest exists | "destination not empty" | --force or pick another |
| 4 | smoke build fail | "dotnet build failed" | inspect destination, file issue |
| 5 | git init fail | "git init failed" | check git installed, file an issue |
| 5 | dotnet sln add fail | "dotnet sln add failed" | check dotnet installed |
| 5 | copy / rename fail | "filesystem error" | check permissions |

### 6.2 Cleanup on Failure

- Step 1-4 실패 시: filesystem 변경 0, cleanup 불필요.
- Step 5 이상 실패 시: 결과물은 in-place 보존 (debug 가능). README/help에 명시.
- `--dry-run` 실패: 일반 실패와 동일 exit code, filesystem 변경 0.

### 6.3 Idempotency

- 동일 인자 재실행 시 dest 충돌로 exit 3 (default) — `--force`만 덮어쓰기.
- `--force` 사용 시 dest를 통째로 삭제 후 fresh scaffold (merge 아님 — 단순화).

---

## 7. Security Considerations

본 cycle 범위에서:

- [ ] **인자 인젝션**: `NEW_NAME`은 정규식 `^[A-Z][A-Za-z0-9]{0,63}$`로 제한 → shell metacharacter 침투 불가능. `DEST`는 사용자 입력 문자열 — bash에서는 항상 `"${DEST}"` 큰따옴표 quoting, PS에서는 named param.
- [ ] **경로 traversal**: dest가 `../../etc` 같은 형태여도 사용자가 자기 시스템에 권한 있는 경로만 접근 가능 (sudo 안 씀). 별도 검증 없음.
- [ ] **secret 누출**: 결과물에 secret 0. `appsettings.json`은 listen port/log level만.
- [ ] **shell command injection**: `dotnet`/`git` 호출은 fixed string + quoted user args만. eval/`$(...)` 미사용.
- [ ] **sed 인젝션**: 치환 토큰은 검증 통과한 ASCII PascalCase. `s/X/Y/g` 의 Y에 `&`/`/` 같은 sed metachar는 정의상 들어올 수 없음.

---

## 8. Test Plan

### 8.1 Test Scope

| Type | 적용 형태 | Tool | Phase |
|------|-----------|------|-------|
| L1 (정적) | 셸/PS 스크립트 lint | shellcheck / Invoke-ScriptAnalyzer (best effort) | Check |
| L2 (단위 동작) | 입력 검증 / 토큰 치환 / 옵션 처리 | Golden-file diff (shell-based runner) | Check |
| L3 (시나리오) | 끝에서 끝 scaffold + dotnet build | runner가 직접 실행 | Check |
| L4 (parity) | 3환경 byte-identical | GitHub Actions matrix + sha256sum | Check |

### 8.2 L1: Lint

| # | 검증 | 명령 | 기대 |
|---|------|------|------|
| 1 | bash script lint | `shellcheck scripts/scaffold-game-server.sh tests/scaffold/run.sh` | 0 issue (또는 의도적 disable 주석) |
| 2 | ps1 script lint | `Invoke-ScriptAnalyzer scripts/scaffold-game-server.ps1` | 0 error |

### 8.3 L2: Golden-file Cases

각 case는 `tests/scaffold/<case>/{input,expected}/`. runner가 input으로 scaffold 실행 후 결과 트리를 expected와 SHA256 단위로 비교.

| Case | 입력 | 기대 |
|------|------|------|
| **case-01-simple** | `MyLobbyServer ./out --no-git --skip-smoke` | exit 0, 트리 일치, sha256 일치 |
| **case-02-blocked-name** | `Application ./out` | exit 2, stderr 에 "blocked tokens list" |
| **case-03-regex-meta** | `My$Game ./out` | exit 2, stderr 에 "does not match" |
| **case-04-existing-dest-no-force** | `Foo ./prepopulated` (prepopulated/test.txt 존재) | exit 3 |
| **case-05-existing-dest-with-force** | `Foo ./prepopulated --force --no-git --skip-smoke` | exit 0, dest 비워졌다 fresh scaffold |
| **case-06-dry-run** | `Foo ./out --dry-run` | exit 0, dest 생성 0, stdout 에 [DRY-RUN] |
| **case-07-no-git-no-smoke** | `Foo ./out --no-git --skip-smoke` | exit 0, .git 없음, smoke 스텝 건너뜀 |

> case-01의 expected 트리는 양쪽 스크립트가 동일하게 생성하는 상태를 기록 — 이게 곧 "byte-identical parity" 게이트. timestamp 포함 항목 (`.git/`, sln의 GUID는 dotnet new sln의 결정성에 따름)은 expected 비교에서 제외.

### 8.4 L3: End-to-end Smoke

| # | 시나리오 | 단계 | 기대 |
|---|----------|------|------|
| 1 | dogfood: 새 toy 프로젝트 부트스트랩 | `./scripts/scaffold-game-server.sh MyLobbyServer ../mygame-test` | wall-clock ≤ 60초, smoke build PASS |
| 2 | 결과물 echo 동작 | `cd ../mygame-test && dotnet run --project MyLobbyServer -c Release` (별도 터미널: SampleClient 또는 nc) | server listen + accept 정상 |
| 3 | 결과물 git 상태 | `cd ../mygame-test && git log --oneline` | 1 commit ("Initial scaffold from FastPortGameServerTemplate") |

### 8.5 L4: Cross-platform Parity

GitHub Actions matrix (`ubuntu-latest`, `macos-latest`, `windows-latest`-PS7) 에서:

```yaml
- run: ./scripts/scaffold-game-server.sh   FooBar ./out --no-git --skip-smoke   # ubuntu/macos
- run: ./scripts/scaffold-game-server.ps1  FooBar ./out --no-git --skip-smoke   # windows
- run: find ./out -type f -print0 | sort -z | xargs -0 sha256sum > ./out.sha256
- name: Upload sha256
  uses: actions/upload-artifact@v4
  with: { name: out-${{ matrix.os }}, path: ./out.sha256 }
- name: Compare across OSes (final job)
  run: diff out-ubuntu/out.sha256 out-macos/out.sha256 && diff out-ubuntu/out.sha256 out-windows/out.sha256
```

(Windows의 `find`/`sort`/`sha256sum`은 GitHub-hosted Windows runner에 git bash 통해 사용 가능. 또는 `Get-ChildItem | Get-FileHash`로 동등 표현.)

### 8.6 Seed Data

해당 없음 — 모든 테스트 입력은 `tests/scaffold/<case>/input/` 폴더에 정적으로 포함.

---

## 9. Clean Architecture (.NET 적용)

본 cycle은 .NET 코드 변경 0 — 셸/PS 스크립트만. Clean Architecture 적용 X.

대신 **셸 스크립트 컨벤션**을 명시:

### 9.1 Bash Script Convention

| Aspect | Rule |
|--------|------|
| Shebang | `#!/usr/bin/env bash` |
| Strict mode | `set -euo pipefail` 첫 줄에 |
| Functions | 모든 reusable 로직은 함수화. main 흐름은 함수 호출만 |
| Variables | `local` 명시, ALLCAPS = 상수, snake_case = 지역 변수 |
| Quoting | 모든 변수 expansion은 큰따옴표 (`"$var"`) |
| Error path | `>&2` 로 출력, exit code 명시 |
| bash 3.2 회피 | `mapfile`, `readarray`, `[[ -v var ]]`, associative array |

### 9.2 PowerShell Script Convention

| Aspect | Rule |
|--------|------|
| Version | `#Requires -Version 7.0` |
| Strict mode | `Set-StrictMode -Version Latest` + `$ErrorActionPreference = 'Stop'` |
| Functions | advanced functions (`[CmdletBinding()]`, `param([Parameter(...)])`) |
| Variables | PascalCase for params, camelCase for locals |
| Encoding | 모든 파일 쓰기는 `[System.IO.File]::WriteAllText(path, text, [System.Text.UTF8Encoding]::new($false))` |
| Path | `Join-Path` 사용, 직접 문자열 concat 회피 |

---

## 10. Coding Convention Reference

### 10.1 File Structure

| 영역 | 위치 |
|------|------|
| 양쪽 스크립트 | `scripts/scaffold-game-server.{sh,ps1}` |
| 스크립트 README | `scripts/README.md` |
| Shared spec | `tests/scaffold/_shared/{blocked-tokens.txt,name-validation.txt}` |
| Test runner | `tests/scaffold/run.{sh,ps1}` |
| Test cases | `tests/scaffold/case-NN-<slug>/{input,expected,README.md}/` |
| CI workflow | `.github/workflows/scaffold.yml` |
| LF 강제 | `.gitattributes` (root, NEW or extended) |
| Repo docs 갱신 | `README.md`, `README.ko.md`, `HANDOFF.md` |

### 10.2 Naming Conventions

| Target | Bash | PowerShell |
|--------|------|------------|
| Script file | kebab-case + `.sh` | kebab-case + `.ps1` |
| Function | snake_case | PascalCase (Verb-Noun if cmdlet-style) |
| Constant | ALLCAPS | PascalCase |
| Variable | snake_case | camelCase |

### 10.3 Logging

- 진행 로그: `[N/12] <step>...` + `OK | FAIL` suffix
- Error: `error: <description>` to stderr
- Hint: `hint: <recovery action>` to stderr
- Dry-run prefix: `[DRY-RUN]` to stdout

---

## 11. Implementation Guide

### 11.1 File Structure (구체)

```
FastPortSharp/
├── scripts/
│   ├── scaffold-game-server.sh                    ← NEW (~250 lines)
│   ├── scaffold-game-server.ps1                   ← NEW (~250 lines)
│   └── README.md                                  ← NEW
│
├── tests/scaffold/
│   ├── _shared/
│   │   ├── blocked-tokens.txt                     ← NEW (single source of truth)
│   │   └── name-validation.txt                    ← NEW
│   ├── run.sh                                     ← NEW (golden-file diff runner)
│   ├── run.ps1                                    ← NEW (PS runner)
│   ├── case-01-simple/                            ← NEW
│   ├── case-02-blocked-name/                      ← NEW
│   ├── case-03-regex-meta/                        ← NEW
│   ├── case-04-existing-dest-no-force/            ← NEW
│   ├── case-05-existing-dest-with-force/          ← NEW
│   ├── case-06-dry-run/                           ← NEW
│   └── case-07-no-git-no-smoke/                   ← NEW
│
├── .github/workflows/
│   └── scaffold.yml                               ← NEW (3-env matrix)
│
├── .gitattributes                                 ← NEW (LF 강제)
├── README.md                                      ← MODIFY (한 줄 사용 예시)
├── README.ko.md                                   ← MODIFY (동일 한국어)
└── HANDOFF.md                                     ← MODIFY (Architecture Decision + Roadmap)
```

### 11.2 Implementation Order

| 순서 | 작업 | 산출물 | 검증 |
|------|------|--------|------|
| 1 | `tests/scaffold/_shared/blocked-tokens.txt` + `name-validation.txt` 작성 | 2 파일 | 양쪽 스크립트가 읽을 SoT 확보 |
| 2 | `scripts/scaffold-game-server.sh` 작성 (12-step) | 1 파일 | 단독 실행으로 case-01 통과 |
| 3 | `scripts/scaffold-game-server.ps1` 작성 (12-step) | 1 파일 | 단독 실행으로 case-01 통과 |
| 4 | `tests/scaffold/case-01-simple` ~ `case-07-no-git-no-smoke` 7개 케이스 input/expected | 14 폴더 | 각 case input 으로 스크립트 실행 시 expected 일치 |
| 5 | `tests/scaffold/run.sh` + `run.ps1` (golden-file diff runner) | 2 파일 | 양쪽 runner가 7 case 모두 PASS 보고 |
| 6 | `.gitattributes` 작성 (LF + UTF-8) | 1 파일 | git 자동 변환 동작 |
| 7 | `.github/workflows/scaffold.yml` 작성 (3환경 matrix) | 1 파일 | CI 통과 (sha256 byte-identical) |
| 8 | `scripts/README.md` 작성 | 1 파일 | 사용법 / 옵션 / 트러블슈팅 |
| 9 | repo `README.md` + `README.ko.md` Game Server Template 섹션에 한 줄 예시 추가 | 2 modify | scaffold 명령 노출 |
| 10 | `HANDOFF.md` Architecture Decision + Roadmap §3 후속 cycle 명시 | 1 modify | 문서 일관성 |
| 11 | dogfood: 본인이 새 toy 프로젝트 1개 부트스트랩 (`MyLobbyServer ../mygame-test --skip-smoke`)로 60초 측정 | (측정 노트) | wall-clock ≤ 60s |
| 12 | 본 레포 회귀 검증 (`dotnet build/test FastPortSharp.sln`) | (CI) | 0 warning / 139 tests pass |

### 11.3 Session Guide

> Auto-generated from §11.2. `--scope` 키로 세션을 분할.

#### Module Map

| Module | Scope Key | Description | Estimated Turns |
|--------|-----------|-------------|:---------------:|
| Shared spec | `tests-shared` | blocked-tokens.txt + name-validation.txt | 6-10 |
| Bash 스크립트 | `scripts-bash` | scaffold-game-server.sh 작성 (12-step) | 30-40 |
| PowerShell 스크립트 | `scripts-ps1` | scaffold-game-server.ps1 작성 (12-step) | 30-40 |
| 테스트 케이스 데이터 | `tests-cases` | 7 case의 input/expected 폴더 | 25-35 |
| 테스트 러너 | `tests-runners` | run.sh + run.ps1 + 7 case 통과 검증 | 20-30 |
| .gitattributes + .github CI | `ci-gitattributes` | LF 강제 + 3환경 matrix | 12-18 |
| Docs | `docs-update` | scripts/README.md + repo README/HANDOFF | 12-18 |
| Dogfood + 본 레포 회귀 | `dogfood-and-regression` | 실제 scaffold + dotnet build/test | 8-12 |

#### Recommended Session Plan

| Session | Phase | Scope | Turns |
|---------|-------|-------|:-----:|
| 1 | Plan + Design | 전체 | 30-35 |
| 2 | Do | `--scope tests-shared,scripts-bash` | 35-50 |
| 3 | Do | `--scope scripts-ps1` | 30-40 |
| 4 | Do | `--scope tests-cases,tests-runners` | 45-65 |
| 5 | Do | `--scope ci-gitattributes,docs-update` | 25-35 |
| 6 | Do | `--scope dogfood-and-regression` | 8-12 |
| 7 | Check + Report | 전체 | 25-35 |

> 최소 모드: Session 2-6를 한 세션에 묶으면 turn 합 ≈ 145-200. 본인 dogfood이라 사용자 재량.

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-09 | Initial draft (Option C — Pragmatic Balance, shared blocked-tokens spec, 12-step flow, 7 golden-file cases, 3-env CI matrix) | boinred |
