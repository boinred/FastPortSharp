# Gap Analysis: adaptive-client-pacing-threshold-tuning

> Date: 2026-05-03 | Design: docs/02-design/features/adaptive-client-pacing-threshold-tuning.design.md

---

## Match Rate: 86%

The code implementation matches the designed tuning mechanics and now includes the extra client-side operation-duration telemetry needed to isolate the receive-timeout trigger. Stability-restore and header-wait pressure candidates were both tested and rejected because they made disconnects and/or receive timeouts worse. The retained candidate still fails the hard stability guardrails, and this feature has exceeded the normal Act iteration limit. Further client-side threshold/header-wait changes are now out of confidence; the next work should switch target instead of continuing this same iterate loop.

Scoring basis:

- Implementation coverage: 18 / 18 designed and diagnostic code tasks.
- Runtime guardrail coverage after iteration: 6 / 12 benchmark guardrails.
- Weighted match: 86%, because the pacing defaults, hard guardrails, duplex lifecycle fix, and operation-duration diagnostics are complete, and two more client-side pacing candidates have been rejected, but the current retained 10K run still loses sessions and still records receive timeouts.

## Summary

`adaptive-client-pacing-threshold-tuning` completed the intended first-pass tuning and subsequent diagnostics. The latest iteration extends observed telemetry with optional client operation durations; it does not change `LibNetworks` engine send/receive behavior, `FastPortTestSmokeServer` echo behavior, or cloud scripts.

The final iterated defaults are present in both runner and validation:

```text
MinWindow=1
InitialWindow=4
MaxWindow=8
RttTargetMs=14000
RttHighMs=24000
IncreaseEveryResponses=128
```

Release build, Release tests, smoke validation, and focused 10K validation all ran after the operation-duration iteration. The retained focused 10K result is not acceptable under the stricter guardrails: peak sessions reach only `9,802 / 10,000`, final disconnects reach `2,152`, and `receive|IOException|TimedOut` remains non-zero at `410`.

The new diagnostic signal narrows the trigger: `send-write` max is only `67.28ms`, while `receive-header` max reaches `52,261.89ms`. This points to waiting for the next response header after backlog buildup, not client-side write blocking.

An additional stability-restore experiment using the older adaptive defaults (`16/12s/20s/256`) was rejected. It produced only `9,690 / 10,000` peak sessions, `5,936` final disconnects, `4,554` receive timeouts, RTT P95/P99 `20,569.94ms / 32,961.34ms`, and `receive-header` max `73,423.99ms`.

A header-wait pressure experiment was also rejected. It reduced send-side `NoBufferSpaceAvailable` to `1,217`, but regressed the critical guardrails: only `9,698 / 10,000` peak sessions, `2,453` final disconnects, `1,236` receive timeouts, RTT P95/P99 `23,529.55ms / 29,609.81ms`, and `receive-header` max `59,731.73ms`.

## Implemented Items

- [x] Updated `FastPortTestLoadRunner/LoadRunnerOptions.cs` adaptive defaults.
- [x] Updated `FastPortTestLoadRunner/LoadRunnerOptions.cs` usage text.
- [x] Updated `FastPortTestLoadValidation/LoadValidationOptions.cs` adaptive defaults.
- [x] Updated `FastPortTestLoadValidation/LoadValidationOptions.cs` usage text.
- [x] Added runner default adaptive-window test coverage.
- [x] Added validation default adaptive-window test coverage.
- [x] Strengthened validation command builder propagation checks for initial window, RTT target, RTT high, and increase cadence.
- [x] Kept the core `OutstandingRequestPacer` algorithm unchanged.
- [x] Kept `LibNetworks` engine send/receive behavior unchanged.
- [x] Kept `FastPortTestSmokeServer/` unchanged for this feature.
- [x] Kept `scripts/cloud/` unchanged for this feature.
- [x] Ran Release build successfully.
- [x] Ran Release tests successfully.
- [x] Ran smoke validation successfully.
- [x] Ran focused same-machine 10K validation successfully.
- [x] Ran `scripts/load-validation/decompose-summary.sh` against the new 10K summary.
- [x] Updated benchmark documentation with the new comparison.
- [x] Wrote Do notes with verification and benchmark classification.
- [x] Added hard validation guardrails for final disconnects and `receive|IOException|TimedOut`.
- [x] Added focused evaluator tests for those guardrails.
- [x] Re-ran Release build, Release tests, and smoke validation after the guardrail change.
- [x] Added load-runner duplex phase cancellation so receive failure stops the sibling send phase.
- [x] Added unit tests for receive completion/failure cancelling send.
- [x] Re-ran smoke and focused 10K after the duplex lifecycle fix.
- [x] Added client operation-duration telemetry for `send-write`, `receive-header`, and `receive-body`.
- [x] Added observed metrics serialization and validation summary propagation for operation durations.
- [x] Added unit tests for operation-duration aggregation, JSON mapping, evaluator propagation, and summary output.
- [x] Re-ran smoke and focused 10K after the operation-duration telemetry change.
- [x] Tested and rejected the stability-restore threshold candidate (`16/12s/20s/256`).
- [x] Reverted that rejected candidate and kept the prior retained defaults (`8/14s/24s/128`).
- [x] Tested and rejected a header-wait pressure candidate.
- [x] Reverted that rejected candidate because it worsened receive timeouts and disconnects.

## Runtime Comparison

Baseline:

```text
artifacts/load-validation/s5-session-rtt-validation/summary.json
```

First candidate:

```text
artifacts/load-validation/adaptive-pacing-threshold-s5/summary.json
```

Fallback candidate:

```text
artifacts/load-validation/adaptive-pacing-threshold-fallback-s5/summary.json
```

Duplex-cancel diagnostic:

```text
artifacts/load-validation/adaptive-pacing-duplex-cancel-s5/summary.json
```

Operation-duration diagnostic:

```text
artifacts/load-validation/adaptive-pacing-operation-duration-s5/summary.json
```

Rejected stability-restore candidate:

```text
artifacts/load-validation/adaptive-pacing-stability-restore-s5/summary.json
```

Rejected header-pressure candidate:

```text
artifacts/load-validation/adaptive-pacing-header-pressure-s5/summary.json
```

| Metric | Baseline | Duplex-Cancel Diagnostic | Operation-Duration Diagnostic | Rejected Stability Restore | Rejected Header Pressure | Classification |
|--------|---------:|-------------------------:|------------------------------:|---------------------------:|-------------------------:|----------------|
| Result under new hard guardrails | Would fail | Failed | Failed | Failed | Failed | Regression |
| Peak sessions | `10,000 / 10,000` | `9,830 / 10,000` | `9,802 / 10,000` | `9,690 / 10,000` | `9,698 / 10,000` | Regression |
| Final disconnects | `0` | `1,856` | `2,152` | `5,936` | `2,453` | Regression |
| Max TPS | `9,371.08` | `9,058.89` | `9,254.96` | `8,176.32` | `9,735.19` | Mixed |
| RTT P95 | `19,210.39ms` | `16,310.30ms` | `14,980.04ms` | `20,569.94ms` | `23,529.55ms` | Mixed |
| RTT P99 | `24,863.90ms` | `18,863.93ms` | `22,016.89ms` | `32,961.34ms` | `29,609.81ms` | Mixed |
| Per-session P95-of-P95 | `18,211.02ms` | `15,818.94ms` | `17,044.66ms` | `17,924.93ms` | `27,259.05ms` | Regression |
| Pending requests/session | `3.67` | `3.80` | `3.75` | `3.81` | `4.05` | Regression |
| Pending server send requests | `1,095` | `1,016` | `986` | `1,194` | `1,438` | Regression |
| Server send backpressure events | `1,583` | `2,986` | `3,850` | `3,225` | `1,863` | Mixed |
| Max server send buffer bytes | `64,204` | `63,122` | `87,228` | `69,042` | `88,030` | Mixed |
| Pacing average wait max | `2,857.09ms` | `2,410.34ms` | `2,515.80ms` | `2,739.45ms` | `2,263.84ms` | Mixed |
| Pacing window range | `1-5` | `1-7` | `1-6` | `1-5` | `1-6` | Expected expansion |
| `send\|IOException\|NoBufferSpaceAvailable` | `1,639` | `1,396` | `1,742` | `1,340` | `1,217` | Mixed |
| `receive\|IOException\|TimedOut` | `184` | `460` | `410` | `4,554` | `1,236` | Regression |
| Max scheduler drift | `12.12ms` | `10.64ms` | `34.35ms` | `502.25ms` | `57.59ms` | Regression |

## Operation-Duration Finding

| Operation | Count | Average | Max | Interpretation |
|-----------|------:|--------:|----:|----------------|
| `receive-header` | `1,992,940` | `1,474.78ms` | `52,261.89ms` | Waiting for the next response header dominates the receive timeout path. |
| `receive-body` | `2,008,597` | `8.34ms` | `3,512.89ms` | Body reads can spike, but the first-order signal is much smaller than header wait. |
| `send-write` | `2,044,075` | `0.07ms` | `67.28ms` | Client writes are not blocking long enough to explain the receive timeout. |

Rejected stability-restore operation durations:

| Operation | Count | Average | Max | Interpretation |
|-----------|------:|--------:|----:|----------------|
| `receive-header` | `1,782,649` | `1,519.41ms` | `73,423.99ms` | Worse than the retained candidate; header wait remains the dominant timeout path. |
| `receive-body` | `1,807,084` | `8.30ms` | `1,134.88ms` | Body reads are still secondary. |
| `send-write` | `1,829,714` | `0.09ms` | `398.59ms` | Higher than the retained candidate but still much smaller than response-header waits. |

Rejected header-pressure operation durations:

| Operation | Count | Average | Max | Interpretation |
|-----------|------:|--------:|----:|----------------|
| `receive-header` | `1,929,589` | `1,480.13ms` | `59,731.73ms` | Worse than the retained candidate; reducing window from this signal did not stop timeout growth. |
| `receive-body` | `1,959,333` | `10.75ms` | `3,635.43ms` | Body reads remain secondary. |
| `send-write` | `1,992,245` | `0.10ms` | `106.58ms` | Still not the first-order timeout source. |

Late-ramp timeline:

- At `12:47:45`, peak sessions reached `9,802`, pending requests were already `35,552`, and `receive-header` max was `14,909.94ms`.
- At `12:47:46`, `NoBufferSpaceAvailable` appeared (`350`) and sessions started falling (`9,533`).
- At `12:48:00`, `receive|IOException|TimedOut` appeared (`95`) with `receive-header` max already `25,377.33ms`.
- At `12:48:37`, `receive-header` max crossed `32,784.10ms`; final timeout/no-buffer counts had already accumulated (`410` / `1,742`).

## Missing Items

- [x] No missing code item from the first-pass design.
- [x] The fallback tuning candidate has been tried and verified.
- [x] The validator now rejects final disconnects and receive timeouts instead of treating them as acceptable socket-error-rate noise.
- [x] Receive phase failure now cancels the sibling send phase in the load runner.
- [x] Client operation-duration telemetry now separates send write, response header wait, and response body read duration.
- [x] The older `16/12s/20s/256` adaptive default candidate was retested and rejected.
- [x] The header-wait pressure candidate was retested and rejected.
- [ ] The root cause for the disconnect/receive-timeout regression is still open.
- [x] The current client-side iterate loop reached its stop condition without a safe retained candidate.
- [ ] Switch target to server/test-server response processing or cloud split validation.

## Changed Items

- [x] The design's expected effect said pacing average wait might drop if pacing collapse was dominant. Actual focused 10K increased pacing average wait from `2,857.09ms` to `3,052.51ms`.
- [x] The design allowed `MaxObservedPacingWindow` to rise above `5`; actual focused 10K reached `7`. This matched the mechanism but increased pressure elsewhere.
- [x] The design warned that looser thresholds might increase socket/server pressure. Actual focused 10K confirmed that risk through higher receive timeouts and server send backpressure.
- [x] Iteration 1 fallback improved the first candidate's RTT tail and pacing wait, but did not remove disconnect/receive-timeout tradeoffs.

## Gap Details

| Category | Item | Status | Evidence |
|----------|------|--------|----------|
| Match | Runner tuned defaults | Implemented | `LoadPacingOptions.DefaultMaxWindow = 8`, target `14_000`, high `24_000`, increase `128` |
| Match | Validation tuned defaults | Implemented | `LoadValidationPacingOptions` mirrors runner defaults |
| Match | CLI usage text | Implemented | Runner and validation help text show tuned defaults |
| Match | Default-value tests | Implemented | `LoadRunnerOptions_TryParse_UsesTunedAdaptiveDefaults`, `LoadValidationOptions_TryParse_UsesTunedAdaptiveDefaults` |
| Match | Command propagation | Implemented | `LoadRunnerCommandBuilder_BuildsStageCommand` asserts adaptive arguments |
| Match | Scope boundary | Implemented | No `LibNetworks` engine send/receive, `FastPortTestSmokeServer`, or `scripts/cloud` behavior changes; only `LibNetworks/Telemetry` was extended for diagnostics |
| Match | Smoke validation | Passed | `artifacts/load-validation/adaptive-pacing-threshold-smoke/summary.md` |
| Match | Focused 10K execution | Passed | `artifacts/load-validation/adaptive-pacing-threshold-s5/summary.md` |
| Match | Fallback candidate | Implemented | Design section 4.3 `14s/24s` candidate applied and validated |
| Match | Hard disconnect guardrail | Implemented | `LoadValidationEvaluator_FailsFinalDisconnects` |
| Match | Hard receive-timeout guardrail | Implemented | `LoadValidationEvaluator_FailsReceiveTimeoutClass` |
| Match | Duplex phase cancellation | Implemented | `LoadSession_RunDuplexAsync_CancelsSendWhenReceiveCompletes`, `LoadSession_RunDuplexAsync_CancelsSendWhenReceiveFails` |
| Match | Client operation-duration telemetry | Implemented | `operationDurations` appears in `adaptive-pacing-operation-duration-s5/summary.json` |
| Changed | Benchmark acceptance | Failed under new guardrails | RTT P95/P99 improved, but peak sessions, disconnects, receive timeouts, and server backpressure miss guardrails |

## Recommendation

Do not proceed to report as a completed optimization. Also do not add another client-side threshold-only or header-wait-only candidate under this feature.

This feature's Act loop has reached the practical stop condition:

- match rate remains `86%`, below the `90%` report threshold;
- iteration count is already above the normal PDCA maximum;
- every retained or rejected client-side pacing candidate still fails the hard guardrails;
- the design explicitly excludes `LibNetworks`, `FastPortTestSmokeServer`, and server send/response behavior changes.

The next pass should move to a new or already-active target: server/test-server response processing, or cloud split validation to remove same-machine scheduling noise before further pacing work.

2026-05-05 recheck after later send-queue work:

- The latest available post-queue adaptive artifact, `artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive/summary.md`, still does not satisfy the current hard guardrails if interpreted with the stricter evaluator added by this feature.
- It reaches `9,975 / 10,000` peak sessions, but still has `2` final disconnects and `receive|IOException|TimedOut = 1,266`.
- That result is better than the failed threshold-only artifacts for peak session retention, but it is not enough to reopen client threshold tuning as a safe path.
- The feature remains below the report threshold at `86%`; the unresolved gap is root cause and target selection, not missing implementation of the original client-side design.

Iteration 1 applied the design fallback candidate:

```text
MaxWindow=8
RttTargetMs=14000
RttHighMs=24000
IncreaseEveryResponses=128
```

Observed effect:

- Better than the first candidate for max TPS, RTT P95/P99, per-session P95-of-P95, pending server send, server backpressure, `NoBufferSpaceAvailable`, scheduler drift, and pacing wait.
- Better than the original baseline for RTT P95/P99, pending server send, max send buffer, pacing wait, and `NoBufferSpaceAvailable`.
- Still worse than the original baseline for disconnects, receive timeouts, server backpressure, socket error rate, and pending request depth.
- Under the new hard guardrails, all current 10K tuning candidates fail even though the old validation thresholds marked earlier candidates as passed.
- The duplex-cancel run showed the load runner was hiding session loss after receive failures; this is now corrected.
- The operation-duration run shows the receive-timeout trigger is not client write blocking. The dominant wait is `receive-header`, which grows past 25s after late-ramp backlog and then trips receive timeout.
- The stability-restore run shows that returning to the older `16/12s/20s/256` defaults is worse under the current hard guardrails and instrumentation: final disconnects jump to `5,936` and receive timeouts to `4,554`.
- The header-pressure run shows that reacting to `receive-header` wait in the client pacer is also too late or in the wrong feedback loop: receive timeouts increased to `1,236` and final disconnects to `2,453`.
- Static threshold-only tuning and header-wait-only feedback are now low confidence. The next change should move the optimization target to the server/test-server response path or run cloud split validation to remove local same-machine scheduling noise before more client pacing work.

Guardrail verification:

```text
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln -c Release --no-build
artifacts/load-validation/adaptive-pacing-guardrail-smoke/summary.md
artifacts/load-validation/adaptive-pacing-duplex-cancel-smoke/summary.md
artifacts/load-validation/adaptive-pacing-duplex-cancel-s5/summary.md
artifacts/load-validation/adaptive-pacing-operation-duration-smoke/summary.md
artifacts/load-validation/adaptive-pacing-operation-duration-s5/summary.md
artifacts/load-validation/adaptive-pacing-stability-restore-smoke/summary.md
artifacts/load-validation/adaptive-pacing-stability-restore-s5/summary.md
artifacts/load-validation/adaptive-pacing-header-pressure-smoke/summary.md
artifacts/load-validation/adaptive-pacing-header-pressure-s5/summary.md
```

## Next Steps

- [x] Run `$pdca iterate adaptive-client-pacing-threshold-tuning`.
- [x] Apply fallback thresholds from the design.
- [x] Re-run Release build and Release tests.
- [x] Re-run smoke validation.
- [x] Re-run focused 10K after smoke passed.
- [x] Compare fallback result against both `s5-session-rtt-validation` and `adaptive-pacing-threshold-s5`.
- [x] Add hard validation guardrails for final disconnect and receive timeout.
- [x] Add duplex phase cancellation and re-run smoke/focused 10K.
- [x] Add client read/write duration telemetry and re-run smoke/focused 10K.
- [x] Test and reject the older stability-restore adaptive defaults.
- [x] Test and reject the header-wait pressure adaptive feedback candidate.
- [x] Stop the current client-side iterate loop after exceeding the iteration limit without a safe retained candidate.
- [x] Recheck the latest post-send-queue adaptive artifact under current hard-guardrail interpretation.
- [ ] Switch target to server/test-server response processing or cloud split validation.
