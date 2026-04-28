# loadrunner-observed-jsonl - Design Document

> Version: 1.0.0 | Date: 2026-04-28 | Status: Completed
> Level: Starter | Plan: docs/01-plan/features/loadrunner-observed-jsonl.plan.md

---

## 1. Overview

FastPortLoadRunner의 JSONL reporter가 내부 `MetricsSnapshot`을 직접 serialize하지 않고 public observed metric contract를 serialize하도록 변경한다.

핵심 설계는 다음과 같다.

- `MetricsCollector`와 `MetricsSnapshot`은 LoadRunner 내부 집계 모델로 유지한다.
- `ConsoleMetricsReporter`는 기존 human-readable output을 유지한다.
- `JsonMetricsReporter`만 `MetricsSnapshot -> ClientObservedMetricsSnapshot -> ObservedMetricsSnapshot` 변환 후 JSONL로 기록한다.
- JSON serialize는 `LibNetworks.Telemetry.ObservedMetricsJson`을 재사용해 server/client observed stream의 naming policy를 통일한다.

## 2. Current Structure

| Area | Current behavior |
|------|------------------|
| `FastPortLoadRunner/Metrics.cs` | `JsonMetricsReporter`가 `MetricsSnapshot`을 camelCase로 직접 serialize한다. |
| `FastPortLoadRunner/ObservedMetricsExtensions.cs` | `MetricsSnapshot`을 `ClientObservedMetricsSnapshot`으로 변환한다. |
| `LibNetworks/Telemetry/ObservedMetrics.cs` | `ObservedMetricsSnapshot`, `ClientObservedMetricsSnapshot`, `ServerObservedMetricsSnapshot`, `ObservedMetricsJson`을 제공한다. |
| `LibCommonTest/ObservedMetricsTests.cs` | observed DTO mapping과 camelCase serialization 일부를 검증한다. |

## 3. Target Architecture

```text
MetricsCollector
  -> MetricsSnapshot
     -> ConsoleMetricsReporter
        -> existing text output
     -> JsonMetricsReporter
        -> ToClientObservedMetricsSnapshot()
        -> ObservedMetricsSnapshot.FromClient(...)
        -> ObservedMetricsJson.Serialize(...)
        -> one JSON object per line
```

### 3.1 Responsibilities

| Component | Responsibility |
|-----------|----------------|
| `MetricsCollector` | counters, rates, RTT samples, snapshot creation |
| `MetricsSnapshot` | internal LoadRunner metric state |
| `ObservedMetricsExtensions` | internal LoadRunner state to public client observed DTO mapping |
| `JsonMetricsReporter` | JSONL file writer and observed envelope serialization |
| `ObservedMetricsJson` | shared serializer options for observed metric contract |

### 3.2 Dependency Direction

`FastPortLoadRunner` may depend on `LibNetworks.Telemetry` for export DTOs. `LibNetworks` must not depend on `FastPortLoadRunner`.

This keeps the generic network/telemetry contract reusable while keeping load generation details outside the engine library.

## 4. Data Model

### 4.1 Internal Snapshot

`MetricsSnapshot` remains unchanged.

Important mapping notes:

| Internal field | Observed field |
|----------------|----------------|
| `TargetSessions` | `clientObserved.targetSessions` |
| `ConnectedSessions` | `clientObserved.currentSessions` |
| `AcceptCount` | `clientObserved.connectCount` |
| `DisconnectCount` | `clientObserved.disconnectCount` |
| `SocketErrorCount` | `clientObserved.socketErrorCount` |
| `SocketErrorRate` | `clientObserved.socketErrorRate` |

### 4.2 JSONL Envelope

Each JSONL line uses the common observed envelope.

```json
{
  "timestamp": "2026-04-28T09:00:00+09:00",
  "clientObserved": {
    "timestamp": "2026-04-28T09:00:00+09:00",
    "targetSessions": 10000,
    "currentSessions": 10000,
    "totalSentPackets": 120000,
    "totalReceivedPackets": 119500,
    "totalSentBytes": 983040000,
    "totalReceivedBytes": 978944000,
    "sentPacketsPerSecond": 10000,
    "receivedPacketsPerSecond": 9950,
    "sentBytesPerSecond": 81920000,
    "receivedBytesPerSecond": 81578666.67,
    "tps": 9950,
    "rttAverageMs": 2.5,
    "rttP50Ms": 2,
    "rttP95Ms": 4,
    "rttP99Ms": 6,
    "connectCount": 10000,
    "disconnectCount": 0,
    "socketErrorCount": 0,
    "socketErrorRate": 0
  },
  "serverObserved": null
}
```

`timestamp` appears both at the envelope and client snapshot level because `ObservedMetricsSnapshot` is designed to support client-only, server-only, and combined streams.

## 5. API and CLI Behavior

### 5.1 CLI Contract

No new CLI option is required in this feature.

Existing behavior:

```text
--output metrics.jsonl
```

Target behavior:

- If `--output` is provided, the file contains observed envelope JSONL.
- If `--output` is omitted, console-only reporting remains unchanged.
- Existing output file creation behavior remains unchanged: create/overwrite target file and create parent directory when needed.

### 5.2 Compatibility Decision

This feature intentionally changes the JSONL file contract from internal `MetricsSnapshot` shape to observed envelope shape. No legacy format switch is added because there is no known external consumer that requires the old internal shape.

If a compatibility requirement appears later, add a separate `--output-format legacy|observed` feature rather than mixing both shapes in one stream.

## 6. Implementation Plan

1. Add `using LibNetworks.Telemetry;` to `FastPortLoadRunner/Metrics.cs`.
2. Remove the private `JsonSerializerOptions` from `JsonMetricsReporter` if it becomes unused.
3. In `JsonMetricsReporter.RunAsync`, replace direct `JsonSerializer.Serialize(snapshot, ...)` with:

```csharp
ClientObservedMetricsSnapshot clientObserved = snapshot.ToClientObservedMetricsSnapshot();
ObservedMetricsSnapshot observed = ObservedMetricsSnapshot.FromClient(clientObserved);
string json = ObservedMetricsJson.Serialize(observed);
```

4. Keep writer lifecycle, delay loop, previous snapshot handling, flushing, and cancellation behavior unchanged.
5. Add or update tests for JSONL output shape.

## 7. Test Plan

### 7.1 Unit Tests

Add a focused test in `LibCommonTest/FastPortLoadRunnerTests.cs` or `LibCommonTest/ObservedMetricsTests.cs`.

Recommended assertions:

- serialized JSON contains root `clientObserved`.
- serialized JSON contains root `serverObserved` with null value for client-only stream.
- `clientObserved.currentSessions` equals the source `MetricsSnapshot.ConnectedSessions`.
- `clientObserved.connectCount` equals the source `MetricsSnapshot.AcceptCount`.
- root-level legacy/internal fields such as `connectedSessions` are not present.

### 7.2 Existing Test Coverage

Existing tests that should remain green:

- `ClientObservedMetricsSnapshot_MapsLoadRunnerMetrics`
- `ObservedMetricsJson_SerializesCamelCase`
- `MetricsCollector_CreateSnapshot_TracksTotalsAndRates`
- option parsing tests that cover `--output`

### 7.3 Verification Command

```bash
dotnet test
```

## 8. Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| JSONL consumers may expect the old internal shape | Medium | Treat the change as an intentional contract update and document it in report. Add compatibility option only if a real consumer needs it. |
| `System.Text.Json` null behavior could omit `serverObserved` if options change later | Low | Test explicit presence and null value of `serverObserved`. |
| Mapping logic drifts from metric naming contract | Medium | Keep mapping centralized in `ObservedMetricsExtensions` and cover representative field mappings in tests. |

## 9. Acceptance Criteria

- [ ] `JsonMetricsReporter` writes `ObservedMetricsSnapshot` envelope JSONL.
- [ ] JSONL uses `ObservedMetricsJson.Serialize`.
- [ ] `MetricsSnapshot` and `ConsoleMetricsReporter` remain behaviorally unchanged.
- [ ] Test coverage validates the new JSONL shape.
- [ ] `dotnet test` passes.
