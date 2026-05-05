# cloud-receive-timeout-rtt-tail-stability - Design Document

> Version: 1.0.0 | Date: 2026-05-05 | Status: Completed
> Level: Starter | Plan: docs/01-plan/features/cloud-receive-timeout-rtt-tail-stability.plan.md

---

## 1. Overview

`cloud-receive-timeout-rtt-tail-stability`는 Azure server/local runner 10K 검증에서 발생한 receive timeout, connection reset, final disconnect, RTT tail을 설명 가능한 신호로 분해하는 작업이다.

이번 설계의 핵심은 엔진 send path를 먼저 고치는 것이 아니다. 직전 cloud baseline에서는 client `send-write` 평균/최대가 낮고 server send backpressure가 거의 없었기 때문에, 우선 `receive` 대기와 connection lifecycle을 더 정확히 계측한다.

## 2. Baseline Problem Shape

기준 run은 `artifacts/load-validation/cloud-server-runner-split/s5-random-10k/summary.md`의 `20260505-140926-staged`다.

| Signal | Baseline |
|--------|---------:|
| Target sessions | `10,000` |
| Peak sessions | `9,337` |
| Peak ratio | `93.37%` |
| Max TPS | `1,085.41` |
| Final disconnects | `752` |
| Socket error rate | `0.28%` |
| RTT P95 | `106,216.65ms` |
| RTT P99 | `274,206.02ms` |
| Session RTT p95-of-p95 | `222,702.93ms` |
| `receive\|IOException\|ConnectionReset` | `495` |
| `receive\|IOException\|TimedOut` | `257` |
| `connect\|SocketException\|TimedOut` | `56` |
| `receive-header` avg/max | `3,269.27ms / 384,958.03ms` |
| `receive-body` avg/max | `2,571.06ms / 396,937.01ms` |

Collected server metrics showed `socketErrors=0`, `sendBackpressureEvents=0`, `sendRejected=1`, max pending server sends `155`, max server send buffer `62,049`, and post-run lingering state `currentSessions=51`, `pendingSendRequests=27`.

해석상 가장 중요한 점은 client는 receive reset/timeout을 보고하지만 server는 socket error를 거의 보지 않는다는 것이다. 따라서 다음 구현은 "왜 receive가 끝났는지"와 "서버 세션이 왜 남았는지"를 분리해서 보여줘야 한다.

## 3. Current Gaps

| Area | Current state | Gap |
|------|---------------|-----|
| Runner receive loop | `ReadExactAsync` returns `false` when `ReadAsync` returns `0` | EOF/partial read가 명시적인 close reason으로 집계되지 않는다 |
| Runner phase lifecycle | send/receive sibling phase is cancelled after one completes | normal completion, fault, cancellation, EOF가 summary에서 분리되지 않는다 |
| Client metrics | socket class, RTT, operation duration are recorded | zero-byte read, partial body, outstanding-at-close, phase completion reason이 없다 |
| Server telemetry | accept/disconnect/send/backpressure/socket counters exist | disconnect reason, lingering session age, active session cleanup state가 부족하다 |
| Split cloud summary | server metrics are collected later by SSH | validation summary가 server-side tail/lingering 상태를 바로 설명하지 못한다 |
| Cloud run hygiene | server start/connectivity scripts exist | 10K 전 server restart, zero/expected session check, stale metrics guard가 명확하지 않다 |

## 4. Target Architecture

### 4.1 Responsibility Boundary

| Module | Responsibility |
|--------|----------------|
| `FastPortTestLoadRunner` | client-side receive close/phase reason을 수집하고 observed metrics JSONL에 기록 |
| `FastPortTestLoadValidation` | close/phase/server-lifecycle 신호를 `summary.json`과 `summary.md`에 노출 |
| `FastPortTestSmokeServer` | test server lifecycle과 echo response drop/close 상황을 설명 가능한 로그 또는 telemetry로 기록 |
| `LibNetworks.Telemetry` | 기존 engine-level accept/disconnect/send/socket counter 유지, 꼭 필요한 최소 확장만 허용 |
| `scripts/cloud` | server restart/readiness/artifact collection flow를 repeatable하게 만든다 |
| Docs | cloud baseline과 candidate 결과를 같은 기준으로 비교한다 |

첫 구현은 runner/validation/scripts를 우선한다. `LibNetworks` 또는 `BaseSession` 변경은 runner와 server test telemetry만으로 원인이 부족할 때만 진행한다.

### 4.2 Diagnostic Flow

```text
server restart
  -> verify listen state
  -> verify stale session state is zero or explicitly expected
  -> smoke run
  -> collect runner metrics and server metrics
  -> staged load ladder
  -> merge or summarize server metrics with runner summary
  -> compare failure shape against baseline
```

10K는 바로 반복하지 않는다. smoke, 1K, 3K, 5K에서 close/timeout shape가 안정적으로 설명된 뒤 focused 10K를 실행한다.

## 5. Data Contract

### 5.1 Client Observed Metrics

`ClientObservedMetricsSnapshot`에 다음 계열의 필드를 추가하는 것을 목표로 한다. 이름은 구현 중 기존 naming style에 맞춰 확정한다.

| Field group | Example keys | Meaning |
|-------------|--------------|---------|
| Receive close counts | `receive-header:eof`, `receive-body:eof`, `receive-body:partial` | `ReadAsync` zero-byte read와 partial body 종료 분류 |
| Phase completion counts | `send:completed`, `receive:faulted`, `receive:cancelled`, `receive:eof` | `RunDuplexAsync`의 phase 종료 원인 |
| Outstanding at close | summary stats or counts | close/reset/timeout 시점의 outstanding request 상태 |
| Operation duration | existing `send-write`, `receive-header`, `receive-body` | 기존 duration 계약 유지 |
| Socket class counts | existing phase/type/code/class | 기존 hard guardrail 유지 |

EOF는 반드시 socket exception과 구분한다. EOF 자체는 peer close의 관측 결과이고, timeout/reset과 같은 실패 원인으로 섞으면 원인 분석이 어려워진다.

### 5.2 Validation Summary

`FastPortTestLoadValidation`은 새 client observed fields를 다음 위치에 반영한다.

| Output | Required change |
|--------|-----------------|
| `summary.json` | close reason, phase completion, outstanding-at-close 요약 추가 |
| `summary.md` | socket error table 근처에 receive close/phase reason table 추가 |
| evaluator thresholds | receive timeout/reset/final disconnect hard failure는 유지 |
| candidate comparison docs | baseline 대비 timeout/reset/final disconnect/RTT tail 변화 기록 |

### 5.3 Server-Side Signals

1차 목표는 test path에서 충분한 설명력을 확보하는 것이다.

| Signal | Preferred location | Notes |
|--------|--------------------|-------|
| Active sessions before run | `scripts/cloud` readiness plus server metrics tail | stale session contamination 방지 |
| Post-run lingering sessions | server metrics tail and collected artifacts | `currentSessions`, pending sends, buffer bytes 유지 |
| Echo response drop | `FastPortTestSmokeServer` telemetry/log | send rejection/backpressure와 client timeout 연결 |
| Disconnect reason | test server telemetry if feasible, otherwise follow-up | engine API 변경이 필요하면 별도 판단 |
| Session lifetime age | follow-up candidate | lingering 원인이 남을 때만 추가 |

`IServerTelemetry` 확장은 blast radius가 있으므로 최소화한다. 단순히 summary 설명에 필요한 값이 test server에서 계산 가능하면 `FastPortTestSmokeServer` 쪽에 먼저 둔다.

## 6. Implementation Order

1. Add runner close/phase reason metrics.
   - `LoadSession.ReadExactAsync`의 `ReadAsync == 0` 경로를 operation별 EOF/partial close reason으로 기록한다.
   - `RunDuplexAsync`의 send/receive completion, fault, cancellation 결과를 metrics에 남긴다.
   - timeout/reset socket classification은 기존 로직을 유지한다.

2. Extend metrics snapshots and tests.
   - `MetricsCollector`와 snapshot record에 close/phase counters를 추가한다.
   - observed JSONL serialization/deserialization compatibility를 테스트한다.

3. Surface diagnostics in validation output.
   - `LoadValidationEvaluator`와 `LoadValidationSummaryWriter`가 close/phase counters를 summary에 표시한다.
   - hard failure 기준은 완화하지 않는다.

4. Harden cloud run hygiene.
   - 10K 전 server restart/readiness 절차를 명확히 한다.
   - server listen state와 stale active-session 상태를 확인한다.
   - `bash -n scripts/cloud/*.sh`로 script syntax를 검증한다.

5. Add minimal server lifecycle diagnostics only if needed.
   - runner/validation/script 변화만으로 reset/timeout/lingering 원인이 설명되지 않으면 test server lifecycle counter를 추가한다.
   - engine telemetry contract 변경이 필요한 경우 별도 rationale을 남긴다.

6. Run staged validation.
   - smoke -> 1K -> 3K -> 5K -> focused 10K 순서로 진행한다.
   - 각 단계에서 final disconnect, receive timeout/reset, RTT P95/P99, phase reason을 baseline과 비교한다.

## 7. File Change Plan

| File | Planned change |
|------|----------------|
| `FastPortTestLoadRunner/LoadSession.cs` | receive EOF/partial close와 phase lifecycle recording |
| `FastPortTestLoadRunner/Metrics.cs` | close/phase counters and snapshot fields |
| `FastPortTestLoadRunner/LoadRunner.cs` | observed metrics export path 유지, 새 counters 포함 |
| `FastPortTestLoadValidation/LoadValidationEvaluator.cs` | new observed fields aggregate and threshold context |
| `FastPortTestLoadValidation/LoadValidationSummaryWriter.cs` | markdown summary tables for close/phase reasons |
| `FastPortTestSmokeServer/Sessions/FastPortTestSmokeClientSession.cs` | only if needed: echo drop or lifecycle diagnostic counter/log |
| `LibNetworks/Telemetry/ServerTelemetry.cs` | avoid unless server lifecycle cause cannot be observed elsewhere |
| `scripts/cloud/*.sh` | restart/readiness/collection flow clarification |
| `docs/azure-server-runner-split-load-validation-runbook.md` | updated cloud validation sequence |
| `docs/load-validation-benchmark-results.md` | update only after verified runtime result |

## 8. Test Plan

### 8.1 Unit Tests

| Test area | Required coverage |
|-----------|-------------------|
| `LoadSession` receive loop | zero-byte header read, zero-byte body read, partial body close classification |
| `MetricsCollector` | close reason counter increment and snapshot export |
| Observed metrics JSON | new optional fields serialize/deserialize without breaking old artifacts |
| `LoadValidationEvaluator` | close/phase counters included in summary model |
| `LoadValidationSummaryWriter` | markdown includes receive close and phase reason tables |

### 8.2 Local Verification

```text
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln -c Release --no-build
bash -n scripts/cloud/*.sh
jq empty docs/.pdca-status.json
git diff --check
```

### 8.3 Runtime Validation

```text
scripts/cloud/ssh-readiness.sh
scripts/cloud/runner-connectivity.sh
scripts/cloud/runner-smoke.sh
```

After smoke passes:

```text
1K staged run
3K staged run
5K staged run
focused 10K run
scripts/cloud/collect-artifacts.sh
```

10K는 pass/fail만 보지 않는다. 실패하더라도 receive timeout/reset, final disconnect, RTT tail, close/phase reason이 baseline보다 명확해졌는지 기록한다.

## 9. Acceptance Criteria

- [ ] `ReadAsync == 0` receive termination is classified separately from socket exceptions.
- [ ] Summary output shows receive close reasons and phase completion reasons.
- [ ] Existing socket error class counters and hard thresholds remain intact.
- [ ] Cloud validation runbook includes server restart/readiness before load.
- [ ] Smoke validation passes after implementation.
- [ ] Staged run output can explain whether failures are timeout, reset, EOF, cancellation, or stale-session related.
- [ ] Focused 10K either improves baseline failure shape or produces an explicit stop-condition report.
- [ ] Build, tests, shell syntax check, status JSON check, and diff whitespace check pass.
- [ ] No credentials, public IPs, private keys, generated artifacts, or `.DS_Store` files are committed.

## 10. Risks And Mitigations

| Risk | Mitigation |
|------|------------|
| Metrics changes perturb performance | Keep counters simple and validate smoke before staged load |
| EOF is overtreated as a bug | Record EOF separately and interpret with phase/outstanding context |
| Server lifecycle requires engine changes | Add test server diagnostics first; only extend `IServerTelemetry` with evidence |
| Azure `Standard_B2s` remains too small for 10K | Treat run as failure-shape validation, not production capacity claim |
| Local runner is the bottleneck | Use operation duration and local readiness data before blaming server |
| Public network tail dominates RTT | Separate path RTT from server processing/backpressure signals |

## 11. Open Questions

- Will EOF/partial read counts explain most final disconnects, or are resets/timeouts still dominant?
- Is `currentSessions=51` after runner exit caused by stale server state, client timeout exit, or normal delayed disconnect?
- Do we need server-side disconnect reason telemetry in `LibNetworks`, or is test server/runbook telemetry enough?
- Should staged 1K/3K/5K be encoded as reusable scripts or kept as manual validation commands first?
- At what point is a second cloud runner VM justified instead of local runner testing?

## 12. Next Phase

Recommended next command:

```text
$pdca do cloud-receive-timeout-rtt-tail-stability
```
