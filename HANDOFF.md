# FastPortSharp Handoff

> Last updated: 2026-04-30
> Branch: `main`
> Remote baseline before this handoff update: `146b729 Add per-session RTT tail telemetry`

## Current State

- Active PDCA features: none.
- Primary feature: none.
- Latest archived PDCA feature: `loadrunner-10k-session-rtt-validation`
- Project level detected by bkit: `Starter`
- Current recommended action: start `$pdca pm throughput-pacing-server-processing-decomposition`.

The latest completed work was diagnostic, not a runtime optimization. It validated the latest 10K run with per-session RTT telemetry and confirmed that the RTT tail is broad load pressure, not only a few isolated slow sessions.

## Latest Completed Work

### LoadRunner 10K Session RTT Validation

- Archived PDCA documents under:
  - `docs/archive/2026-04/loadrunner-10k-session-rtt-validation/`
- Updated `docs/load-validation-benchmark-results.md` with the latest 10K comparison and session RTT interpretation.
- Verified `docs/.pdca-status.json` has no active feature after archive.

Final focused 10K artifact:

- `artifacts/load-validation/s5-session-rtt-validation/summary.md`

Important result:

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
| `send|IOException|NoBufferSpaceAvailable` | `1,639` |
| `receive|IOException|TimedOut` | `184` |
| Max scheduler drift | `12.12ms` |

Session RTT finding:

| Session RTT Metric | Value |
|--------------------|------:|
| Tracked sessions | `10,000` |
| Eligible sessions | `9,922` |
| P50 of session P95 | `13,663.21ms` |
| P95 of session P95 | `18,211.02ms` |
| P99 of session P95 | `23,295.81ms` |
| Max session P95 | `38,710.49ms` |
| Max session P99 | `87,523.53ms` |
| Max session max RTT | `93,670.83ms` |

Interpretation:

- Global RTT P95 is `19,210.39ms`.
- P95 of per-session P95 is `18,211.02ms`.
- These values are close enough to treat the current tail as broad high-load pressure.
- Slow outlier sessions still exist, but they are not the only source of the tail.

## Current Benchmark Interpretation

The current implementation can pass focused same-machine 10K validation, but it is not yet good enough for a real-time game workload.

What improved compared with the original server-merged failure path:

- Peak sessions now reach `10,000 / 10,000`.
- Final disconnects are down to `0`.
- Server pending send depth is no longer exploding into hundreds of thousands.
- Server send buffer bytes remain bounded.
- Per-session RTT telemetry is now available for tail analysis.

What remains weak:

- RTT P95 is still about `19.2s`.
- RTT P99 is still about `24.9s`.
- Max TPS is still too low for high-frequency movement workloads.
- Send-side `NoBufferSpaceAvailable` is reduced but still present.
- Server send backpressure and receive timeout still appear under 10K pressure.

## Recommended Roadmap

### 1. Throughput/Pacing/Server Processing Decomposition

Recommended command:

```text
$pdca pm throughput-pacing-server-processing-decomposition
```

Goal:

- Separate broad 10K RTT pressure into measurable phases:
  - client pacing wait
  - client pending request depth
  - server receive/parse/echo processing
  - server send queue/drain time
  - socket send backpressure

This should decide whether the next code change belongs in `FastPortLoadRunner`, `FastPortSmokeServer`, or `LibNetworks`.

### 2. Follow-up Optimization

Pick only after decomposition data is available.

Likely candidates:

- `adaptive-client-pacing-threshold-tuning`
- `server-processing-throughput-tuning`
- `receive-timeout-tail-flow-control`
- `send-throughput-drain-fairness-optimization`

### 3. Game Server Template Foundation

Recommended feature name:

```text
$pdca pm fastport-game-server-template-foundation
```

Purpose:

- Turn the optimized engine shape into a usable server template.
- Keep responsibilities separated:
  - `LibNetworks`: protocol-neutral engine core.
  - `FastPortServer`: basic network engine host/sample.
  - `FastPortSmokeServer`: echo/load validation server.
  - future game template: game protocol/session/handler replacement points.

Expected scope:

- game session base sample
- packet handler registration pattern
- startup/config/logging defaults
- telemetry hook points
- health/startup verification
- README or template usage guide

### 4. MAUI Dashboard

Start after the telemetry and server template boundaries are stable.

Likely candidates:

- `maui-telemetry-dashboard-foundation`
- `maui-load-validation-run-viewer`
- `maui-run-comparison-report-export`

The dashboard should consume the existing observed metric envelope:

- root `timestamp`
- `clientObserved`
- `serverObserved`

Initial views should focus on:

- TPS
- RTT P95/P99
- per-session RTT tail
- pending request/send
- pacing window/wait
- socket error classification
- server backpressure/send buffer
- stage pass/fail comparison

## Important Architecture Decisions

- `LibNetworks` should stay protocol-neutral.
- Smoke/load-test behavior belongs in `FastPortSmokeServer`, `FastPortLoadRunner`, and `FastPortLoadValidation`.
- Do not move echo/smoke protocol behavior back into `FastPortServer`.
- `FastPortServer` should remain a basic network engine host/sample until the game server template is explicitly designed.
- High-load generated artifacts remain under `artifacts/load-validation/` and should not be committed.
- Same-machine 10K results are useful for comparison, but server/runner split-machine validation is still needed before claiming production capacity.

## Verification Recently Run

Latest validation from the archived feature:

```bash
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln --no-build
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --pacing-policy adaptive-window \
  --output artifacts/load-validation/s5-session-rtt-validation \
  --server-metrics artifacts/load-validation/s5-session-rtt-validation/server.metrics.jsonl
```

Results:

- Release build passed with `0` warnings and `0` errors.
- Test suite passed: `104 / 104`.
- Focused 10K validation passed.
- Server/client merge produced `407` merged samples with `0` unmatched client samples.

## Suggested Commands

Check repository state:

```bash
git status --short --branch
```

Inspect PDCA status:

```text
$pdca status
```

Start next PM:

```text
$pdca pm throughput-pacing-server-processing-decomposition
```

## Notes For Next Session

- Start with `docs/.pdca-status.json`; it should have no active feature.
- Treat the latest 10K result as a diagnostic baseline, not a clean performance win.
- Keep server template work after the next decomposition/optimization pass.
- Keep MAUI dashboard after server template boundaries are stable.
- Do not commit `.DS_Store` files.
