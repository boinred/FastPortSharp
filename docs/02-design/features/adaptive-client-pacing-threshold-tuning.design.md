# adaptive-client-pacing-threshold-tuning - Design Document

> Version: 1.0.0 | Date: 2026-05-03 | Status: Completed
> Level: Starter | Plan: docs/01-plan/features/adaptive-client-pacing-threshold-tuning.plan.md

---

## 1. Overview

This feature tunes the load-runner adaptive outstanding-request pacing thresholds used by `--pacing-policy adaptive-window`.

The feature is intentionally scoped to client-side load generation and validation. It should not change `LibNetworks`, `FastPortTestSmokeServer`, or server send behavior.

The current focused same-machine 10K artifact shows:

- RTT tail is broad pressure, not a small set of isolated slow sessions.
- Adaptive pacing is active and frequently constrained.
- The observed adaptive window range is only `1-5`.
- Max pacing average wait is `2,857.09ms`.
- Pending requests/session is still `3.67`.
- Server send and socket pressure are visible but bounded enough that a load-runner pacing pass is the next lowest-risk optimization.

## 2. Baseline

Baseline artifact:

```text
artifacts/load-validation/s5-session-rtt-validation/summary.json
```

Baseline decomposition:

| Metric | Value |
|--------|------:|
| Peak sessions | `10,000 / 10,000` |
| Max TPS | `9,371.08` |
| RTT P95 | `19,210.39ms` |
| RTT P99 | `24,863.90ms` |
| Per-session P95-of-P95 | `18,211.02ms` |
| RTT gap ratio | `5.20%` |
| Pending requests/session | `3.67` |
| Pending server send requests | `1,095` |
| Server send backpressure events | `1,583` |
| Max server send buffer bytes | `64,204` |
| Pacing average wait max | `2,857.09ms` |
| Pacing window range | `1-5` |
| `send\|IOException\|NoBufferSpaceAvailable` | `1,639` |
| `receive\|IOException\|TimedOut` | `184` |
| Max scheduler drift | `12.12ms` |

Current adaptive defaults:

| Option | Current |
|--------|--------:|
| `MinWindow` | `1` |
| `InitialWindow` | `4` |
| `MaxWindow` | `16` |
| `RttTargetMs` | `12,000` |
| `RttHighMs` | `20,000` |
| `IncreaseEveryResponses` | `256` |

## 3. Current Implementation

### 3.1 Pacer

`FastPortTestLoadRunner/OutstandingRequestPacer.cs` owns request permit pacing.

Behavior today:

- `WaitForPermitAsync` waits while `_inFlight >= _currentWindow`.
- `OnResponse` decrements `_inFlight` and calls `AdjustAdaptiveWindow`.
- `AdjustAdaptiveWindow`:
  - halves window when `rttMs >= RttHighMs`;
  - increments window by `1` after `IncreaseEveryResponses` responses with `rttMs <= RttTargetMs`;
  - resets stable response count for RTT in the neutral range;
  - records window increase/decrease and min/max samples.

### 3.2 Options

`FastPortTestLoadRunner/LoadRunnerOptions.cs` and `FastPortTestLoadValidation/LoadValidationOptions.cs` each define matching adaptive defaults.

`FastPortTestLoadValidation/LoadRunnerCommandBuilder.cs` forwards all adaptive options to `FastPortTestLoadRunner`, so validation and runner defaults must stay aligned.

### 3.3 Existing Coverage

Existing tests already cover:

- option parsing for explicit adaptive values;
- invalid adaptive value rejection;
- validation command propagation of adaptive options;
- adaptive window increase/decrease behavior;
- summary and JSON output of pacing metrics.

Missing coverage:

- no direct assertion that `--pacing-policy adaptive-window` alone uses the intended tuned defaults.

## 4. Design Decision

### 4.1 Chosen Strategy

Tune adaptive defaults, not the core algorithm.

The first pass should keep the algorithm simple:

- no new smoothing model;
- no percentile-based adaptive logic;
- no per-session adaptive windows;
- no server feedback loop;
- no `LibNetworks` changes.

The current issue can plausibly be caused by thresholds that are too conservative for the current 10K envelope:

- `RttTargetMs = 12,000` is below the observed broad session tail center.
- `RttHighMs = 20,000` is close to current global P95 and below current P99, so large parts of the tail trigger halving.
- `IncreaseEveryResponses = 256` can make recovery slow when the window collapses.
- `MaxWindow = 16` allows aggressive recovery if thresholds are loosened, so it should be capped lower in the tuned pass.

### 4.2 Tuned Defaults

Proposed tuned defaults:

| Option | Current | Tuned | Rationale |
|--------|--------:|------:|-----------|
| `MinWindow` | `1` | `1` | Keep the safety floor to avoid forcing more socket pressure during severe tail events. |
| `InitialWindow` | `4` | `4` | Keep ramp-up behavior stable and comparable to current artifacts. |
| `MaxWindow` | `16` | `8` | Cap recovery so looser RTT thresholds do not burst to a high outstanding depth. |
| `RttTargetMs` | `12,000` | `16,000` | Allow recovery when RTT is in the current broad-pressure but non-extreme range. |
| `RttHighMs` | `20,000` | `28,000` | Avoid halving on every sample around the current P95/P99 boundary; still back off on severe tail. |
| `IncreaseEveryResponses` | `256` | `128` | Recover faster after stable response periods without changing the step size. |

Expected effect:

- higher observed `MaxObservedPacingWindow` than current `5`, but capped at `8`;
- lower average pacing wait if pacing collapse is the dominant bottleneck;
- possibly higher TPS;
- potential risk of increased `NoBufferSpaceAvailable` or send backpressure, guarded by validation thresholds.

### 4.3 Fallback Candidate

If the tuned defaults fail guardrails, use a more conservative second candidate in the iterate phase:

| Option | Fallback |
|--------|---------:|
| `MinWindow` | `1` |
| `InitialWindow` | `4` |
| `MaxWindow` | `8` |
| `RttTargetMs` | `14,000` |
| `RttHighMs` | `24,000` |
| `IncreaseEveryResponses` | `128` |

The fallback keeps faster recovery but restores a stricter high watermark.

## 5. Files And Changes

### 5.1 Required Code Changes

| File | Change |
|------|--------|
| `FastPortTestLoadRunner/LoadRunnerOptions.cs` | Change adaptive default constants and usage text. |
| `FastPortTestLoadValidation/LoadValidationOptions.cs` | Change matching adaptive default constants and usage text. |
| `FastPortTestLoadValidation/LoadValidationStage.cs` | Add hard validation guardrails for final disconnects and receive timeout socket classifications. |
| `FastPortTestLoadValidation/LoadValidationEvaluator.cs` | Fail a stage when those hard guardrails are exceeded. |
| `FastPortTests/FastPortTestLoadRunnerTests.cs` | Add/adjust tests for default adaptive values. |
| `FastPortTests/FastPortTestLoadValidationTests.cs` | Add/adjust tests for validation default adaptive values, command propagation, and hard guardrail failures. |

### 5.2 Optional Documentation Changes

| File | Change |
|------|--------|
| `docs/load-validation-benchmark-results.md` | Add post-validation comparison after smoke and focused 10K results exist. |
| `HANDOFF.md` | Update next command and result summary at the end of the phase. |

### 5.3 Files Not To Change

| File/Area | Reason |
|-----------|--------|
| `LibNetworks/` | This feature tunes load generation, not engine send/receive behavior. |
| `FastPortTestSmokeServer/` | Echo behavior is not the current target. |
| `scripts/cloud/` | Cloud capacity is a separate blocked feature. |

## 6. Implementation Order

1. Update runner adaptive defaults.
   - `DefaultMaxWindow: 16 -> 8`
   - `DefaultRttTargetMs: 12_000 -> 16_000`
   - `DefaultRttHighMs: 20_000 -> 28_000`
   - `DefaultIncreaseEveryResponses: 256 -> 128`
   - keep min/initial unchanged.
2. Update validation adaptive defaults to the same values.
3. Update CLI usage text for runner and validation.
4. Add tests that assert `--pacing-policy adaptive-window` with no explicit tuning values resolves to the tuned defaults in both runner and validation.
5. Verify command builder still forwards adaptive tuning fields.
6. Run build/test.
7. Run smoke validation with `--pacing-policy adaptive-window`.
8. Run focused 10K validation only after smoke passes.
9. Run decomposition script on the new summary and compare against baseline.
10. Update benchmark docs and do notes with the result classification.

## 7. Verification Plan

### 7.1 Unit/Build

```bash
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln --no-build
```

Required test expectations:

- `FastPortTestLoadRunner` adaptive defaults:
  - min `1`, initial `4`, max `8`, target `16000`, high `28000`, increase every `128`.
- `FastPortTestLoadValidation` adaptive defaults:
  - same values as runner.
- explicit CLI values still override defaults.
- invalid adaptive values are still rejected.

### 7.2 Smoke Validation

```bash
dotnet run -c Release --project FastPortTestLoadValidation -- \
  --profile smoke \
  --pacing-policy adaptive-window \
  --output artifacts/load-validation/adaptive-pacing-threshold-smoke \
  --server-metrics artifacts/load-validation/adaptive-pacing-threshold-smoke/server.metrics.jsonl
```

Expected:

- run passes;
- manifest records tuned defaults;
- pacing fields appear in `summary.md` / `summary.json`.

### 7.3 Focused 10K Validation

```bash
dotnet run -c Release --project FastPortTestLoadValidation -- \
  --profile staged \
  --stage s5-random-10k \
  --pacing-policy adaptive-window \
  --output artifacts/load-validation/adaptive-pacing-threshold-s5 \
  --server-metrics artifacts/load-validation/adaptive-pacing-threshold-s5/server.metrics.jsonl
```

Post-run:

```bash
scripts/load-validation/decompose-summary.sh artifacts/load-validation/adaptive-pacing-threshold-s5/summary.json
```

## 8. Result Classification

Classify the 10K result as follows.

### Improvement

- peak sessions remain `10,000 / 10,000`;
- final disconnects remain near `0`;
- RTT P95 and/or P99 improves materially;
- pacing average wait drops materially or window recovers above current `5`;
- `NoBufferSpaceAvailable` does not materially exceed `1,639`;
- receive timeout count does not materially exceed `184`;
- pending requests/session does not exceed `3.67`.

### Mixed

- RTT improves but socket/server pressure regresses;
- pacing wait improves but pending requests/session grows;
- TPS improves while RTT P95/P99 worsens;
- smoke passes but 10K only partially improves.

### Regression

- peak sessions fail to reach `10,000 / 10,000`;
- disconnects or receive timeouts rise materially;
- `NoBufferSpaceAvailable` rises materially above baseline;
- RTT P95/P99 worsens without compensating pressure reduction.

### Hard Fail

The validation evaluator must fail a stage regardless of RTT improvement when either condition is observed:

- final disconnect count exceeds `0`;
- `receive|IOException|TimedOut` exceeds `0`.

This is stricter than the original ratio-based socket-error threshold. It exists because a lower RTT tail is not acceptable if the candidate loses sessions or converts pressure into receive timeouts.

## 9. Risks

| Risk | Mitigation |
|------|------------|
| Looser thresholds increase socket pressure | Cap max window at `8` and use socket/server pressure guardrails. |
| Same-machine result is noisy | Compare with existing artifact and mark result as local until cloud split validation exists. |
| Defaults change affects all adaptive runs | Add explicit tests and update usage text so the behavior is visible. |
| Algorithm tuning is insufficient | Use fallback candidate or add client write/read duration metrics in a follow-up. |
| 10K run is expensive | Run smoke first and only run focused 10K after tests/smoke pass. |

## 10. References

- `docs/01-plan/features/adaptive-client-pacing-threshold-tuning.plan.md`
- `docs/archive/2026-05/throughput-pacing-server-processing-decomposition/throughput-pacing-server-processing-decomposition.report.md`
- `docs/load-validation-benchmark-results.md`
- `artifacts/load-validation/s5-session-rtt-validation/summary.json`
- `FastPortTestLoadRunner/OutstandingRequestPacer.cs`
- `FastPortTestLoadRunner/LoadRunnerOptions.cs`
- `FastPortTestLoadValidation/LoadValidationOptions.cs`
- `FastPortTestLoadValidation/LoadRunnerCommandBuilder.cs`
- `FastPortTests/FastPortTestLoadRunnerTests.cs`
- `FastPortTests/FastPortTestLoadValidationTests.cs`
- `scripts/load-validation/decompose-summary.sh`
