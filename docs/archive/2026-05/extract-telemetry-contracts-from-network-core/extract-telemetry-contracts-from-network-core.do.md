# extract-telemetry-contracts-from-network-core - Do

> Date: 2026-05-05 | Phase: Do | Status: Completed

## Implementation Summary

- Added `LibTestTelemetry` as the shared test telemetry contract library.
- Moved observed metrics JSONL contracts and server telemetry exporter from `LibNetworks` to `LibTestTelemetry`.
- Kept engine/runtime telemetry hooks in `LibNetworks.Telemetry`:
  - `IServerTelemetry`
  - `ServerTelemetryCollector`
  - `NullServerTelemetry`
  - `ServerTelemetrySnapshot`
- Updated test tooling projects to consume observed contract types through `LibTestTelemetry`.
- Removed the direct `LibNetworks` reference from `FastPortTestLoadValidation` because it only needs observed JSONL contracts after this split.

## Files Changed

- `FastPortCharp.sln`
- `LibTestTelemetry/LibTestTelemetry.csproj`
- `LibTestTelemetry/ObservedMetrics.cs`
- `LibNetworks/Telemetry/ObservedMetrics.cs` moved to `LibTestTelemetry/ObservedMetrics.cs`
- `FastPortTestLoadRunner/FastPortTestLoadRunner.csproj`
- `FastPortTestLoadRunner/Metrics.cs`
- `FastPortTestLoadRunner/ObservedMetricsExtensions.cs`
- `FastPortTestLoadValidation/FastPortTestLoadValidation.csproj`
- `FastPortTestLoadValidation/JsonlObservedMetricsReader.cs`
- `FastPortTestLoadValidation/ObservedMetricsMerger.cs`
- `FastPortTestLoadValidation/LoadValidationEvaluator.cs`
- `FastPortTestLoadValidation/LoadValidationStage.cs`
- `FastPortTestLoadValidation/LoadValidationSummaryWriter.cs`
- `FastPortTestSmokeServer/FastPortTestSmokeServer.csproj`
- `FastPortTestSmokeServer/Program.cs`
- `FastPortTestSmokeServer/ServerTelemetryExportBackgroundService.cs`
- `FastPortTests/FastPortTests.csproj`
- `FastPortTests/ObservedMetricsTests.cs`
- `FastPortTests/ServerTelemetryTests.cs`
- `FastPortTests/FastPortTestSmokeServerTests.cs`
- `FastPortTests/FastPortTestLoadRunnerTests.cs`
- `FastPortTests/FastPortTestLoadValidationTests.cs`

## Boundary Check

- `LibNetworks` does not reference `LibTestTelemetry`.
- `LibTestTelemetry` references `LibNetworks` only to map `IServerTelemetry`/`ServerTelemetrySnapshot` to observed artifact snapshots.
- JSON contract type names and record properties were preserved.
- `ObservedMetricsJson.SerializerOptions` keeps camelCase serialization.

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

## Notes

- This phase is a project boundary extraction only.
- No send/receive runtime behavior was intentionally changed.
- A 10K load validation run is not required for this Do phase because the runtime telemetry counters and network paths were not modified.
