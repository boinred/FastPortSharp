# Completion Report: 10k-load-bottleneck-telemetry

> Date: 2026-04-29 | Status: Completed | Match Rate: 100%

---

## 1. Summary

`10k-load-bottleneck-telemetry` extended FastPortSharp telemetry so the 10K load bottleneck can be diagnosed with cause-oriented signals instead of only pass/fail output.

Completion rate: 100%

The implementation is complete, but the focused 10K measurement did not pass. That is an important outcome of this feature: logging reduction alone did not fix the 10K bottleneck. The new telemetry shows successful connect attempts reached 10,000 with zero connect failures, but active sessions later dropped, pending requests accumulated, and RTT P95/P99 spiked heavily.

## 2. Related Documents

- Plan: `docs/01-plan/features/10k-load-bottleneck-telemetry.plan.md`
- Design: `docs/02-design/features/10k-load-bottleneck-telemetry.design.md`
- Do: `docs/02-design/features/10k-load-bottleneck-telemetry.do.md`
- Analysis: `docs/03-analysis/10k-load-bottleneck-telemetry.analysis.md`
- Focused run summary: `artifacts/load-validation/s5-logging-off/summary.md`
- Focused run JSON: `artifacts/load-validation/s5-logging-off/summary.json`
- Focused run JSONL: `artifacts/load-validation/s5-logging-off/s5-random-10k.metrics.jsonl`

## 3. Completed Items

- Added server-side send request, send completion, pending send, max pending send, send backpressure, send buffer sample, and max send buffer counters.
- Kept `LibNetworks` telemetry protocol-neutral.
- Added server observed metric fields and per-second send/backpressure rates.
- Instrumented `BaseSession` send enqueue, send completion, send buffer depth, and send-buffer backpressure.
- Added client-side connect attempt/failure counters.
- Added client pending request and max pending request counters.
- Added active session ratio.
- Added metrics reporter scheduler drift average/max.
- Extended `ClientObservedMetricsSnapshot` and `ServerObservedMetricsSnapshot`.
- Kept `FastPortLoadRunner` JSONL in the existing observed envelope shape.
- Extended `FastPortLoadValidation` summary records, evaluator, JSON output, and Markdown output with bottleneck fields.
- Added focused unit/integration coverage for telemetry counters, observed DTO mapping, JSON serialization, validation aggregation, and smoke-path server telemetry.
- Executed focused `s5-random-10k` validation and compared it with baseline.

## 4. Quality Metrics

| Metric | Result |
|--------|--------|
| Match rate | 100% |
| Design items implemented | 25 / 25 |
| Missing items | 0 |
| Build command | `dotnet build FastPortCharp.sln` |
| Build result | 0 warnings, 0 errors |
| Test command | `dotnet test FastPortCharp.sln --no-build` |
| Test result | 72 passed, 0 failed |
| Release build command | `dotnet build FastPortCharp.sln -c Release` |
| Release build result | 0 warnings, 0 errors |
| Focused validation command | `./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation --profile staged --stage s5-random-10k --output artifacts/load-validation/s5-logging-off` |
| Focused validation result | Failed thresholds, artifacts generated |

## 5. Measurement Result

Focused validation result:

| Metric | Baseline staged-local | Logging-off focused run |
|--------|----------------------:|------------------------:|
| Target sessions | 10,000 | 10,000 |
| Peak current sessions | 9,767 | 8,624 |
| Peak session ratio | 97.67% | 86.24% |
| Final disconnect count | 233 | 1,782 |
| Max socket error rate | 0.03% | 0.19% |
| Max RTT P95 | 8,738.94 ms | 43,268.80 ms |
| Max RTT P99 | 10,137.04 ms | 44,895.97 ms |
| Max pending request count | Not available | 55,695 |
| Max scheduler drift | Not available | 28.21 ms |
| Connect attempts | Not available | 10,000 |
| Connect failures | Not available | 0 |

Validation failures:

- Peak session ratio 86.24% is below 95.00%.
- Disconnect ratio 17.82% exceeds 5.00%.

Note: focused-run `Max TPS` in `summary.json` is `4,058,000.00`, but this is a one-sample per-second spike and should not be used as the comparative throughput conclusion. The later steady stdout samples were closer to roughly 2.3K TPS while active sessions stayed around 8,218.

## 6. Interpretation

The new telemetry narrows the bottleneck:

- Connect establishment was not the primary failure point in this run because `connectAttemptCount` reached 10,000 and `connectFailureCount` was 0.
- Active sessions peaked at 8,624 and settled around 8,218, so the missing capacity came from disconnects after successful connections.
- `maxPendingRequestCount` reached 55,695, showing request/response backlog accumulation during the high-load window.
- `schedulerDriftMaxMs` reached 28.21 ms, which is measurable but too small to explain RTT P95/P99 in the 43-45 second range by itself.
- Logging-off did not improve the previous baseline on this machine; it worsened peak ratio, disconnect count, socket error rate, and RTT tail latency.

## 7. Lessons Learned

### Keep

- Keep bottleneck telemetry in protocol-neutral counters in `LibNetworks`.
- Keep client-specific request backlog and scheduler drift in `FastPortLoadRunner`.
- Keep high-load validation artifacts out of git while documenting their paths in PDCA reports.

### Problem

- Current client-only JSONL does not capture server send backlog during the focused run, even though server telemetry counters now exist.
- The validation summary can record misleading one-sample rate spikes for `Max TPS` when an interval is abnormally short.
- The focused run shows disconnect/socket-error pressure after successful connects, but socket error details are not yet classified.

### Try

- Add server telemetry export/merge for focused validation runs so pending send and send buffer depth are available next to client pending request.
- Add client socket exception classification to separate reset, timeout, cancellation, and local resource pressure.
- Add worker delay instrumentation around receive parsing, channel write, send buffer drain, and socket send completion.
- Consider clamping or annotating anomalous per-second rate samples in `LoadValidationSummaryWriter`.

## 8. Next Steps

1. Archive this PDCA after review.
2. Open a follow-up PDCA for 10K disconnect/backlog root-cause analysis.
3. Prioritize server observed JSONL merge and socket error classification before another 10K comparison run.
