# move-test-projects-to-testprojects-folder Design

> **Summary**: Option A — Textual edit. `git mv` 5개 디렉토리 후 `FastPortSharp.sln` (5 lines) + 4개 csproj ProjectReference (~14 lines) + scripts/cloud (5 lines) + docs (3 lines)를 직접 수정. GUID 보존, diff 깔끔, 단일 commit.
>
> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-10
> **Status**: Draft
> **Plan**: [../../01-plan/features/move-test-projects-to-testprojects-folder.plan.md](../../01-plan/features/move-test-projects-to-testprojects-folder.plan.md)

---

## Context Anchor

| Key | Value |
|-----|-------|
| **WHY** | main green 안정 + MAUI dashboard cycle 진입 전 production/test surface 정리. |
| **WHO** | Repo committer + 외부 contributor + AI agent. |
| **RISK** | (R-1) ProjectReference path 누락 / (R-3) sln 무결성 / (R-4) git history 흐름 |
| **SUCCESS** | build 0/0, test 139/139, scaffold CI green, scripts/cloud 동작, `git log --follow` 동작 |
| **SCOPE** | 5 dirs git mv + sln + csproj × 4 + scripts/cloud × 3 + docs × 1 = ~27 lines, 단일 commit |

---

## 1. Overview

### 1.1 Design Goals

1. **단순성**: textual edit으로 변경 line 명확. dotnet CLI 호출 0.
2. **GUID 보존**: sln의 project GUID 재발급 0 (textual edit이라 자동).
3. **Diff 깔끔**: sln line 순서 변동 0, csproj 안 path만 ../→../..로 1글자 변경.
4. **단일 commit**: 부수 갱신까지 함께 (Plan §7.1 결정).
5. **회귀 검증 단계**: build → test → scaffold runner → git log --follow 4단계 모두 통과.

### 1.2 Design Principles

- **Atomic refactor**: 한 commit 안에 모든 path 갱신. 중간 상태에서 build 실패 허용 안 함 (commit 시점에 검증).
- **No tool magic**: `dotnet sln`, `dotnet add reference` 같은 CLI 호출 없이 file system + text edit으로만 작업.
- **History continuity**: `git mv` 명시 사용 → rename detection.

---

## 2. Architecture Options (Selected)

### 2.0 Comparison

| Criteria | **Option A: Textual** | Option B: Dotnet CLI | Option C: 조합 |
|---|:-:|:-:|:-:|
| Sln GUID 보존 | ✅ | ❌ (재발급 가능) | △ |
| Diff 깔끔 | ✅ (line별 명확) | ❌ (line 순서 변동) | △ |
| Sln 무결성 | △ (사람 책임) | ✅ (CLI 보장) | △ |
| Effort | Low | Medium | High |
| Reversibility | High (`git revert`) | High | High |
| **Recommendation** | **Selected** | | |

### 2.1 Selected: Option A — Textual edit

**Rationale**:
- 변경 line ~27로 작고 명확. textual edit이 가장 추적성 높음.
- sln은 stable text format이라 직접 편집해도 안전 (.NET이 이를 정상 처리해 온 역사 길음).
- `dotnet sln remove/add`는 GUID 재발급 + sln line 순서 변동 가능 → diff 노이즈 ↑.
- 직전 4 cycle 패턴 (textual + git diff 검증)과 일관.

### 2.2 Component Diagram

```
[BEFORE]
FastPortSharp/
├── FastPortServer/                  ← production
├── FastPortClient/                  ← production
├── FastPortGameServerTemplate/      ← production
├── FastPortGameServerTemplate.SampleClient/
├── LibCommons/                      ← production
├── LibNetworks/                     ← production
├── Protocols/                       ← production
├── FastPortTests/                   ← test
├── FastPortTestLoadRunner/          ← test (executable)
├── FastPortTestLoadValidation/      ← test (executable)
├── FastPortTestSmokeServer/         ← test (executable)
└── LibTestTelemetry/                ← test (lib)

[AFTER]
FastPortSharp/
├── FastPortServer/                  ← production (unchanged)
├── FastPortClient/                  ← production
├── FastPortGameServerTemplate/      ← production
├── FastPortGameServerTemplate.SampleClient/
├── LibCommons/                      ← production
├── LibNetworks/                     ← production
├── Protocols/                       ← production
└── tests-projects/                  ← NEW grouping
    ├── FastPortTests/
    ├── FastPortTestLoadRunner/
    ├── FastPortTestLoadValidation/
    ├── FastPortTestSmokeServer/
    └── LibTestTelemetry/
```

---

## 3. Data Model

해당 없음 (refactor only, no schema change).

---

## 4. API Specification

해당 없음.

---

## 5. UI/UX Design

해당 없음.

---

## 6. Error Handling

| 단계 | 실패 모드 | 복구 |
|---|---|---|
| `git mv` | 권한 / 충돌 | 즉시 stop, 원인 확인. 본 cycle은 fresh working tree에서 시작이라 충돌 가능성 낮음. |
| sln textual edit | 잘못된 path | `dotnet build` 즉시 실패. `git checkout -- FastPortSharp.sln` 후 재시도. |
| csproj edit | path mismatch | `dotnet build` 실패. 단일 csproj `git checkout` 후 재시도. |
| scripts/docs edit | grep miss | build/test에는 영향 없음. 본 cycle SC §4.1에서 명시 검증. |
| GHA build.yml fail | sln/csproj 누락 path | rerun 후 fail-fast. 새 commit으로 hotfix. |

---

## 7. Security Considerations

해당 없음.

---

## 8. Test Plan

### 8.1 Local Verification (Do phase 끝에 일괄)

| # | 검증 | 명령 | 기대 |
|---|---|---|---|
| L1 | 디렉토리 이동 확인 | `ls -d tests-projects/*` | 5개 (`FastPortTests`, `FastPortTestLoadRunner`, `FastPortTestLoadValidation`, `FastPortTestSmokeServer`, `LibTestTelemetry`) |
| L2 | 루트에 test 디렉토리 0 | `ls FastPortTest* LibTestTelemetry 2>&1` | "No such file" |
| L3 | 빌드 | `dotnet build FastPortSharp.sln -c Release` | 0 warn / 0 err |
| L4 | 전체 테스트 | `dotnet test ... --no-build` | 139/0/0 |
| L5 | scaffold runner | `tests/scaffold/run.sh` | 7/7 PASS |
| L6 | scaffold runner (ps1) | `pwsh -File tests/scaffold/run.ps1` | 7/7 PASS |
| L7 | production diff = 0 | `git diff -- LibCommons/ LibNetworks/ Protocols/ FastPortServer/ FastPortClient/ FastPortGameServerTemplate/ FastPortGameServerTemplate.SampleClient/` | 0 lines |
| L8 | git history 보존 | `git log --follow tests-projects/FastPortTests/FastPortTests.csproj` | rename detection 작동 |

### 8.2 CI Verification

- `gh run list --workflow=build.yml --limit 1` 1차 push에서 3-OS PASS
- `gh run list --workflow=scaffold.yml` (test 프로젝트 path 미참조)는 trigger 없음 (path filter상)

---

## 9. Clean Architecture (.NET 적용)

해당 없음.

---

## 10. Coding Convention Reference

### 10.1 File Layout

이동 후 root는 production 7개 + `tests-projects/` 1개. PDCA test infra(`tests/`)와 분리 유지.

### 10.2 Naming

- 폴더: `tests-projects/` (kebab-case, Plan 결정)
- 프로젝트명: 기존 그대로 (`FastPortTests`, `FastPortTestLoadRunner` 등)

---

## 11. Implementation Guide

### 11.1 File Structure

이동 후:
```
FastPortSharp/
├── FastPortSharp.sln               ← MODIFY (5 lines)
├── tests-projects/                  ← NEW (git mv 결과)
│   ├── FastPortTests/
│   │   └── FastPortTests.csproj    ← MODIFY (../../로 ../ 변경, 6 lines)
│   ├── FastPortTestLoadRunner/
│   │   └── FastPortTestLoadRunner.csproj  ← MODIFY (3 lines)
│   ├── FastPortTestSmokeServer/
│   │   └── FastPortTestSmokeServer.csproj ← MODIFY (4 lines)
│   ├── FastPortTestLoadValidation/
│   │   └── FastPortTestLoadValidation.csproj ← MODIFY (1 line)
│   └── LibTestTelemetry/
│       └── LibTestTelemetry.csproj  ← UNCHANGED (out-edge 0)
├── scripts/cloud/
│   ├── runner-smoke.sh              ← MODIFY (2 lines)
│   ├── server-start.sh              ← MODIFY (1 line)
│   └── runner-10k.sh                ← MODIFY (2 lines)
└── docs/staged-load-validation-test-guide.md  ← MODIFY (3 lines)
```

### 11.2 Implementation Order

| 순서 | 작업 | 검증 |
|---|---|---|
| 1 | `git mv` 5 디렉토리 → `tests-projects/` 안으로 | `ls -d tests-projects/*` |
| 2 | `FastPortSharp.sln` 5 project line의 path 갱신 (`FastPortTests\` → `tests-projects\FastPortTests\` 등). Windows path separator `\` 그대로. | textual diff 명확 |
| 3 | 4개 csproj 안 ProjectReference path 갱신: `..\LibCommons\` → `..\..\LibCommons\` 등 (LibTestTelemetry 제외) | grep으로 잔존 ../ 확인 |
| 4 | `scripts/cloud/runner-smoke.sh` 2 line: `FastPortTestLoadRunner/` → `tests-projects/FastPortTestLoadRunner/` 등 | shellcheck/syntax |
| 5 | `scripts/cloud/server-start.sh` 1 line, `scripts/cloud/runner-10k.sh` 2 line 동일 갱신 | |
| 6 | `docs/staged-load-validation-test-guide.md` 3 line | textual |
| 7 | **검증 single shot**: `dotnet build -c Release && dotnet test --no-build && tests/scaffold/run.sh` | 모두 PASS |
| 8 | `git diff -- <production>` = 0 확인 | 0 lines |
| 9 | `git log --follow tests-projects/FastPortTests/FastPortTests.csproj` 확인 | history 추적 |
| 10 | 단일 commit + push | GHA build.yml trigger |

### 11.3 Session Guide

> 단일 세션 ≤ 25 turn 예상. `--scope` 분할 불필요.

| Module | Scope Key | Description | Estimated Turns |
|---|---|---|:-:|
| Move dirs + sln | `move-and-sln` | git mv 5 + sln 5 line | 5-7 |
| csproj path | `csproj-paths` | 4 csproj 안 ProjectReference 갱신 | 5-7 |
| Scripts/docs | `scripts-docs` | scripts/cloud × 3 + docs × 1 | 4-6 |
| Verify | `verify` | build/test/scaffold/git log/diff 4단계 | 3-5 |

#### Recommended Session Plan

| Session | Phase | Scope | Turns |
|---|---|---|:-:|
| 1 | Plan + Design | 전체 | 15 (already done) |
| 2 | Do (전체) | (no --scope) | 17-25 |
| 3 | Check + Report + Archive | 전체 | 12-15 |

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 0.1 | 2026-05-10 | Initial design — Option A textual edit, 5 modules / single session | boinred |
