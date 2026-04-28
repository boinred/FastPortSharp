# loadrunner-observed-jsonl - Do Document

> Version: 1.0.0 | Date: 2026-04-28 | Status: Completed
> Design: docs/02-design/features/loadrunner-observed-jsonl.design.md

---

## 1. Implementation Summary

FastPortLoadRunner JSONL reporter now writes the public observed metrics envelope instead of the internal `MetricsSnapshot` shape.

## 2. Code Changes

| File | Change |
|------|--------|
| `FastPortLoadRunner/Metrics.cs` | `JsonMetricsReporter` maps `MetricsSnapshot` to `ClientObservedMetricsSnapshot`, wraps it with `ObservedMetricsSnapshot.FromClient`, and serializes through `ObservedMetricsJson`. |
| `LibCommonTest/FastPortLoadRunnerTests.cs` | Added serialization coverage for the observed client envelope and representative field names. |

## 3. Contract Notes

- `--output` continues to create JSONL output at the requested path.
- Each JSONL line now has root `timestamp`, `clientObserved`, and `serverObserved` fields.
- `serverObserved` is explicitly `null` for client-only LoadRunner streams.
- Legacy/internal root fields such as `connectedSessions` are not emitted at the JSON root.
- Console reporting remains unchanged.

## 4. Verification

Run:

```bash
dotnet test
```

Expected result: all tests pass.

Result: passed on 2026-04-28. `dotnet test` reported 62 passed, 0 failed.
