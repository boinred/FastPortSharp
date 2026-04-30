# Completion Report: loadrunner-10k-session-rtt-validation

> Date: 2026-04-30 | Status: Completed

---

## 1. Summary

`loadrunner-10k-session-rtt-validation` completed the planned measurement validation for per-session RTT telemetry under focused 10K load.

This feature did not improve runtime code. Its value is diagnostic:

- It verified `sessionRtt` in real 10K artifacts.
- It merged server metrics with client metrics for the final run.
- It updated benchmark documentation.
- It identified the next optimization direction from evidence.

Final conclusion:

The 10K RTT tail is broad pressure, not only a few isolated slow sessions. Slow outliers exist, but the p95-of-session-P95 is already close to global RTT P95.

## 2. Related Documents

- Plan: `docs/01-plan/features/loadrunner-10k-session-rtt-validation.plan.md`
- Design: `docs/02-design/features/loadrunner-10k-session-rtt-validation.design.md`
- Do: `docs/02-design/features/loadrunner-10k-session-rtt-validation.do.md`
- Analysis: `docs/03-analysis/loadrunner-10k-session-rtt-validation.analysis.md`
- Benchmark: `docs/load-validation-benchmark-results.md`

## 3. Completed Items

- [x] Release build verified.
- [x] Focused 10K validation executed.
- [x] Server telemetry export enabled for final run.
- [x] Client/server metrics merged.
- [x] `summary.md` verified for `Session RTT` and slow session lines.
- [x] `summary.json` verified for `sessionRtt*` fields.
- [x] Raw client JSONL verified for `clientObserved.sessionRtt`.
- [x] Benchmark document updated.
- [x] Unit tests passed.
- [x] Next bottleneck feature direction selected.

## 4. Quality Metrics

| Check | Result |
|-------|--------|
| Match rate | `100%` |
| Release build | Passed |
| Build warnings/errors | `0 / 0` |
| Unit tests | `104 / 104` passed |
| Focused 10K validation | Passed |
| Server/client merge | `407 / 0` unmatched client samples |
| Runtime code changed | No |

Verification commands:

```bash
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln --no-build
```

Runtime validation command:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --pacing-policy adaptive-window \
  --output artifacts/load-validation/s5-session-rtt-validation \
  --server-metrics artifacts/load-validation/s5-session-rtt-validation/server.metrics.jsonl
```

## 5. Final 10K Result

Artifact:

`artifacts/load-validation/s5-session-rtt-validation/summary.md`

| Metric | Value |
|--------|------:|
| Run ID | `20260430-172637-staged` |
| Result | Passed |
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
| Max scheduler drift | `12.12ms` |

## 6. Session RTT Findings

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

Interpretation:

- Global RTT P95: `19,210.39ms`
- P95 of per-session P95: `18,211.02ms`
- Max session P95: `38,710.49ms`

Because global RTT P95 and p95-of-session-P95 are close, most eligible sessions are already slow. The Top 5 slow sessions are worse, but they are not the sole source of the tail.

## 7. Comparison To Previous Candidate

Compared with `s5-send-channel-queue-batch-pool-adaptive`, the final validation run is a mixed re-measurement.

Improved:

- Peak sessions: `9,975 / 10,000` -> `10,000 / 10,000`
- Final disconnects: `2` -> `0`
- Max TPS: `7,901.40` -> `9,371.08`
- Max pending request count: `38,246` -> `36,695`
- Max pending send requests: `1,282` -> `1,095`
- RTT P99: `27,398.15ms` -> `24,863.90ms`
- receive timeout: `1,266` -> `184`
- Max scheduler drift: `19.66ms` -> `12.12ms`

Still problematic:

- RTT P95: `17,796.60ms` -> `19,210.39ms`
- send `NoBufferSpaceAvailable`: `0` -> `1,639`
- server send backpressure: `0` -> `1,583`
- socket error rate: `0.12%` -> `0.13%`

These differences should not be described as improvements caused by this feature. No performance code changed. The result is a better-instrumented re-measurement.

## 8. Lessons Learned

### Keep

- Keep server metrics merge enabled for focused 10K benchmark runs.
- Keep benchmark docs separate from gitignored runtime artifacts.
- Keep distinguishing measurement features from optimization features.

### Problem

- The previous high RTT result was under-explained by global RTT P95/P99 alone.
- Running without server telemetry export makes server-side pressure columns misleading.
- The smoke server logs are very verbose during 10K runs.

### Try

- Decompose broad RTT pressure into client pacing wait, server processing, server send queue/drain, and socket backpressure phases.
- Add or reuse telemetry that can explain where the broad delay accumulates.
- Treat socket error correlation as supporting evidence, not the primary next step.

## 9. Next Steps

Recommended next PM feature:

`throughput-pacing-server-processing-decomposition`

Purpose:

Separate the broad 10K RTT pressure into measurable phases:

- client pacing wait
- client pending request depth
- server receive/parse/echo processing
- server send queue/drain time
- socket send backpressure

Suggested next command:

`$pdca pm throughput-pacing-server-processing-decomposition`
