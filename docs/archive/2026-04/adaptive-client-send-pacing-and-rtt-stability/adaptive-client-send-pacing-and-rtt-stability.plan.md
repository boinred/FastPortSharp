# adaptive-client-send-pacing-and-rtt-stability - Plan Document

> Version: 1.0.0 | Date: 2026-04-29 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`adaptive-client-send-pacing-and-rtt-stability`는 10K focused load에서 확인된 client send-buffer pressure를 줄이되, RTT tail latency와 receive timeout을 악화시키지 않는 pacing 정책을 찾기 위한 feature다.

직전 `client-send-buffer-pressure-receive-flow-control` 결과는 두 가지를 동시에 보여줬다.

1. server-only budgeted drain은 server-side send pressure를 낮췄지만 client `NoBufferSpaceAvailable`을 줄이지 못했다.
2. load runner의 고정 per-session cap `4`는 `NoBufferSpaceAvailable`을 크게 줄였지만 RTT P95/P99와 receive timeout을 악화시켰다.

따라서 이번 feature의 목적은 고정 cap을 기본값으로 채택하는 것이 아니라, 부하 상태에 따라 client send pacing을 조절하는 정책과 검증 기준을 만드는 것이다.

### 1.2 Current Evidence

기준 비교는 `docs/load-validation-benchmark-results.md`의 client send buffer pressure follow-up 결과를 사용한다.

| Metric | Baseline | Budgeted Drain | Client Cap 4 |
|--------|---------:|---------------:|-------------:|
| Peak sessions | 10,000 / 10,000 | 10,000 / 10,000 | 10,000 / 10,000 |
| Final disconnects | 0 | 0 | 26 |
| Max pending request count | 36,653 | 36,166 | 37,509 |
| Max pending send requests | 905 | 675 | 154 |
| Max send buffer bytes | 195,683 | 162,411 | 61,567 |
| `send|IOException|NoBufferSpaceAvailable` | 7,344 | 8,370 | 905 |
| Other socket classifications | None material | None material | `receive|IOException|TimedOut = 629`, `send|IOException|Shutdown = 1` |
| Socket error rate | 0.55% | 0.82% | 0.12% |
| RTT P95 | 10,611.83 ms | 9,250.43 ms | 18,259.91 ms |
| RTT P99 | 12,949.47 ms | 10,371.08 ms | 62,738.87 ms |

핵심 해석:

- `NoBufferSpaceAvailable`은 server send backlog 하나만의 문제가 아니다.
- client/load-generator pacing이 `NoBufferSpaceAvailable`에 강하게 영향을 준다.
- 고정 cap `4`는 diagnostic evidence로 유효하지만, RTT tail과 receive timeout 관점에서 최종 해법으로 보기 어렵다.

### 1.3 PM Framing

이번 단계의 판단 질문은 네 가지다.

1. 고정 cap보다 완만한 adaptive pacing이 `NoBufferSpaceAvailable`을 줄이면서 RTT tail을 보존할 수 있는가?
2. pacing signal은 per-session outstanding request, RTT, socket error burst, scheduler drift 중 무엇을 기준으로 삼아야 하는가?
3. adaptive pacing은 load generator 전용 정책으로 둘 것인가, library-level flow-control 정책으로 일반화할 것인가?
4. 10K focused validation에서 안정성과 처리량을 동시에 비교할 수 있는 summary 기준을 어떻게 고정할 것인가?

## 2. Goals

### 2.1 Primary Goals

- [ ] 10K peak session stability를 유지한다.
- [ ] baseline `NoBufferSpaceAvailable = 7,344`를 의미 있게 낮춘다.
- [ ] cap `4`에서 관측된 RTT tail 악화와 receive timeout 증가를 피한다.
- [ ] adaptive pacing 정책의 input signal, decision rule, output action을 명확히 정의한다.
- [ ] load validation summary에서 pacing policy와 주요 결과 지표를 비교할 수 있게 한다.
- [ ] 기존 send correctness invariant를 유지한다: send queue는 `Socket.SendAsync`가 반환한 `sentSize`만큼만 drain한다.
- [ ] smoke/reduced/focused 10K 검증 흐름을 유지한다.

### 2.2 Non-Goals

- 고정 `--max-pending-requests-per-session 4`를 기본값으로 바로 채택하지 않는다.
- `sentSize`보다 많은 byte를 send queue에서 제거하지 않는다.
- 정상 응답을 silent drop하여 pass처럼 보이게 만들지 않는다.
- OS/kernel socket tuning 자동화를 만들지 않는다.
- protocol-level ACK/window negotiation을 새로 만들지 않는다.
- distributed or multi-machine load generation을 만들지 않는다.
- broad session lifecycle rewrite를 하지 않는다.

## 3. Scope

### 3.1 In Scope

- `FastPortLoadRunner`의 per-session pacing policy 설계
- fixed cap, adaptive cap, backoff, RTT-aware pacing 후보 비교
- pacing state를 summary 또는 metrics에 노출하는 방법 검토
- `FastPortLoadValidation`에서 pacing 관련 CLI 옵션 전달과 summary 표시
- focused 10K 비교 기준 정의
- smoke/reduced validation으로 correctness regression 확인
- unit tests for pacing decision behavior
- 기존 `SessionSendOptions` / server send drain 정책과의 상호작용 검토

### 3.2 Out of Scope

- production protocol 변경
- server application response 정책 변경
- server queue bound 재설계
- external network stack tuning
- load validation threshold 완화만으로 pass 처리
- MAUI/web dashboard 작업
- large-scale runner orchestration

## 4. Success Criteria

### 4.1 Functional Criteria

- [ ] adaptive pacing policy가 설계 문서에 명확히 정의된다.
- [ ] policy는 최소한 enabled/disabled 상태와 주요 threshold를 CLI 또는 options로 제어할 수 있다.
- [ ] policy 적용 여부가 summary 또는 metrics에서 식별 가능하다.
- [ ] fixed cap diagnostic run과 adaptive pacing run을 별도 output directory로 비교할 수 있다.
- [ ] smoke validation이 통과한다.
- [ ] focused 10K run에서 server metrics merge path가 계속 작동한다.
- [ ] unit tests가 pacing decision의 increase/decrease/hold behavior를 검증한다.

### 4.2 Performance Criteria

Baseline: `artifacts/load-validation/s5-send-backpressure-iterate2/summary.md`

Diagnostic cap reference: `artifacts/load-validation/s5-client-cap-4/summary.md`

| Metric | Baseline | Cap 4 Reference | Target |
|--------|---------:|----------------:|-------:|
| Peak session ratio | 100.00% | 100.00% | >= 99.00% |
| Final disconnect count | 0 | 26 | <= 100 |
| `NoBufferSpaceAvailable` count | 7,344 | 905 | <= 3,500 first target |
| Socket error rate | 0.55% | 0.12% | <= 0.55% |
| `receive|IOException|TimedOut` | 0 material | 629 | no material increase |
| RTT P95 | 10,611.83 ms | 18,259.91 ms | <= 12,000 ms first target |
| RTT P99 | 12,949.47 ms | 62,738.87 ms | <= 20,000 ms first target |
| Max pending request count | 36,653 | 37,509 | no material increase |
| Max pending send requests | 905 | 154 | no material regression |

The first target is intentionally pragmatic. A useful result does not need to match cap `4`'s NoBuffer reduction if it avoids cap `4`'s RTT/timeout regression.

## 5. Candidate Policy Directions

### 5.1 Static Cap Sweep

Run cap variants before committing to adaptive behavior.

Candidate values:

- uncapped baseline
- cap `1`
- cap `2`
- cap `4`
- cap `8`

Purpose:

- identify whether cap `4` is too aggressive or too loose;
- find whether a simple cap has an acceptable point before adaptive logic is added.

Risk:

- a fixed cap may be workload-specific and brittle.

### 5.2 AIMD-Style Per-Session Window

Adaptive Increase / Multiplicative Decrease policy:

- slowly increase per-session outstanding window while RTT and socket errors are stable;
- reduce the window quickly when `NoBufferSpaceAvailable`, receive timeout, or RTT spike is observed;
- keep min/max window bounds configurable.

Purpose:

- retain throughput under healthy conditions;
- back off during transient pressure without permanently throttling every session.

Risk:

- bad thresholds can oscillate or hide pressure.

### 5.3 RTT-Aware Send Delay

Introduce a small per-session delay when observed RTT tail exceeds a threshold.

Purpose:

- protect tail latency while avoiding a hard outstanding cap.

Risk:

- RTT signal may lag behind pressure and react too late.

### 5.4 Error-Burst Backoff

Back off only after classified send socket errors such as `NoBufferSpaceAvailable`.

Purpose:

- react directly to the dominant failure signal.

Risk:

- this is reactive, so it may reduce repeated errors but not prevent the first pressure burst.

## 6. Measurement Plan

### 6.1 Required Validation Runs

Design should choose the exact run matrix, but the plan expects at least these categories:

1. reduced smoke validation with pacing enabled;
2. focused 10K baseline-compatible run with adaptive pacing disabled;
3. focused 10K adaptive pacing run;
4. optional cap sweep run if design cannot choose thresholds confidently.

Suggested output directories:

- `artifacts/load-validation/adaptive-pacing-smoke`
- `artifacts/load-validation/s5-adaptive-pacing-baseline`
- `artifacts/load-validation/s5-adaptive-pacing-window`
- `artifacts/load-validation/s5-adaptive-pacing-cap-sweep`

### 6.2 Required Comparison Fields

- peak session ratio
- final disconnect count
- max pending request count
- max pending send requests
- server send backpressure events
- max send buffer bytes
- rejected send requests/bytes
- drain yield count/max queued bytes
- `send|IOException|NoBufferSpaceAvailable`
- `receive|IOException|TimedOut`
- socket error rate
- RTT P95/P99
- max scheduler drift
- pacing policy name and effective threshold/window range

## 7. Schedule

| Phase | Target Date | Status |
|-------|-------------|--------|
| Plan | 2026-04-29 | Completed |
| Design | 2026-04-29 | Pending |
| Implementation | TBD | Pending |
| Analyze | TBD | Pending |
| Report | TBD | Pending |

## 8. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Adaptive policy hides failures by throttling too much | High | Medium | Report throughput, RTT, timeout, and error metrics together |
| Cap sweep overfits to one machine | Medium | Medium | Treat fixed cap as evidence, not final design, unless data is clearly stable |
| RTT signal reacts too late | Medium | Medium | Combine RTT with outstanding request and socket-error signals |
| Aggressive backoff causes receive timeout | High | Medium | Use cap `4` timeout result as a guardrail |
| Too many CLI options make validation confusing | Medium | Medium | Keep options explicit and summarize effective policy in output |
| Server-side send improvements regress | High | Low | Keep max pending send and max send buffer as success criteria |
| Partial send correctness regresses | High | Low | Preserve `sentSize`-only drain tests |

## 9. Architecture Considerations

- `FastPortLoadRunner` is the first place to implement pacing because the remaining evidence points to load-generator pressure.
- `LibNetworks` should not receive generic flow-control changes until load-runner pacing data proves the policy is broadly useful.
- `FastPortLoadValidation` should make pacing configuration auditable in command generation and summaries.
- Existing observed metric naming and server metrics merge contract must remain stable.
- The design should avoid mixing server-flow-control and client-pacing changes in the same comparison run.

## 10. Open Questions For Design

- Should the first implementation be a cap sweep, AIMD-style adaptive window, or error-burst backoff?
- What default min/max outstanding window should be used?
- Which signal should trigger decrease: NoBuffer count, RTT spike, timeout, pending request, or a combined score?
- Should the policy be global across all sessions or per-session?
- Should pacing delay be time-based, window-based, or both?
- How should summary output represent effective adaptive window over time?
- What is the minimum 10K evidence needed before considering the policy successful?

## 11. References

- `docs/load-validation-benchmark-results.md`
- `docs/archive/2026-04/client-send-buffer-pressure-receive-flow-control/client-send-buffer-pressure-receive-flow-control.report.md`
- `docs/archive/2026-04/server-send-backpressure-queue-drain-optimization/server-send-backpressure-queue-drain-optimization.report.md`
- `FastPortLoadRunner/LoadSession.cs`
- `FastPortLoadRunner/LoadRunnerOptions.cs`
- `FastPortLoadValidation/LoadRunnerCommandBuilder.cs`
- `FastPortLoadValidation/LoadValidationSummaryWriter.cs`
- `LibNetworks/Sessions/BaseSession.cs`
- `LibNetworks/Sessions/SessionSendOptions.cs`
