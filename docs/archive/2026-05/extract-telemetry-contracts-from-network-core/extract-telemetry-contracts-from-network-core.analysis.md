# Gap Analysis: extract-telemetry-contracts-from-network-core

> Date: 2026-05-05 | Design: docs/02-design/features/extract-telemetry-contracts-from-network-core.design.md

---

## Match Rate: 100%

Implementation matched 30 of 30 checked design items.

## Summary

`LibNetworks`에서 observed metrics JSONL 계약과 exporter를 분리하는 핵심 목표는 달성됐다. 새 `LibTestTelemetry` 프로젝트가 solution에 추가됐고, observed contract 타입은 `LibTestTelemetry` namespace로 이동했다. `LibNetworks`에는 engine/runtime telemetry hook과 collector만 남아 있으며, JSON field contract와 camelCase serialization도 유지됐다.

Iterate 단계에서 `FastPortTestLoadRunner`의 unused `LibNetworks` project reference도 제거했다. 현재 runner는 `LibTestTelemetry`와 `Protocols`, `LibCommons`만 직접 참조하며, observed contract 타입은 모두 `LibTestTelemetry`에서 가져온다.

## Implemented Items

- [x] `LibTestTelemetry/LibTestTelemetry.csproj`를 추가했다.
- [x] `FastPortCharp.sln`에 `LibTestTelemetry` 프로젝트를 추가했다.
- [x] `ObservedMetricsSnapshot`을 `LibTestTelemetry`로 이동했다.
- [x] `SessionRttSummarySnapshot`을 `LibTestTelemetry`로 이동했다.
- [x] `SlowSessionRttSnapshot`을 `LibTestTelemetry`로 이동했다.
- [x] `ObservedOperationDurationSnapshot`을 `LibTestTelemetry`로 이동했다.
- [x] `ClientObservedMetricsSnapshot`을 `LibTestTelemetry`로 이동했다.
- [x] `ServerObservedMetricsSnapshot`을 `LibTestTelemetry`로 이동했다.
- [x] `IServerTelemetryExporter`를 `LibTestTelemetry`로 이동했다.
- [x] `ServerTelemetryExporter`를 `LibTestTelemetry`로 이동했다.
- [x] `ObservedMetricsJson`을 `LibTestTelemetry`로 이동했다.
- [x] moved file namespace를 `LibTestTelemetry`로 변경했다.
- [x] `LibTestTelemetry/ObservedMetrics.cs`가 `LibNetworks.Telemetry`를 import해서 `IServerTelemetry`와 `ServerTelemetrySnapshot`을 사용한다.
- [x] `LibNetworks/Telemetry/ObservedMetrics.cs`는 제거됐고, `LibNetworks/Telemetry`에는 `ServerTelemetry.cs`만 남았다.
- [x] `IServerTelemetry`는 `LibNetworks.Telemetry`에 남아 있다.
- [x] `ServerTelemetryCollector`는 `LibNetworks.Telemetry`에 남아 있다.
- [x] `NullServerTelemetry`는 `LibNetworks.Telemetry`에 남아 있다.
- [x] `ServerTelemetrySnapshot`은 `LibNetworks.Telemetry`에 남아 있다.
- [x] `SessionSendOptions`와 `SendCompletionTracker`는 `LibNetworks/Sessions`에 남아 있다.
- [x] `LibNetworks`는 `LibTestTelemetry`를 참조하지 않는다.
- [x] `LibTestTelemetry`는 설계대로 `LibNetworks`를 참조한다.
- [x] `FastPortTestLoadRunner`는 `LibTestTelemetry`를 참조한다.
- [x] `FastPortTestLoadValidation`은 `LibTestTelemetry`를 참조한다.
- [x] `FastPortTestSmokeServer`는 `LibTestTelemetry`를 참조한다.
- [x] `FastPortTests`는 `LibTestTelemetry`를 참조한다.
- [x] `FastPortTestLoadValidation`의 직접 `LibNetworks` reference를 제거했다.
- [x] call site에서 observed contract 타입은 `using LibTestTelemetry`로 분리했다.
- [x] engine hook 타입 사용처는 `using LibNetworks.Telemetry`를 유지했다.
- [x] `ObservedMetricsJson.SerializerOptions`의 camelCase 정책을 유지했다.
- [x] build/test 검증이 통과했다.

## Missing Items

- [x] None. `FastPortTestLoadRunner/FastPortTestLoadRunner.csproj`의 unused `LibNetworks` project reference를 제거했고 build/test로 검증했다.

## Changed Items (Deviations From Design)

- [x] 기능 동작 변경은 없다.
- [x] JSON payload field 이름 변경은 없다.
- [x] send/receive runtime path 변경은 없다.
- [x] `LatencyStats`, `ServerTelemetryCollector`, `ServerTelemetrySnapshot`은 설계대로 후속 작업으로 남겼다.

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
rg -n "ProjectReference Include=.*LibTestTelemetry|ProjectReference Include=.*LibNetworks" FastPortTestLoadRunner FastPortTestLoadValidation FastPortTestSmokeServer FastPortTests LibTestTelemetry
```

Result:

- `LibTestTelemetry` -> `LibNetworks`
- `FastPortTestLoadRunner` -> `LibTestTelemetry`
- `FastPortTestLoadValidation` -> `LibTestTelemetry`
- `FastPortTestSmokeServer` -> `LibNetworks`, `LibTestTelemetry`
- `FastPortTests` -> `LibNetworks`, `LibTestTelemetry`

## Recommendations

1. Proceed to `$pdca report extract-telemetry-contracts-from-network-core`.
2. Keep `ServerTelemetryCollector`, `ServerTelemetrySnapshot`, and `LatencyStats` as explicit follow-up scope, not part of this feature.

## Next Steps

- [x] Proceed to report is allowed because match rate is above 90%.
- [x] Iterate pass cleaned the remaining project reference gap.
