# Gap Analysis: 10k-load-bottleneck-telemetry

> Date: 2026-04-29 | Design: docs/02-design/features/10k-load-bottleneck-telemetry.design.md

---

## Match Rate: 100%

25 of 25 design items are implemented.

## Summary

`10k-load-bottleneck-telemetry`의 핵심 구현은 설계와 대부분 일치한다. `LibNetworks`에는 protocol-neutral send/backpressure counter가 추가되었고, `FastPortLoadRunner`는 connect attempt/failure, pending request, active ratio, scheduler drift를 client-observed JSONL contract로 내보낸다. `FastPortLoadValidation`도 새 bottleneck field를 stage summary와 Markdown table에 집계한다.

`s5-random-10k` logging-off focused validation도 실행되어 `artifacts/load-validation/s5-logging-off/` 아래에 산출물이 생성되었다. 재측정 결과는 pass가 아니라 fail이다. peak session ratio는 baseline 97.67%에서 86.24%로 하락했고, final disconnect count는 233에서 1,782로 증가했다. RTT P95/P99도 8,738.94 ms / 10,137.04 ms에서 43,268.80 ms / 44,895.97 ms까지 상승했다.

따라서 이번 feature의 code/contract 구현은 완료됐지만, 측정 결과는 logging 감소만으로 10K 병목이 해결되지 않음을 보여준다. 새 telemetry가 추가한 client bottleneck signal에서는 max pending request 55,695, max scheduler drift 28.21 ms, connect failures 0이 관측되었다. 즉 이번 실행의 peak 미달은 connect 실패보다 연결 이후 disconnect/socket error와 request backlog 누적 쪽으로 해석하는 것이 더 타당하다.

검증 결과:

- `dotnet build FastPortCharp.sln`: passed, 0 warnings, 0 errors.
- `dotnet test FastPortCharp.sln --no-build`: passed, 72 passed, 0 failed, 0 skipped.
- `dotnet build FastPortCharp.sln -c Release`: passed, 0 warnings, 0 errors.
- `./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation --profile staged --stage s5-random-10k --output artifacts/load-validation/s5-logging-off`: completed with exit code 2 because validation thresholds failed.

## Implemented Items

- [x] `IServerTelemetry`에 send request, send completion, backpressure, send buffer sample 기록 API가 추가되었다.
- [x] `ServerTelemetryCollector`가 `Interlocked` 기반으로 send request, pending send, max pending send, backpressure event, send buffer sample/max 값을 관리한다.
- [x] `NullServerTelemetry`가 새 telemetry API를 no-op으로 구현한다.
- [x] `ServerTelemetrySnapshot`에 `SendRequests`, `PendingSendRequests`, `MaxPendingSendRequests`, `SendBackpressureEvents`, `SendBufferBytes`, `MaxSendBufferBytes`가 추가되었다.
- [x] `ServerObservedMetricsSnapshot`에 `TotalSendRequests`, `PendingSendRequests`, `MaxPendingSendRequests`, `SendRequestsPerSecond`, `SendBackpressureEvents`, `SendBackpressureEventsPerSecond`, `SendBufferBytes`, `MaxSendBufferBytes`가 추가되었다.
- [x] `BaseSession.RequestSendBuffers`가 send enqueue 후 queue depth와 send request를 기록한다.
- [x] `BaseSession.OnSocketEventsSentCompleted`가 send completion을 기록한다.
- [x] `BaseSession.DoWorkSendBuffers`가 send buffer depth sample을 기록한다.
- [x] send pending threshold와 per-session send buffer threshold 기반 backpressure event가 기록된다.
- [x] `MetricsCollector`가 connect attempt/failure, pending request, max pending request, active session ratio, scheduler drift average/max를 추적한다.
- [x] `LoadSession`이 connect 시도와 실패를 명시적으로 기록하고, 실패한 connect를 disconnect로 계산하지 않는다.
- [x] `LoadSession` send/receive path가 pending request count를 sent-minus-received 방식으로 갱신한다.
- [x] `MetricsReporterClock`이 reporter loop의 expected wake time 대비 actual wake time drift를 기록한다.
- [x] `ClientObservedMetricsSnapshot`에 client bottleneck field가 optional-friendly default 값과 함께 추가되었다.
- [x] `ObservedMetricsExtensions`가 `MetricsSnapshot`의 새 field를 `ClientObservedMetricsSnapshot`으로 매핑한다.
- [x] `JsonMetricsReporter`는 기존 observed envelope를 유지하고 `clientObserved`만 채운 JSONL을 계속 출력한다.
- [x] `ObservedMetricsSnapshot.Combined` 경로는 client/server observed snapshot 병합에 사용할 수 있게 유지된다.
- [x] `JsonlObservedMetricsReader`는 기존 observed envelope를 읽고, 새 field가 없는 JSONL도 DTO default 값으로 처리할 수 있는 구조를 유지한다.
- [x] `LoadValidationStageSummary`에 `FinalConnectAttemptCount`, `FinalConnectFailureCount`, `MaxPendingRequestCount`, `MaxSchedulerDriftMs`, `MaxActiveSessionRatio`가 추가되었다.
- [x] `LoadValidationEvaluator`가 새 client bottleneck field를 stage summary에 집계한다.
- [x] `LoadValidationSummaryWriter`가 Markdown summary에 `Max Pending Req`, `Max Drift`, `RTT P95`, `RTT P99`를 표시한다.
- [x] server telemetry, observed metric serialization, load runner metric aggregation, load validation summary aggregation 테스트가 추가되었다.
- [x] smoke-path integration test가 server send request/max pending/max send buffer counter 노출을 검증한다.

## Missing Items

None.

## Measurement Result

Focused validation artifact:

- Summary: `artifacts/load-validation/s5-logging-off/summary.md`
- JSON summary: `artifacts/load-validation/s5-logging-off/summary.json`
- JSONL metrics: `artifacts/load-validation/s5-logging-off/s5-random-10k.metrics.jsonl`
- Samples: 421
- Result: Failed

| Metric | Baseline staged-local | Logging-off focused run | Direction |
|--------|----------------------:|------------------------:|-----------|
| Target sessions | 10,000 | 10,000 | Same |
| Peak current sessions | 9,767 | 8,624 | Worse |
| Peak session ratio | 97.67% | 86.24% | Worse |
| Final disconnect count | 233 | 1,782 | Worse |
| Max socket error rate | 0.03% | 0.19% | Worse |
| Max TPS | 8,785.21 | 4,058,000.00 | Not comparable; one-sample spike in summary |
| Max RTT P95 | 8,738.94 ms | 43,268.80 ms | Worse |
| Max RTT P99 | 10,137.04 ms | 44,895.97 ms | Worse |
| Max pending request count | Not available | 55,695 | Newly observed |
| Max scheduler drift | Not available | 28.21 ms | Newly observed |
| Connect attempts | Not available | 10,000 | Newly observed |
| Connect failures | Not available | 0 | Newly observed |

Validation failures:

- Peak session ratio 86.24% is below 95.00%.
- Disconnect ratio 17.82% exceeds 5.00%.

## Changed Items (Deviations from Design)

- [x] 설계의 `SendBackpressureRate` 의미는 구현에서 `SendBackpressureEventsPerSecond`로 명확화되었다.
- [x] 설계에서 언급한 `FastPortLoadValidation/LoadValidationStageSummary.cs`는 별도 파일이 아니라 기존 `FastPortLoadValidation/LoadValidationStage.cs` record 확장으로 구현되었다.
- [x] server-side summary field(`MaxPendingSendRequests`, `MaxSendBackpressureEvents`, `MaxSendBufferBytes`)는 설계상 server JSONL merge 이후 단계 항목으로 분리되어 있었고, 이번 구현은 client-only LoadRunner summary 범위에 맞춰 포함하지 않았다.

## Evidence

| Design item | Implementation evidence | Status |
|-------------|--------------------------|--------|
| Server telemetry API extension | `LibNetworks/Telemetry/ServerTelemetry.cs` defines `RecordSendRequested`, `RecordSendCompleted`, `RecordSendBackpressure`, `RecordSendBufferSample`. | Match |
| Server send counters | `ServerTelemetryCollector` maintains send request, pending, max pending, backpressure, send buffer sample/max counters. | Match |
| Server observed rates | `ServerObservedMetricsSnapshot.FromTelemetry` maps send request and backpressure event deltas to per-second rates. | Match |
| BaseSession instrumentation | `BaseSession` records send request enqueue depth, send completion, send buffer sample, and queue-size backpressure. | Match |
| Client connect counters | `LoadSession.RunAsync` records connect attempts and failures before connected sessions are counted. | Match |
| Client pending request counters | `MetricsCollector.RecordSentPacket` increments pending requests and `RecordReceivedPacket` decrements them. | Match |
| Scheduler drift | `MetricsReporterClock.RecordDrift` records positive reporter delay into `MetricsCollector`. | Match |
| Client observed DTO contract | `ClientObservedMetricsSnapshot` has connect attempt/failure, pending request, active ratio, and scheduler drift fields with defaults. | Match |
| JSONL envelope | `JsonMetricsReporter.SerializeSnapshot` emits `ObservedMetricsSnapshot.FromClient(...)` with `serverObserved: null`. | Match |
| Validation summary aggregation | `LoadValidationEvaluator` computes final connect attempts/failures, max pending request, max scheduler drift, and max active ratio. | Match |
| Markdown summary fields | `LoadValidationSummaryWriter` includes `Max Pending Req`, `Max Drift`, `RTT P95`, and `RTT P99`. | Match |
| Test coverage | `ServerTelemetryTests`, `ObservedMetricsTests`, `FastPortLoadRunnerTests`, `FastPortLoadValidationTests`, and `FastPortSmokeServerTests` cover the new paths. | Match |
| Build verification | `dotnet build FastPortCharp.sln` passed with 0 warnings and 0 errors. | Match |
| Unit/integration verification | `dotnet test FastPortCharp.sln --no-build` passed: 72 passed, 0 failed. | Match |
| Focused 10K logging-off run | `artifacts/load-validation/s5-logging-off/summary.md` exists and records the failed focused run. | Match |
| Baseline vs logging-off comparison | This analysis compares peak ratio, disconnect count, socket error rate, RTT P95/P99, pending request, and scheduler drift. | Match |

## Recommendations

1. Proceed to `$pdca report 10k-load-bottleneck-telemetry`.
2. Treat the focused 10K result as a valid bottleneck finding, not a feature failure: telemetry contract work is complete, and the measurement shows logging-off alone did not remove the 10K bottleneck.
3. Use the next PDCA feature to investigate why connected sessions drop after successful connect attempts. Candidate focus areas are server send backlog export, client socket exception classification, and receive/send worker delay instrumentation.

## Next Steps

- [x] Code contract match is 100%.
- [x] Focused 10K validation was executed and compared with baseline.
- [x] Proceed to report phase.
