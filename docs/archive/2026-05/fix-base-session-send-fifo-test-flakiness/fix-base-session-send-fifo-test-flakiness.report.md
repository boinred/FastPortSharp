# fix-base-session-send-fifo-test-flakiness Completion Report

> **Project**: FastPortSharp
> **Author**: boinred
> **Date**: 2026-05-10
> **Status**: ✅ COMPLETED — Match Rate 100%
> **Cycle Duration**: 2026-05-10 (단일 일자, ≈ 2시간)

---

## Executive Summary

| 관점 | 내용 |
|---|---|
| **Problem** | `BaseSessionSendPolicyTests` 두 메서드가 send queue worker와 driver 사이의 race로 GHA macOS에서 비결정적으로 fail. 직전 commit `6693573` (build.yml 도입)에서 처음 노출. |
| **Solution** | Option C — Test Logic Refactor. white-box 단언(`sendBuffers.Count >= 2`)을 black-box `BatchedFifoObserver` 기반 누적 wire bytes 검증으로 대체. **Production code 0줄 변경**. |
| **Function/UX/Effect** | 두 테스트가 batching 구현 디테일이 아닌 observable wire outcome만 검증. BaseSession이 어떤 batching 정책을 쓰든 통과 → future-proof. |
| **Core Value** | CI flake 0 → main green 신뢰 회복. 직전 cycle(scaffold-scripts)의 build.yml 1차 PASS와 더해 PR 머지 신호 클리어. |

### Value Delivered (실측)

| 지표 | 목표 | 실측 |
|---|---|---|
| BaseSession.cs diff | 0줄 | **0줄** (게이트 met) |
| macOS GHA 5회 연속 PASS | 5/5 | **5/5** (각 23-29s) |
| 로컬 50회 stress | 0 fail | **50/50** |
| 전체 139 tests | 139/0/0 | **139/0/0** |
| Plan SC | met | **10 / 10 met** |
| 변경 파일 수 | ≤ 2 | **2 정확** (test 파일 + stress runner) |

---

## 1. PRD → Plan → Design → Code Journey

### 1.1 PRD (Why)

직전 cycle(`game-server-template-scaffold-scripts`) 마무리에서 build.yml CI를 처음 도입했을 때 GHA macos-latest에서 SendPolicy 테스트가 비결정적으로 실패. main 머지 신호가 빨간 상태로 다음 PR을 막을 위험이 있어 **결정적 CI**로 복원이 cycle 동기.

Persona는 본인 + 외부 contributor + AI agent (모두 결정적 CI를 필요).

### 1.2 Plan (What/Constraints)

10개 Success Criteria:
- DoD 8: hook 추가, 2 tests 수정, BaseSession 0줄, build 0/0, test 139/139, 50회 stress, macOS 5/5 PASS, ubuntu/windows 회귀 0
- Quality 2: 한국어 주석, 변경 파일 ≤ 2

핵심 결정: Action delegate 선호, 자매 테스트도 선제 수정, 로컬 50 + CI 5 rerun.

### 1.3 Design (How)

**Option C — Test Logic Refactor** 선택. Rationale:
- Plan SC §4.1 "BaseSession.cs 0줄 변경" 게이트가 Plan §7.1 "Action delegate" 선호보다 우선.
- 본 race는 테스트의 **over-specification**(batching 디테일)이지 production 결함이 아니므로 fix는 test 한정이 적절.

`BatchedFifoObserver` (private nested class) + `BuildExpectedWire` helper + 두 test rewrite로 race 제거.

### 1.4 Implementation (단일 세션)

Single session, 5 task, 30분 추정. 실제 구현 후 즉시 검증 → CI 5회 rerun.

| Task | 결과 |
|---|---|
| BatchedFifoObserver 추가 (~80줄) | ✅ |
| Test #1 재작성 (FirstPacketWireLength gate) | ✅ |
| Test #2 재작성 (ChunkLimit per-batch + BatchCount ≥ 2) | ✅ |
| repeat-tests.sh 작성 (50회 race-detector) | ✅ |
| 검증 (139 tests + 50회 + git diff + GHA 5 rerun) | ✅ |

---

## 2. Plan Success Criteria — Final Status

| # | Criterion | Status | Evidence |
|---|---|:-:|---|
| 1 | TestSession에 동기화 hook (Plan §7.1 "Action delegate") | ✅ Met (재해석) | Design checkpoint에서 Option C 채택 → hook 미도입, observer로 대체. 0줄 게이트가 우선. |
| 2 | 영향 테스트 2건 수정 | ✅ | line 222 + 290 모두 observer 기반 재구성 |
| 3 | `BaseSession.cs` git diff 0줄 | ✅ | `git diff -- LibNetworks/Sessions/BaseSession.cs` = 0 |
| 4 | `dotnet build -c Release` 0/0 | ✅ | 6.27s, 0 warning / 0 error |
| 5 | `dotnet test` 139/0/0 | ✅ | 두 차례 측정 모두 통과 |
| 6 | 로컬 50회 반복 stress 0 fail | ✅ | `tests/scripts/repeat-tests.sh 50` → 50/50 |
| 7 | GHA macos 5회 연속 PASS | ✅ | run 25616986247 attempts #1-#5 모두 macOS PASS (23-29s) |
| 8 | GHA ubuntu / windows 회귀 0 | ✅ (with note) | ubuntu 5/5. windows 4/5 — 1 fail은 다른 테스트 (out-of-scope) |
| 9 | hook 의미 한국어 주석 | ✅ | observer + 두 테스트 진입부에 design ref 주석 |
| 10 | 변경 파일 ≤ 2 | ✅ | 정확히 2개 |

**Overall: 10 / 10 (100%)**

---

## 3. Key Decisions & Outcomes

| Decision | Source | Outcome |
|---|---|---|
| Production 코드 0줄 변경 게이트 | PRD §3 / Plan §3.2 / Design §1.1 | ✅ `git diff` 0줄 |
| 자매 테스트 선제 수정 | Plan checkpoint | ✅ Test #2 동일 패턴 race 사전 제거 |
| 로컬 50 + CI 5 rerun | Plan checkpoint | ✅ 두 검증 모두 통과 |
| **Option C — Test refactor** (Action delegate 대신) | Design checkpoint | ✅ hook 도입 불필요, BaseSession 무결 유지 |
| `BatchedFifoObserver` private nested class + `lock` | Design §3.1 | ✅ thread-safe 누적 |
| `BuildExpectedWire` helper (Little-endian UInt16 prefix) | Design §3.1 | ✅ wire layout deterministic |
| Test #1 phase-gated (FirstPacketWireLength=4) | Design §3.3 | ✅ partial completion semantics 보존 |
| Test #2 per-batch ChunkLimit + BatchCount ≥ 2 | Design §3.4 | ✅ chunk 정책 검증 + batching 동작 자체는 여전히 단언 |

**Deviations**: 1건 reinterpretation
- Plan §7.1 "Action delegate" 선호 → Design Option C로 superseded. Plan §4.1의 "0줄 게이트"가 우선. 사용자 명시 승인.

---

## 4. Final Match Rate (from Analysis)

| Axis | Weight | Score |
|---|:-:|:-:|
| Structural | 0.15 | 100% |
| Functional | 0.25 | 100% |
| Contract | 0.25 | 100% |
| Runtime | 0.35 | 100% |
| **Overall** | 1.00 | **100%** |

Critical / Important issues: **0**

---

## 5. Artifacts Inventory

### 5.1 Modified Files (count: 1)

- `FastPortTests/BaseSessionSendPolicyTests.cs`
  - +112 줄 (`BatchedFifoObserver` ~80줄 + `BuildExpectedWire` ~22줄 + 주석)
  - -47 줄 (기존 white-box 단언 제거)
  - 두 테스트 본문 rewrite

### 5.2 New Files (count: 1)

- `tests/scripts/repeat-tests.sh` (~30줄, race-detector helper, executable)

### 5.3 PDCA Documents

- `docs/00-pm/fix-base-session-send-fifo-test-flakiness.prd.md`
- `docs/01-plan/features/fix-base-session-send-fifo-test-flakiness.plan.md`
- `docs/02-design/features/fix-base-session-send-fifo-test-flakiness.design.md`
- `docs/03-analysis/fix-base-session-send-fifo-test-flakiness.analysis.md`
- `docs/04-report/fix-base-session-send-fifo-test-flakiness.report.md` (this file)

### 5.4 Commits

- `a2900b5` Stabilize BaseSession send-batch tests (Option C: test refactor) — 5 files / +1034 / -47 (PRD/Plan/Design/test/runner 모두 포함)

### 5.5 CI Runs

- `25616986247` — build.yml, 5 attempts: macOS 5/5 + ubuntu 5/5 + windows 4/5

---

## 6. Lessons Learned

### 6.1 What worked well

- **Black-box assertion 전환**: white-box `sendBuffers.Count >= 2`을 누적 wire bytes 검증으로 바꾸자, race 자체가 무의미해짐. 같은 wire 결과면 1×N batch이든 N×1 batch이든 통과.
- **Production 0 변경 게이트**: BaseSession에 hook을 추가하지 않은 결정 덕분에 production timing/throughput 영향 가능성 zero, 그리고 future BaseSession 리팩터에 robust한 테스트로 격상.
- **Local 50× stress**: GHA 5×보다 강력한 race detector. macOS 5/5 PASS 결과를 50× 로컬로 미리 검증한 덕에 GHA 1차부터 PASS.
- **선제적 자매 테스트 수정**: Test #2 (`BatchedSendRespectsChunkLimit`)도 동일 패턴 race 가능성. 함께 수정해 follow-up cycle 1건 절약.

### 6.2 Surprises / Gotchas

- **MSTest `Sort-Object` 식별 불가**: 무관 (이번 cycle은 MSTest 그대로 사용). 단, `Interlocked.Increment(ref batchExceededLimit)` 패턴이 lock-free 카운터로 깔끔.
- **Plan→Design rationale conflict**: Plan §7.1 "Action delegate"와 Plan §4.1 "0줄 게이트"가 잠재 conflict. Design checkpoint에서 사용자 명시 승인으로 해소. 향후 Plan에서 이런 종류의 conflict 가능성을 명시하는 패턴이 유용.
- **GHA windows 별도 flake 노출**: 의도와 다르게 다른 flaky test (`ServerTelemetryExport`)가 attempt #2에서 fail. 본 cycle scope 외이지만, build.yml CI가 처음 가동되며 잠재 flake 2종을 한꺼번에 surface. CI 신뢰도 강화의 가치 입증.

### 6.3 Future improvements (별도 cycle 후보)

| 항목 | 우선순위 | 비고 |
|---|---|---|
| `ServerTelemetryExportBackgroundService_WritesServerObservedJsonl` flake (windows) | Low | Analysis §10.1. 같은 timing-dependent 패턴. 동일한 black-box refactor 접근 가능 |
| `BatchedFifoObserver`를 `FastPortTests/Helpers/`로 추출 (다른 SendPolicy 변형 테스트에서 재사용) | Low | YAGNI: 현재 사용처 2곳만, 추출은 사용처 ≥ 3 시점에 |
| stress runner를 GHA `workflow_dispatch`로 노출 | Low | 100×/500× stress 옵션 |
| shellcheck를 build.yml에 통합 | Low | scaffold cycle의 quality criteria로도 후순위 |

---

## 7. Cycle Boundaries

### 7.1 In Scope (delivered)

- `BaseSessionSendPolicyTests.cs` 두 테스트의 race-free refactor
- `BatchedFifoObserver` + `BuildExpectedWire` helper
- `tests/scripts/repeat-tests.sh` race-detector
- 로컬 50회 + GHA macOS 5회 연속 검증
- BaseSession.cs 0줄 변경 게이트

### 7.2 Explicitly Out of Scope

- `ServerTelemetryExport*` flaky test (별도 cycle)
- production code의 동기화 정책 변경
- 다른 timing-dependent 테스트 audit
- shellcheck/PSScriptAnalyzer 같은 lint 추가 (이전 cycle과 동일)

---

## 8. Recommended Next Steps

1. **Archive**: `/pdca archive fix-base-session-send-fifo-test-flakiness` — 5 PDCA 문서를 `docs/archive/2026-05/` 로 이동, archive index 갱신.
2. **Commit + push**: report + analysis 문서 푸시 (코드는 이미 commit `a2900b5`로 푸시됨).
3. (선택) `fix-server-telemetry-export-jsonl-flush-flakiness` cycle 시작 — 발견된 다른 flake 처리. 같은 Option C 패턴 적용 가능.
4. (선택) HANDOFF.md에 본 cycle 결과 한 줄 요약 추가 (직전 cycle 패턴 따라).

---

## Version History

| Version | Date | Changes | Author |
|---|---|---|---|
| 1.0 | 2026-05-10 | Initial completion report. Match Rate 100%, 10/10 SC met, BaseSession.cs 0줄 변경, macOS 5/5 GHA + 50/50 local stress. | boinred |
