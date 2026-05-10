# move-test-projects-to-testprojects-folder Plan

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-10
> **Status**: Draft
> **PRD**: (lightweight refactor — PRD 생략, 본 Plan에 직접 motivation 포함)

---

## Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | 5개 test-related 프로젝트 (`FastPortTests`, `FastPortTestLoadRunner`, `FastPortTestLoadValidation`, `FastPortTestSmokeServer`, `LibTestTelemetry`)가 repo root에 비-test 프로젝트와 섞여 있어 새 contributor가 production vs test surface를 한눈에 구분하기 어려움. |
| **Solution** | 5개 프로젝트를 `tests-projects/` 폴더로 이동. sln + csproj ProjectReference path + scripts/cloud + docs를 일괄 갱신하여 회귀 0. **Production code 0줄 변경**. |
| **Function/UX/Effect** | `ls` 결과가 production 7개 + tests-projects/ 1개로 깔끔. 새 contributor가 `tests-projects/` 한 곳을 보면 모든 test surface 파악 가능. |
| **Core Value** | 의도가 코드 구조에 반영 → 향후 cycle (MAUI dashboard, run viewer 등)에서 production/test 경계 결정이 쉬워짐. |

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | 직전 4 cycle 성공으로 main green이 안정된 시점. 본격 feature cycle (MAUI dashboard) 진입 전에 production/test surface 분리해 onboarding 마찰 ↓. |
| **WHO** | Repo committer + 외부 contributor + AI agent (모두 코드 탐색 시 production vs test 구분 필요). |
| **RISK** | (R-1) ProjectReference path 누락 / (R-2) scripts/cloud 운영 영향 / (R-3) sln 무결성 / (R-4) git history 흐름 깨짐 |
| **SUCCESS** | build 0/0, test 139/139, scaffold CI green, scripts/cloud 동작, git log --follow로 file history 추적 가능 |
| **SCOPE** | 5 프로젝트 디렉토리 이동 + sln + csproj × 7 + scripts/cloud × 5 + docs × 3 일괄 수정 |

---

## 1. Overview

### 1.1 Motivation

이전 turn read-only 조사 결과:
- 비-test 프로젝트가 test 프로젝트를 참조하는 in-edge: **0건** → 단방향 의존성, 안전 이동 가능.
- Test 프로젝트끼리 참조 (`../FastPortTests/...` 등): 이동 후 같은 폴더 안이라 path 그대로.
- Test → 비-test 참조 (`../LibCommons/...` 등): 이동 후 `../../LibCommons/...`로 path 한 단계 깊이 증가.

### 1.2 Why this cycle, why now

- 4 cycles 연속 main green 확보, build.yml + scaffold.yml CI 가동 중. 회귀 즉시 감지 가능.
- MAUI dashboard cycle (`maui-telemetry-dashboard-foundation`) 시작 전이라 새 프로젝트 추가 시 위치 결정이 깔끔.
- `tests-projects/` 폴더 신설은 같은 repo의 `tests/` (PDCA test infra: scaffold golden files + repeat-tests.sh runner)와 의도가 다르므로 이름 분리 효과.

---

## 2. Scope

### 2.1 In Scope

5개 프로젝트 이동 (현재 → 신규):

| 현재 | 신규 |
|---|---|
| `FastPortTests/` | `tests-projects/FastPortTests/` |
| `FastPortTestLoadRunner/` | `tests-projects/FastPortTestLoadRunner/` |
| `FastPortTestLoadValidation/` | `tests-projects/FastPortTestLoadValidation/` |
| `FastPortTestSmokeServer/` | `tests-projects/FastPortTestSmokeServer/` |
| `LibTestTelemetry/` | `tests-projects/LibTestTelemetry/` |

부수 갱신:

- `FastPortSharp.sln` 5 project line의 path
- 5 csproj 안 ProjectReference path (총 ~7개 ../ → ../..)
- `scripts/cloud/runner-smoke.sh` (2 lines)
- `scripts/cloud/server-start.sh` (1 line)
- `scripts/cloud/runner-10k.sh` (2 lines)
- `docs/staged-load-validation-test-guide.md` (3 lines)

### 2.2 Out of Scope

- Production 코드 (`LibCommons/`, `LibNetworks/`, `Protocols/`, `FastPortServer/`, `FastPortClient/`, `FastPortGameServerTemplate*`) 변경 0줄
- `tests/` (PDCA test infra) 이동 안 함 (의도가 다른 surface)
- `docs/archive/**` 갱신 (immutable)
- 기존 archive 안 path 참조 그대로 유지 (역사적 정확성)
- README.md / README.ko.md / HANDOFF.md (test 프로젝트 path 미참조 — 직전 grep 결과 기준)
- CI workflow 자체 변경 (sln만 참조하므로 자동 흡수)

---

## 3. Requirements

### 3.1 Functional

- **FR-1**: 5개 디렉토리는 `git mv`로 이동되어 git rename detection이 작동해야 함 (`git log --follow` 동작).
- **FR-2**: 이동 후 `dotnet build FastPortSharp.sln -c Release` 0 warning / 0 error.
- **FR-3**: `dotnet test FastPortSharp.sln -c Release --no-build` 139/0/0.
- **FR-4**: `tests/scaffold/run.sh` (scaffold runner) 7/7 PASS — 영향 없어야 함.
- **FR-5**: scripts/cloud/*.sh 명령들이 새 path로 정상 빌드 (실제 cloud 실행은 검증 안 함, 명령 syntax만).

### 3.2 Non-Functional

- **NFR-1**: production code 0줄 변경 (`git diff -- LibCommons/ LibNetworks/ Protocols/ FastPortServer/ FastPortClient/ FastPortGameServerTemplate/ FastPortGameServerTemplate.SampleClient/` = 0).
- **NFR-2**: 단일 commit으로 마무리.
- **NFR-3**: GHA build.yml 3-OS 모두 PASS.
- **NFR-4**: scaffold.yml은 영향 없음 (test 프로젝트 미참조).

### 3.3 Compatibility

- net10.0 그대로
- IDE는 sln 다시 열기 필요 (path 변경)
- 기존 archive docs는 historical path 유지 (의도된 결정)

---

## 4. Success Criteria

### 4.1 Definition of Done

- [ ] 5개 디렉토리 `git mv`로 이동
- [ ] `FastPortSharp.sln` 5 project line path 갱신
- [ ] 7개 csproj ProjectReference path 갱신 (../ → ../..)
- [ ] `scripts/cloud/runner-smoke.sh` 2 lines 갱신
- [ ] `scripts/cloud/server-start.sh` 1 line 갱신
- [ ] `scripts/cloud/runner-10k.sh` 2 lines 갱신
- [ ] `docs/staged-load-validation-test-guide.md` 3 lines 갱신
- [ ] `dotnet build FastPortSharp.sln -c Release` 0/0
- [ ] `dotnet test ... --no-build` 139/0/0
- [ ] `tests/scaffold/run.sh` 7/7 PASS (회귀)
- [ ] production diff 0줄 (`git diff -- <production paths>` = 0)
- [ ] 단일 commit
- [ ] GHA build.yml 1차 push에서 3-OS 모두 PASS
- [ ] GHA scaffold.yml 회귀 0 (test 프로젝트 path 미참조)

### 4.2 Quality Criteria

- [ ] `git log --follow tests-projects/FastPortTests/FastPortTests.csproj` 동작 (rename detection)
- [ ] sln 정렬: `tests-projects/...`로 시작하는 5 entry가 logical하게 그룹화 (선택, sln은 textual sort)

---

## 5. Risks and Mitigation

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| (R-1) ProjectReference path 누락 → 빌드 실패 | Low | Medium | csproj 7개 변경 후 `dotnet build` 즉시 검증. 누락 시 build error로 표면화. |
| (R-2) scripts/cloud 운영 영향 | Low | Low | 이번 cycle은 syntactic 갱신만. 실제 cloud run은 별도 cycle (이번 commit이 cloud 명령 syntax 깨지지 않는지 dry-validate). |
| (R-3) sln 무결성 (GUID/section 누락) | Low | High | git mv + sln textual edit만 함. dotnet sln remove/add 명령으로 안전 갱신 가능 (대안). |
| (R-4) git history 흐름 깨짐 | Low | Low | git mv 사용 → rename detection 자동. `git log --follow` 명시 검증. |
| (R-5) IDE/캐시 stale state | Low | Low | bin/obj는 .gitignore이라 영향 없음. user는 IDE에서 sln 재열기 필요. |
| (R-6) GHA windows의 file path case sensitivity | Low | Low | 모든 path가 case-correct. 직전 cycle에서 verified. |

---

## 6. Impact Analysis

### 6.1 영향 받는 파일

| 영역 | 파일 | 변경 형태 | 라인 |
|---|---|---|:-:|
| sln | `FastPortSharp.sln` | edit | 5 |
| csproj | `tests-projects/FastPortTests/FastPortTests.csproj` | edit (path) | ~6 |
| csproj | `tests-projects/FastPortTestLoadRunner/FastPortTestLoadRunner.csproj` | edit | ~3 |
| csproj | `tests-projects/FastPortTestSmokeServer/FastPortTestSmokeServer.csproj` | edit | ~4 |
| csproj | `tests-projects/FastPortTestLoadValidation/FastPortTestLoadValidation.csproj` | edit | ~1 |
| csproj | `tests-projects/LibTestTelemetry/LibTestTelemetry.csproj` | (out-edge 0이라 변경 0) | 0 |
| scripts | `scripts/cloud/runner-smoke.sh` | edit | 2 |
| scripts | `scripts/cloud/server-start.sh` | edit | 1 |
| scripts | `scripts/cloud/runner-10k.sh` | edit | 2 |
| docs | `docs/staged-load-validation-test-guide.md` | edit | 3 |
| **dirs** | 5 디렉토리 | git mv | (rename) |

총 ~27 라인 + 5 directory rename.

### 6.2 영향 받지 않는 영역

- production 코드 전체 (`LibCommons/`, `LibNetworks/`, `Protocols/`, `FastPortServer/`, `FastPortClient/`, `FastPortGameServerTemplate*`)
- `tests/` (scaffold golden + repeat runner)
- `scripts/scaffold-game-server.{sh,ps1}`, `scripts/README.md`
- `.gitattributes`, `.gitignore`
- `.github/workflows/build.yml`, `.github/workflows/scaffold.yml`
- README.md / README.ko.md / HANDOFF.md
- `docs/archive/**` (historical 보존)

### 6.3 Performance Impact

- build/test 시간: 동일 (path만 변경)
- runtime: 0
- IDE: sln 재열기 필요 (1회)

---

## 7. Architecture Considerations

### 7.1 Decision Confirmed (Plan Checkpoint)

| Decision | Choice | Rationale |
|---|---|---|
| 폴더 이름 | **`tests-projects/`** (kebab-case) | 사용자 명시 선택. 기존 `tests/` (PDCA infra)와 의도 차이 분명. |
| 커밋 단위 | **단일 commit** | 사용자 명시 선택. 직전 cycle들 일관성. atomic 회귀 검증 가능. |
| Production 변경 게이트 | **0줄** | 직전 2 cycle 패턴. 검증 axis. |
| `git mv` 사용 | **Yes** | history 보존 (`git log --follow` 동작). |

### 7.2 Open Decisions for Design Phase

- sln 갱신 방법: textual edit vs `dotnet sln remove/add` 명령 (안전성 vs 정확성 trade-off)
- 부수 변경 (scripts/cloud, docs) 파일별 commit 분리 vs 합침 (이미 단일 commit 결정됨)

---

## 8. Convention Prerequisites

- 한국어 주석 컨벤션 그대로
- git mv 사용 (cp + rm + git add 안 함)
- sln 끝 trailing newline 유지 (직전 .gitattributes 정책)

---

## 9. Next Steps

1. `/pdca design move-test-projects-to-testprojects-folder`
   - 3 architecture options:
     - **A**: textual `git mv` + 직접 file edit (가장 단순)
     - **B**: `dotnet sln remove/add` 명령 사용 (sln 무결성 보장)
     - **C**: 두 가지 조합 (git mv + dotnet sln rename)
   - sln 갱신 방법 확정
2. `/pdca do ...` (단일 세션, ≤ 20 turn 추정)
3. `/pdca analyze` + `report` + `archive`

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-10 | Initial plan (5 dirs into tests-projects/, single commit, production 0줄 변경) | boinred |
