# 10k-load-bottleneck-telemetry - Do Document

> Version: 1.0.0 | Date: 2026-04-29 | Status: Completed
> Design: docs/02-design/features/10k-load-bottleneck-telemetry.design.md

---

## 1. Implementation Summary

Implemented bottleneck telemetry for the 10K load investigation without adding protocol-specific behavior to `LibNetworks`.

## 2. Code Changes

| File | Change |
|------|--------|
| `LibNetworks/Telemetry/ServerTelemetry.cs` | Added send request, pending send, max pending send, send backpressure, and send buffer depth counters. |
| `LibNetworks/Telemetry/ObservedMetrics.cs` | Extended client/server observed DTOs with optional-friendly bottleneck fields and server delta rates. |
| `LibNetworks/Sessions/BaseSession.cs` | Records send request enqueue depth, send buffer samples, send completions, and send-buffer backpressure signals. |
| `FastPortLoadRunner/Metrics.cs` | Tracks connect attempts/failures, pending request count, max pending request count, active session ratio, and reporter scheduler drift. |
| `FastPortLoadRunner/LoadSession.cs` | Records connect attempts/failures and avoids counting failed connects as disconnects. |
| `FastPortLoadRunner/ObservedMetricsExtensions.cs` | Maps new internal metrics into `ClientObservedMetricsSnapshot`. |
| `FastPortLoadValidation/LoadValidationEvaluator.cs` | Aggregates bottleneck fields into stage summaries. |
| `FastPortLoadValidation/LoadValidationStage.cs` | Adds summary fields for connect attempts/failures, max pending requests, max scheduler drift, and max active ratio. |
| `FastPortLoadValidation/LoadValidationSummaryWriter.cs` | Adds max pending request, scheduler drift, and RTT P95/P99 columns to Markdown summaries. |
| `LibCommonTest/*` | Adds focused coverage for new telemetry counters, JSON field emission, summary aggregation, and smoke-path server send counters. |

## 3. Contract Notes

- Existing observed JSON envelope fields remain unchanged.
- New DTO fields have default values so older JSONL that lacks the new fields still deserializes to zero-valued metrics.
- `FastPortLoadRunner` continues to emit client-only `ObservedMetricsSnapshot` JSONL.
- Server-side counters remain protocol-neutral and are sourced from send enqueue/completion behavior.
- `SendBufferBytes` is a last-observed sample, while `MaxSendBufferBytes` is the max observed sample.

## 4. Verification

Commands run:

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
git diff --check
```

Result:

- Build passed with 0 warnings and 0 errors.
- Test suite passed: 72 passed, 0 failed, 0 skipped.
- `git diff --check` reported no whitespace errors.

## 5. Follow-up

- Run `$pdca analyze 10k-load-bottleneck-telemetry`.
- Run the focused `s5-random-10k` measurement after deciding whether to keep server logging reduced for the comparison run.
