# Completion Report: adaptive-client-pacing-threshold-tuning

> Date: 2026-05-05 | Status: Completed With Failed Optimization Outcome | Match Rate: 86%

---

## 1. Summary

`adaptive-client-pacing-threshold-tuning` completed the planned client-side adaptive pacing tuning and the follow-up diagnostics needed to explain why the tuning should not be continued in this feature.

This is not a successful performance optimization report. It is a completed investigation and stop-condition report.

The retained adaptive defaults are implemented and tested:

```text
MinWindow=1
InitialWindow=4
MaxWindow=8
RttTargetMs=14000
RttHighMs=24000
IncreaseEveryResponses=128
```

The retained candidate and the later diagnostic candidates still fail the hard stability guardrails. The dominant failure signal is not client `send-write` blocking. The strongest observed signal is long `receive-header` wait time after late-ramp backlog buildup.

## 2. Related Documents

- Plan: `docs/01-plan/features/adaptive-client-pacing-threshold-tuning.plan.md`
- Design: `docs/02-design/features/adaptive-client-pacing-threshold-tuning.design.md`
- Do notes: `docs/02-design/features/adaptive-client-pacing-threshold-tuning.do.md`
- Analysis: `docs/03-analysis/adaptive-client-pacing-threshold-tuning.analysis.md`
- Benchmark summary: `docs/load-validation-benchmark-results.md`

## 3. Completed Items

### 3.1 Client Adaptive Defaults

- [x] Updated `FastPortTestLoadRunner` adaptive-window defaults.
- [x] Updated `FastPortTestLoadValidation` adaptive-window defaults.
- [x] Kept runner and validation defaults aligned.
- [x] Updated CLI usage text.
- [x] Added unit coverage for adaptive-window default parsing.
- [x] Strengthened command-builder propagation tests.

### 3.2 Validation Guardrails

- [x] Added hard validation failure for `FinalDisconnectCount > 0`.
- [x] Added hard validation failure for `receive|IOException|TimedOut > 0`.
- [x] Added evaluator tests for final disconnect and receive-timeout failures.

### 3.3 Load Runner Diagnostics

- [x] Added duplex phase cancellation so receive completion/failure cancels the sibling send phase.
- [x] Added tests for duplex cancellation behavior.
- [x] Added client operation-duration telemetry for:
  - `send-write`
  - `receive-header`
  - `receive-body`
- [x] Propagated operation-duration metrics through observed metrics, validation summary JSON, and markdown output.
- [x] Added unit coverage for operation-duration aggregation, serialization, mapping, evaluator propagation, and summary output.

### 3.4 Candidate Evaluation

- [x] Tested the first tuned candidate.
- [x] Tested the fallback threshold candidate.
- [x] Tested and rejected the older stability-restore candidate (`MaxWindow=16`, target/high `12s/20s`, increase every `256`).
- [x] Tested and rejected the header-wait pressure candidate.
- [x] Rechecked the latest post-send-queue adaptive artifact under the current hard-guardrail interpretation.

## 4. Quality Metrics

| Metric | Result |
|--------|--------|
| Match rate | 86% |
| Implementation coverage | 18 / 18 designed and diagnostic code tasks |
| Runtime guardrail coverage | 6 / 12 benchmark guardrails |
| Release tests after latest doc/status update | 113 passed, 0 failed, 0 skipped |
| JSON validation | `jq empty docs/.pdca-status.json` passed |
| Whitespace validation | `git diff --check` passed |

The match rate remains below 90% because the implementation is complete, but the runtime outcome is not acceptable under the hard stability guardrails.

## 5. Runtime Outcome

Baseline:

```text
artifacts/load-validation/s5-session-rtt-validation/summary.json
```

Primary retained diagnostic:

```text
artifacts/load-validation/adaptive-pacing-operation-duration-s5/summary.json
```

Latest post-queue adaptive recheck:

```text
artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive/summary.json
```

| Metric | Baseline | Operation-Duration Diagnostic | Post-Queue Adaptive Recheck |
|--------|---------:|------------------------------:|-----------------------------:|
| Result under hard guardrails | Would fail by timeout class | Failed | Failed |
| Peak sessions | `10,000 / 10,000` | `9,802 / 10,000` | `9,975 / 10,000` |
| Final disconnects | `0` | `2,152` | `2` |
| `receive\|IOException\|TimedOut` | `184` | `410` | `1,266` |
| `send\|IOException\|NoBufferSpaceAvailable` | `1,639` | `1,742` | `0` |
| Max TPS | `9,371.08` | `9,254.96` | `7,901.40` |
| RTT P95 | `19,210.39ms` | `14,980.04ms` | `17,796.60ms` |
| RTT P99 | `24,863.90ms` | `22,016.89ms` | `27,398.15ms` |
| Max pending requests | `36,695` | `37,544` | `38,246` |
| Max pending server send requests | `1,095` | `986` | `1,282` |
| Server send backpressure events | `1,583` | `3,850` | `0` |

## 6. Diagnostic Finding

Operation-duration telemetry isolated the most important symptom:

| Operation | Count | Average | Max | Interpretation |
|-----------|------:|--------:|----:|----------------|
| `receive-header` | `1,992,940` | `1,474.78ms` | `52,261.89ms` | Waiting for the next response header dominates the timeout path. |
| `receive-body` | `2,008,597` | `8.34ms` | `3,512.89ms` | Body reads can spike, but are secondary. |
| `send-write` | `2,044,075` | `0.07ms` | `67.28ms` | Client writes are not blocking long enough to explain the receive timeout. |

The failure is therefore not explained by local client write blocking. The unresolved problem is response-header wait growth after late-ramp backlog buildup.

## 7. Rejected Paths

### 7.1 Stability Restore

The older `16/12s/20s/256` adaptive defaults were retested and rejected.

Observed result:

- peak sessions: `9,690 / 10,000`
- final disconnects: `5,936`
- `receive|IOException|TimedOut`: `4,554`
- RTT P95/P99: `20,569.94ms / 32,961.34ms`
- `receive-header` max: `73,423.99ms`

### 7.2 Header-Wait Pressure

The header-wait feedback candidate was also rejected.

Observed result:

- peak sessions: `9,698 / 10,000`
- final disconnects: `2,453`
- `receive|IOException|TimedOut`: `1,236`
- RTT P95/P99: `23,529.55ms / 29,609.81ms`
- `receive-header` max: `59,731.73ms`

It reduced send-side `NoBufferSpaceAvailable`, but made the critical receive-timeout and disconnect guardrails worse.

## 8. Lessons Learned

### Keep

- Keep hard validation guardrails for final disconnects and receive timeout classifications.
- Keep operation-duration telemetry. It gave a clearer failure signal than aggregate RTT alone.
- Keep duplex phase cancellation. It makes session loss visible instead of allowing a broken receive phase to keep generating send pressure.

### Problem

- RTT threshold-only tuning can improve some RTT metrics while making correctness/stability worse.
- Header-wait feedback reacts to a real symptom, but appears too late or in the wrong feedback loop.
- Same-machine 10K results are still useful for diagnostics, but they are not enough to claim production capacity.

### Try

- Move the next optimization target to server/test-server response processing, not another client threshold-only iteration.
- Use cloud split validation when the server resource is available, so client and server scheduling pressure are separated.
- Keep comparing against both the original adaptive reference and the latest send-queue artifact.

## 9. Residual Risks

- Root cause for the receive-header wait growth is still open.
- Current hard guardrails may fail previously accepted artifacts; this is intentional and should be preserved.
- The retained adaptive defaults are diagnostic defaults, not a proven throughput improvement.
- The next fix may require changing `FastPortTestSmokeServer` response behavior or server response scheduling, which is outside this feature's original scope.

## 10. Final Decision

Do not continue `adaptive-client-pacing-threshold-tuning` with additional client-side threshold-only or header-wait-only candidates.

This feature is complete as an investigation with a failed optimization outcome. It should be archived after the report, and the next PDCA target should switch to one of:

- server/test-server response processing;
- cloud server/runner split validation;
- a narrower receive-header wait root-cause feature.

## 11. Verification

Latest checks after the report/iterate documentation update:

```text
dotnet test FastPortCharp.sln -c Release --no-build
jq empty docs/.pdca-status.json
git diff --check
```

Test result:

```text
Passed: 113
Failed: 0
Skipped: 0
```

## 12. Next Steps

- [ ] Archive this feature:

  ```text
  $pdca archive adaptive-client-pacing-threshold-tuning
  ```

- [ ] Start or resume the next target:

  ```text
  $pdca analyze cloud-server-runner-split-load-validation
  ```

- [ ] If cloud resources are still blocked, create a focused server/test-server response processing feature.
