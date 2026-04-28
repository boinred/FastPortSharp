# Gap Analysis: telemetry-export-metric-contract

> Date: 2026-04-28 | Design: docs/02-design/features/telemetry-export-metric-contract.design.md

---

## Match Rate: 92%

The implementation matches the core design: ambiguous raw telemetry fields are preserved but adapted into explicit observed metric DTOs, server/client ownership is clear, JSON export uses camelCase, per-second server deltas are implemented, and tests verify the important semantic mappings.

The remaining gaps are intentionally small and mostly integration-facing. The current implementation does not yet switch `FastPortLoadRunner` JSONL output to the new client-observed DTO shape, and no HTTP/stream endpoint is implemented. The endpoint was explicitly future scope in the design, so it is not a blocker for this phase.

Match rate calculation:

```text
Implemented design items: 24
Total design items:       26
Match rate:               24 / 26 = 92%
```

## Summary

`telemetry-export-metric-contract` is ready to move to report for this scope. It establishes a concrete contract for future dashboard and staged load validation work without breaking existing `ServerTelemetrySnapshot` or `FastPortLoadRunner.MetricsSnapshot` consumers.

The strongest implementation point is the adapter strategy: the existing raw snapshots remain stable, while new export-facing DTOs make the semantics explicit.

## Implemented Items

- [x] `ObservedMetricsSnapshot` added.
- [x] `ClientObservedMetricsSnapshot` added.
- [x] `ServerObservedMetricsSnapshot` added.
- [x] `ServerTelemetrySnapshot` remains as the low-level raw collector snapshot.
- [x] `MetricsSnapshot` remains stable in `FastPortLoadRunner`.
- [x] `ServerTelemetrySnapshot.SentPackets` maps to `ServerObservedMetricsSnapshot.TotalSendCompletions`.
- [x] `ServerTelemetrySnapshot.ReceivedBytes` maps to `ServerObservedMetricsSnapshot.TotalParsedPacketBytes`.
- [x] Server current session mapping is explicit as `CurrentSessions`.
- [x] Server accepted/disconnected counters map to total session fields.
- [x] Server accept/socket/parse/protocol errors map to explicit error count fields.
- [x] `ServerObservedMetricsSnapshot.FromTelemetry` supports first snapshot semantics.
- [x] `ServerObservedMetricsSnapshot.FromTelemetry(current, previous)` computes per-second deltas.
- [x] `ObservedMetricsJson` serializes snapshots with camelCase naming.
- [x] `ServerTelemetryExporter` added.
- [x] `IServerTelemetryExporter` added.
- [x] `ObservedMetricsSnapshot.FromServer`, `FromClient`, and `Combined` added.
- [x] `FastPortLoadRunner.MetricsSnapshot` can map to `ClientObservedMetricsSnapshot`.
- [x] `FastPortSmokeServer` DI registers `IServerTelemetryExporter`.
- [x] `FastPortSmokeServerTests` verify exported `ServerObservedMetricsSnapshot` in real smoke runs.
- [x] Unit tests verify server semantic mapping.
- [x] Unit tests verify per-second delta fields.
- [x] Unit tests verify first snapshot per-second fields are zero.
- [x] Unit tests verify camelCase JSON output.
- [x] Unit tests verify client metric adapter mapping.

## Missing Items

- [ ] `FastPortLoadRunner` JSONL output still serializes the existing `MetricsSnapshot`, not `ClientObservedMetricsSnapshot`.
- [ ] No HTTP or streaming telemetry endpoint is implemented. This was noted as future work in the design and is not required for this phase.

## Changed Items (Deviations from Design)

- [ ] The implementation uses `CreateSnapshot(ServerObservedMetricsSnapshot? previous)` instead of the early interface sketch `CreateSnapshot(ServerTelemetrySnapshot? previous)`. This matches the preferred implementation described later in the design and avoids exposing raw snapshot delta handling to callers.
- [ ] `ClientObservedMetricsSnapshot` includes `Timestamp`; the design snippet omitted it, but the combined export model needs timestamps for reliable JSON/dashboard consumption.

## Verification Results

- [x] `dotnet build FastPortCharp.sln`
  - Result: success
  - Warnings: 0
  - Errors: 0

- [x] `dotnet test FastPortCharp.sln --no-build`
  - Result: success
  - Passed: 61
  - Failed: 0

## Item-by-Item Design Comparison

| Design Item | Status | Evidence |
|-------------|--------|----------|
| Server observed DTO | Match | `ServerObservedMetricsSnapshot` |
| Client observed DTO | Match | `ClientObservedMetricsSnapshot` |
| Combined observed snapshot | Match | `ObservedMetricsSnapshot` |
| Preserve raw server snapshot | Match | `ServerTelemetrySnapshot` unchanged |
| Preserve LoadRunner snapshot | Match | `MetricsSnapshot` unchanged |
| Explicit send completion naming | Match | `TotalSendCompletions` |
| Explicit parsed packet byte naming | Match | `TotalParsedPacketBytes` |
| Server telemetry exporter | Match | `ServerTelemetryExporter`, `IServerTelemetryExporter` |
| Server per-second deltas | Match | `FromTelemetry(current, previous)` |
| First snapshot per-second fields zero | Match | `ServerObservedMetricsSnapshot_FirstSnapshot_PerSecondFieldsAreZero` |
| camelCase JSON serializer | Match | `ObservedMetricsJson` |
| Client metric adapter | Match | `ToClientObservedMetricsSnapshot()` |
| Smoke server DI wiring | Match | `FastPortSmokeServer/Program.cs` |
| Smoke test export verification | Match | `AssertServerObservedMetrics()` |
| Unit tests for semantic mapping | Match | `ObservedMetricsTests` |
| Build/test verification | Match | build and 61 tests passed |
| LoadRunner JSONL aligned to observed DTO | Missing | `JsonMetricsReporter` still serializes `MetricsSnapshot` |
| HTTP/stream endpoint | Future scope | design states payload shape only for this phase |

## Recommendations

1. Proceed to `$pdca report telemetry-export-metric-contract`.
2. Keep `ServerTelemetrySnapshot` as the raw collector contract and use observed DTOs for dashboard/export work.
3. Treat LoadRunner JSONL output alignment as a small follow-up if dashboard tooling needs a combined observed stream.
4. Keep HTTP/stream endpoint implementation for the MAUI dashboard phase.

## Next Steps

- [ ] Run `$pdca report telemetry-export-metric-contract`.
- [ ] Commit the feature after report/archive or before starting the next implementation scope.
