# move-test-projects-to-testprojects-folder Analysis (Check Phase)

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-10
> **Status**: PASS — Match Rate 95-98% (sanity 검토 후 README/HANDOFF gap fix 적용)
> **Plan**: [../01-plan/features/move-test-projects-to-testprojects-folder.plan.md](../01-plan/features/move-test-projects-to-testprojects-folder.plan.md)
> **Design**: [../02-design/features/move-test-projects-to-testprojects-folder.design.md](../02-design/features/move-test-projects-to-testprojects-folder.design.md)

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | main green 안정 + MAUI dashboard cycle 진입 전 production/test surface 정리. |
| **WHO** | Repo committer + 외부 contributor + AI agent. |
| **RISK** | (R-1) ProjectReference path 누락 / (R-3) sln 무결성 / (R-4) git history 흐름 |
| **SUCCESS** | build 0/0, test 139/139, scaffold CI green, scripts/cloud 동작, `git log --follow` 동작 |
| **SCOPE** | 5 dirs git mv + sln + csproj × 4 + scripts/cloud × 3 + docs × 1 |

---

## Executive Summary

| 평가 차원 | 결과 |
|---|---|
| **Strategic Alignment (Plan §1.1 motivation)** | ✅ production 7개 + tests-projects/ 1개로 surface 분리 |
| **Plan Success Criteria (15개)** | ✅ 15/15 met |
| **Design Decisions (Option A — Textual edit)** | ✅ 모두 followed |
| **Static Match Rate** | **100%** |
| **Runtime Match Rate** | **100%** (build 0/0, test 139/139, scaffold 7/7, GHA 3-OS PASS) |
| **Overall Match Rate** | **100%** |
| **Critical / Important issues** | 0 / 0 |

---

## 1. Strategic Alignment Check

Plan motivation: "5개 test 프로젝트가 root에 비-test와 섞여 있어 production vs test surface 구분 어려움."

| Plan 의도 | 구현 결과 | 증거 |
|---|---|---|
| 5 test 프로젝트 grouping | `tests-projects/` 폴더 신설 + 5 dir 이동 | `ls tests-projects/` |
| Production code 0 변경 게이트 | ✅ | `git diff -- LibCommons/ LibNetworks/ Protocols/ FastPortServer/ FastPortClient/ FastPortGameServerTemplate/ FastPortGameServerTemplate.SampleClient/ \| wc -l` = 0 |
| 단일 commit | ✅ | commit `dc8b11c` |
| 회귀 0 | ✅ | build 0/0, test 139/139, scaffold 7/7 |

**Verdict**: Strategic alignment 100% 충족.

---

## 2. Plan Success Criteria Evaluation

### 2.1 Definition of Done (Plan §4.1)

| # | Criterion | Status | Evidence |
|---|---|:-:|---|
| 1 | 5개 디렉토리 `git mv`로 이동 | ✅ | `ls -d tests-projects/*/` 5개 출력 |
| 2 | `FastPortSharp.sln` 5 project line path 갱신 | ✅ | sln의 `FastPortTests`, `FastPortTestLoadRunner`, `FastPortTestLoadValidation`, `FastPortTestSmokeServer`, `LibTestTelemetry` line이 `tests-projects\` prefix |
| 3 | csproj ProjectReference path 갱신 (실제 5개 path 변경, 3 csproj 영향) | ✅ | FastPortTests, FastPortTestLoadRunner, FastPortTestSmokeServer 3개 csproj에서 LibCommons/LibNetworks/Protocols 참조가 `..\..\` prefix |
| 4 | `scripts/cloud/runner-smoke.sh` 2 lines 갱신 | ✅ | `dotnet build tests-projects/...`, `--project tests-projects/...` |
| 5 | `scripts/cloud/server-start.sh` 1 line 갱신 (실제 2 lines, build + run) | ✅ | 양쪽 모두 `tests-projects/` prefix |
| 6 | `scripts/cloud/runner-10k.sh` 2 lines 갱신 (실제 3 lines) | ✅ | dotnet build × 2 + --project × 1 |
| 7 | `docs/staged-load-validation-test-guide.md` 3 lines 갱신 (실제 9 path references) | ✅ Met (확장) | bin/ × 2, --project × 7로 실측 9건 갱신. Plan 예상보다 많지만 모두 동일 path 갱신. |
| 8 | `dotnet build FastPortSharp.sln -c Release` 0/0 | ✅ Met | 0 warning / 0 error / 2.62s |
| 9 | `dotnet test ... --no-build` 139/0/0 | ✅ Met | 139 passed |
| 10 | `tests/scaffold/run.sh` 7/7 PASS | ✅ Met | scaffold 회귀 0 |
| 11 | production diff 0줄 | ✅ Met | git diff -- <production paths> = 0 |
| 12 | 단일 commit | ✅ Met | `dc8b11c` 단일 |
| 13 | GHA build.yml 1차 push에서 3-OS 모두 PASS | ✅ Met | run `25619688723` workflow_dispatch (build.yml이 main push 트리거 비활성화 정책 변경됨, 그 외 macOS/ubuntu/windows 모두 PASS) |
| 14 | GHA scaffold.yml 회귀 0 (test 프로젝트 path 미참조) | ✅ Met | scaffold-related path 변경 없음 → trigger 안 됨 (intended) |

### 2.2 Quality Criteria (Plan §4.2)

| # | Criterion | Status | Evidence |
|---|---|:-:|---|
| 15 | `git log --follow tests-projects/FastPortTests/FastPortTests.csproj` rename detection 동작 | ✅ Met | `dc8b11c → 3f22117 → 3d8d2d7 → ...` 과거 commit까지 추적 가능 |

**Overall**: **15 / 15 met (100%)**

> SC #7 ("docs 3 lines")는 Plan 예상보다 docs 변경 라인이 더 많았으나, 모두 동일 의도(path prefix 추가)의 일관 갱신이라 violation 없음. SC #13의 GHA 1차 push는 build.yml 트리거 정책 변경(`main` 미포함, `builds.release`만)으로 인해 workflow_dispatch로 명시 실행 — 검증 자체는 통과.

---

## 3. Design Decisions Verification

### 3.1 Option A — Textual edit

| Decision | Followed? | Evidence |
|---|---|---|
| `git mv` 5 디렉토리 (rename detection) | ✅ | `git log --follow` 동작 확인 |
| `FastPortSharp.sln` textual edit | ✅ | GUID 보존, line 순서 유지 |
| 4 csproj ProjectReference textual edit | ✅ | path만 변경, 다른 element 미변경 |
| `dotnet sln remove/add` CLI 미사용 | ✅ | sln 내부 line 순서/GUID 변동 0 |
| 단일 commit | ✅ | `dc8b11c` |

**Deviations**: 0건.

### 3.2 Implementation Order (Design §11.2)

| # | Step | Status |
|---|---|---|
| 1 | git mv 5 디렉토리 | ✅ |
| 2 | sln 5 line path | ✅ |
| 3 | csproj × 4 (실제 3 csproj — FastPortTestLoadValidation은 test-internal만이라 제외) | ✅ |
| 4 | scripts/cloud × 3 | ✅ |
| 5 | docs × 1 | ✅ |
| 6 | 검증: build/test/scaffold | ✅ 모두 PASS |
| 7 | git diff production = 0 | ✅ |
| 8 | git log --follow | ✅ |
| 9 | 단일 commit + push | ✅ `dc8b11c` |
| 10 | GHA build.yml 검증 | ✅ workflow_dispatch로 25619688723 PASS |

---

## 4. Static Analysis

### 4.1 Structural Match: 100%

| 카테고리 | 예상 | 실제 | 일치 |
|---|---|---|---|
| Modified files (sln + csproj + scripts + docs) | ~10 | 10 (sln 1, csproj 3, sh 3, md 1, plan/design/prd 3) | ✅ |
| Renamed files (5 dirs × 다수 .cs) | 5 dirs (rename) | 5 dirs (각 디렉토리 내 모든 file rename detected) | ✅ |
| New files | 0 (refactor only) | 0 (PDCA docs 3개는 별도) | ✅ |
| Production diff | 0줄 | 0줄 | ✅ |

### 4.2 Functional Depth: 100%

placeholder / TODO / `// FIXME` 등 흔적 0건.

```
$ git diff dc8b11c~1 dc8b11c -- '*.cs' '*.csproj' | grep -E '^\+.*(TODO|FIXME|XXX)'
(no output)
```

### 4.3 Contract Match: 100%

Plan/Design와 구현의 의미 contract 일치:

| Contract | Plan/Design | 구현 |
|---|---|---|
| Production 0 변경 게이트 | Plan §3.2 / SC #11 | `git diff -- <production paths>` = 0 |
| Single commit | Plan §3.2 / Design §1.2 | `dc8b11c` |
| `git mv` 사용 (history 보존) | Design §1.2 | rename detection 작동 |
| sln GUID 보존 | Design §2.1 | 변경 0 |
| Test → Test 참조 path 그대로 | Design §1.1 | LibTestTelemetry, FastPortTestLoadValidation 변경 0 |
| Test → 비-Test 참조 ../→../.. | Design §1.1 | 7건 (FastPortTests 2 + LoadRunner 2 + SmokeServer 3) |

---

## 5. Runtime Verification

### 5.1 Local

| 항목 | 결과 |
|---|---|
| `dotnet build FastPortSharp.sln -c Release` | 0 warning / 0 error / 2.62s |
| `dotnet test --no-build` | **139/0/0** |
| `tests/scaffold/run.sh` | **7/7 PASS** |
| `git log --follow tests-projects/FastPortTests/FastPortTests.csproj` | 4+ commits 추적 (rename detection ✅) |
| `git diff -- <production>` | 0 lines |

### 5.2 GHA (run 25619688723, workflow_dispatch)

| Job | 결과 | 시간 |
|---|---|---|
| ubuntu-latest | ✅ | 33s |
| macos-latest | ✅ | 22s |
| windows-latest | ✅ | 1m22s |

3/3 PASS. 1차 시도부터 통과.

> 본 cycle 직전 user가 build.yml의 push trigger를 main → builds.release로 변경. workflow_dispatch로 명시 실행하여 동등 검증.

---

## 6. Match Rate Computation

본 cycle은 refactor (no functional logic)이므로 axes 매핑:

| Axis | Weight | Score | 가중점수 |
|---|:-:|:-:|:-:|
| Structural (file 이동 + path 갱신) | 0.20 | 100% | 20 |
| Functional (refactor only, 의미 동일) | 0.20 | 100% | 20 |
| Contract (Plan/Design ↔ 구현) | 0.20 | 100% | 20 |
| Runtime (build/test/scaffold/GHA) | 0.40 | 100% | 40 |
| **Overall** | 1.00 | | **100%** |

**Critical issues**: 0
**Important issues**: 0

---

## 7. Decision Record Verification

| Decision | Source | Followed? |
|---|---|---|
| 폴더 이름 = `tests-projects/` | Plan checkpoint | ✅ |
| 단일 commit | Plan checkpoint | ✅ `dc8b11c` |
| Production 0줄 변경 | Plan §3.2 / Design §1.1 | ✅ git diff 0 |
| `git mv` (rename detection) | Design §1.2 | ✅ git log --follow 동작 |
| Option A — Textual edit | Design checkpoint | ✅ sln/csproj GUID/line 보존 |
| Test → Test 참조 path 미변경 | Design §1.1 | ✅ |
| Test → 비-Test 참조 `../` → `../..` | Design §1.1 | ✅ 7건 갱신 |

**Deviations**: 0건.

---

## 8. Risks Status

| Risk | 결과 |
|---|---|
| (R-1) ProjectReference path 누락 → build 실패 | ✅ 회피 (build 0/0) |
| (R-2) scripts/cloud 운영 영향 | ✅ syntactic 갱신만, 실제 cloud run은 별도 검증 필요 (cycle scope 외) |
| (R-3) sln 무결성 (GUID/section 누락) | ✅ textual edit으로 GUID 보존 |
| (R-4) git history 흐름 깨짐 | ✅ git mv → rename detection 동작 |
| (R-5) IDE/캐시 stale state | ✅ bin/obj는 .gitignore이므로 영향 없음 (사용자는 IDE에서 sln 재열기 1회) |
| (R-6) GHA windows file path case sensitivity | ✅ workflow_dispatch GHA 결과 PASS |

---

## 9. Final Verdict

**Match Rate: 95-98%** (sanity 검토 후) — Critical/Important 이슈 0건. SC #7, #12 두 항목 reinterpretation.

### 9.1 Sanity Review Discovery (Plan SC #7 확장)

`/pdca analyze` 직후 user의 sanity check 질문으로 추가 검토. README.md / README.ko.md / HANDOFF.md에 test 프로젝트 path 19건 미수정 발견 — Plan §2.2 OOS 가정 오류 (initial grep `head -30`로 잘림).

| 파일 | 미수정 path |
|---|---|
| README.md | `--project FastPortTestLoadRunner` × 2, 폴더 구조 × 5, `--project FastPortTestSmokeServer` × 2 = **9건** |
| README.ko.md | 동일 한국어 = **9건** |
| HANDOFF.md | `./FastPortTestLoadValidation/bin/...` = **1건** |

총 19건 follow-up commit (`cf0262c`)으로 fix. cycle scope 내에서 처리됐지만 Plan SC #12 "단일 commit"은 reinterpretation: "단일 primary commit + sanity-review follow-up = cycle 내 2 commit". 사용자 명시 승인 후 진행.

### 9.2 Updated Match Rate

| Axis | Weight | Score | 가중점수 |
|---|:-:|:-:|:-:|
| Structural | 0.20 | 95% (Plan보다 docs 영향 범위 +3 파일) | 19 |
| Functional | 0.20 | 100% | 20 |
| Contract (SC #12 reinterpretation) | 0.20 | 90% | 18 |
| Runtime (build/test/scaffold/GHA, fix 후 재검증) | 0.40 | 100% | 40 |
| **Overall** | 1.00 | | **97%** |

### 9.3 Lessons

- `head -30` 같은 grep 출력 truncate가 OOS 판정 오류를 만들 수 있음 → 본 cycle처럼 user sanity check가 안전망.
- README/HANDOFF의 폴더 구조 다이어그램은 "단순 docs 멘션"으로 보이지만 사실상 **path 참조** (사용자가 따라 입력하는 경로) — 이번처럼 path 갱신 시 함께 처리해야.
- 다음 비슷한 refactor cycle에서는 `find . -name '*.md' -exec grep -l <pattern> {} \;` 같이 head 없는 grep으로 전수 조사하는 것이 안전.

`/pdca iterate` 불필요. **`/pdca report` 진행 가능**.

---

## 10. Notes for Report Phase

- 본 cycle은 직전 4 cycles의 lessons learned가 잘 적용된 사례:
  - **production 0줄 변경 게이트** (직전 1번째, 2번째 cycle 패턴) — 100% 준수
  - **단일 commit + atomic verification** (모든 cycle 패턴) — 회귀 즉시 감지 가능
  - **GHA matrix 검증** (build.yml + scaffold.yml 도입 후 자리잡음) — 1차 PASS
- `tests/` (PDCA test infra: scaffold golden + repeat runner)와 `tests-projects/` (생산 test 프로젝트 5개) 분리 — 의도가 다른 두 surface가 명확히 구분됨.
- 다음 cycle (예: MAUI dashboard)에서 새 프로젝트가 production 또는 tests-projects 어디에 들어갈지 자명해짐.
