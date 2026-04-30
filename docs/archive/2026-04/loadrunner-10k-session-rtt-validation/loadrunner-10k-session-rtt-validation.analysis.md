# Gap Analysis: loadrunner-10k-session-rtt-validation

> Date: 2026-04-30 | Design: docs/02-design/features/loadrunner-10k-session-rtt-validation.design.md

---

## Match Rate: 100%

## Summary

`loadrunner-10k-session-rtt-validation` matches the design. The feature was intentionally measurement-focused and did not require engine or LoadRunner implementation changes.

The focused 10K validation was executed with adaptive-window pacing, server telemetry export, and server/client metrics merge. The resulting artifacts include raw client metrics, server metrics, combined metrics, `summary.json`, and `summary.md`. The new `sessionRtt` telemetry is present in raw JSONL and summarized in both machine-readable and Markdown outputs.

The validation answered the PM question: the current RTT tail is not explained only by a few slow sessions. The broad session-level distribution is already high, so the next feature should focus on throughput/pacing/server processing decomposition before isolated starvation tuning.

## Design Checklist

| # | Design Item | Result | Evidence |
|---|-------------|--------|----------|
| 1 | Release build passes | Match | `dotnet build FastPortCharp.sln -c Release` passed with 0 warnings and 0 errors |
| 2 | 10K validation run completes | Match | `s5-random-10k` passed in run `20260430-172637-staged` |
| 3 | `summary.md` contains `Session RTT` | Match | `artifacts/load-validation/s5-session-rtt-validation/summary.md` includes the table column and formatted session RTT values |
| 4 | `summary.md` contains slow session lines | Match | Top 5 slow session lines are present for sessions `7977`, `8587`, `9484`, `6764`, and `8095` |
| 5 | `summary.json` contains non-zero session RTT fields | Match | `sessionRttTrackedSessionCount = 10000`, `sessionRttEligibleSessionCount = 9922` |
| 6 | raw observed JSONL contains `clientObserved.sessionRtt` | Match | `s5-random-10k.metrics.jsonl` contains `clientObserved.sessionRtt` |
| 7 | benchmark document is updated | Match | `docs/load-validation-benchmark-results.md` now has `Session RTT Validation Follow-up` |
| 8 | next bottleneck feature is selected from evidence | Match | next direction is `throughput/pacing/server processing decomposition` |

## Runtime Result

Artifact root:

`artifacts/load-validation/s5-session-rtt-validation/`

Final run:

| Field | Value |
|-------|------:|
| Run ID | `20260430-172637-staged` |
| Result | Passed |
| Stage | `s5-random-10k` |
| Peak sessions | `10,000 / 10,000` |
| Final disconnects | `0` |
| Max TPS | `9,371.08` |
| Max pending request count | `36,695` |
| Max pending send requests | `1,095` |
| Server send backpressure events | `1,583` |
| Max send buffer bytes | `64,204` |
| RTT P95 | `19,210.39ms` |
| RTT P99 | `24,863.90ms` |
| Socket error rate | `0.13%` |
| `send\|IOException\|NoBufferSpaceAvailable` | `1,639` |
| `receive\|IOException\|TimedOut` | `184` |
| Merge | `407 / 0` unmatched client samples |

## Session RTT Evidence

| Session RTT Metric | Value |
|--------------------|------:|
| Tracked sessions | `10,000` |
| Eligible sessions | `9,922` |
| Max excluded low-sample sessions | `773` |
| P50 of session P95 | `13,663.21ms` |
| P95 of session P95 | `18,211.02ms` |
| P99 of session P95 | `23,295.81ms` |
| Max session P95 | `38,710.49ms` |
| Max session P99 | `87,523.53ms` |
| Max session max RTT | `93,670.83ms` |

Slowest sessions:

| Session | Samples | RTT P50 | RTT P95 | RTT P99 | Max RTT |
|---------|--------:|--------:|--------:|--------:|--------:|
| `7977` | `39 / 39` | `2,893.24ms` | `38,710.49ms` | `54,278.30ms` | `57,058.88ms` |
| `8587` | `26 / 26` | `3,337.31ms` | `31,466.97ms` | `34,156.44ms` | `35,037.68ms` |
| `9484` | `71 / 71` | `7,534.53ms` | `31,103.52ms` | `44,122.22ms` | `45,754.75ms` |
| `6764` | `49 / 49` | `191.68ms` | `30,370.43ms` | `32,200.57ms` | `32,711.67ms` |
| `8095` | `33 / 33` | `355.45ms` | `29,626.03ms` | `30,251.24ms` | `30,398.60ms` |

## Comparison Interpretation

Compared with `s5-send-channel-queue-batch-pool-adaptive`, the validation result is mixed.

Improved:

- Peak sessions: `9,975 / 10,000` -> `10,000 / 10,000`
- Final disconnects: `2` -> `0`
- Max TPS: `7,901.40` -> `9,371.08`
- Max pending request count: `38,246` -> `36,695`
- Max pending send requests: `1,282` -> `1,095`
- RTT P99: `27,398.15ms` -> `24,863.90ms`
- receive timeout: `1,266` -> `184`
- Max scheduler drift: `19.66ms` -> `12.12ms`

Regressed or still problematic:

- RTT P95: `17,796.60ms` -> `19,210.39ms`
- send `NoBufferSpaceAvailable`: `0` -> `1,639`
- server send backpressure: `0` -> `1,583`
- socket error rate: `0.12%` -> `0.13%`

This should not be treated as a code improvement because no performance code changed in this feature. The differences are runtime re-measurement results under the same broad candidate line, now with the additional session RTT visibility and server metrics merge.

## Deviations From Design

| Item | Type | Explanation |
|------|------|-------------|
| Server metrics merge | Intentional strengthening | The design allowed combined metrics as optional. The final run enabled `--server-metrics` so the result can be compared with prior 10K benchmarks that used server telemetry. |
| Initial non-merged run | Excluded from final benchmark | An initial run validated `sessionRtt`, but server telemetry was disabled. It was documented in the Do notes and not used as the final benchmark result. |

No design requirement is missing.

## Remaining Risks

- Run-to-run variance is still present. The result should not be interpreted as a performance improvement caused by this feature.
- Server logs are extremely verbose during 10K because smoke sessions log accept/disconnect events. This does not block the feature, but it makes long-run terminal output noisy.
- Socket pressure remains: `send|IOException|NoBufferSpaceAvailable = 1,639` and server send backpressure `1,583`.
- Broad RTT pressure remains: global RTT P95 `19,210.39ms` and p95-of-session-P95 `18,211.02ms`.

## Recommendation

Proceed to report. Match rate is 100%.

The next PM feature should be:

`throughput-pacing-server-processing-decomposition`

Suggested initial goal:

Separate the broad 10K RTT pressure into client pacing delay, server receive/parse/echo processing, server send queue/drain time, and socket backpressure time so the next optimization changes code rather than only improving measurement.

## Next Steps

- [x] Complete Check phase for `loadrunner-10k-session-rtt-validation`.
- [ ] Run `$pdca report loadrunner-10k-session-rtt-validation`.
- [ ] Start PM for `throughput-pacing-server-processing-decomposition`.
