# extract-telemetry-contracts-from-network-core - Plan Document

> Version: 1.0.0 | Date: 2026-05-05 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`extract-telemetry-contracts-from-network-core`는 load validation, smoke server, diagnostic telemetry 계약이 `LibNetworks`와 `LibCommons` engine/core 라이브러리에 섞여 있는 범위를 분리하는 feature다.

목표는 엔진 동작을 바꾸지 않고, 테스트/부하검증/관측 계약을 별도 라이브러리로 옮길 수 있는 경계를 먼저 정리하는 것이다.

### 1.2 Background

현재 성능 검증 과정에서 다음 코드가 core library에 들어와 있다.

| Area | Current location | Observation |
|------|------------------|-------------|
| Observed metrics JSON contract | `LibNetworks/Telemetry/ObservedMetrics.cs` | `FastPortTestLoadRunner`, `FastPortTestLoadValidation`, `FastPortTestSmokeServer` 중심으로 사용된다. |
| Server telemetry collector/exporter | `LibNetworks/Telemetry/ServerTelemetry.cs`, `ObservedMetrics.cs` | `IServerTelemetry` hook은 engine path에 연결되어 있지만 collector/exporter는 diagnostics 성격이 강하다. |
| Latency stats | `LibCommons/LatencyStats.cs` | client/smoke diagnostic utility이며 buffer/packet/id 공용 자료구조와 성격이 다르다. |
| Send policy/accounting | `LibNetworks/Sessions/SessionSendOptions.cs`, `SendCompletionTracker.cs` | 실제 send path 정책/완료 회계이므로 core에 남기는 것이 적절하다. |

현재 참조 구조상 `BaseSession`, `BaseListener`, `BaseSessionClient`, `BaseSessionServer`가 `IServerTelemetry`를 직접 호출한다. 따라서 첫 pass에서 `IServerTelemetry`를 무리하게 빼면 engine constructor/API surface가 크게 흔들릴 수 있다.

## 2. Goals

### 2.1 Primary Goals

- [ ] `LibNetworks`에 남겨야 하는 engine hook과 외부로 분리할 telemetry contract를 구분한다.
- [ ] 새 telemetry/diagnostics 라이브러리 후보를 정의한다.
- [ ] `ObservedMetrics*` JSON contract를 `LibNetworks` 밖으로 옮기는 1차 설계를 만든다.
- [ ] `ServerTelemetryCollector`/`ServerTelemetryExporter`의 분리 가능성을 평가한다.
- [ ] `LibCommons/LatencyStats.cs`를 core 자료구조에서 분리할지 결정한다.
- [ ] 기존 `FastPortTestLoadRunner`, `FastPortTestLoadValidation`, `FastPortTestSmokeServer`, tests가 깨지지 않는 migration path를 정한다.

### 2.2 Non-Goals

- 이번 feature에서 네트워크 engine send/receive 동작을 바꾸지 않는다.
- 이번 feature에서 `SessionSendOptions`, `SendCompletionTracker`, BaseSession send queue 구조를 옮기지 않는다.
- 이번 feature에서 observed metrics JSON field 이름을 변경하지 않는다.
- 이번 feature에서 benchmark 수치 개선을 목표로 하지 않는다.
- 이번 feature에서 MAUI dashboard나 cloud validation 기능을 추가하지 않는다.
- 이번 feature에서 모든 telemetry를 한 번에 core 밖으로 제거하지 않는다.

## 3. Scope

### 3.1 In Scope

- 새 라이브러리 후보:
  - `FastPortTelemetry`, or
  - `FastPortDiagnostics`
- 1차 이동 후보:
  - `ObservedMetricsSnapshot`
  - `ClientObservedMetricsSnapshot`
  - `ServerObservedMetricsSnapshot`
  - `SessionRttSummarySnapshot`
  - `SlowSessionRttSnapshot`
  - `ObservedOperationDurationSnapshot`
  - `ObservedMetricsJson`
  - `IServerTelemetryExporter`
  - `ServerTelemetryExporter`
- 검토 후보:
  - `ServerTelemetryCollector`
  - `ServerTelemetrySnapshot`
  - `NullServerTelemetry`
  - `LatencyStats`
- core에 남길 가능성이 높은 항목:
  - `IServerTelemetry`
  - `SessionSendOptions`
  - `SendCompletionTracker`
  - BaseSession/BaseListener telemetry hook 호출 위치

### 3.2 Out of Scope

- Protocol schema 변경.
- Load validation threshold 변경.
- Cloud deployment/resource 생성.
- `LibNetworks` public send/receive behavior 변경.
- 대규모 namespace rename.
- generated artifacts 이동/커밋.

## 4. Success Criteria

- [ ] 분리 대상과 잔류 대상이 design 문서에 명확히 기록된다.
- [ ] 새 라이브러리 이름과 project reference 방향이 결정된다.
- [ ] `LibNetworks`가 load validation JSON contract를 직접 소유하지 않는 구조가 설계된다.
- [ ] `FastPortTestLoadRunner`와 `FastPortTestLoadValidation`은 기존 observed JSONL field 계약을 유지한다.
- [ ] `FastPortTestSmokeServer` telemetry export가 기존 summary/merge workflow와 호환된다.
- [ ] Release build/test 기준이 명확하다.
- [ ] 변경 후 `dotnet build FastPortCharp.sln -c Release`와 `dotnet test FastPortCharp.sln -c Release --no-build`가 통과해야 한다.

## 5. Schedule

| Phase | Target Date | Status |
|-------|------------|--------|
| Plan | 2026-05-05 | Completed |
| Design | TBD | Pending |
| Implementation | TBD | Pending |
| Check | TBD | Pending |
| Report | TBD | Pending |

## 6. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| `IServerTelemetry`를 성급히 이동해 engine API가 크게 흔들림 | High | Medium | 1차 pass는 observed contract 이동 중심으로 제한한다. |
| 새 라이브러리가 `LibNetworks`를 다시 참조하면서 순환 참조 발생 | High | Medium | project reference 방향을 design에서 먼저 고정한다. |
| JSON field 이름 변경으로 기존 benchmark artifact reader가 깨짐 | High | Low | record 이름 이동과 namespace 변경은 허용하되 JSON property naming은 유지한다. |
| `LatencyStats`가 client/smoke 양쪽에서 엮여 있어 이동 범위가 커짐 | Medium | Medium | 1차 구현에서 제외하거나 별도 phase로 분리한다. |
| 테스트 fixture가 여러 프로젝트를 동시에 참조해 migration diff가 커짐 | Medium | Medium | observed contract tests부터 이동/수정하고 build break를 단계적으로 해소한다. |

## 7. Architecture Considerations

Preferred dependency direction:

```text
LibCommons
  ^
  |
LibNetworks
  ^
  |
FastPortTelemetry / FastPortDiagnostics
  ^
  |
FastPortTestSmokeServer / FastPortTestLoadRunner / FastPortTestLoadValidation
```

Open design question:

- If `FastPortTelemetry` needs `ServerTelemetrySnapshot`, then `ServerTelemetrySnapshot` may also move.
- If `LibNetworks` must keep `IServerTelemetry.CreateSnapshot()`, then the snapshot type keeps `LibNetworks` tied to telemetry data contracts.
- A safer alternative is to keep `IServerTelemetry` + `ServerTelemetrySnapshot` in `LibNetworks` for one pass and move only observed/export JSON contracts first.

## 8. References

- `LibNetworks/Telemetry/ObservedMetrics.cs`
- `LibNetworks/Telemetry/ServerTelemetry.cs`
- `LibNetworks/Sessions/BaseSession.cs`
- `LibNetworks/BaseListener.cs`
- `LibCommons/LatencyStats.cs`
- `FastPortTestLoadRunner/Metrics.cs`
- `FastPortTestLoadRunner/ObservedMetricsExtensions.cs`
- `FastPortTestLoadValidation/JsonlObservedMetricsReader.cs`
- `FastPortTestLoadValidation/ObservedMetricsMerger.cs`
- `FastPortTestSmokeServer/ServerTelemetryExportBackgroundService.cs`
- `FastPortTests/ObservedMetricsTests.cs`
- `FastPortTests/ServerTelemetryTests.cs`

## 9. Next Phase

Recommended next command:

```text
$pdca design extract-telemetry-contracts-from-network-core
```
