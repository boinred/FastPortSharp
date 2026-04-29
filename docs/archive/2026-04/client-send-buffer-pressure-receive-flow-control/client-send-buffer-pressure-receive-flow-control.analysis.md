# Gap Analysis: client-send-buffer-pressure-receive-flow-control

> Date: 2026-04-29 | Design: docs/02-design/features/client-send-buffer-pressure-receive-flow-control.design.md

---

## Match Rate: 100%

Scoring basis: 17 matched or substantially matched items out of 17 design-scored items. The core code path, deterministic edge-case tests, Release build, smoke runtime validation, focused 10K uncapped/capped validation, and benchmark documentation are complete.

## Summary

The implementation matches the main design direction:

- server send queue still drains only the `sentSize` returned from `Socket.SendAsync`;
- server send draining is now budgeted per wake by bytes and send-operation count;
- transient `NoBufferSpaceAvailable` / `WouldBlock` backoff is configurable;
- send drain yield telemetry is exposed through raw and observed server metrics;
- the load runner has an optional per-session outstanding request cap;
- load validation forwards the cap and reports drain yield metrics.

The implementation covers transient socket backpressure and load-runner gating with deterministic tests. Release smoke validation confirms the binaries and server metrics merge path. Focused 10K uncapped/capped runs now provide the improvement/tradeoff data for `NoBufferSpaceAvailable`.

## Design Match Matrix

| # | Design Item | Implementation Evidence | Status |
|---:|-------------|-------------------------|--------|
| 1 | Preserve `Drain(sentSize)` invariant | `BaseSession.DoWorkSendBuffers` peeks a bounded chunk, calls `Socket.SendAsync`, then drains only `sentSize`. | Match |
| 2 | Do not revert to full-queue send | `readSize` remains bounded by queued bytes, `SendChunkBytes`, and remaining drain budget. | Match |
| 3 | Extend `SessionSendOptions` | `MaxDrainBytesPerSignal`, `MaxDrainOperationsPerSignal`, `TransientSendBackoffMs`, and normalized properties exist. | Match |
| 4 | Enforce max bytes per send wake | `drainedBytesThisWake` is checked against `NormalizedMaxDrainBytesPerSignal`. | Match |
| 5 | Enforce max operations per send wake | `sendOperationsThisWake` is checked against `NormalizedMaxDrainOperationsPerSignal`. | Match |
| 6 | Resignal and yield on budget exhaustion | `RecordSendDrainYieldAndResignal()` records queued bytes, signals, and `Task.Yield()` is used. | Match |
| 7 | Configurable transient send backoff | `WaitTransientSendBackoffAsync` uses normalized backoff and yields when the value is zero. | Match |
| 8 | Do not drain or complete on transient send error | The transient exception path records telemetry and continues without `Drain` or completion; deterministic test verifies pending send and sent counters do not move before retry success. | Match |
| 9 | Add send drain yield telemetry | `RecordSendDrainYield`, `SendDrainYieldCount`, and `MaxSendDrainYieldQueuedBytes` are implemented. | Match |
| 10 | Add observed drain yield metrics | `ServerObservedMetricsSnapshot` maps count, per-second rate, and max queued bytes. | Match |
| 11 | Add load-runner scenario cap | `MaxPendingRequestsPerSession` is parsed, carried into `LoadScenario`, and printed in the plan. | Match |
| 12 | Gate sends by per-session outstanding count | `LoadSession` waits before send when `_outstandingRequests >= cap`; deterministic tests verify blocking, decrement release, and cancellation exit. | Match |
| 13 | Decrement outstanding count on valid response | `ParseEchoResponse` decrements after a valid protobuf response parse. | Match |
| 14 | Forward cap through load validation | `LoadValidationOptions` and `LoadRunnerCommandBuilder` pass `--max-pending-requests-per-session`. | Match |
| 15 | Extend validation summary | `LoadValidationStageSummary`, evaluator, and markdown summary include drain yield fields. | Match |
| 16 | Unit tests for edge cases | Option normalization, telemetry, observed metrics, command forwarding, budget yield, transient no-drain/no-completion, pacing gate, cancellation, and valid-response decrement are covered. | Match |
| 17 | Runtime validation matrix and benchmark comparison | Debug build/test, Release build, smoke runtime validation, focused 10K uncapped, focused 10K cap `4`, and benchmark documentation are complete. | Match |

## Implemented Items

- [x] `SessionSendOptions` gained per-wake drain and transient backoff settings.
- [x] `BaseSession` send loop now budgets bytes and operations per signal.
- [x] Partial-send correctness remains based on `Drain(sentSize)`.
- [x] Budget exhaustion records send drain yield and reschedules the send loop.
- [x] `NoBufferSpaceAvailable` and `WouldBlock` still record backpressure and retry without draining.
- [x] Server telemetry and observed metrics expose drain yield count/rate/max queued bytes.
- [x] Load runner supports `--max-pending-requests-per-session`.
- [x] Load validation forwards the new cap to the runner.
- [x] Validation summaries include drain yield information.
- [x] Unit tests were updated for option parsing, telemetry mapping, evaluator output, summary writing, and budget-yield behavior.
- [x] Unit tests now cover transient send backpressure no-drain/no-completion behavior.
- [x] Unit tests now cover load-runner cap gating, cancellation, and valid-response decrement behavior.
- [x] Verification completed: `dotnet build FastPortCharp.sln`, `dotnet test FastPortCharp.sln --no-build`, `dotnet build FastPortCharp.sln -c Release`, smoke load validation, and `git diff --check`.
- [x] Focused 10K uncapped/capped validation completed and summarized in `docs/load-validation-benchmark-results.md`.

## Missing Items

- [x] Add deterministic test coverage for the transient send backpressure path:
  - no queue drain on `NoBufferSpaceAvailable` / `WouldBlock`;
  - no pending send completion on transient send error;
  - zero backoff uses cooperative yield.
- [x] Add deterministic test coverage for load-runner pacing:
  - cap reached blocks the next send;
  - valid response decrements outstanding count;
  - cancellation exits the gate cleanly.
- [x] Run Release verification:
  - `dotnet build FastPortCharp.sln -c Release`.
- [x] Run smoke runtime validation:
  - output: `artifacts/load-validation/receive-flow-control-smoke/summary.md`;
  - result: passed;
  - `smoke-fixed-10`: peak `10/10`, max pending request `2`, max pending send `2`, socket error rate `0.00%`;
  - `smoke-random-25`: peak `25/25`, max pending request `5`, max pending send `4`, socket error rate `0.00%`.
- [x] Run focused runtime validation matrix:
  - focused 10K uncapped budgeted-drain;
  - focused 10K with `--max-pending-requests-per-session 4`;
  - optional cap `1` diagnostic remains optional.
- [x] Update benchmark/reporting docs with observed `NoBufferSpaceAvailable`, max pending request, max pending send, RTT, and drain yield comparison.

## Changed Items

- [x] Design suggested `--runner-args "--max-pending-requests-per-session 4"` as one possible validation route. Implementation chose an explicit `FastPortLoadValidation` option instead, which the design allowed if generic runner-args forwarding did not exist.
- [x] Receive pause remains deferred. This matches the design's deferred-item section and is not counted as a missing implementation item.

## Recommendations

1. Proceed to archive for this PDCA feature.
2. Open a follow-up PDCA if we want a balanced pacing/receive strategy that keeps the NoBuffer reduction without cap `4` RTT tail regression.
3. Optionally run cap `1` only if we need a conservative bound; cap `4` is already sufficient to prove client pacing is material.

## Next Steps

- [x] Fix missing/partial tests.
- [x] Run Release and smoke validation.
- [x] Re-run analysis and raise match rate above `90%`.
- [x] Proceed to `$pdca report client-send-buffer-pressure-receive-flow-control`.
- [x] Run focused 10K benchmark variants as follow-up measurement work.
- [ ] Archive with `$pdca archive client-send-buffer-pressure-receive-flow-control`.
