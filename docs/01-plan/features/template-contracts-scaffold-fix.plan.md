# template-contracts-scaffold-fix Planning Document

> **Summary**: 이전 cycle `template-contracts-extraction`이 도입한 `FastPortGameServerTemplate.Contracts` 라이브러리를 `scaffold-game-server.{sh,ps1}` 가 복사·등록하도록 업데이트하고, 영향 받는 golden-file fixture (case-01 sha256/tree, case-05/07 files-present)를 재생성한다.
>
> **Project**: FastPortSharp
> **Version**: 1.0
> **Author**: das_young
> **Date**: 2026-05-12
> **Status**: Draft
> **Predecessor Cycle**: [template-contracts-extraction.plan.md](./template-contracts-extraction.plan.md) — commits `b3e4e2c`, `ff105ea`

---

## Executive Summary

| Perspective | Content |
|-------------|---------|
| **Problem** | 직전 cycle이 `Contracts` lib을 분리했지만 scaffold 스크립트는 여전히 `TEMPLATE_SRC` + `LibCommons` + `LibNetworks` 3개만 복사함. 결과적으로 외부 사용자가 `scaffold-game-server`를 실행하면 생성된 `<NewName>.csproj`가 존재하지 않는 `..\<NewName>.Contracts\...`를 참조해 빌드 실패. |
| **Solution** | scaffold script 양쪽(sh/ps1)에 `CONTRACTS_SRC` 변수 + Contracts 복사 + sln 등록 + token replacement subtree 확장 + Contracts 폴더/csproj 이름 변경 로직 추가. golden-file fixture 재생성 (case-01 auto, case-05/07 manual edit). |
| **Function/UX Effect** | scaffold 출력이 4개 프로젝트(`<NewName>`, `<NewName>.Contracts`, `LibCommons`, `LibNetworks`)를 포함한 빌드 가능 상태로 회복. cross-OS byte-identical 보장 유지. |
| **Core Value** | (1) `template-contracts-extraction` cycle 의 regression 폐쇄, (2) 외부 게임 사용자 onboarding 경로 정상화, (3) CI scaffold workflow PASS 회복. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 이전 cycle이 Contracts lib을 도입하면서 scaffold 측을 업데이트하지 않아 scaffold 출력이 컴파일 불가 상태. CI scaffold workflow도 PR push 시 실패 예정. |
| **WHO** | 외부 게임 사용자(scaffold-game-server 실행자), scaffold workflow CI, repo 유지 개발자. |
| **RISK** | (a) sh/ps1 byte-identical 출력 어긋남 — 크로스-OS CI matrix 깨짐. (b) token replacement scope 확장 시 LibCommons/LibNetworks 까지 의도치 않게 변환될 가능성. (c) sln 생성 순서 변경으로 case-01 sha256 갱신 누락 시 CI fail. |
| **SUCCESS** | (1) `bash scripts/scaffold-game-server.sh Foo /tmp/foo --no-git --skip-smoke` → `Foo.Contracts/Foo.Contracts.csproj` 생성 확인, (2) `dotnet build /tmp/foo/Foo.sln` 성공, (3) `tests/scaffold/run.sh` 모든 case PASS, (4) sh / ps1 출력 sha256 동일. |
| **SCOPE** | scaffold script 2개 수정, fixture 3개 case 갱신 (auto: case-01, manual: case-05/07), 그 외 case 영향 없음 확인, cross-OS CI matrix push 후 확인. |

---

## 1. Overview

### 1.1 Purpose

직전 cycle이 도입한 contract lib을 scaffold workflow가 일관되게 처리하도록 보강하여, 외부 사용자가 `scaffold-game-server`로 만든 새 프로젝트가 즉시 빌드 가능하도록 한다.

### 1.2 Background

`template-contracts-extraction` (commits `b3e4e2c`, `ff105ea`) 이후:

```
Repo Source (now):
  template-projects/FastPortGameServerTemplate/                    ← Exe, depends on Contracts
  template-projects/FastPortGameServerTemplate.Contracts/          ← Class Lib (proto + PacketIds)
  template-projects/FastPortGameServerTemplate.SampleClient/       ← Exe, depends on Contracts

scaffold-game-server.{sh,ps1} 현재 동작:
  1) ${TEMPLATE_SRC} → <dest>/${TEMPLATE_TOKEN}
  2) ${LIBCOMMONS_SRC} → <dest>/LibCommons
  3) ${LIBNETWORKS_SRC} → <dest>/LibNetworks
  4) token replace + rename → <dest>/<NewName>
  5) sln add 3 projects

문제:
  - 4) 후 <dest>/<NewName>/<NewName>.csproj 가 ..\<NewName>.Contracts\<NewName>.Contracts.csproj 참조
  - 5) 의 sln 에 Contracts 누락
  - 결과: <dest>/Foo.sln 빌드 실패 (Contracts 프로젝트 없음)
```

본 cycle은 이 gap을 메운다.

### 1.3 Related Documents

- Predecessor: [`template-contracts-extraction.plan.md`](./template-contracts-extraction.plan.md), [`.design.md`](../../02-design/features/template-contracts-extraction.design.md)
- Fixture authority: `tests/scaffold/case-*/expected/`
- HANDOFF.md:282 — cross-OS byte-identical 출력 정책

---

## 2. Scope

### 2.1 In Scope

**Part A — scaffold script 갱신 (sh + ps1)**
- [ ] `CONTRACTS_SRC` 변수 추가 (`${REPO_ROOT}/template-projects/${TEMPLATE_TOKEN}.Contracts`)
- [ ] `copy_contracts()` 함수 추가 (`<dest>/${TEMPLATE_TOKEN}.Contracts` 로 복사)
- [ ] `replace_tokens()` 의 subtree iteration 을 Template + Contracts 둘 다 처리하도록 확장
- [ ] Token replace 후 디렉터리 + csproj rename 을 Contracts에도 적용:
  - `<dest>/${TEMPLATE_TOKEN}.Contracts` → `<dest>/${NEW_NAME}.Contracts`
  - `<dest>/${NEW_NAME}.Contracts/${TEMPLATE_TOKEN}.Contracts.csproj` → `<dest>/${NEW_NAME}.Contracts/${NEW_NAME}.Contracts.csproj`
- [ ] `generate_sln()` 에 4번째 `dotnet sln add` (Contracts) 추가
- [ ] Dry-run 출력 + `(3 projects)` → `(4 projects)` + Contracts copy line 추가
- [ ] sh/ps1 출력이 byte-identical 유지하는지 확인

**Part B — fixture 재생성**
- [ ] `tests/scaffold/run.sh --update-golden case-01-simple` 실행 → `sha256.txt` + `tree.txt` 갱신 (auto)
- [ ] `tests/scaffold/case-01-simple/expected/files-present.txt` 에 Contracts 엔트리 추가 (manual)
- [ ] `tests/scaffold/case-05-existing-dest-with-force/expected/files-present.txt` 에 Contracts 엔트리 추가 (manual)
- [ ] `tests/scaffold/case-07-no-git-no-smoke/expected/files-present.txt` 에 Contracts 엔트리 추가 (manual)
- [ ] case-02 / 03 / 04 / 06: 영향 없음 확인 (negative cases 또는 generic pattern)

**Part C — 검증**
- [ ] `bash tests/scaffold/run.sh` 모든 case PASS
- [ ] `pwsh tests/scaffold/run.sh --script ps1` 모든 case PASS (macOS pwsh가 있으면)
- [ ] 임시 dest에 실제 scaffold 후 `dotnet build /tmp/Foo/Foo.sln` 성공
- [ ] 양 스크립트 sha256 동일 검증 (run.sh 또는 수동)

### 2.2 Out of Scope

- scaffold 출력의 `<dest>/template-projects/<NewName>` 같은 그룹 폴더 도입 — 외부 사용자 영향 0 유지를 위해 flat 그대로 (단지 `<NewName>.Contracts` 만 추가)
- `template-contracts-extraction` cycle 의 다른 부분(folder 이동, Contracts lib 자체) 수정
- HANDOFF.md 의 cross-OS 정책 변경
- Linux/Windows 실제 CI workflow 실행 검증 (push 후 자동, 본 cycle 책임 외)
- scaffold 의 step counter `[N/12]` 재할당 — 기존 fixture stdout-contains 보존 위해 12 유지

### 2.3 Step Counter 정책

기존 fixture `stdout-contains.txt`가 `[1/12]`, `[12/12]` 를 검사하므로 step 수 변경 금지. 새 Contracts 작업은 기존 step 안에 sub-action으로 포함:
- `[5/12] Copying ${TEMPLATE_TOKEN}` → 내부에서 Template + Contracts 둘 다 copy
- `[8/12] Replacing tokens` → 두 subtree 모두 iterate
- `[10/12] Creating ${NEW_NAME}.sln (4 projects)` → 라벨만 갱신

---

## 3. Requirements

### 3.1 Functional Requirements

| ID | Requirement | Priority | Status |
|----|-------------|----------|--------|
| FR-01 | `scaffold-game-server.sh` 가 Contracts 디렉터리를 `<dest>/${NEW_NAME}.Contracts` 로 생성 | High | Pending |
| FR-02 | `scaffold-game-server.ps1` 가 동일 결과 (byte-identical 출력) | High | Pending |
| FR-03 | 양 스크립트의 generated sln 이 4개 프로젝트 모두 포함 (`<NewName>`, `<NewName>.Contracts`, `LibCommons`, `LibNetworks`) | High | Pending |
| FR-04 | Token replacement 이 `<dest>/${NEW_NAME}.Contracts` 의 `Sample.proto` + `PacketIds.cs` + `<NewName>.Contracts.csproj` 도 처리 | High | Pending |
| FR-05 | `dotnet build <dest>/<NewName>.sln` 성공 (Contracts → Template/SampleClient 의존 정상) | High | Pending |
| FR-06 | `tests/scaffold/run.sh` 모든 7 case PASS | High | Pending |
| FR-07 | step counter `[1/12]` ~ `[12/12]` 유지 (기존 stdout fixture 보존) | Medium | Pending |
| FR-08 | case-01 sha256.txt + tree.txt + files-present.txt 갱신 | High | Pending |
| FR-09 | case-05, case-07 files-present.txt Contracts 엔트리 추가 | High | Pending |

### 3.2 Non-Functional Requirements

| Category | Criteria | Measurement |
|----------|----------|-------------|
| Cross-OS Identical | sh 와 ps1 의 scaffold 출력 byte-identical | `tests/scaffold/run.sh --script sh` 와 `--script ps1` 양쪽 PASS 동일 sha256 |
| LF/CRLF Policy | `.sln`은 CRLF, 그 외 LF (HANDOFF.md:282) | `.gitattributes` 정책 유지 |
| Backward Compat | 기존 scaffold CLI/옵션 변경 없음 | `--help`, `--dry-run`, `--force`, `--no-git`, `--skip-smoke` 동작 동일 |

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] FR-01 ~ FR-09 충족
- [ ] `bash tests/scaffold/run.sh` exit 0
- [ ] 새 scaffold 출력의 `dotnet build` exit 0
- [ ] sh / ps1 cross-script 출력 sha256 일치 (macOS pwsh 사용 가능 시)
- [ ] commit message 에 "fixture regenerated" 명시

### 4.2 Quality Criteria

- [ ] token replacement 가 LibCommons / LibNetworks 영역을 건드리지 않음 (substring 침투 확인)
- [ ] Contracts.csproj 의 PackageReference 가 token replacement 대상이 아님 (proto namespace 외엔 토큰 미포함 확인)
- [ ] step counter regression 없음

---

## 5. Risks and Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| sh / ps1 출력 byte-divergence | High | Medium | 동일 로직 구조 유지 + `run.sh --script ps1` 로 검증 + 추가 시 substring 단위로 동일 패턴 적용 |
| token replacement 가 LibCommons 영역으로 새어나감 | High | Low | 기존 코드도 `<subtree>` 한정으로 처리. Contracts subtree 추가 시에도 동일 한정. find 명령의 root path 명시. |
| case-01 sha256 재생성 시 `--update-golden` 옵션 동작 안 함 | Medium | Low | run.sh 코드에 옵션 존재 확인. fallback: 수동 `find ... -exec sha256sum {} \;` |
| `[1/12]` step counter 변경 시 stdout fixture 깨짐 | High | Low | 본 cycle 정책상 step 수 유지. 새 작업은 기존 step 내부에 packing. |
| .gitattributes CRLF/LF 정책으로 인한 sha256 noise | Medium | Medium | scaffold 가 생성하는 `.gitattributes`는 이미 정책 포함. fixture 재생성은 같은 OS에서 1회만. |
| pwsh macOS 미설치 → ps1 검증 불가 | Low | Medium | sh 만 로컬 검증, ps1 은 CI 의존. 명시적 caveat 기록. |
| scaffold 의 `[11/12] dotnet build` 가 smoke 활성 시 Contracts 의존 인식 필요 | Medium | Low | scaffold 가 `dotnet sln add` 4개 한 뒤 build 하므로 자동 처리. case-07은 --skip-smoke 사용. |

---

## 6. Impact Analysis

### 6.1 Changed Resources

| Resource | Type | Change |
|----------|------|--------|
| `scripts/scaffold-game-server.sh` | Modified | `CONTRACTS_SRC` 추가, `copy_contracts()` 추가, `replace_tokens()` subtree 확장 + Contracts rename, `generate_sln()` 4 projects, dry-run log 갱신 |
| `scripts/scaffold-game-server.ps1` | Modified | sh와 1:1 대응 |
| `tests/scaffold/case-01-simple/expected/sha256.txt` | Regenerated | `--update-golden` 자동 |
| `tests/scaffold/case-01-simple/expected/tree.txt` | Regenerated | `--update-golden` 자동 |
| `tests/scaffold/case-01-simple/expected/files-present.txt` | Modified | Contracts 3 파일 엔트리 추가 |
| `tests/scaffold/case-01-simple/expected/files-absent.txt` | Verify | 기존 token leak 검증은 그대로 유효 |
| `tests/scaffold/case-05-existing-dest-with-force/expected/files-present.txt` | Modified | Contracts 엔트리 |
| `tests/scaffold/case-07-no-git-no-smoke/expected/files-present.txt` | Modified | Contracts 엔트리 |
| `tests/scaffold/case-06-dry-run/expected/stdout-contains.txt` | Verify | 현재 generic phrase 만 — 영향 없을 가능성 높음 |
| `scripts/README.md` | Modified | scaffold 가 만드는 출력 구조 안내 갱신 (4 프로젝트) |

### 6.2 Current Consumers

| Resource | Operation | Path | Impact |
|----------|-----------|------|--------|
| `scaffold-game-server.sh` | CI | `.github/workflows/scaffold.yml` matrix | PASS after fix |
| `scaffold-game-server.ps1` | CI | 동일 | PASS after fix |
| Fixtures | local test | `tests/scaffold/run.sh` | PASS after regen |
| External users | manual | game project bootstrap | Working scaffold output |

### 6.3 Verification

- [ ] `bash tests/scaffold/run.sh` exit 0
- [ ] 임시 dest scaffold 후 `dotnet build` 성공
- [ ] sh / ps1 cross-flavor sha256 동일 (macOS pwsh 가능 시)
- [ ] case-02/03/04/06 fixture 변경 없이 PASS 유지

---

## 7. Architecture Considerations

### 7.1 Project Level Selection

해당 없음 — 스크립트 + 테스트 fixture refactor (Enterprise-style boundary 강화에 가까움).

### 7.2 Key Architectural Decisions

| Decision | Options | Selected | Rationale |
|----------|---------|----------|-----------|
| 출력 구조 | (a) flat: `<dest>/<NewName>.Contracts`, (b) grouped: `<dest>/template-projects/<NewName>.Contracts` | **(a) flat** | 외부 사용자 onboarding 단순화. `template-projects/` 는 본 repo 의 internal 그룹화 — 외부에는 노출 안 함. |
| step counter | (a) 12 유지, (b) 14 로 확장 | **(a) 유지** | 기존 stdout-contains fixture 4개 case 영향 0. 작업은 기존 step 내부 packing. |
| Contracts copy 순서 | (a) Template 직후, (b) LibNetworks 후 | **(a) Template 직후** | 의존 그래프 leaf-first 자연스러움 + dry-run 로그 가독성. |
| Token subtree | (a) Template + Contracts 각각 별도 `find`, (b) 두 subtree 를 하나의 find -path 표현식 | **(a) 각각** | 코드 명확성 + 한정 명시. PS1 / sh 양쪽에서 동일 패턴. |
| fixture 재생성 방법 | (a) `--update-golden` 자동, (b) 수동 sha256sum | **(a) auto** | 기존 도구 활용. 단, files-present.txt 는 manual edit. |

---

## 8. Convention Prerequisites

### 8.1 Existing

- ☑ `tests/scaffold/run.sh` (golden runner)
- ☑ `.gitattributes` cross-OS LF/CRLF 정책
- ☑ 각 case `expected/` 디렉터리 패턴

### 8.2 To Define

해당 없음 — 본 cycle 은 기존 convention 답습.

---

## 9. Next Steps

1. [ ] Design 단계: 짧게 — sh/ps1 의 정확한 diff 위치 명세 + fixture 변경 라인 명세
2. [ ] Do 단계: 모듈
   - **M1** — sh script 수정 (CONTRACTS_SRC, copy_contracts, replace_tokens 확장, sln 4 projects, dry-run)
   - **M2** — ps1 script 수정 (sh와 1:1 대응)
   - **M3** — `run.sh --update-golden case-01-simple` 실행 → sha256/tree 갱신
   - **M4** — case-01/05/07 files-present.txt manual edit
   - **M5** — `bash tests/scaffold/run.sh` 전체 PASS 검증
   - **M6** — 임시 dest scaffold + `dotnet build` smoke
   - **M7** — pwsh 가능 시 ps1 검증
   - **M8** — `scripts/README.md` 갱신
3. [ ] Check: 모든 case PASS + scaffold output build success
4. [ ] Archive → 직전 2 commits (`b3e4e2c`, `ff105ea`) + 본 cycle commits 함께 push 가능 상태

---

## Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-05-12 | Initial draft. 직전 cycle regression 폐쇄. | das_young |
