# Completion Report: server-telemetry-export-merge-socket-error-classification

> Date: 2026-04-29 | Match Rate: 96%

---

## Summary

`server-telemetry-export-merge-socket-error-classification` 기능은 완료 기준을 충족했다.

이번 변경으로 10K 부하 실패를 client-only timeline이 아니라 server observed timeline과 결합해서 볼 수 있게 됐다. `FastPortSmokeServer`는 server telemetry JSONL을 주기적으로 export하고, `FastPortLoadValidation`은 optional server metrics file을 읽어 stage별 combined JSONL과 summary fields를 생성한다. `FastPortLoadRunner`는 client socket/protocol errors를 phase/type/code/class key로 누적한다.

## Related Documents

- Plan: `docs/01-plan/features/server-telemetry-export-merge-socket-error-classification.plan.md`
- Design: `docs/02-design/features/server-telemetry-export-merge-socket-error-classification.design.md`
- Analysis: `docs/03-analysis/server-telemetry-export-merge-socket-error-classification.analysis.md`

## Completed Items

- Server telemetry export
  - `FastPortSmokeServerTelemetryOptions`
  - `ServerTelemetryExportBackgroundService`
  - `--Telemetry:Output`
  - `--Telemetry:IntervalSeconds`
- Client socket error classification
  - phase counters
  - exception type counters
  - socket error code counters
  - combined class key counters
- Observed metrics contract extension
  - optional socket classification dictionaries on `ClientObservedMetricsSnapshot`
  - existing `ObservedMetricsSnapshot` envelope preserved
- Load validation merge
  - `--server-metrics`
  - `--merge-tolerance-ms`
  - server-only JSONL reader
  - `ObservedMetricsMerger`
  - `{stageId}.combined.metrics.jsonl`
- Summary output
  - server sample count
  - merged/unmatched count
  - max merge skew
  - max pending send requests
  - max send buffer bytes
  - max send backpressure events
  - socket error classification counters
- Tests
  - server export JSONL
  - load validation server reader
  - metrics merge matched/unmatched
  - evaluator merged summary fields
  - load runner socket classification counters
  - client observed serialization with classification dictionaries

## Verification

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
dotnet build FastPortCharp.sln -c Release
```

Results:

- Debug build passed: 0 warnings, 0 errors
- Test suite passed: 78 passed, 0 failed
- Release build passed: 0 warnings, 0 errors

Reduced smoke export/merge:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile smoke \
  --output artifacts/load-validation/server-merge-smoke \
  --server-metrics artifacts/load-validation/server-merge-smoke/server.metrics.jsonl
```

Results:

| Stage | Result | Server Samples | Matched | Unmatched | Max Pending Send | Max Send Buffer |
|-------|--------|---------------:|--------:|----------:|-----------------:|----------------:|
| `smoke-fixed-10` | Passed | 20 | 11 | 0 | 2 | 1,068 |
| `smoke-random-25` | Passed | 41 | 19 | 0 | 5 | 16,376 |

Artifacts:

- `artifacts/load-validation/server-merge-smoke/server.metrics.jsonl`
- `artifacts/load-validation/server-merge-smoke/smoke-fixed-10.combined.metrics.jsonl`
- `artifacts/load-validation/server-merge-smoke/smoke-random-25.combined.metrics.jsonl`
- `artifacts/load-validation/server-merge-smoke/summary.md`
- `artifacts/load-validation/server-merge-smoke/summary.json`

## Residual Risk

- `LoadSession` protocol-invalid response classification is implemented but not directly unit-tested by injecting malformed response bytes.
- The first 10K run with server export enabled may still be sensitive to local machine state. The smoke run confirms the export/merge contract, not high-load stability.
- Markdown summary is wider than before. JSON summary should be treated as the detailed source of truth.

## Recommended Next Step

Run the focused 10K stage with server export enabled:

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer \
  --Logging:LogLevel:Default Warning \
  --Logging:LogLevel:Microsoft Warning \
  --Telemetry:Output artifacts/load-validation/s5-server-merged/server.metrics.jsonl \
  --Telemetry:IntervalSeconds 1
```

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-server-merged \
  --server-metrics artifacts/load-validation/s5-server-merged/server.metrics.jsonl
```

Use the resulting `summary.json` and combined JSONL to decide whether the next optimization should target server send backlog, client receive/send path, or OS/socket pressure.
