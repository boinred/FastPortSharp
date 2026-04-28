# telemetry-export-metric-contract - Design Document

> Version: 1.0.0 | Date: 2026-04-28 | Status: Completed
> Level: Starter | Plan: docs/01-plan/features/telemetry-export-metric-contract.plan.md

---

## 1. Overview

`telemetry-export-metric-contract` defines a stable metric contract for FastPortSharp before MAUI dashboard and staged load validation work begins.

The feature has two responsibilities:

1. Rename or adapt ambiguous client/server metric names into explicit `clientObserved*` and `serverObserved*` fields.
2. Add a protocol-neutral telemetry export surface that can be consumed by tests, LoadRunner tooling, and a future dashboard.

This design intentionally avoids building the MAUI UI. The output of this phase is a code-level contract and JSON export shape.

## 2. Design Goals

- Make every metric name reveal:
  - observer: client or server
  - lifetime: current, total, or per-second
  - unit: packets, bytes, sessions, completions, errors, milliseconds
- Preserve protocol-neutral telemetry in `LibNetworks`.
- Avoid moving smoke protocol logic into `FastPortServer`.
- Keep JSON output camelCase.
- Provide a small export model that can evolve into dashboard streaming.
- Keep compatibility risk low by adding a canonical export adapter first, then optionally renaming internal fields later.

## 3. Current Model Summary

### 3.1 Client Metrics

Current type:

```csharp
FastPortLoadRunner.MetricsSnapshot
```

Current fields:

| Current Field | Current Meaning | Contract Direction |
|---------------|-----------------|--------------------|
| `TargetSessions` | configured target session count | `clientObservedTargetSessions` |
| `ConnectedSessions` | LoadRunner active sessions | `clientObservedCurrentSessions` |
| `TotalSentPackets` | LoadRunner sent FastPort packets | `clientObservedTotalSentPackets` |
| `TotalReceivedPackets` | LoadRunner received response packets | `clientObservedTotalReceivedPackets` |
| `TotalSentBytes` | bytes written by LoadRunner | `clientObservedTotalSentBytes` |
| `TotalReceivedBytes` | bytes received by LoadRunner | `clientObservedTotalReceivedBytes` |
| `SentPacketsPerSecond` | client send packet delta/sec | `clientObservedSentPacketsPerSecond` |
| `ReceivedPacketsPerSecond` | client receive packet delta/sec | `clientObservedReceivedPacketsPerSecond` |
| `SentBytesPerSecond` | client sent byte delta/sec | `clientObservedSentBytesPerSecond` |
| `ReceivedBytesPerSecond` | client received byte delta/sec | `clientObservedReceivedBytesPerSecond` |
| `Tps` | successful received responses/sec | `clientObservedTps` |
| `Rtt*Ms` | client-observed RTT stats | `clientObservedRtt*Ms` |
| `AcceptCount` | LoadRunner session connect count | `clientObservedConnectCount` |
| `DisconnectCount` | LoadRunner session disconnect count | `clientObservedDisconnectCount` |
| `SocketErrorCount` | client socket errors | `clientObservedSocketErrorCount` |
| `SocketErrorRate` | client socket error ratio | `clientObservedSocketErrorRate` |

### 3.2 Server Telemetry

Current type:

```csharp
LibNetworks.Telemetry.ServerTelemetrySnapshot
```

Current fields:

| Current Field | Current Meaning | Contract Direction |
|---------------|-----------------|--------------------|
| `AcceptedSessions` | accepted TCP clients | `serverObservedTotalAcceptedSessions` |
| `DisconnectedSessions` | first disconnect transitions | `serverObservedTotalDisconnectedSessions` |
| `ConnectedSessions` | derived current sessions | `serverObservedCurrentSessions` |
| `ReceivedPackets` | parsed FastPort packet count | `serverObservedTotalReceivedPackets` |
| `SentPackets` | socket send completion count | `serverObservedTotalSendCompletions` |
| `ReceivedBytes` | parsed packet size total | `serverObservedTotalParsedPacketBytes` |
| `SentBytes` | socket send completion bytes | `serverObservedTotalSentBytes` |
| `AcceptErrors` | accept/bind/null socket errors | `serverObservedAcceptErrorCount` |
| `SocketErrors` | server socket errors | `serverObservedSocketErrorCount` |
| `ParseErrors` | payload parse failures | `serverObservedParseErrorCount` |
| `ProtocolErrors` | unexpected protocol id/mismatch | `serverObservedProtocolErrorCount` |
| `SocketErrorRate` | server socket error ratio | `serverObservedSocketErrorRate` |

## 4. Canonical Export Model

### 4.1 Combined Snapshot

Add a dashboard/export DTO that can hold client-only, server-only, or combined snapshots:

```csharp
public sealed record ObservedMetricsSnapshot(
    DateTimeOffset Timestamp,
    ClientObservedMetricsSnapshot? ClientObserved,
    ServerObservedMetricsSnapshot? ServerObserved);
```

This shape keeps source ownership explicit and avoids a huge flat type in code. JSON remains easy to consume:

```json
{
  "timestamp": "2026-04-28T00:00:00+09:00",
  "clientObserved": {
    "targetSessions": 10000,
    "currentSessions": 9972,
    "totalSentPackets": 123456,
    "totalReceivedPackets": 123000,
    "sentPacketsPerSecond": 8000,
    "receivedPacketsPerSecond": 7950,
    "sentBytesPerSecond": 65536000,
    "receivedBytesPerSecond": 65100000,
    "tps": 7950,
    "rttAverageMs": 3.2,
    "rttP95Ms": 8.4,
    "socketErrorRate": 0.0001
  },
  "serverObserved": {
    "currentSessions": 9972,
    "totalAcceptedSessions": 10000,
    "totalDisconnectedSessions": 28,
    "totalReceivedPackets": 123456,
    "totalSendCompletions": 123000,
    "totalParsedPacketBytes": 65536000,
    "totalSentBytes": 65100000,
    "acceptedSessionsPerSecond": 120,
    "disconnectedSessionsPerSecond": 3,
    "socketErrorRate": 0
  }
}
```

### 4.2 Client Snapshot DTO

```csharp
public sealed record ClientObservedMetricsSnapshot(
    int TargetSessions,
    int CurrentSessions,
    long TotalSentPackets,
    long TotalReceivedPackets,
    long TotalSentBytes,
    long TotalReceivedBytes,
    double SentPacketsPerSecond,
    double ReceivedPacketsPerSecond,
    double SentBytesPerSecond,
    double ReceivedBytesPerSecond,
    double Tps,
    double RttAverageMs,
    double RttP50Ms,
    double RttP95Ms,
    double RttP99Ms,
    long ConnectCount,
    long DisconnectCount,
    long SocketErrorCount,
    double SocketErrorRate);
```

Mapping note: this can be an adapter over current `FastPortLoadRunner.MetricsSnapshot`; the internal record does not need to be renamed in the first implementation.

### 4.3 Server Snapshot DTO

```csharp
public sealed record ServerObservedMetricsSnapshot(
    long CurrentSessions,
    long TotalAcceptedSessions,
    long TotalDisconnectedSessions,
    long TotalReceivedPackets,
    long TotalSendCompletions,
    long TotalParsedPacketBytes,
    long TotalSentBytes,
    double ReceivedPacketsPerSecond,
    double SendCompletionsPerSecond,
    double ParsedPacketBytesPerSecond,
    double SentBytesPerSecond,
    double AcceptedSessionsPerSecond,
    double DisconnectedSessionsPerSecond,
    long AcceptErrorCount,
    long SocketErrorCount,
    long ParseErrorCount,
    long ProtocolErrorCount,
    double SocketErrorRate);
```

Mapping note:

- Current `ServerTelemetrySnapshot.SentPackets` maps to `TotalSendCompletions`.
- Current `ServerTelemetrySnapshot.ReceivedBytes` maps to `TotalParsedPacketBytes`.
- This avoids lying about exact packet counts while preserving current instrumentation.

## 5. Export Architecture

### 5.1 Project Boundary

| Project | Responsibility |
|---------|----------------|
| `LibNetworks` | Server-side observed metrics contract, snapshot delta calculation, JSON serialization helpers |
| `FastPortLoadRunner` | Client-side observed metrics adapter and JSONL output alignment |
| `FastPortSmokeServer` | Wire server telemetry export for smoke/dashboard tests |
| `LibCommonTest` | Unit tests for metric naming, mapping, and per-second semantics |

### 5.2 Server Export Surface

Add a small service in `LibNetworks.Telemetry`:

```csharp
public interface IServerTelemetryExporter
{
    ServerObservedMetricsSnapshot CreateSnapshot();
    ServerObservedMetricsSnapshot CreateSnapshot(ServerTelemetrySnapshot? previous);
    string SerializeSnapshot(ServerObservedMetricsSnapshot snapshot);
}
```

Alternative implementation if the previous raw `ServerTelemetrySnapshot` is not enough:

```csharp
public sealed class ServerTelemetryExporter(IServerTelemetry telemetry)
{
    public ServerObservedMetricsSnapshot CreateSnapshot(
        ServerObservedMetricsSnapshot? previous = null);
}
```

Preferred implementation: store the previous export snapshot at caller/reporter level, matching `MetricsCollector.CreateSnapshot(previous)`.

### 5.3 JSON Export

Use `System.Text.Json` with camelCase policy:

```csharp
new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
```

For this phase, a simple snapshot serializer and optional JSONL writer are enough. A network HTTP endpoint can be added later without changing the DTOs.

### 5.4 Future HTTP/Stream Endpoint

Not required in the first implementation, but the contract should support:

```text
GET /telemetry/snapshot
```

Response body:

```json
{
  "timestamp": "...",
  "serverObserved": { ... }
}
```

If a dashboard needs real-time streaming later:

```text
GET /telemetry/stream
```

Transport can be Server-Sent Events, WebSocket, or periodic polling. This design only fixes the payload shape.

## 6. Metric Semantics

### 6.1 Packet and Byte Rules

| Name | Must Mean |
|------|-----------|
| `*Packets` | FastPort packet count |
| `*SendCompletions` | socket send completion callback count |
| `*SentBytes` | bytes completed by socket send path unless explicitly named otherwise |
| `*ReceivedBytes` | observer-specific raw receive bytes only if measured at socket receive path |
| `*ParsedPacketBytes` | parsed FastPort packet sizes |
| `*PerSecond` | current snapshot total minus previous snapshot total divided by elapsed seconds |

### 6.2 Server Derived Values

Server exporter computes:

```text
currentSessions = max(0, totalAcceptedSessions - totalDisconnectedSessions)
acceptedSessionsPerSecond = delta(totalAcceptedSessions) / elapsedSeconds
disconnectedSessionsPerSecond = delta(totalDisconnectedSessions) / elapsedSeconds
receivedPacketsPerSecond = delta(totalReceivedPackets) / elapsedSeconds
sendCompletionsPerSecond = delta(totalSendCompletions) / elapsedSeconds
parsedPacketBytesPerSecond = delta(totalParsedPacketBytes) / elapsedSeconds
sentBytesPerSecond = delta(totalSentBytes) / elapsedSeconds
```

If `previous` is null, all per-second values are `0`.

## 7. Implementation Plan

1. Add `ServerObservedMetricsSnapshot` in `LibNetworks.Telemetry`.
2. Add a mapper/exporter from `ServerTelemetrySnapshot` to `ServerObservedMetricsSnapshot`.
3. Add previous-snapshot delta support for server per-second fields.
4. Add JSON serializer helper with camelCase options.
5. Add server telemetry unit tests for renamed semantics:
   - `SentPackets` maps to `TotalSendCompletions`
   - `ReceivedBytes` maps to `TotalParsedPacketBytes`
   - per-second deltas are calculated correctly
6. Add client adapter only if needed by tests or JSON contract verification.
7. Keep `FastPortLoadRunner.MetricsSnapshot` internals stable unless a design gap requires a rename.
8. Run build/test.

## 8. Test Plan

### 8.1 Unit Tests

- `ServerObservedMetricsSnapshot_MapsCurrentTelemetrySemantics`
- `ServerObservedMetricsSnapshot_PerSecondFields_UsePreviousSnapshotDelta`
- `ServerObservedMetricsSnapshot_FirstSnapshot_PerSecondFieldsAreZero`
- `ServerObservedMetricsSnapshot_JsonSerialization_UsesCamelCase`
- `ClientObservedMetricsSnapshot_MapsLoadRunnerMetrics`

### 8.2 Smoke Tests

Update existing `FastPortSmokeServerTests` only if the exporter is wired into the test host. Minimum assertion:

- server observed current sessions returns to 0
- server observed total received packets > 0
- server observed total send completions > 0
- server observed total parsed packet bytes > 0
- JSON serialization contains `serverObserved` or server snapshot camelCase fields

### 8.3 Verification Commands

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
```

## 9. Compatibility Strategy

Do not break the existing `ServerTelemetrySnapshot` immediately. Instead:

- keep existing raw snapshot fields
- add explicit export DTO fields
- update tests and future dashboard code to consume the export DTO
- consider internal rename only after consumers have moved to explicit fields

This keeps current smoke tests stable while giving the dashboard an unambiguous contract.

## 10. Open Questions

- Should `ServerTelemetrySnapshot` itself be renamed later, or should it remain the low-level raw collector snapshot?
- Should raw socket receive bytes be added now, or deferred until the dashboard needs it?
- Should server telemetry JSONL output live in `LibNetworks` or in `FastPortSmokeServer` as host-level wiring?

Recommended answers for first implementation:

- Keep `ServerTelemetrySnapshot` as the low-level raw collector snapshot.
- Defer raw socket receive bytes unless there is an immediate consumer.
- Put DTOs and serializer in `LibNetworks`, host-level writer wiring in `FastPortSmokeServer` or tests.
