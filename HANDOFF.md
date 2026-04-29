# FastPortSharp Handoff

> Last updated: 2026-04-29
> Branch: `main`
> Remote baseline before this handoff update: `7e61767 Archive staged load validation docs`

## Current State

- Latest pushed commit before current local work: `7e61767 Archive staged load validation docs`
- Completed and archived PDCA feature: `10k-load-bottleneck-telemetry`
- Active PDCA feature: `server-telemetry-export-merge-socket-error-classification`
- Active PDCA phase: `design`
- Project level detected by bkit: `Starter`

## Completed Since Previous Handoff

### 10K Bottleneck Telemetry

- Added server send-path telemetry counters:
  - send requests
  - pending send requests
  - max pending send requests
  - send backpressure events
  - send buffer byte sample
  - max send buffer bytes
- Added client bottleneck telemetry:
  - connect attempts
  - connect failures
  - pending request count
  - max pending request count
  - active session ratio
  - scheduler drift average/max
- Extended observed DTOs and JSONL output while keeping the existing `ObservedMetricsSnapshot` envelope.
- Extended load validation summaries with bottleneck fields.
- Added focused tests for server telemetry, observed metrics, load runner metrics, load validation summaries, and smoke server telemetry.
- Ran focused `s5-random-10k` validation with logging reduced.
- Archived PDCA docs under:
  - `docs/archive/2026-04/10k-load-bottleneck-telemetry/`

## 10K Focused Run Result

Focused run artifact path:

- `artifacts/load-validation/s5-logging-off/summary.md`
- `artifacts/load-validation/s5-logging-off/summary.json`
- `artifacts/load-validation/s5-logging-off/s5-random-10k.metrics.jsonl`

Important result:

| Metric | Value |
|--------|------:|
| Target sessions | 10,000 |
| Peak current sessions | 8,624 |
| Peak session ratio | 86.24% |
| Final disconnect count | 1,782 |
| Max socket error rate | 0.19% |
| Max pending request count | 55,695 |
| Max scheduler drift | 28.21 ms |
| Max RTT P95 | 43,268.80 ms |
| Max RTT P99 | 44,895.97 ms |
| Connect attempts | 10,000 |
| Connect failures | 0 |

Interpretation:

- Connect establishment was not the primary failure point in this focused run.
- The problem appears after successful connections: disconnect/socket-error pressure plus request backlog accumulation.
- Client-only JSONL is insufficient for the next diagnosis because `serverObserved` is still `null` in LoadRunner output.

## Active Next Work

Current active feature:

```text
server-telemetry-export-merge-socket-error-classification
```

Plan document:

```text
docs/01-plan/features/server-telemetry-export-merge-socket-error-classification.plan.md
```

Recommended next command:

```text
$pdca design server-telemetry-export-merge-socket-error-classification
```

Primary scope:

- Export `FastPortSmokeServer` server observed telemetry to JSONL.
- Merge client and server metrics in `FastPortLoadValidation`.
- Include server backlog fields in validation summaries.
- Classify client socket errors by phase/type/code.
- Preserve client-only metrics compatibility.

## Important Architecture Decisions

- `LibNetworks` should stay protocol-neutral.
- Smoke/load-test behavior belongs in `FastPortSmokeServer`, `FastPortLoadRunner`, and `FastPortLoadValidation`.
- `FastPortServer` should remain a basic network engine host/sample.
- The observed metrics envelope should remain:
  - root `timestamp`
  - `clientObserved`
  - `serverObserved`
- High-load generated artifacts remain under `artifacts/load-validation/` and should not be committed.

## Verification Recently Run

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
dotnet build FastPortCharp.sln -c Release
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation --profile staged --stage s5-random-10k --output artifacts/load-validation/s5-logging-off
```

Results:

- Debug build passed with 0 warnings and 0 errors.
- Test suite passed: 72 passed, 0 failed.
- Release build passed with 0 warnings and 0 errors.
- Focused 10K validation produced artifacts but failed thresholds, as expected for the bottleneck investigation.

## Suggested Commands

Check repository state:

```bash
git status --short --branch
```

Run tests:

```bash
dotnet test FastPortCharp.sln --no-build
```

Build:

```bash
dotnet build FastPortCharp.sln
```

Inspect active PDCA:

```text
$pdca status
```

## Notes For Next Session

- Start by checking `docs/.pdca-status.json`.
- Continue with the design phase for `server-telemetry-export-merge-socket-error-classification`.
- Do not move echo/smoke protocol behavior back into `FastPortServer`.
- Add server telemetry export at the application layer, not as protocol-specific logic inside `LibNetworks`.
- Keep socket error classification useful for 10K diagnosis but avoid making it a production monitoring framework in this feature.
