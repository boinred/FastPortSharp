# Completion Report: extract-telemetry-contracts-from-network-core

> Date: 2026-05-05 | Status: Completed | Match Rate: 100%

---

## Summary

`extract-telemetry-contracts-from-network-core`는 observed metrics JSONL 계약과 server telemetry exporter를 `LibNetworks`에서 분리해 새 `LibTestTelemetry` 라이브러리로 이동한 작업이다.

핵심 runtime networking 경로는 건드리지 않았고, `LibNetworks`에는 engine hook 성격의 telemetry 타입만 남겼다. JSON field contract와 camelCase serializer 설정은 유지했으며, load runner, load validation, smoke server, tests는 새 `LibTestTelemetry`를 통해 observed contract를 참조하도록 정리했다.

최종 match rate는 100%다.

## Related Documents

- Plan: `docs/01-plan/features/extract-telemetry-contracts-from-network-core.plan.md`
- Design: `docs/02-design/features/extract-telemetry-contracts-from-network-core.design.md`
- Do: `docs/02-design/features/extract-telemetry-contracts-from-network-core.do.md`
- Analysis: `docs/03-analysis/extract-telemetry-contracts-from-network-core.analysis.md`

## Completed Items

- `LibTestTelemetry/LibTestTelemetry.csproj`를 추가했다.
- `FastPortCharp.sln`에 `LibTestTelemetry` 프로젝트를 추가했다.
- `LibNetworks/Telemetry/ObservedMetrics.cs`를 `LibTestTelemetry/ObservedMetrics.cs`로 이동했다.
- observed metrics contract 타입을 `LibTestTelemetry` namespace로 분리했다:
  - `ObservedMetricsSnapshot`
  - `SessionRttSummarySnapshot`
  - `SlowSessionRttSnapshot`
  - `ObservedOperationDurationSnapshot`
  - `ClientObservedMetricsSnapshot`
  - `ServerObservedMetricsSnapshot`
  - `ObservedMetricsJson`
- server observed artifact exporter를 `LibTestTelemetry`로 이동했다:
  - `IServerTelemetryExporter`
  - `ServerTelemetryExporter`
- `LibNetworks`에 다음 runtime/core telemetry 타입을 유지했다:
  - `IServerTelemetry`
  - `ServerTelemetryCollector`
  - `NullServerTelemetry`
  - `ServerTelemetrySnapshot`
- test tooling 프로젝트 참조를 정리했다:
  - `FastPortTestLoadRunner` -> `LibTestTelemetry`
  - `FastPortTestLoadValidation` -> `LibTestTelemetry`
  - `FastPortTestSmokeServer` -> `LibNetworks`, `LibTestTelemetry`
  - `FastPortTests` -> `LibNetworks`, `LibTestTelemetry`
- `FastPortTestLoadRunner`와 `FastPortTestLoadValidation`의 direct `LibNetworks` project reference를 제거했다.
- observed contract 사용처를 `using LibTestTelemetry`로 분리했다.
- engine hook 사용처는 `using LibNetworks.Telemetry`를 유지했다.

## Quality Metrics

| Metric | Result |
|--------|--------|
| Design match rate | 100% |
| Iteration count | 1 |
| Release build | Passed |
| Unit tests | 117 passed, 0 failed, 0 skipped |
| JSON status validation | Passed |
| Diff whitespace check | Passed |

## Verification

```text
dotnet build FastPortCharp.sln -c Release
```

Result:

```text
경고 0개
오류 0개
```

```text
dotnet test FastPortCharp.sln -c Release --no-build
```

Result:

```text
통과: 117, 실패: 0, 건너뜀: 0
```

Additional checks:

```text
rg -n "ObservedMetricsSnapshot|ServerObservedMetricsSnapshot|ClientObservedMetricsSnapshot|ObservedMetricsJson|IServerTelemetryExporter|ServerTelemetryExporter|SessionRttSummarySnapshot|SlowSessionRttSnapshot|ObservedOperationDurationSnapshot" LibNetworks
```

Result: no matches.

```text
rg -n "LibNetworks|using LibNetworks" FastPortTestLoadRunner
```

Result: no matches.

## Lessons Learned

### Keep

- Observed artifact contract와 engine telemetry hook을 namespace/project boundary로 나누니 dependency 방향이 명확해졌다.
- JSON payload compatibility는 record property와 serializer option만 보존하면 namespace 이동과 독립적으로 유지된다.

### Problem

- `IServerTelemetry.CreateSnapshot()`이 `ServerTelemetrySnapshot`을 직접 반환하기 때문에 collector/snapshot까지 한 번에 분리하면 `LibNetworks` dependency 방향이 흔들린다.
- `LatencyStats`는 diagnostics utility 성격이 강하지만 `LibCommons` 이동은 별도 참조 정리가 필요하다.

### Try

- 후속 작업에서는 engine-facing telemetry sink를 더 작은 interface로 분리해 `Record*` write path와 snapshot/export read path를 분리한다.
- `LatencyStats`는 smoke/test diagnostics library로 이동 가능한지 별도 feature에서 검토한다.

## Next Steps

Recommended next command:

```text
$pdca archive extract-telemetry-contracts-from-network-core
```

Follow-up candidate:

```text
$pdca pm extract-runtime-telemetry-sink-from-network-core
```
