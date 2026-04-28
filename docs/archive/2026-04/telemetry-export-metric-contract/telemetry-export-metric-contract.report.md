# telemetry-export-metric-contract - Completion Report

> Version: 1.0.0 | Date: 2026-04-28 | Status: Completed
> Match Rate: 92%

---

## 1. Summary

`telemetry-export-metric-contract`는 server/client observed metric naming을 정리하고, dashboard/export가 소비할 수 있는 명확한 telemetry contract를 추가한 단계다.

이번 작업에서는 기존 raw snapshot을 깨지 않고 유지했다. 대신 export-facing DTO와 adapter를 추가해 `sentPackets`, `receivedBytes`처럼 애매했던 값의 의미를 `TotalSendCompletions`, `TotalParsedPacketBytes`처럼 명확한 이름으로 노출한다.

## 2. Related Documents

- Plan: `docs/01-plan/features/telemetry-export-metric-contract.plan.md`
- Design: `docs/02-design/features/telemetry-export-metric-contract.design.md`
- Do: `docs/02-design/features/telemetry-export-metric-contract.do.md`
- Analysis: `docs/03-analysis/telemetry-export-metric-contract.analysis.md`

## 3. Completed Items

- `ObservedMetricsSnapshot` 추가
- `ClientObservedMetricsSnapshot` 추가
- `ServerObservedMetricsSnapshot` 추가
- `ServerTelemetrySnapshot` -> `ServerObservedMetricsSnapshot` adapter 추가
- `FastPortLoadRunner.MetricsSnapshot` -> `ClientObservedMetricsSnapshot` adapter 추가
- `IServerTelemetryExporter` 추가
- `ServerTelemetryExporter` 추가
- `ObservedMetricsJson` camelCase serializer 추가
- `FastPortSmokeServer` DI에 telemetry exporter 등록
- smoke test에서 `ServerObservedMetricsSnapshot` export 결과 검증
- observed metric semantic unit tests 추가

## 4. Quality Metrics

| Metric | Result |
|--------|--------|
| Design match rate | 92% |
| Build | Passed |
| Build warnings | 0 |
| Tests | 61 passed, 0 failed |
| JSON naming | camelCase verified |
| Server metric semantic tests | Passed |
| Client adapter tests | Passed |

Verification commands:

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
```

## 5. Key Contract Decisions

| Raw Field | Export Field | Meaning |
|-----------|--------------|---------|
| `ServerTelemetrySnapshot.SentPackets` | `ServerObservedMetricsSnapshot.TotalSendCompletions` | socket send completion callback count |
| `ServerTelemetrySnapshot.ReceivedBytes` | `ServerObservedMetricsSnapshot.TotalParsedPacketBytes` | parsed FastPort packet size total |
| `ServerTelemetrySnapshot.ReceivedPackets` | `ServerObservedMetricsSnapshot.TotalReceivedPackets` | parsed FastPort packet count |
| `MetricsSnapshot.AcceptCount` | `ClientObservedMetricsSnapshot.ConnectCount` | client-side session connect count |
| `MetricsSnapshot.ConnectedSessions` | `ClientObservedMetricsSnapshot.CurrentSessions` | client-side active sessions |

`ServerTelemetrySnapshot` remains the low-level raw collector snapshot. Future dashboard and export code should consume observed DTOs instead.

## 6. Remaining Limits

- `FastPortLoadRunner` JSONL output still serializes existing `MetricsSnapshot`.
- No HTTP or streaming telemetry endpoint is implemented yet.
- raw socket receive bytes are still not collected separately.
- MAUI dashboard is not implemented in this scope.
- staged load validation is not implemented in this scope.

## 7. Lessons Learned

### Keep

- Keep low-level telemetry collection stable and add explicit export adapters above it.
- Keep server/client observer ownership visible in metric names.
- Keep JSON payloads camelCase so dashboard code can consume them without custom naming rules.

### Problem

- Existing metric names were compact but ambiguous. `sentPackets` looked like packet count but actually represented socket send completions on the server side.

### Try

- Use observed DTOs as the contract for the MAUI dashboard.
- Decide whether LoadRunner JSONL should switch to `ClientObservedMetricsSnapshot` before building dashboard ingestion.
- Add HTTP/stream export only when a real consumer exists.

## 8. Next Steps

1. `$pdca archive telemetry-export-metric-contract` after user review.
2. Commit the implementation and PDCA documents.
3. Start the next PDCA for one of:
   - `maui-dashboard`
   - `staged-load-validation`
   - `loadrunner-observed-jsonl`
