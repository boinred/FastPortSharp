# FastPortDashboard.Maui

Real-time telemetry dashboard for FastPortSharp servers (`FastPortTestSmokeServer`,
`FastPortGameServerTemplate`, etc.). Reads `ObservedMetricsSnapshot` JSONL files
produced by `ServerTelemetryExportBackgroundService` and visualises throughput
+ session KPIs in 1-second intervals.

> **Foundation cycle scope** — single view, single chart (server throughput),
> 6 KPIs. See `docs/archive/2026-05/maui-telemetry-dashboard-foundation/` for
> the full PDCA record. Multi-run / RTT charts / report export will come in
> follow-up cycles.

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | **10.0+** |
| MAUI workload | `dotnet workload install maui` |
| OS | macOS 15+ (Catalyst) or Windows 10/11 desktop |

## Build & Run

```bash
# from repo root
dotnet workload restore FastPortSharp.Dashboard.sln  # 한 번만
dotnet build FastPortSharp.Dashboard.sln -c Release

# macOS Catalyst run
dotnet build FastPortDashboard.Maui/FastPortDashboard.Maui.csproj \
  -c Release -f net10.0-maccatalyst -t:Run
```

## Quick Start (Mock data)

1. App 실행
2. **Use Mock data** 체크박스 ON
3. **⚡ Connect** 클릭
4. Throughput chart가 1초마다 갱신되는지 확인 (mock random walk)

## Quick Start (실제 server)

1. 다른 터미널에서 server 실행:
   ```bash
   # tests-projects/FastPortTestSmokeServer가 server.metrics.jsonl을 출력
   dotnet run -c Release --project tests-projects/FastPortTestSmokeServer
   ```
2. Dashboard 실행 → **Use Mock** OFF → **Browse** 클릭
3. server.metrics.jsonl 선택 (보통 `artifacts/load-validation/<run>/server.metrics.jsonl`)
4. **⚡ Connect** → 실시간 KPI + chart 갱신 확인

## Architecture

| Layer | 위치 |
|-------|------|
| **View** | `MainPage.xaml` / `.xaml.cs` |
| **ViewModel** | `ViewModels/DashboardViewModel.cs` |
| **Adapter** | `Adapters/IPollingAdapter.cs` + `JsonlPollingAdapter.cs` + `MockPollingAdapter.cs` |
| **Domain (data contract)** | `tests-projects/LibTestTelemetry/ObservedMetrics.cs` (재사용) |

직전 cycle (`fix-server-telemetry-export-jsonl-flush-flakiness`)의 lesson 적용:
**JSONL reader는 `FileShare.ReadWrite | FileShare.Delete` 명시** (windows에서 producer가
Write를 잡고 있을 때 IOException 회피).

## KPIs

| 지표 | 출처 |
|---|---|
| Current Sessions | `ServerObservedMetricsSnapshot.CurrentSessions` |
| Total Accepted | `TotalAcceptedSessions` |
| Total Sent Bytes | `TotalSentBytes` |
| Pending Send | `PendingSendRequests` |
| Send Buffer Bytes | `SendBufferBytes` |
| Last Update | `Timestamp` |

## Chart

| 차트 | y축 데이터 |
|---|---|
| Server Throughput (bytes/sec) | `SentBytesPerSecond` |

## Limitations

- iOS / Android 빌드 미포함 (별도 cycle)
- RTT P95 chart 미포함 (`ClientObservedMetricsSnapshot` 필요 — 다음 cycle)
- Multi-run side-by-side, PDF export 미포함
- File rotation/truncation 시 graceful 재시작은 지원하지만 카운터 reset

## Troubleshooting

| 증상 | 해결 |
|---|---|
| `error NETSDK1147: maui-maccatalyst 워크로드 미설치` | `dotnet workload restore FastPortSharp.Dashboard.sln` |
| Mock 모드에서 chart가 비어있음 | Connect 버튼을 누른 뒤 1-2초 대기 |
| 실 server file 지정 후에도 chart 비어있음 | server가 실제로 1초마다 line 추가 중인지 `tail -f` 확인 |
| `IOException` 반복 | reader가 `FileShare.ReadWrite` 명시 — JsonlPollingAdapter.cs 확인 |
