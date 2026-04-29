# FastPortSharp Handoff

> Last updated: 2026-04-29
> Branch: `main`
> Remote baseline before this handoff update: `e524ab4 Checkpoint 10K telemetry and next PDCA plan`

## Current State

- Latest pushed commit before current local work: `e524ab4 Checkpoint 10K telemetry and next PDCA plan`
- Completed and archived PDCA feature: `10k-load-bottleneck-telemetry`
- Completed PDCA feature: `server-telemetry-export-merge-socket-error-classification`
- Match rate: 96%
- Current recommended action: run focused 10K with server telemetry export enabled
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

### Server Telemetry Export/Merge + Socket Error Classification

- Added `FastPortSmokeServer` server observed JSONL export:
  - `--Telemetry:Output`
  - `--Telemetry:IntervalSeconds`
  - `ServerTelemetryExportBackgroundService`
- Added client socket error classification in `FastPortLoadRunner`:
  - phase counters
  - exception type counters
  - socket error code counters
  - combined class key counters
- Extended `ClientObservedMetricsSnapshot` with optional socket classification dictionaries.
- Extended `FastPortLoadValidation`:
  - `--server-metrics`
  - `--merge-tolerance-ms`
  - server-only observed JSONL reader
  - client/server nearest timestamp merge
  - `{stageId}.combined.metrics.jsonl`
  - server backlog fields in summary JSON/Markdown
- Added tests for export, reader, merger, evaluator, summary, and socket classification counters.
- Completed PDCA docs:
  - `docs/01-plan/features/server-telemetry-export-merge-socket-error-classification.plan.md`
  - `docs/02-design/features/server-telemetry-export-merge-socket-error-classification.design.md`
  - `docs/03-analysis/server-telemetry-export-merge-socket-error-classification.analysis.md`
  - `docs/04-report/server-telemetry-export-merge-socket-error-classification.report.md`

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

## Next Work

Recommended focused run:

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer \
  --Logging:LogLevel:Default Warning \
  --Logging:LogLevel:Microsoft Warning \
  --Telemetry:Output artifacts/load-validation/s5-server-merged/server.metrics.jsonl \
  --Telemetry:IntervalSeconds 1
```

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-server-merged \
  --server-metrics artifacts/load-validation/s5-server-merged/server.metrics.jsonl
```

Optional PDCA cleanup after review:

```text
$pdca archive server-telemetry-export-merge-socket-error-classification
```

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
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation --profile smoke --output artifacts/load-validation/server-merge-smoke --server-metrics artifacts/load-validation/server-merge-smoke/server.metrics.jsonl
```

Results:

- Debug build passed with 0 warnings and 0 errors.
- Test suite passed: 78 passed, 0 failed.
- Release build passed with 0 warnings and 0 errors.
- Focused 10K validation produced artifacts but failed thresholds, as expected for the bottleneck investigation.
- Reduced smoke export/merge passed.
- Reduced smoke artifacts:
  - `artifacts/load-validation/server-merge-smoke/server.metrics.jsonl` (45 lines)
  - `artifacts/load-validation/server-merge-smoke/smoke-fixed-10.combined.metrics.jsonl` (11 lines)
  - `artifacts/load-validation/server-merge-smoke/smoke-random-25.combined.metrics.jsonl` (19 lines)

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
- Do not move echo/smoke protocol behavior back into `FastPortServer`.
- Server telemetry export is intentionally application-layer code in `FastPortSmokeServer`.
- Socket classification is intentionally diagnosis-oriented, not a production monitoring framework.
- Next useful data point is a focused `s5-random-10k` run with `--server-metrics`.
