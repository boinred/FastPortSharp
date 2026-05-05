# adaptive-client-pacing-threshold-tuning - Do Notes

> Date: 2026-05-03 | Phase: Act | Status: Needs another iterate
> Design: docs/02-design/features/adaptive-client-pacing-threshold-tuning.design.md

## Summary

This pass implemented the tuned adaptive client pacing defaults from the design document and verified them with unit tests, smoke validation, and a focused same-machine 10K run.

The initial pacing implementation was intentionally scoped to load generation and validation. Later diagnostics preserved engine behavior while extending the observed telemetry contract:

- no `LibNetworks` engine send/receive behavior changes;
- one `LibNetworks/Telemetry` contract extension for optional client `operationDurations`;
- no `FastPortTestSmokeServer` behavior changes;
- no pacing algorithm rewrite;
- no cloud split validation, because OCI A1 capacity is still unavailable.

## Code Changes

| File | Change |
|------|--------|
| `FastPortTestLoadRunner/LoadRunnerOptions.cs` | Updated adaptive-window defaults and usage text. |
| `FastPortTestLoadValidation/LoadValidationOptions.cs` | Mirrored the same adaptive-window defaults and usage text. |
| `FastPortTests/FastPortTestLoadRunnerTests.cs` | Added default adaptive-window option coverage. |
| `FastPortTests/FastPortTestLoadValidationTests.cs` | Added default adaptive-window option coverage and strengthened command propagation assertions. |

Tuned defaults:

| Option | Previous | Tuned |
|--------|---------:|------:|
| `MinWindow` | `1` | `1` |
| `InitialWindow` | `4` | `4` |
| `MaxWindow` | `16` | `8` |
| `RttTargetMs` | `12,000` | `16,000` |
| `RttHighMs` | `20,000` | `28,000` |
| `IncreaseEveryResponses` | `256` | `128` |

## Verification

| Check | Result |
|-------|--------|
| `dotnet build FastPortCharp.sln -c Release` | Passed, 0 warnings, 0 errors |
| `dotnet test FastPortCharp.sln -c Release --no-build` | Passed, 106 tests |
| Smoke validation | Passed |
| Focused 10K validation | Passed |
| Manifest default propagation | Passed |

Smoke artifact:

```text
artifacts/load-validation/adaptive-pacing-threshold-smoke/summary.md
```

Focused 10K artifact:

```text
artifacts/load-validation/adaptive-pacing-threshold-s5/summary.md
```

The manifest for both validation runs records:

```text
policy=adaptive-window
minWindow=1
initialWindow=4
maxWindow=8
rttTargetMs=16000
rttHighMs=28000
increaseEveryResponses=128
```

## Focused 10K Result

Baseline:

```text
artifacts/load-validation/s5-session-rtt-validation/summary.json
```

Candidate:

```text
artifacts/load-validation/adaptive-pacing-threshold-s5/summary.json
```

| Metric | Baseline | Candidate | Direction |
|--------|---------:|----------:|-----------|
| Result | Passed | Passed | Stable |
| Peak sessions | `10,000 / 10,000` | `10,000 / 10,000` | Stable |
| Final disconnects | `0` | `82` | Regression |
| Max TPS | `9,371.08` | `8,188.50` | Regression |
| RTT P95 | `19,210.39ms` | `18,166.43ms` | Improved |
| RTT P99 | `24,863.90ms` | `26,430.64ms` | Regression |
| Per-session P95-of-P95 | `18,211.02ms` | `19,777.54ms` | Regression |
| Pending requests/session | `3.67` | `3.71` | Slight regression |
| Pending server send requests | `1,095` | `994` | Improved |
| Server send backpressure events | `1,583` | `3,678` | Regression |
| Max server send buffer bytes | `64,204` | `63,059` | Slight improvement |
| Pacing average wait max | `2,857.09ms` | `3,052.51ms` | Regression |
| Pacing window range | `1-5` | `1-7` | Expanded |
| `send\|IOException\|NoBufferSpaceAvailable` | `1,639` | `1,657` | Slight regression |
| `receive\|IOException\|TimedOut` | `184` | `768` | Regression |
| Max scheduler drift | `12.12ms` | `43.30ms` | Regression |

## Interpretation

The tuned defaults are functionally valid, but the focused 10K result is mixed and should not be treated as a clean benchmark improvement.

What improved:

- global RTT P95 improved by about 5.4%;
- pending server send requests dropped from `1,095` to `994`;
- max server send buffer bytes dropped slightly;
- adaptive window now recovers to `7` instead of being capped in practice around `5`.

What regressed:

- max TPS dropped by about 12.6%;
- final disconnects increased to `82`;
- RTT P99 and per-session tail worsened;
- receive timeouts increased from `184` to `768`;
- server send backpressure more than doubled;
- pacing average wait increased.

This supports the design risk that looser RTT thresholds can recover the adaptive window more often, but may also move pressure into server send/socket paths. The next phase should analyze the implementation/design match and decide whether to iterate to the fallback candidate:

```text
MaxWindow=8
RttTargetMs=14000
RttHighMs=24000
IncreaseEveryResponses=128
```

## Superseded Next Step

```text
$pdca iterate adaptive-client-pacing-threshold-tuning
```

## Iteration 1: Fallback Thresholds

After the first focused 10K result was classified as mixed, the Act phase applied the design fallback candidate:

```text
MaxWindow=8
RttTargetMs=14000
RttHighMs=24000
IncreaseEveryResponses=128
```

Additional verification:

| Check | Result |
|-------|--------|
| `dotnet build FastPortCharp.sln -c Release` | Passed, 0 warnings, 0 errors |
| `dotnet test FastPortCharp.sln -c Release --no-build` | Passed, 106 tests |
| Fallback smoke validation | Passed |
| Fallback focused 10K validation | Passed |

Fallback artifacts:

```text
artifacts/load-validation/adaptive-pacing-threshold-fallback-smoke/summary.md
artifacts/load-validation/adaptive-pacing-threshold-fallback-s5/summary.md
```

| Metric | Baseline | First Candidate | Fallback Candidate |
|--------|---------:|----------------:|-------------------:|
| Peak sessions | `10,000 / 10,000` | `10,000 / 10,000` | `10,000 / 10,000` |
| Final disconnects | `0` | `82` | `90` |
| Max TPS | `9,371.08` | `8,188.50` | `8,554.46` |
| RTT P95 | `19,210.39ms` | `18,166.43ms` | `16,746.72ms` |
| RTT P99 | `24,863.90ms` | `26,430.64ms` | `21,643.96ms` |
| Per-session P95-of-P95 | `18,211.02ms` | `19,777.54ms` | `18,653.15ms` |
| Pending requests/session | `3.67` | `3.71` | `3.72` |
| Pending server send requests | `1,095` | `994` | `945` |
| Server send backpressure events | `1,583` | `3,678` | `3,056` |
| Max server send buffer bytes | `64,204` | `63,059` | `62,832` |
| Pacing average wait max | `2,857.09ms` | `3,052.51ms` | `2,711.68ms` |
| Pacing window range | `1-5` | `1-7` | `1-7` |
| `send\|IOException\|NoBufferSpaceAvailable` | `1,639` | `1,657` | `1,497` |
| `receive\|IOException\|TimedOut` | `184` | `768` | `939` |
| Max scheduler drift | `12.12ms` | `43.30ms` | `15.23ms` |

Iteration result:

- fallback is better than the first candidate on the main RTT, pacing, and send pressure metrics;
- fallback is still not a clean win against the original baseline because disconnects, receive timeouts, server backpressure, socket error rate, and pending request depth remain worse;
- the feature is not ready for report under the stricter stability interpretation, because final disconnects and receive timeouts are correctness/stability regressions rather than ordinary performance tradeoffs.

## Iteration 2: Validation Guardrails

The previous report-ready classification was too permissive. It treated RTT improvement as enough to close the tuning pass even though the candidate had final disconnects and many receive timeouts.

Code changes:

| File | Change |
|------|--------|
| `FastPortTestLoadValidation/LoadValidationStage.cs` | Added `MaxFinalDisconnectCount = 0` and `receive|IOException|TimedOut = 0` as default hard guardrails. |
| `FastPortTestLoadValidation/LoadValidationEvaluator.cs` | Added failure messages for final disconnect count and configured socket-error class thresholds. |
| `FastPortTests/FastPortTestLoadValidationTests.cs` | Added focused evaluator tests for final disconnects and receive timeout classifications. |

Verification:

| Check | Result |
|-------|--------|
| `dotnet build FastPortCharp.sln -c Release` | Passed, 0 warnings, 0 errors |
| `dotnet test FastPortCharp.sln -c Release --no-build` | Passed, 108 tests |
| Guardrail smoke validation | Passed |

Guardrail smoke artifact:

```text
artifacts/load-validation/adaptive-pacing-guardrail-smoke/summary.md
```

Updated classification:

- `adaptive-pacing-threshold-s5` is not acceptable under the new guardrails because it has `82` final disconnects and `receive|IOException|TimedOut = 768`.
- `adaptive-pacing-threshold-fallback-s5` is not acceptable under the new guardrails because it has `90` final disconnects and `receive|IOException|TimedOut = 939`.
- The next pass should isolate why looser adaptive pacing creates disconnects/timeouts instead of further relaxing RTT thresholds.

## Iteration 3: Duplex Phase Cancellation

The next suspicious behavior was in `FastPortTestLoadRunner.LoadSession`: if the receive phase failed, the send phase could keep running until the whole validation run ended. That meant a session with `receive|IOException|TimedOut` could continue adding send pressure, and `currentSessions` could look healthier than the actual receive path.

Code changes:

| File | Change |
|------|--------|
| `FastPortTestLoadRunner/LoadSession.cs` | Added per-session linked cancellation for the send/receive duplex pair. When either phase completes or fails, the sibling phase is cancelled. |
| `FastPortTests/FastPortTestLoadRunnerTests.cs` | Added tests that verify receive completion/failure cancels the send phase. |

Verification:

| Check | Result |
|-------|--------|
| `dotnet build FastPortCharp.sln -c Release` | Passed, 0 warnings, 0 errors |
| `dotnet test FastPortCharp.sln -c Release --no-build` | Passed, 110 tests |
| Duplex-cancel smoke validation | Passed |
| Duplex-cancel focused 10K validation | Failed as expected under hard guardrails |

Artifacts:

```text
artifacts/load-validation/adaptive-pacing-duplex-cancel-smoke/summary.md
artifacts/load-validation/adaptive-pacing-duplex-cancel-s5/summary.md
```

Focused 10K result:

| Metric | Fallback Candidate | Duplex-Cancel Diagnostic | Direction |
|--------|-------------------:|-------------------------:|-----------|
| Result | Passed under old thresholds | Failed under hard guardrails | Expected stricter failure |
| Peak sessions | `10,000 / 10,000` | `9,830 / 10,000` | Revealed hidden session loss |
| Final disconnects | `90` | `1,856` | Worse, but more truthful |
| `receive\|IOException\|TimedOut` | `939` | `460` | Improved |
| `send\|IOException\|NoBufferSpaceAvailable` | `1,497` | `1,396` | Slight improvement |
| Max TPS | `8,554.46` | `9,058.89` | Improved |
| RTT P95 | `16,746.72ms` | `16,310.30ms` | Slight improvement |
| RTT P99 | `21,643.96ms` | `18,863.93ms` | Improved |
| Pending requests/session | `3.72` | `3.80` | Regression |

Interpretation:

- The load runner was over-reporting session health after receive phase failure.
- The duplex cancellation change is correct for lifecycle accounting, but it is not a performance fix.
- The actual remaining problem is earlier: around the 9K+ ramp-up point, receive timeouts begin and cause session loss.
- The next iteration should isolate the receive timeout trigger itself, likely with client read/write duration or per-phase lifetime telemetry rather than more RTT threshold tuning.

## Iteration 4: Client Operation Duration Telemetry

The next gap was diagnostic visibility. We knew the candidate failed through `receive|IOException|TimedOut`, but not whether the timeout was caused by client write blocking, response body reads, or waiting for the next response header.

Code changes:

| File | Change |
|------|--------|
| `FastPortTestLoadRunner/LoadSession.cs` | Records operation durations for `send-write`, `receive-header`, and `receive-body`. |
| `FastPortTestLoadRunner/Metrics.cs` | Aggregates operation duration count, average ms, and max ms. |
| `LibNetworks/Telemetry/ObservedMetrics.cs` | Extends the client observed metrics contract with optional `operationDurations`. |
| `FastPortTestLoadValidation/*` | Carries operation durations into `summary.json` and `summary.md`. |
| `FastPortTests/*` | Adds coverage for duration aggregation, JSON serialization, mapping, evaluator propagation, and summary markdown output. |

Verification:

| Check | Result |
|-------|--------|
| `dotnet build FastPortCharp.sln -c Release` | Passed, 0 warnings, 0 errors |
| `dotnet test FastPortCharp.sln -c Release --no-build` | Passed, 111 tests |
| Operation-duration smoke validation | Passed |
| Operation-duration focused 10K validation | Failed under hard guardrails |

Artifacts:

```text
artifacts/load-validation/adaptive-pacing-operation-duration-smoke/summary.md
artifacts/load-validation/adaptive-pacing-operation-duration-s5/summary.md
artifacts/load-validation/adaptive-pacing-operation-duration-s5/s5-random-10k.metrics.jsonl
artifacts/load-validation/adaptive-pacing-operation-duration-s5/s5-random-10k.combined.metrics.jsonl
```

Focused 10K result:

| Metric | Duplex-Cancel Diagnostic | Operation-Duration Diagnostic | Direction |
|--------|-------------------------:|------------------------------:|-----------|
| Result | Failed | Failed | Stable hard failure |
| Peak sessions | `9,830 / 10,000` | `9,802 / 10,000` | Slight regression |
| Final disconnects | `1,856` | `2,152` | Regression |
| `receive\|IOException\|TimedOut` | `460` | `410` | Slight improvement |
| `send\|IOException\|NoBufferSpaceAvailable` | `1,396` | `1,742` | Regression |
| Max TPS | `9,058.89` | `9,254.96` | Improvement |
| RTT P95 | `16,310.30ms` | `14,980.04ms` | Improvement |
| RTT P99 | `18,863.93ms` | `22,016.89ms` | Regression |
| Pending requests/session | `3.80` | `3.75` | Slight improvement |
| Server backpressure events | `2,986` | `3,850` | Regression |
| Pacing average wait max | `2,410.34ms` | `2,515.80ms` | Regression |

Operation-duration findings:

| Operation | Count | Average | Max | Interpretation |
|-----------|------:|--------:|----:|----------------|
| `receive-header` | `1,992,940` | `1,474.78ms` | `52,261.89ms` | Waiting for the next response header dominates the timeout path. |
| `receive-body` | `2,008,597` | `8.34ms` | `3,512.89ms` | Body reads can spike but are not the first-order timeout source. |
| `send-write` | `2,044,075` | `0.07ms` | `67.28ms` | Client writes are not blocking long enough to explain the receive timeout. |

Timeline note:

- At `12:47:45`, peak sessions reached `9,802`, pending requests were already `35,552`, and `receive-header` max was `14,909.94ms`.
- At `12:47:46`, `NoBufferSpaceAvailable` appeared (`350`) and sessions started falling (`9,533`).
- At `12:48:00`, `receive|IOException|TimedOut` appeared (`95`) with `receive-header` max already `25,377.33ms`.
- At `12:48:37`, `receive-header` max crossed `32,784.10ms`; final timeout/no-buffer counts had already accumulated (`410` / `1,742`).

Interpretation:

- The remaining failure is not client `WriteAsync` stalling.
- The response stream waits too long for the next packet header after the system accumulates a broad outstanding backlog around late ramp-up.
- The next optimization should reduce late-ramp outstanding pressure or server response latency before receive-header waits cross timeout territory.

## Iteration 5: Rejected Stability-Restore Candidate

After confirming that the latest failed path was not client write blocking, we tested whether restoring the older adaptive defaults would recover stability:

```text
MaxWindow=16
RttTargetMs=12000
RttHighMs=20000
IncreaseEveryResponses=256
```

This was a validation experiment only. The candidate was not kept in code because it regressed the hard guardrails badly.

Verification:

| Check | Result |
|-------|--------|
| `dotnet build FastPortCharp.sln -c Release` | Passed, 0 warnings, 0 errors |
| `dotnet test FastPortCharp.sln -c Release --no-build` | Passed, 111 tests |
| Stability-restore smoke validation | Passed |
| Stability-restore focused 10K validation | Failed under hard guardrails |

Artifacts:

```text
artifacts/load-validation/adaptive-pacing-stability-restore-smoke/summary.md
artifacts/load-validation/adaptive-pacing-stability-restore-s5/summary.md
artifacts/load-validation/adaptive-pacing-stability-restore-s5/s5-random-10k.metrics.jsonl
artifacts/load-validation/adaptive-pacing-stability-restore-s5/s5-random-10k.combined.metrics.jsonl
```

Focused 10K result:

| Metric | Operation-Duration Diagnostic | Stability-Restore Candidate | Direction |
|--------|------------------------------:|----------------------------:|-----------|
| Result | Failed | Failed | Stable hard failure |
| Peak sessions | `9,802 / 10,000` | `9,690 / 10,000` | Regression |
| Final disconnects | `2,152` | `5,936` | Major regression |
| `receive\|IOException\|TimedOut` | `410` | `4,554` | Major regression |
| `send\|IOException\|NoBufferSpaceAvailable` | `1,742` | `1,340` | Improvement |
| Max TPS | `9,254.96` | `8,176.32` | Regression |
| RTT P95 | `14,980.04ms` | `20,569.94ms` | Regression |
| RTT P99 | `22,016.89ms` | `32,961.34ms` | Regression |
| Pending requests/session | `3.75` | `3.81` | Regression |
| Server backpressure events | `3,850` | `3,225` | Improvement |
| `receive-header` max | `52,261.89ms` | `73,423.99ms` | Regression |
| `send-write` max | `67.28ms` | `398.59ms` | Regression |

Interpretation:

- Restoring `12s/20s/256` did reduce send-side `NoBufferSpaceAvailable`, but it caused much worse response-header waits and session loss.
- This confirms the current failure cannot be solved by simply making adaptive backoff more conservative.
- The candidate was reverted; the retained defaults remain:

```text
MaxWindow=8
RttTargetMs=14000
RttHighMs=24000
IncreaseEveryResponses=128
```

- The next viable lane should be pressure-aware pacing that reacts to backlog/receive-header delay earlier, or a server/test-server response path change. Static threshold-only tuning is now low confidence.

## Iteration 6: Rejected Header-Wait Pressure Candidate

We tested a minimal pressure-aware pacing candidate that reduced the adaptive window when a `receive-header` read waited longer than the current adaptive RTT thresholds:

- `receive-header >= RttTargetMs`: decrement the current adaptive window by `1`.
- `receive-header >= RttHighMs`: halve the current adaptive window.
- Keep the retained defaults: `MaxWindow=8`, `RttTargetMs=14000`, `RttHighMs=24000`, `IncreaseEveryResponses=128`.

This was also rejected after focused 10K. The signal reduced send-side `NoBufferSpaceAvailable`, but it increased receive timeouts, disconnects, RTT tail, and pending request depth. The code change was reverted.

Verification:

| Check | Result |
|-------|--------|
| `dotnet build FastPortCharp.sln -c Release` | Passed, 0 warnings, 0 errors |
| `dotnet test FastPortCharp.sln -c Release --no-build` | Passed, 112 tests while candidate existed; passed 111 tests after revert |
| Header-pressure smoke validation | Passed |
| Header-pressure focused 10K validation | Failed under hard guardrails |

Artifacts:

```text
artifacts/load-validation/adaptive-pacing-header-pressure-smoke/summary.md
artifacts/load-validation/adaptive-pacing-header-pressure-s5/summary.md
artifacts/load-validation/adaptive-pacing-header-pressure-s5/s5-random-10k.metrics.jsonl
artifacts/load-validation/adaptive-pacing-header-pressure-s5/s5-random-10k.combined.metrics.jsonl
```

Focused 10K result:

| Metric | Operation-Duration Diagnostic | Header-Pressure Candidate | Direction |
|--------|------------------------------:|--------------------------:|-----------|
| Result | Failed | Failed | Stable hard failure |
| Peak sessions | `9,802 / 10,000` | `9,698 / 10,000` | Regression |
| Final disconnects | `2,152` | `2,453` | Regression |
| `receive\|IOException\|TimedOut` | `410` | `1,236` | Regression |
| `send\|IOException\|NoBufferSpaceAvailable` | `1,742` | `1,217` | Improvement |
| Max TPS | `9,254.96` | `9,735.19` | Improvement |
| RTT P95 | `14,980.04ms` | `23,529.55ms` | Regression |
| RTT P99 | `22,016.89ms` | `29,609.81ms` | Regression |
| Pending requests/session | `3.75` | `4.05` | Regression |
| Server backpressure events | `3,850` | `1,863` | Improvement |
| `receive-header` max | `52,261.89ms` | `59,731.73ms` | Regression |
| `send-write` max | `67.28ms` | `106.58ms` | Regression |

Interpretation:

- Header-wait pressure reacted to a real symptom but too late or in the wrong feedback loop.
- It traded send-side pressure for worse receive timeout and session loss, so it should not be kept.
- The next lane should not be another client threshold-only or header-wait-only change. Move toward server/test-server response processing or cloud split validation to remove same-machine scheduling noise before further tuning.

## Iteration 7: Stop Condition

The subsequent `$pdca iterate` request was handled as a stop-condition pass, not a code-change pass.

Reason:

- the feature is intentionally scoped to client-side load generation and validation;
- the current design says not to change `LibNetworks`, `FastPortTestSmokeServer`, or server send behavior;
- retained defaults still fail hard guardrails with peak `9,802 / 10,000`, final disconnects `2,152`, and `receive|IOException|TimedOut = 410`;
- stability-restore and header-wait pressure candidates both made the critical guardrails worse;
- the feature already exceeded the normal PDCA Act iteration limit.

Decision:

- no new code candidate was kept for this iteration;
- do not reapply static threshold-only or header-wait-only pacing feedback under this feature;
- keep the retained defaults at `MaxWindow=8`, `RttTargetMs=14000`, `RttHighMs=24000`, `IncreaseEveryResponses=128`;
- move the next optimization lane to server/test-server response processing or cloud split validation.

## Iteration 9: Post-Queue-Refactor Recheck

The 2026-05-05 iterate request was treated as a recheck after later send-queue and test-tool renaming work, not as a new threshold candidate.

Reason:

- `BaseSession` send-queue work happened after the adaptive threshold experiments, so the old adaptive failure needed to be reinterpreted against newer artifacts.
- The latest available adaptive-focused post-queue artifact is still not clean under the hard guardrails:

```text
artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive/summary.md
```

| Metric | Value | Hard-Guardrail Interpretation |
|--------|------:|-------------------------------|
| Peak sessions | `9,975 / 10,000` | Better than the failed threshold artifacts, but not full retention. |
| Final disconnects | `2` | Fails current `MaxFinalDisconnectCount = 0`. |
| `receive\|IOException\|TimedOut` | `1,266` | Fails current receive-timeout class threshold. |
| Max TPS | `7,901.40` | Worse than prior adaptive reference. |
| RTT P95 / P99 | `17,796.60ms / 27,398.15ms` | P99 remains above the intended tail target. |

Decision:

- no additional client threshold-only or header-wait-only candidate is justified in this feature;
- keep the hard validation guardrails and operation-duration diagnostics;
- treat the retained adaptive defaults as diagnostic, not as a proven improvement;
- close this feature only after writing a report that explicitly records the failed optimization outcome, or split the next fix into server/test-server response processing or cloud split validation.
