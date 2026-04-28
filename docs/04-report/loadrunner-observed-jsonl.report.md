# Completion Report: loadrunner-observed-jsonl

> Date: 2026-04-28 | Status: Completed | Match Rate: 100%

---

## 1. Summary

`FastPortLoadRunner` JSONL output now uses the shared observed telemetry contract instead of exposing the internal `MetricsSnapshot` shape. This aligns load test output with the server/client metric naming contract established by `telemetry-export-metric-contract` and gives future dashboard/export tooling one envelope shape to consume.

Completion rate: 100%

## 2. Related Documents

- Plan: `docs/01-plan/features/loadrunner-observed-jsonl.plan.md`
- Design: `docs/02-design/features/loadrunner-observed-jsonl.design.md`
- Do: `docs/02-design/features/loadrunner-observed-jsonl.do.md`
- Analysis: `docs/03-analysis/loadrunner-observed-jsonl.analysis.md`

## 3. Completed Items

- `JsonMetricsReporter` now maps `MetricsSnapshot` to `ClientObservedMetricsSnapshot`.
- JSONL output is wrapped with `ObservedMetricsSnapshot.FromClient(...)`.
- JSON serialization uses `ObservedMetricsJson.Serialize(...)`.
- Client-only LoadRunner JSONL emits `serverObserved: null`.
- Console metrics output remains unchanged.
- `MetricsSnapshot` remains the internal LoadRunner aggregation model.
- Unit test coverage verifies the observed envelope shape and representative field mappings.

## 4. Quality Metrics

| Metric | Result |
|--------|--------|
| Match rate | 100% |
| Design items implemented | 10 / 10 |
| Missing items | 0 |
| Deviations | 0 |
| Test command | `dotnet test` |
| Test result | 62 passed, 0 failed |

## 5. Contract Notes

This is an intentional JSONL contract change:

- Old output exposed internal LoadRunner fields at the JSON root.
- New output uses root `timestamp`, `clientObserved`, and `serverObserved`.
- The client metric field for live connections is `clientObserved.currentSessions`.
- The client metric field for successful connects is `clientObserved.connectCount`.
- Legacy root fields such as `connectedSessions` are not emitted.

No legacy format switch was added because there is no known external consumer that requires the old internal shape.

## 6. Lessons Learned

### Keep

- Keep `MetricsSnapshot` as an internal runtime model.
- Keep observed DTO mapping centralized in `ObservedMetricsExtensions`.
- Keep JSON contract tests focused on public field names rather than the entire JSON body.

### Problem

- bkit phase status can lag when check completes with no required iteration, so the analysis document should remain the source of truth for report readiness.

### Try

- For the future dashboard/export work, consume the `ObservedMetricsSnapshot` envelope directly instead of adding a separate LoadRunner-specific schema.

## 7. Next Steps

- Archive with `$pdca archive loadrunner-observed-jsonl` after review.
- Commit the code and PDCA documents when ready.
