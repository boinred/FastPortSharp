# Gap Analysis: loadrunner-observed-jsonl

> Date: 2026-04-28 | Design: docs/02-design/features/loadrunner-observed-jsonl.design.md

---

## Match Rate: 100%

10 of 10 design items are implemented.

## Summary

The implementation matches the design. `FastPortLoadRunner` keeps `MetricsSnapshot` as the internal aggregation model, leaves console reporting unchanged, and changes only the JSONL reporter path to emit the public observed metrics envelope.

`dotnet test` passed with 62 tests, 0 failures.

## Implemented Items

- [x] `JsonMetricsReporter` no longer serializes `MetricsSnapshot` directly.
- [x] `JsonMetricsReporter` converts `MetricsSnapshot` to `ClientObservedMetricsSnapshot`.
- [x] `JsonMetricsReporter` wraps client metrics with `ObservedMetricsSnapshot.FromClient(...)`.
- [x] JSONL serialization uses `ObservedMetricsJson.Serialize(...)`.
- [x] Root JSON contains `clientObserved`.
- [x] Root JSON contains `serverObserved: null` for client-only LoadRunner output.
- [x] Legacy/internal root field `connectedSessions` is not emitted.
- [x] Console reporter behavior remains unchanged.
- [x] `MetricsSnapshot` structure remains unchanged.
- [x] Unit test coverage validates representative observed field mappings and JSON shape.

## Missing Items

None.

## Changed Items (Deviations from Design)

None.

## Evidence

| Design item | Implementation evidence | Status |
|-------------|--------------------------|--------|
| Use observed envelope for JSONL | `FastPortLoadRunner/Metrics.cs` has `JsonMetricsReporter.SerializeSnapshot(...)` creating `ObservedMetricsSnapshot.FromClient(...)`. | Match |
| Reuse shared serializer | `JsonMetricsReporter.SerializeSnapshot(...)` returns `ObservedMetricsJson.Serialize(observed)`. | Match |
| Keep internal snapshot model | `MetricsSnapshot` remains unchanged and is still produced by `MetricsCollector.CreateSnapshot(...)`. | Match |
| Keep console output stable | `ConsoleMetricsReporter` code path was not modified. | Match |
| Validate JSON shape | `JsonMetricsReporter_SerializeSnapshot_WritesObservedClientEnvelope` asserts `clientObserved`, `serverObserved: null`, `currentSessions`, `connectCount`, and no root `connectedSessions`. | Match |
| Verify tests | `dotnet test` passed: 62 passed, 0 failed. | Match |

## Recommendations

1. Proceed to `$pdca report loadrunner-observed-jsonl`.
2. Treat the JSONL shape change as an intentional contract update in the report.
3. Defer any legacy JSONL compatibility switch until a real consumer requires it.

## Next Steps

- [x] Proceed to report phase because match rate is above 90%.
