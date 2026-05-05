# cloud-receive-timeout-rtt-tail-stability - Completion Report

> Version: 1.0.0 | Date: 2026-05-05 | Status: Completed
> Match Rate: 94% | Iterations: 2

---

## 1. Summary

`cloud-receive-timeout-rtt-tail-stability`는 Azure server/local runner 10K baseline에서 나타난 receive timeout, connection reset, final disconnect, RTT tail 문제를 더 정확히 분해하기 위한 진단 feature다.

이번 작업은 성능 최적화 자체가 아니라 실패 원인 분류 능력을 높이는 데 집중했다. 엔진 send path, `BaseSession`, server send queue는 변경하지 않았다.

완료 결과:

- client receive 종료를 socket exception과 EOF/partial EOF로 분리했다.
- phase completion/cancel/fault를 별도 counter로 기록한다.
- validation summary에 close/phase 진단 정보를 노출한다.
- Azure runbook에 clean server start와 stale-session 확인 절차를 추가했다.
- Azure 서버를 재시작한 뒤 cloud smoke validation을 통과했다.

남은 항목:

- staged cloud validation, 즉 1K/3K/5K/focused 10K는 아직 실행하지 않았다.
- benchmark result markdown은 새 staged runtime 결과가 생긴 뒤 갱신해야 한다.

## 2. Related Documents

| Phase | Document |
|-------|----------|
| Plan | `docs/01-plan/features/cloud-receive-timeout-rtt-tail-stability.plan.md` |
| Design | `docs/02-design/features/cloud-receive-timeout-rtt-tail-stability.design.md` |
| Do | `docs/02-design/features/cloud-receive-timeout-rtt-tail-stability.do.md` |
| Analysis | `docs/03-analysis/cloud-receive-timeout-rtt-tail-stability.analysis.md` |
| Runbook | `docs/azure-server-runner-split-load-validation-runbook.md` |

## 3. Completed Items

### 3.1 Runner Diagnostics

- `FastPortTestLoadRunner/LoadSession.cs`
  - `ReadAsync == 0` receive termination is classified separately from socket exceptions.
  - zero-byte read before any bytes is recorded as `eof`.
  - zero-byte read after partial bytes is recorded as `partial-eof`.
  - receive close records outstanding request count at the close point.

- `FastPortTestLoadRunner/Metrics.cs`
  - added receive close counters by operation, reason, and class.
  - added max outstanding request count at receive close.
  - added phase completion counters.

- `FastPortTestLoadRunner/ObservedMetricsExtensions.cs`
  - maps the new runner metrics into client observed metrics.

### 3.2 Observed Metrics Contract

- `LibNetworks/Telemetry/ObservedMetrics.cs`
  - added optional additive fields:
    - `receiveCloseCountsByOperation`
    - `receiveCloseCountsByReason`
    - `receiveCloseCountsByClass`
    - `maxOutstandingRequestsAtReceiveClose`
    - `phaseCompletionCounts`

Existing JSONL artifacts without these fields remain readable because the new record properties have defaults.

### 3.3 Validation Summary

- `FastPortTestLoadValidation/LoadValidationStage.cs`
  - added stage summary fields for receive close and phase completion diagnostics.

- `FastPortTestLoadValidation/LoadValidationEvaluator.cs`
  - copies close/phase counters from the final client observed sample into stage summary.

- `FastPortTestLoadValidation/LoadValidationSummaryWriter.cs`
  - writes receive close classes and phase completion counts to `summary.md` when present.

Existing hard guardrails were preserved:

- max socket error rate;
- final disconnect count;
- disconnect ratio;
- receive timeout socket class threshold.

### 3.4 Cloud Runbook

- `docs/azure-server-runner-split-load-validation-runbook.md`
  - added before-every-load-run server restart procedure.
  - added listener check.
  - added latest server metrics check.
  - documented that `currentSessions` should be `0` before starting a new run unless explicitly explained.

## 4. Quality Metrics

| Metric | Result |
|--------|--------|
| Match rate | `94%` |
| Iteration count | `2` |
| Build | Passed |
| Unit tests | `117/117` passed |
| Shell syntax | Passed |
| PDCA status JSON | Passed |
| Diff whitespace | Passed |
| Cloud connectivity | Passed |
| Cloud smoke validation | Passed |

Verification commands executed:

```text
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln -c Release --no-build
bash -n scripts/cloud/*.sh
jq empty docs/.pdca-status.json
git diff --check
scripts/cloud/runner-connectivity.sh
scripts/cloud/runner-smoke.sh
```

## 5. Cloud Smoke Result

Cloud server was restarted before smoke validation. The latest server metrics before the run showed:

```text
currentSessions = 0
pendingSendRequests = 0
socketErrorCount = 0
```

Smoke run:

```text
Run ID: 20260505-154128-smoke
Summary: artifacts/load-validation/cloud-server-runner-split/smoke/summary.md
```

| Stage | Result | Peak | Final Disconnects | Socket Error Rate | RTT P95 | RTT P99 |
|-------|--------|------|------------------:|------------------:|--------:|--------:|
| `smoke-fixed-10` | Passed | `10/10` | `0` | `0.00%` | `88.89ms` | `96.50ms` |
| `smoke-random-25` | Passed | `25/25` | `0` | `0.00%` | `97.07ms` | `109.67ms` |

Smoke did not produce receive close counters, phase close counters, disconnects, or socket errors. That is expected for a healthy smoke run and confirms the new fields do not disturb the happy path.

## 6. Lessons Learned

### Keep

- Keep receive timeout/reset guardrails hard. They caught the original cloud 10K failure shape and should not be weakened.
- Keep cloud server restart/readiness as part of every runtime validation. The previous baseline had lingering sessions after the run.
- Keep engine changes gated by evidence. This feature did not justify changing `BaseSession` or `IServerTelemetry`.

### Problem

- A same-machine or local smoke pass is not enough to explain cloud receive tail behavior.
- The previous cloud 10K summary could not distinguish EOF, partial EOF, cancellation, timeout, and reset clearly enough.
- Server-side metrics can be unavailable in the runner summary if artifacts are collected after the run rather than merged during validation.

### Try

- Run staged cloud validation next with the new diagnostics:
  - 1K
  - 3K
  - 5K
  - focused 10K
- Preserve `summary.json`, `summary.md`, runner metrics, and server metrics for each stage.
- Only add server lifecycle telemetry if staged validation still leaves reset/timeout/lingering sessions unexplained.

## 7. Deferred Work

- No new staged cloud validation result was produced by this feature.
- No benchmark result markdown was updated because the only new runtime result is smoke-level validation.
- No server-side disconnect reason telemetry was added.
- No cloud runner VM was introduced.

## 8. Next Steps

Recommended next command:

```text
$pdca archive cloud-receive-timeout-rtt-tail-stability
```

Recommended follow-up feature:

```text
$pdca pm cloud-staged-rtt-tail-validation
```

The follow-up should focus on staged runtime validation with the new close/phase diagnostics, not on additional engine changes by default.
