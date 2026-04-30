# loadrunner-10k-session-rtt-validation - Do Document

> Version: 1.0.0 | Date: 2026-04-30 | Status: Completed
> Plan: docs/01-plan/features/loadrunner-10k-session-rtt-validation.plan.md
> Design: docs/02-design/features/loadrunner-10k-session-rtt-validation.design.md

---

## 1. Summary

Executed the focused 10K load validation for `loadrunner-10k-session-rtt-validation` and verified the new `sessionRtt` telemetry in runtime artifacts.

No engine or LoadRunner code was changed. The work consisted of:

- Release build verification
- focused 10K validation execution
- server telemetry export and merge
- `summary.md`, `summary.json`, raw JSONL artifact inspection
- benchmark document update
- unit test verification

## 2. Commands Run

### 2.1 Release Build

```bash
dotnet build FastPortCharp.sln -c Release
```

Result:

- Passed
- Warnings: `0`
- Errors: `0`

### 2.2 Server With Telemetry Export

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer \
  --Telemetry:Output artifacts/load-validation/s5-session-rtt-validation/server.metrics.jsonl \
  --Telemetry:IntervalSeconds 1
```

Result:

- Server started on `0.0.0.0:6628`
- Server telemetry export enabled
- Server process was stopped after validation
- Port `6628` was verified as released afterward

### 2.3 Focused 10K Validation

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --pacing-policy adaptive-window \
  --output artifacts/load-validation/s5-session-rtt-validation \
  --server-metrics artifacts/load-validation/s5-session-rtt-validation/server.metrics.jsonl
```

Result:

- Exit code: `0`
- Summary: `artifacts/load-validation/s5-session-rtt-validation/summary.md`
- Run ID: `20260430-172637-staged`
- Started: `2026-04-30T17:26:37.3803570+09:00`
- Completed: `2026-04-30T17:33:44.1348890+09:00`

### 2.4 Unit Tests

```bash
dotnet test FastPortCharp.sln --no-build
```

Result:

- Passed: `104`
- Failed: `0`
- Skipped: `0`

## 3. Artifacts

| Artifact | Purpose |
|----------|---------|
| `artifacts/load-validation/s5-session-rtt-validation/manifest.json` | Run manifest and pacing options |
| `artifacts/load-validation/s5-session-rtt-validation/summary.md` | Human-readable validation summary |
| `artifacts/load-validation/s5-session-rtt-validation/summary.json` | Machine-readable validation summary |
| `artifacts/load-validation/s5-session-rtt-validation/s5-random-10k.metrics.jsonl` | Client observed metrics stream |
| `artifacts/load-validation/s5-session-rtt-validation/server.metrics.jsonl` | Server observed metrics stream |
| `artifacts/load-validation/s5-session-rtt-validation/s5-random-10k.combined.metrics.jsonl` | Client/server merged metrics stream |

Artifact line counts:

| File | Lines |
|------|------:|
| `server.metrics.jsonl` | `435` |
| `s5-random-10k.combined.metrics.jsonl` | `407` |
| `s5-random-10k.metrics.jsonl` | `407` |

## 4. Final 10K Result

| Metric | Value |
|--------|------:|
| Result | Passed |
| Peak sessions | `10,000 / 10,000` |
| Final disconnects | `0` |
| Max TPS | `9,371.08` |
| Max pending request count | `36,695` |
| Max pending send requests | `1,095` |
| Server send backpressure events | `1,583` |
| Rejected send requests/bytes | `0 / 0` |
| Max send buffer bytes | `64,204` |
| Max scheduler drift | `12.12ms` |
| RTT P95 | `19,210.39ms` |
| RTT P99 | `24,863.90ms` |
| Socket error rate | `0.13%` |
| `send\|IOException\|NoBufferSpaceAvailable` | `1,639` |
| `receive\|IOException\|TimedOut` | `184` |
| Merge | `407 / 0` unmatched client samples |

## 5. Session RTT Verification

`sessionRtt` was verified in all required outputs:

- `summary.md`: `Session RTT` table column exists.
- `summary.md`: slow session lines exist.
- `summary.json`: `sessionRtt*` stage summary fields exist.
- raw client JSONL: `clientObserved.sessionRtt` exists.
- combined JSONL: client/server merged samples were generated.

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

Top slow sessions:

| Session | Samples | RTT P50 | RTT P95 | RTT P99 | Max RTT |
|---------|--------:|--------:|--------:|--------:|--------:|
| `7977` | `39 / 39` | `2,893.24ms` | `38,710.49ms` | `54,278.30ms` | `57,058.88ms` |
| `8587` | `26 / 26` | `3,337.31ms` | `31,466.97ms` | `34,156.44ms` | `35,037.68ms` |
| `9484` | `71 / 71` | `7,534.53ms` | `31,103.52ms` | `44,122.22ms` | `45,754.75ms` |
| `6764` | `49 / 49` | `191.68ms` | `30,370.43ms` | `32,200.57ms` | `32,711.67ms` |
| `8095` | `33 / 33` | `355.45ms` | `29,626.03ms` | `30,251.24ms` | `30,398.60ms` |

## 6. Interpretation

The 10K RTT tail is not explained only by a few slow sessions.

The key evidence is:

- Global RTT P95: `19,210.39ms`
- P95 of per-session P95: `18,211.02ms`
- P99 of per-session P95: `23,295.81ms`
- Max session P95: `38,710.49ms`

Because global RTT P95 and p95-of-session-P95 are close, most eligible sessions are already experiencing high latency. There are still slow-session outliers, but they sit on top of broad pressure rather than being the only cause of the tail.

The next bottleneck feature should focus first on throughput/pacing/server processing decomposition. Socket error correlation is still useful because `send|IOException|NoBufferSpaceAvailable = 1,639` and `receive|IOException|TimedOut = 184` remain present, but the primary shape is broad load pressure.

## 7. Documentation Updated

Updated:

- `docs/load-validation-benchmark-results.md`

Added:

- latest 10K comparison against `s5-send-channel-queue-batch-pool-adaptive`
- session RTT validation follow-up section
- slowest session Top 5
- interpretation and next feature direction

## 8. Notes

An initial 10K run was executed without server telemetry export. It validated `sessionRtt`, but server pressure metrics were unavailable, so it was not used as the final benchmark result. The final documented result is the server-metrics merged run with Run ID `20260430-172637-staged`.
