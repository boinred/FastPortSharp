# template-contracts-scaffold-fix Design Document

> **Summary**: scaffold-game-server.{sh,ps1} 에 Contracts copy / token replace / sln 등록 로직을 Option C(Pragmatic — 별도 `copy_contracts()` 함수 + 기존 패턴 답습)로 추가하고, 영향 받는 4개 case fixture (case-01 auto, case-05/07 manual) 를 재생성.
>
> **Project**: FastPortSharp
> **Version**: 1.0
> **Author**: das_young
> **Date**: 2026-05-12
> **Status**: Draft
> **Planning Doc**: [template-contracts-scaffold-fix.plan.md](../../01-plan/features/template-contracts-scaffold-fix.plan.md)

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 직전 cycle이 Contracts lib을 도입하면서 scaffold 측을 업데이트하지 않아 scaffold 출력이 컴파일 불가 상태. CI scaffold workflow도 PR push 시 실패 예정. |
| **WHO** | 외부 게임 사용자(scaffold-game-server 실행자), scaffold workflow CI, repo 유지 개발자. |
| **RISK** | sh/ps1 byte-divergence, token replace 의 LibCommons 침투, step counter 변경, fixture regen 누락. |
| **SUCCESS** | scaffold 출력 빌드 가능, 모든 case PASS, sh/ps1 동일 sha256, fixture 4개 갱신 완료. |
| **SCOPE** | sh 1, ps1 1, fixture 4 case (auto+manual), README 1. |

---

## 1. Overview

### 1.1 Design Goals

- **Backward compat**: 기존 CLI/옵션/step counter 그대로
- **Cross-OS parity**: sh/ps1 출력 byte-identical 유지
- **최소 침습**: 기존 `copy_template / copy_libcommons / copy_libnetworks` 패턴 답습
- **fixture 자동화 활용**: case-01 sha256/tree 는 `run.sh --update-golden` 위임

### 1.2 Design Principles

- 새 sub-project 추가는 "기존 패턴의 4번째 인스턴스" 로 처리 — refactor 회피
- token replacement 의 root는 sub-project 마다 별도 `find` (스코프 격리)
- step counter `[1/12]` ~ `[12/12]` 보존 (fixture stdout-contains 비손상)

---

## 2. Architecture Options

### 2.0 Comparison

| Criteria | Option A: Minimal | Option B: Clean | Option C: Pragmatic |
|----------|:-:|:-:|:-:|
| Approach | inline 추가 | array-based refactor | 별도 함수 + 패턴 답습 |
| Lines (sh) | +20 | +80 | +35 |
| Lines (ps1) | +25 | +90 | +40 |
| 기존 코드 변경 | 1 함수 | 전체 copy/replace 로직 | 2 함수 확장 |
| byte-identical 위험 | Low | Medium | Low |
| 유지보수성 | 중 | 상 | 상 |
| **Selected** | | | ✅ |

**Selected: Option C — Pragmatic**.

### 2.1 Component Diagram

```
scaffold-game-server.{sh,ps1}
├── Variables
│   ├── TEMPLATE_TOKEN     ("FastPortGameServerTemplate")
│   ├── TEMPLATE_SRC       (template-projects/<TEMPLATE_TOKEN>)
│   ├── CONTRACTS_SRC      ← NEW (template-projects/<TEMPLATE_TOKEN>.Contracts)
│   ├── LIBCOMMONS_SRC
│   └── LIBNETWORKS_SRC
│
├── [5/12] copy
│   ├── copy_template()              <dest>/<TEMPLATE_TOKEN>
│   ├── copy_contracts()        NEW  <dest>/<TEMPLATE_TOKEN>.Contracts
│   ├── copy_libcommons()
│   └── copy_libnetworks()
│
├── [8/12] replace_tokens()
│   ├── iterate over Template subtree    ← 기존
│   ├── iterate over Contracts subtree   ← NEW
│   ├── rename Template dir → <NEW_NAME>
│   ├── rename Contracts dir → <NEW_NAME>.Contracts    ← NEW
│   ├── rename Template csproj → <NEW_NAME>.csproj
│   └── rename Contracts csproj → <NEW_NAME>.Contracts.csproj    ← NEW
│
└── [10/12] generate_sln()
    ├── dotnet new sln
    ├── sln add <NEW_NAME>/<NEW_NAME>.csproj
    ├── sln add <NEW_NAME>.Contracts/<NEW_NAME>.Contracts.csproj    ← NEW
    ├── sln add LibCommons/LibCommons.csproj
    └── sln add LibNetworks/LibNetworks.csproj
```

### 2.2 Dry-Run Output Diff

기존:
```
[DRY-RUN]   <TEMPLATE_SRC>    -> <DEST>/<NEW_NAME>
[DRY-RUN]   <LIBCOMMONS_SRC>  -> <DEST>/LibCommons
[DRY-RUN]   <LIBNETWORKS_SRC> -> <DEST>/LibNetworks
[DRY-RUN]   <DEST>/<NEW_NAME>.sln (3 projects)
```

신규:
```
[DRY-RUN]   <TEMPLATE_SRC>    -> <DEST>/<NEW_NAME>
[DRY-RUN]   <CONTRACTS_SRC>   -> <DEST>/<NEW_NAME>.Contracts
[DRY-RUN]   <LIBCOMMONS_SRC>  -> <DEST>/LibCommons
[DRY-RUN]   <LIBNETWORKS_SRC> -> <DEST>/LibNetworks
[DRY-RUN]   <DEST>/<NEW_NAME>.sln (4 projects)
```

---

## 3. Data Model

해당 없음 — script + fixture refactor.

---

## 4. API Specification

해당 없음.

---

## 5. UI/UX Design

해당 없음.

---

## 6. Error Handling

scaffold 의 기존 error path 유지:
- `dotnet sln add` 실패 시 `set -euo pipefail` 으로 즉시 abort (sh)
- PowerShell `$ErrorActionPreference = 'Stop'` 으로 동일
- 추가 error case: Contracts dir 부재 시 명확 메시지 — 단, 본 repo 에서 `template-projects/FastPortGameServerTemplate.Contracts/` 는 직전 cycle commit 으로 보장됨

---

## 7. Security Considerations

- token replacement 가 LibCommons/LibNetworks subtree 로 새지 않도록 `find <subtree>` 한정 유지
- `<TEMPLATE_TOKEN>` 은 26자 unique compound (HANDOFF.md:282) — collateral substitution 차단 보장

---

## 8. Test Plan

### 8.1 Test Scope

| Type | Target | Tool | Phase |
|------|--------|------|-------|
| L1: golden-file | case-01~07 expected vs actual | `bash tests/scaffold/run.sh` | Do M5 |
| L1: smoke build | scaffold output `dotnet build` | `dotnet build` | Do M6 |
| L1: cross-flavor | sh vs ps1 byte-identical | `run.sh --script ps1` if available | Do M7 |

### 8.2 Detailed Scenarios

| # | 검증 | 명령 | 통과 기준 |
|---|------|------|----------|
| T1 | M1 sh 단독 dry-run | `bash scripts/scaffold-game-server.sh Foo /tmp/dry --dry-run` | exit 0, Contracts line + "(4 projects)" 출력 |
| T2 | M1 sh 실제 scaffold + build | `bash scripts/scaffold-game-server.sh Foo /tmp/foo --no-git` (smoke 활성) | exit 0, `dotnet build` 성공 |
| T3 | M2 ps1 동등 (가능 시) | `pwsh scripts/scaffold-game-server.ps1 -NewProjectName Foo -DestinationPath /tmp/foo-ps -NoGit` | 동일 sha256 |
| T4 | M3 case-01 golden 재생성 | `bash tests/scaffold/run.sh --update-golden case-01-simple` | sha256.txt + tree.txt 갱신, run.sh 실행 시 PASS |
| T5 | M5 전체 case | `bash tests/scaffold/run.sh` | 7 case 모두 PASS |
| T6 | LibCommons token 침투 검증 | grep `<NEW_NAME>` in `<DEST>/LibCommons/` | 0 matches |
| T7 | step counter 보존 | grep `\[1/12\]\|\[12/12\]` in actual stdout | match |
| T8 | dry-run smoke check | `bash tests/scaffold/run.sh case-06-dry-run` | PASS (기존 fixture 그대로 통과해야 함) |

### 8.3 Seed Data

case-01 input/args.txt: `MyLobbyServer`, `{DEST}`, `--no-git`, `--skip-smoke` (기존 그대로)

---

## 9. Clean Architecture

### 9.1 Layer Structure

| Layer | Responsibility | Location |
|-------|---------------|----------|
| Tool | scaffold orchestration | `scripts/scaffold-game-server.{sh,ps1}` |
| Source | template projects | `template-projects/FastPortGameServerTemplate*/` |
| Golden Fixture | expected output | `tests/scaffold/case-*/expected/` |
| Test Harness | golden diff runner | `tests/scaffold/run.{sh,ps1}` |

### 9.2 File Import Rules

| From | Reads | Writes |
|------|-------|--------|
| scaffold script | `template-projects/`, NuGet packages | `<DEST>/` (destination only) |
| run.sh | `tests/scaffold/case-*/`, scaffold output tmpdir | (test-time only) |
| Fixture | (none) | (none — read-only assertions) |

---

## 10. Coding Convention Reference

### 10.1 sh/ps1 1:1 Mapping Convention

| Concept | bash | PowerShell |
|---------|------|-----------|
| Variable | `readonly NAME="value"` | `$Script:Name = 'value'` |
| Path join | `"${ROOT}/${SUBDIR}"` | `Join-Path $Script:Root $Subdir` |
| Function | `func_name() { ... }` | `function Func-Name { ... }` |
| Logging | `log "msg"` | `Write-Log "msg"` |
| Subtree iterate | `find <root> -type f ...` | `Get-ChildItem -Recurse -Path $Root ...` |

### 10.2 New Functions Naming

| sh | ps1 |
|----|-----|
| `copy_contracts()` | `Copy-Contracts` (or inline in step 5 block) |

### 10.3 Code Comment Convention

각 새 라인에:
```bash
# Design Ref: template-contracts-scaffold-fix §2.1 — Contracts sub-project added.
```

---

## 11. Implementation Guide

### 11.1 sh — Exact Diff Locations

**scripts/scaffold-game-server.sh**:

```diff
@@ line 48-51 (constants) @@
 readonly TEMPLATE_SRC="${REPO_ROOT}/template-projects/${TEMPLATE_TOKEN}"
+readonly CONTRACTS_SRC="${REPO_ROOT}/template-projects/${TEMPLATE_TOKEN}.Contracts"
 readonly LIBCOMMONS_SRC="${REPO_ROOT}/LibCommons"
 readonly LIBNETWORKS_SRC="${REPO_ROOT}/LibNetworks"

@@ line ~240-249 (dry-run output) @@
 log "[DRY-RUN]   ${TEMPLATE_SRC}    -> ${DEST_PATH}/${NEW_NAME}"
+log "[DRY-RUN]   ${CONTRACTS_SRC}   -> ${DEST_PATH}/${NEW_NAME}.Contracts"
 log "[DRY-RUN]   ${LIBCOMMONS_SRC}  -> ${DEST_PATH}/LibCommons"
 log "[DRY-RUN]   ${LIBNETWORKS_SRC} -> ${DEST_PATH}/LibNetworks"
 ...
-log "[DRY-RUN]   ${DEST_PATH}/${NEW_NAME}.sln (3 projects)"
+log "[DRY-RUN]   ${DEST_PATH}/${NEW_NAME}.sln (4 projects)"

@@ line ~258-268 (copy functions) @@
 copy_template() {
   copy_tree "${TEMPLATE_SRC}"   "${DEST_PATH}/${TEMPLATE_TOKEN}"
 }
+copy_contracts() {
+  # Design Ref: template-contracts-scaffold-fix §2.1 — Contracts sub-project.
+  copy_tree "${CONTRACTS_SRC}"  "${DEST_PATH}/${TEMPLATE_TOKEN}.Contracts"
+}
 copy_libcommons() {
   copy_tree "${LIBCOMMONS_SRC}"  "${DEST_PATH}/LibCommons"
 }

@@ line ~290-320 (replace_tokens) @@
 replace_tokens() {
-  local subtree="${DEST_PATH}/${TEMPLATE_TOKEN}"
+  # Design Ref: §2.1 — both Template and Contracts subtrees need token replace.
+  local subtrees=("${DEST_PATH}/${TEMPLATE_TOKEN}" "${DEST_PATH}/${TEMPLATE_TOKEN}.Contracts")
   local find_expr
   find_expr="$(build_text_find_args)"

   local count=0
   local file
-  while IFS= read -r file; do
-    [ -f "${file}" ] || continue
-    if grep -F -q -- "${TEMPLATE_TOKEN}" "${file}" 2>/dev/null; then
-      replace_in_file "${file}" "${TEMPLATE_TOKEN}" "${NEW_NAME}"
-      count=$((count + 1))
-    fi
-  done <<EOF
-$(eval "find \"${subtree}\" -type f ${find_expr}")
-EOF
+  local subtree
+  for subtree in "${subtrees[@]}"; do
+    while IFS= read -r file; do
+      [ -f "${file}" ] || continue
+      if grep -F -q -- "${TEMPLATE_TOKEN}" "${file}" 2>/dev/null; then
+        replace_in_file "${file}" "${TEMPLATE_TOKEN}" "${NEW_NAME}"
+        count=$((count + 1))
+      fi
+    done <<EOF
+$(eval "find \"${subtree}\" -type f ${find_expr}")
+EOF
+  done

-  mv "${DEST_PATH}/${TEMPLATE_TOKEN}" "${DEST_PATH}/${NEW_NAME}"
-  mv "${DEST_PATH}/${NEW_NAME}/${TEMPLATE_TOKEN}.csproj" \
-     "${DEST_PATH}/${NEW_NAME}/${NEW_NAME}.csproj"
+  # Rename Template subtree + csproj.
+  mv "${DEST_PATH}/${TEMPLATE_TOKEN}" "${DEST_PATH}/${NEW_NAME}"
+  mv "${DEST_PATH}/${NEW_NAME}/${TEMPLATE_TOKEN}.csproj" \
+     "${DEST_PATH}/${NEW_NAME}/${NEW_NAME}.csproj"
+
+  # Rename Contracts subtree + csproj (Design Ref: §2.1).
+  mv "${DEST_PATH}/${TEMPLATE_TOKEN}.Contracts" "${DEST_PATH}/${NEW_NAME}.Contracts"
+  mv "${DEST_PATH}/${NEW_NAME}.Contracts/${TEMPLATE_TOKEN}.Contracts.csproj" \
+     "${DEST_PATH}/${NEW_NAME}.Contracts/${NEW_NAME}.Contracts.csproj"

   log "        replaced token in ${count} files."
 }

@@ line ~409-418 (generate_sln) @@
   ( cd "${DEST_PATH}" \
     && dotnet new sln --format sln -n "${NEW_NAME}"                          >/dev/null \
     && dotnet sln "${NEW_NAME}.sln" add "${NEW_NAME}/${NEW_NAME}.csproj"     >/dev/null \
+    && dotnet sln "${NEW_NAME}.sln" add "${NEW_NAME}.Contracts/${NEW_NAME}.Contracts.csproj"  >/dev/null \
     && dotnet sln "${NEW_NAME}.sln" add "LibCommons/LibCommons.csproj"       >/dev/null \
     && dotnet sln "${NEW_NAME}.sln" add "LibNetworks/LibNetworks.csproj"     >/dev/null )
 }

@@ main() — add copy_contracts call after copy_template @@
-  log "[5/12]  Copying ${TEMPLATE_TOKEN}..."
+  log "[5/12]  Copying ${TEMPLATE_TOKEN} + ${TEMPLATE_TOKEN}.Contracts..."
   copy_template
+  copy_contracts
   log "[6/12]  Copying LibCommons..."
   copy_libcommons
```

### 11.2 ps1 — Mirror Diff

기본 패턴 동일. 핵심 변경:

```powershell
# constants
$Script:ContractsSrc = Join-Path $Script:RepoRoot (Join-Path 'template-projects' "$Script:TemplateToken.Contracts")

# dry-run log
Write-Log "[DRY-RUN]   $Script:ContractsSrc -> $(Join-Path $Script:DestPathResolved "$NewProjectName.Contracts")"

# copy
Copy-TreeFiltered -Src $Script:ContractsSrc -Dest (Join-Path $Script:DestPathResolved "$Script:TemplateToken.Contracts")

# replace + rename (foreach subtree)
$subtrees = @(
  Join-Path $Script:DestPathResolved $Script:TemplateToken
  Join-Path $Script:DestPathResolved "$Script:TemplateToken.Contracts"
)
foreach ($subtree in $subtrees) { ... }

Move-Item (Join-Path $Script:DestPathResolved "$Script:TemplateToken.Contracts") `
          (Join-Path $Script:DestPathResolved "$NewProjectName.Contracts")
Move-Item (Join-Path $Script:DestPathResolved "$NewProjectName.Contracts" "$Script:TemplateToken.Contracts.csproj") `
          (Join-Path $Script:DestPathResolved "$NewProjectName.Contracts" "$NewProjectName.Contracts.csproj")

# sln add
& dotnet sln "$NewProjectName.sln" add "$NewProjectName.Contracts/$NewProjectName.Contracts.csproj"
```

### 11.3 Fixture Updates

**case-01-simple/expected/files-present.txt** (manual edit, 3 lines 추가):
```
MyLobbyServer.Contracts/MyLobbyServer.Contracts.csproj
MyLobbyServer.Contracts/Protocols/Sample.proto
MyLobbyServer.Contracts/Handlers/PacketIds.cs
```

**case-01-simple/expected/sha256.txt + tree.txt**: `bash tests/scaffold/run.sh --update-golden case-01-simple` 자동 재생성.

**case-05-existing-dest-with-force/expected/files-present.txt**:
```diff
 Foo/Foo.csproj
 Foo/Program.cs
+Foo.Contracts/Foo.Contracts.csproj
+Foo.Contracts/Protocols/Sample.proto
+Foo.Contracts/Handlers/PacketIds.cs
 LibCommons/LibCommons.csproj
 LibNetworks/LibNetworks.csproj
 Foo.sln
 .gitignore
```

**case-07-no-git-no-smoke/expected/files-present.txt**: 동일 (Contracts 3 lines 추가).

**case-06-dry-run/expected/stdout-contains.txt**: 변경 없음 (generic phrases only).

**case-02/03/04**: 영향 없음 — validation/error path 만 검사.

### 11.4 Session Guide

#### Module Map

| Module | Scope Key | 설명 | Estimated Turns |
|--------|-----------|------|:--:|
| M1 | `sh-script` | scaffold-game-server.sh 수정 (constants, copy, replace, sln) | 10-15 |
| M2 | `ps1-script` | scaffold-game-server.ps1 mirror | 10-15 |
| M3 | `case-01-golden` | run.sh --update-golden 실행 + files-present 갱신 | 3-5 |
| M4 | `case-05-07-fixture` | files-present manual edit | 2-3 |
| M5 | `full-suite-verify` | bash run.sh 전체 case PASS | 3-5 |
| M6 | `build-smoke` | 임시 dest scaffold + dotnet build | 5-8 |
| M7 | `ps1-verify` | pwsh run.sh --script ps1 (가능 시) | 3-5 |
| M8 | `docs` | scripts/README.md 갱신 | 2-3 |

#### Recommended Session Plan

| Session | Phase | Scope | Turns |
|---------|-------|-------|:-----:|
| 1 (현재) | Plan + Design | 전체 | 30-40 |
| 2 | Do | `--scope sh-script,ps1-script` (M1+M2) | 25-35 |
| 3 | Do | `--scope case-01-golden,case-05-07-fixture,full-suite-verify,build-smoke,ps1-verify,docs` (M3-M8) | 20-30 |
| 4 | Check + Report + Archive | 전체 | 15-25 |

또는 한 세션에 M1-M8 모두 (refactor 단순, ~50-80 turns).

### 11.5 Commit Strategy

**선호: 1개 atomic commit**

```
fix(scaffold): copy Contracts + add to sln; regenerate golden fixtures

- scripts/scaffold-game-server.sh: CONTRACTS_SRC, copy_contracts(),
  replace_tokens() iterates Template+Contracts subtrees, generate_sln() 4 projects
- scripts/scaffold-game-server.ps1: mirror
- tests/scaffold/case-01-simple/expected/: sha256/tree regenerated, files-present updated
- tests/scaffold/case-05-existing-dest-with-force/expected/files-present.txt: +3 lines
- tests/scaffold/case-07-no-git-no-smoke/expected/files-present.txt: +3 lines
- scripts/README.md: scaffold output structure mention

Closes regression introduced by template-contracts-extraction (ff105ea).
Verified: tests/scaffold/run.sh full pass, dotnet build of scaffold output OK.
```

이유: scaffold script 변경과 fixture 재생성은 논리적으로 한 묶음(서로 의존). 분리 시 중간 commit에 build/test 깨진 상태 발생.

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-12 | Initial draft. Option C 채택. | das_young |
