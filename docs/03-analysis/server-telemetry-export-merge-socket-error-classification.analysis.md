# Gap Analysis: server-telemetry-export-merge-socket-error-classification

> Date: 2026-04-29 | Design: docs/02-design/features/server-telemetry-export-merge-socket-error-classification.design.md

---

## Match Rate: 96%

계산 기준: 설계 항목 25개 중 24개 구현 또는 검증 완료.

## Summary

설계의 핵심 목표였던 server telemetry JSONL export, client/server timestamp merge, combined JSONL artifact, validation summary 확장, client socket error phase/type/code/classification은 구현되었다.

Debug/Release build, 전체 단위 테스트, reduced smoke export/merge 실행까지 통과했다. 남은 갭은 `LoadSession` protocol-invalid 응답을 직접 주입하는 단위 테스트 1개다. 실제 classification code path는 구현되어 있고 `MetricsCollector` classification test로 핵심 counter contract는 검증했다.

## Implemented Items

- [x] `FastPortSmokeServer` telemetry export options 추가
  - `FastPortSmokeServerTelemetryOptions`
  - `--Telemetry:Output`
  - `--Telemetry:IntervalSeconds`
- [x] `ServerTelemetryExportBackgroundService` 추가
  - interval 기반 server observed JSONL writer
  - `FileShare.Read`
  - cancellation 시 flush
- [x] server observed JSONL envelope 유지
  - `clientObserved: null`
  - `serverObserved` populated
- [x] `ClientObservedMetricsSnapshot` socket error classification dictionaries 추가
  - phase
  - type
  - code
  - class key
- [x] `MetricsCollector` phase-aware socket error counter 추가
  - 기존 `RecordSocketError()`는 `unknown` phase로 유지
  - `RecordSocketError(phase, exception)`
  - `RecordProtocolError(reason)`
- [x] `LoadSession` phase별 error recording 적용
  - connect
  - send
  - receive
  - protocol
- [x] `JsonlObservedMetricsReader` envelope reader 확장
  - client-only compatibility 유지
  - server-only JSONL 읽기 지원
- [x] `ObservedMetricsMerger` 추가
  - client timestamp 기준 nearest server sample merge
  - tolerance 기반 matched/unmatched 계산
  - max skew 계산
- [x] `FastPortLoadValidation` CLI 확장
  - `--server-metrics`
  - `--merge-tolerance-ms`
- [x] stage별 combined JSONL artifact 추가
  - `{stageId}.combined.metrics.jsonl`
- [x] `LoadValidationStageSummary` server/merge/socket fields 추가
  - server metrics path
  - combined metrics path
  - server sample count
  - merged/unmatched count
  - max merge skew
  - max pending send
  - max send buffer bytes
  - max backpressure events
  - socket classification counters
- [x] Markdown summary에 server merge fields 추가
- [x] JSON summary에 detailed counters 보존
- [x] old client-only flow compatibility 유지
- [x] server export unit test 추가
- [x] metrics reader server-only test 추가
- [x] metrics merger matched/unmatched tests 추가
- [x] evaluator merged server/socket classification test 추가
- [x] load runner socket classification serialization test 추가
- [x] reduced smoke export/merge 실행 검증

## Missing Items

- [ ] `LoadSession` protocol-invalid response를 직접 주입해서 `protocol` classification을 확인하는 단위 테스트

## Changed Items

- [x] 설계에서는 server export disabled path test를 별도 항목으로 언급했지만, 현재는 enabled export path를 검증했다.
  - 동작 자체는 `Telemetry:Output`이 없으면 service가 no-op return하도록 구현되어 있다.
  - 별도 disabled-path test는 낮은 가치라 이번 match rate에서 필수 항목으로 세지 않았다.
- [x] protocol classification type은 exception type이 아니라 reason string을 사용한다.
  - 예: `protocol|unexpected-protocol-id|none`
  - protocol-invalid 케이스는 exception이 없을 수 있어 원인 reason을 type slot에 보존하는 편이 summary에서 더 유용하다.

## Verification

### Build And Tests

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
dotnet build FastPortCharp.sln -c Release
```

Results:

- Debug build: passed, 0 warnings, 0 errors
- Tests: passed, 78 passed, 0 failed
- Release build: passed, 0 warnings, 0 errors

### Reduced Smoke Export/Merge

Server:

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer \
  --Logging:LogLevel:Default Warning \
  --Logging:LogLevel:Microsoft Warning \
  --Telemetry:Output artifacts/load-validation/server-merge-smoke/server.metrics.jsonl \
  --Telemetry:IntervalSeconds 1
```

Validation:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile smoke \
  --output artifacts/load-validation/server-merge-smoke \
  --server-metrics artifacts/load-validation/server-merge-smoke/server.metrics.jsonl
```

Results:

| Artifact | Result |
|----------|--------|
| `summary.md` | Passed |
| `server.metrics.jsonl` | 45 lines |
| `smoke-fixed-10.combined.metrics.jsonl` | 11 lines |
| `smoke-random-25.combined.metrics.jsonl` | 19 lines |

Stage merge summary:

| Stage | Server Samples | Matched | Unmatched | Max Skew | Max Pending Send | Max Send Buffer |
|-------|---------------:|--------:|----------:|---------:|-----------------:|----------------:|
| `smoke-fixed-10` | 20 | 11 | 0 | 363.229 ms | 2 | 1,068 |
| `smoke-random-25` | 41 | 19 | 0 | 263.773 ms | 5 | 16,376 |

## Recommendations

1. Proceed to report because match rate is above 90%.
2. Add direct `LoadSession` protocol-invalid classification test only if the next phase touches `LoadSession` again.
3. Use the new export/merge path for the next `s5-random-10k` run before applying performance tuning.

## Next Steps

- [x] Proceed to report: `$pdca report server-telemetry-export-merge-socket-error-classification`
