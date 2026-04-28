# telemetry-export-metric-contract - Do Tracking

> Version: 1.0.0 | Date: 2026-04-28 | Status: Implemented
> Design: docs/02-design/features/telemetry-export-metric-contract.design.md

---

## 1. Implementation Summary

`telemetry-export-metric-contract` 설계에 따라 기존 raw telemetry snapshot을 유지하면서, dashboard/export가 소비할 수 있는 명확한 observed metric DTO와 mapper를 추가했다.

핵심 방향은 backward-compatible adapter다. `ServerTelemetrySnapshot`과 `FastPortLoadRunner.MetricsSnapshot`은 유지하고, export 계층에서 `ServerObservedMetricsSnapshot`, `ClientObservedMetricsSnapshot`, `ObservedMetricsSnapshot`으로 의미를 명확히 변환한다.

## 2. Completed Items

- [x] `ObservedMetricsSnapshot` 추가
- [x] `ClientObservedMetricsSnapshot` 추가
- [x] `ServerObservedMetricsSnapshot` 추가
- [x] `ServerTelemetrySnapshot` -> `ServerObservedMetricsSnapshot` mapper 추가
- [x] `ServerTelemetryExporter` 추가
- [x] `IServerTelemetryExporter` 추가
- [x] camelCase JSON serializer helper `ObservedMetricsJson` 추가
- [x] `FastPortSmokeServer` DI에 `IServerTelemetryExporter` 등록
- [x] `FastPortLoadRunner.MetricsSnapshot` -> `ClientObservedMetricsSnapshot` adapter 추가
- [x] server metric semantic tests 추가
- [x] client metric adapter test 추가
- [x] JSON camelCase serialization test 추가
- [x] smoke test에서 `ServerObservedMetricsSnapshot` export 결과 검증

## 3. Implementation Notes

### 3.1 Server Metric Semantics

기존 ambiguous field는 export DTO에서 명확한 이름으로 매핑한다.

| Raw Field | Export Field | Meaning |
|-----------|--------------|---------|
| `AcceptedSessions` | `TotalAcceptedSessions` | server accepted TCP clients |
| `DisconnectedSessions` | `TotalDisconnectedSessions` | first disconnect transitions |
| `ConnectedSessions` | `CurrentSessions` | derived current server sessions |
| `ReceivedPackets` | `TotalReceivedPackets` | parsed FastPort packet count |
| `SentPackets` | `TotalSendCompletions` | socket send completion callback count |
| `ReceivedBytes` | `TotalParsedPacketBytes` | parsed FastPort packet sizes |
| `SentBytes` | `TotalSentBytes` | socket send completion bytes |

`ServerTelemetrySnapshot`은 raw collector snapshot으로 유지한다.

### 3.2 Per-Second Fields

`ServerObservedMetricsSnapshot.FromTelemetry(current, previous)`는 previous snapshot이 있을 때 delta/sec를 계산한다.

- `ReceivedPacketsPerSecond`
- `SendCompletionsPerSecond`
- `ParsedPacketBytesPerSecond`
- `SentBytesPerSecond`
- `AcceptedSessionsPerSecond`
- `DisconnectedSessionsPerSecond`

첫 snapshot 또는 previous가 없는 경우 per-second 값은 `0`이다.

### 3.3 JSON Export

`ObservedMetricsJson`은 `System.Text.Json` camelCase naming policy를 사용한다.

검증된 JSON field 예:

- `serverObserved`
- `totalSendCompletions`
- `totalParsedPacketBytes`

## 4. Verification

- [x] `dotnet build FastPortCharp.sln`
  - Result: success
  - Warnings: 0
  - Errors: 0

- [x] `dotnet test FastPortCharp.sln --no-build`
  - Result: success
  - Passed: 61
  - Failed: 0

## 5. New Tests

- `ServerObservedMetricsSnapshot_MapsCurrentTelemetrySemantics`
- `ServerObservedMetricsSnapshot_PerSecondFields_UsePreviousSnapshotDelta`
- `ServerObservedMetricsSnapshot_FirstSnapshot_PerSecondFieldsAreZero`
- `ObservedMetricsJson_SerializesCamelCase`
- `ClientObservedMetricsSnapshot_MapsLoadRunnerMetrics`

## 6. Current Limits

- HTTP endpoint 또는 streaming endpoint는 아직 구현하지 않았다.
- raw socket receive bytes는 아직 별도 counter로 추가하지 않았다.
- `FastPortLoadRunner` JSONL output은 아직 observed DTO로 교체하지 않았다.
- MAUI dashboard는 다음 단계에서 이 export contract를 소비해야 한다.

## 7. Next Steps

1. `$pdca analyze telemetry-export-metric-contract`로 design 대비 구현 gap을 확인한다.
2. 필요하면 LoadRunner JSONL output을 `ClientObservedMetricsSnapshot` 기반으로 정렬한다.
3. 이후 MAUI dashboard 또는 staged load validation을 별도 PDCA로 진행한다.
