# cloud-receive-timeout-rtt-tail-stability - Do Log

> Version: 1.0.0 | Date: 2026-05-05 | Status: Completed
> Design: docs/02-design/features/cloud-receive-timeout-rtt-tail-stability.design.md

---

## 1. Implementation Summary

이번 구현은 cloud 10K receive timeout/reset/RTT tail을 바로 성능 개선으로 덮지 않고, runner와 validation output에서 종료 원인을 분리해서 볼 수 있게 만드는 데 집중했다.

엔진 send path, `BaseSession`, server send queue는 변경하지 않았다.

## 2. Code Changes

### 2.1 Runner Receive Close Metrics

Updated files:

- `FastPortTestLoadRunner/LoadSession.cs`
- `FastPortTestLoadRunner/Metrics.cs`
- `FastPortTestLoadRunner/ObservedMetricsExtensions.cs`
- `LibNetworks/Telemetry/ObservedMetrics.cs`

Implemented behavior:

- `ReadAsync == 0` is no longer only an implicit receive-loop return.
- Receive close is classified separately from socket exceptions.
- Close classes are recorded as:
  - `receive-header|eof`
  - `receive-body|eof`
  - `receive-body|partial-eof`
- The runner records max outstanding request count at receive close.
- Phase completion is recorded as `{phase}|{reason}`, for example:
  - `send|cancelled`
  - `receive|completed`
  - `receive|faulted`

### 2.2 Observed Metrics Contract

Added optional client observed fields:

- `receiveCloseCountsByOperation`
- `receiveCloseCountsByReason`
- `receiveCloseCountsByClass`
- `maxOutstandingRequestsAtReceiveClose`
- `phaseCompletionCounts`

The fields are additive. Existing JSONL artifacts without these fields still deserialize with defaults.

### 2.3 Validation Summary

Updated files:

- `FastPortTestLoadValidation/LoadValidationStage.cs`
- `FastPortTestLoadValidation/LoadValidationEvaluator.cs`
- `FastPortTestLoadValidation/LoadValidationSummaryWriter.cs`

Implemented behavior:

- Final client sample close/phase counters are propagated into `summary.json`.
- `summary.md` now includes top receive close classes and phase completion classes.
- Existing socket error hard guardrails are unchanged.

### 2.4 Cloud Runbook Hygiene

Updated file:

- `docs/azure-server-runner-split-load-validation-runbook.md`

Implemented behavior:

- Added a before-every-load-run server restart step.
- Added listener and latest server metrics checks before runner execution.
- Documented that `currentSessions` should be `0` before starting a new load run unless explicitly explained.

## 3. Tests Added Or Updated

Updated test files:

- `FastPortTests/FastPortTestLoadRunnerTests.cs`
- `FastPortTests/ObservedMetricsTests.cs`
- `FastPortTests/FastPortTestLoadValidationTests.cs`

Coverage added:

- Header EOF receive close classification.
- Partial body EOF receive close classification.
- Receive close and phase completion counters in `MetricsCollector`.
- Observed metrics JSON serialization/deserialization for new fields.
- Load validation evaluator propagation of close/phase counters.
- Markdown summary output for close/phase diagnostics.

## 4. Verification

Executed:

```text
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln -c Release --no-build
```

Result:

- Build passed with `0` warnings and `0` errors.
- Tests passed: `116/116`.

## 5. Deferred Work

- No cloud smoke or 10K runtime validation was executed in this `do` step.
- No server-side disconnect reason telemetry was added yet.
- No benchmark result markdown was updated because no new runtime result was produced.

## 6. Next Phase

Recommended next command:

```text
$pdca analyze cloud-receive-timeout-rtt-tail-stability
```
