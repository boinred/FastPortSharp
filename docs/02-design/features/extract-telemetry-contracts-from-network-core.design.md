# extract-telemetry-contracts-from-network-core - Design Document

> Version: 1.0.0 | Date: 2026-05-05 | Status: Draft
> Level: Starter | Plan: docs/01-plan/features/extract-telemetry-contracts-from-network-core.plan.md

---

## 1. Overview

`extract-telemetry-contracts-from-network-core`는 `LibNetworks`와 `LibCommons`에 섞여 있는 부하 검증용 telemetry contract를 engine/core 경계 밖으로 분리하는 작업이다.

이번 설계의 1차 목표는 네트워크 동작과 send/receive 경로를 건드리지 않고, observed metrics JSONL 계약과 exporter를 별도 라이브러리로 옮길 수 있는 최소 경계를 확정하는 것이다.

## 2. Current Coupling

현재 telemetry 관련 코드는 다음처럼 섞여 있다.

| Area | Current location | Current consumers | Decision |
|------|------------------|-------------------|----------|
| Observed metrics JSON contract | `LibNetworks/Telemetry/ObservedMetrics.cs` | `FastPortTestLoadRunner`, `FastPortTestLoadValidation`, `FastPortTestSmokeServer`, tests | Move to `LibTestTelemetry` |
| Server telemetry exporter | `LibNetworks/Telemetry/ObservedMetrics.cs` | `FastPortTestSmokeServer`, tests | Move to `LibTestTelemetry` |
| Server telemetry hook/collector | `LibNetworks/Telemetry/ServerTelemetry.cs` | `BaseSession`, `BaseListener`, smoke server, tests | Stay in `LibNetworks` for phase 1 |
| Send policy/accounting | `LibNetworks/Sessions/SessionSendOptions.cs`, `SendCompletionTracker.cs` | `BaseSession`, send policy tests | Stay in `LibNetworks` |
| Latency stats utility | `LibCommons/LatencyStats.cs` | `FastPortTestSmokeServer/Sessions/FastPortTestSmokeClientSession.cs` | Future extraction |

핵심 문제는 `ObservedMetrics.cs`가 load validation JSONL 계약인데도 `LibNetworks`에 있어 runner/validator가 engine library에 묶인다는 점이다.

## 3. Target Architecture

### 3.1 Project Boundary

새 프로젝트 이름은 `LibTestTelemetry`로 한다.

선택 이유:

- 현재 이동 대상은 production telemetry가 아니라 load runner, load validation, smoke server가 공유하는 test/validation telemetry contract다.
- `LibCommons`, `LibNetworks`처럼 core/support library에 `Lib` prefix를 쓰는 기존 naming style과 맞는다.
- `FastPortTelemetry`보다 범위가 좁아서 engine runtime telemetry library로 오해할 가능성이 작다.
- 나중에 production/runtime telemetry가 필요하면 별도 `LibTelemetry` 또는 `LibRuntimeTelemetry` 계층으로 분리할 수 있다.

1차 dependency 방향:

```text
LibCommons
  ^
  |
LibNetworks
  ^
  |
LibTestTelemetry
  ^
  |
FastPortTestSmokeServer / FastPortTestLoadRunner / FastPortTestLoadValidation / FastPortTests
```

Naming policy:

- Engine/core projects keep production-oriented names: `LibCommons`, `LibNetworks`, `Protocols`, `FastPortServer`.
- Test/validation tooling should make its role explicit with `Test` in the project name.
- This feature only creates `LibTestTelemetry`; existing executable project renames are deferred to a separate feature to avoid mixing project identity churn with telemetry contract extraction.

Candidate follow-up names:

| Current project | Candidate name | Reason |
|-----------------|----------------|--------|
| `FastPortTestLoadRunner` | `FastPortTestLoadRunner` | load generator used for validation, not engine runtime |
| `FastPortTestLoadValidation` | `FastPortTestLoadValidation` | benchmark/artifact evaluator |
| `FastPortTestSmokeServer` | `FastPortTestSmokeServer` | smoke/load validation server harness |

`LibTestTelemetry`는 phase 1에서 `LibNetworks`를 참조한다. 이유는 `ServerTelemetryExporter`가 `IServerTelemetry`와 `ServerTelemetrySnapshot`을 사용하기 때문이다.

반대로 `LibNetworks`는 `LibTestTelemetry`를 참조하지 않는다. 이 규칙은 순환 참조를 막기 위한 hard boundary다.

### 3.2 Namespace

새 라이브러리 namespace는 `LibTestTelemetry`로 한다.

기존 namespace `LibNetworks.Telemetry`를 유지하지 않는다. 새 namespace를 사용하면 call site에서 어떤 타입이 engine hook인지, 어떤 타입이 external artifact contract인지 구분된다.

단, 타입 이름과 JSON property 이름은 유지한다.

## 4. Type Movement Matrix

### 4.1 Move In Phase 1

다음 타입은 `LibNetworks/Telemetry/ObservedMetrics.cs`에서 `LibTestTelemetry/ObservedMetrics.cs`로 이동한다.

| Type | Reason |
|------|--------|
| `ObservedMetricsSnapshot` | client/server observed JSONL envelope |
| `SessionRttSummarySnapshot` | load runner output contract |
| `SlowSessionRttSnapshot` | load runner output contract |
| `ObservedOperationDurationSnapshot` | load runner output contract |
| `ClientObservedMetricsSnapshot` | load runner output contract |
| `ServerObservedMetricsSnapshot` | smoke server telemetry export contract |
| `IServerTelemetryExporter` | export boundary for observed metrics artifacts |
| `ServerTelemetryExporter` | maps `IServerTelemetry` snapshot to observed artifact |
| `ObservedMetricsJson` | shared serializer options and JSONL compatibility |

### 4.2 Stay In `LibNetworks` In Phase 1

다음 타입은 engine hook 또는 send path에 직접 연결되어 있으므로 이번 pass에서 이동하지 않는다.

| Type | Reason |
|------|--------|
| `IServerTelemetry` | `BaseSession`, `BaseListener`, session factory constructor surface가 사용 |
| `ServerTelemetryCollector` | engine hook implementation and counter semantics are still core-adjacent |
| `NullServerTelemetry` | core default dependency |
| `ServerTelemetrySnapshot` | `IServerTelemetry.CreateSnapshot()` return type |
| `SessionSendOptions` | send path policy |
| `SendCompletionTracker` | send completion accounting |

### 4.3 Future Extraction

다음 항목은 별도 feature에서 다룬다.

| Candidate | Reason to defer |
|-----------|-----------------|
| `LatencyStats` | smoke diagnostic utility지만 `LibCommons` 이동은 별도 참조 정리가 필요 |
| `ServerTelemetryCollector` | collector를 완전히 core 밖으로 빼려면 `IServerTelemetry.CreateSnapshot()` 분리 또는 interface 축소가 필요 |
| `ServerTelemetrySnapshot` | snapshot을 옮기려면 `LibNetworks`가 telemetry contract library를 참조하게 되어 dependency 방향이 바뀐다 |

## 5. Contract Compatibility

다음 호환성은 반드시 유지한다.

- `ObservedMetricsJson.SerializerOptions`의 camelCase 정책을 유지한다.
- `ObservedMetricsSnapshot` JSON envelope 구조를 유지한다.
- `clientObserved`, `serverObserved`, `timestamp` 필드 이름을 유지한다.
- `ClientObservedMetricsSnapshot`와 `ServerObservedMetricsSnapshot`의 property 이름과 의미를 유지한다.
- `ServerObservedMetricsSnapshot.FromTelemetry(...)`의 per-second delta 계산 의미를 유지한다.
- 기존 JSONL artifact reader가 namespace 변경에 영향을 받지 않게 JSON field contract만 기준으로 검증한다.

Namespace 변경은 JSON payload에 영향을 주지 않는다. System.Text.Json은 현재 record property 이름과 serializer options에 의해 payload를 생성한다.

## 6. Project Reference Changes

### 6.1 Add

- Solution에 `LibTestTelemetry/LibTestTelemetry.csproj`를 추가한다.
- `LibTestTelemetry`는 `LibNetworks`를 참조한다.
- `FastPortTestLoadRunner`는 `LibTestTelemetry`를 참조한다.
- `FastPortTestLoadValidation`은 `LibTestTelemetry`를 참조한다.
- `FastPortTestSmokeServer`는 `LibTestTelemetry`를 참조한다.
- `FastPortTests`는 `LibTestTelemetry`를 참조한다.

### 6.2 Review And Remove If Unused

빌드로 확인한 뒤 필요 없는 참조만 제거한다.

- `FastPortTestLoadValidation`의 `LibNetworks` 참조는 observed contract만 사용 중이면 제거한다.
- `FastPortTestLoadRunner`의 `LibNetworks` 참조는 observed contract만 사용 중이면 제거한다.

`FastPortTestSmokeServer`는 실제 listener/session 구현 때문에 `LibNetworks` 참조를 유지한다.

## 7. File Changes

예상 파일 변경 범위:

| File | Change |
|------|--------|
| `FastPortCharp.sln` | add `LibTestTelemetry` project |
| `LibTestTelemetry/LibTestTelemetry.csproj` | new project |
| `LibTestTelemetry/ObservedMetrics.cs` | moved observed contract/exporter |
| `LibNetworks/Telemetry/ObservedMetrics.cs` | remove after move |
| `FastPortTestLoadRunner/FastPortTestLoadRunner.csproj` | add `LibTestTelemetry` reference, review `LibNetworks` reference |
| `FastPortTestLoadValidation/FastPortTestLoadValidation.csproj` | add `LibTestTelemetry` reference, review `LibNetworks` reference |
| `FastPortTestSmokeServer/FastPortTestSmokeServer.csproj` | add `LibTestTelemetry` reference |
| `FastPortTests/FastPortTests.csproj` | add `LibTestTelemetry` reference |
| `FastPortTestLoadRunner/*.cs` | update `using LibTestTelemetry` for observed contract use |
| `FastPortTestLoadValidation/*.cs` | update `using LibTestTelemetry` for observed contract use |
| `FastPortTestSmokeServer/*.cs` | keep `LibNetworks.Telemetry` for engine hooks, add `LibTestTelemetry` for exporter |
| `FastPortTests/*.cs` | split using directives between engine telemetry and observed contract |

## 8. Implementation Order

1. Create `LibTestTelemetry` project and add it to the solution.
2. Move `ObservedMetrics.cs` contents to `LibTestTelemetry/ObservedMetrics.cs`.
3. Change the moved file namespace to `LibTestTelemetry`.
4. Add `using LibNetworks.Telemetry;` inside `LibTestTelemetry/ObservedMetrics.cs` for `IServerTelemetry` and `ServerTelemetrySnapshot`.
5. Add project references from runner, validator, smoke server, and tests.
6. Update call site `using` directives:
   - observed contract types use `LibTestTelemetry`.
   - engine hook types keep `LibNetworks.Telemetry`.
7. Remove `LibNetworks/Telemetry/ObservedMetrics.cs`.
8. Build and remove no-longer-needed project references only after compile proves they are unused.
9. Run tests and check JSON compatibility tests.

## 9. Test Plan

### 9.1 Required Verification

```text
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln -c Release --no-build
```

### 9.2 Focus Areas

- `ObservedMetricsTests`
  - JSON camelCase compatibility
  - server telemetry to observed metrics mapping
  - client pacing fields deserialization
- `ServerTelemetryTests`
  - exporter still writes server observed JSONL
  - collector counter behavior unchanged
- `FastPortTestLoadRunnerTests`
  - client observed metrics mapping from runner metrics
- `FastPortTestLoadValidationTests`
  - JSONL reader, merger, summary writer still parse old contract
- `FastPortTestSmokeServerTests`
  - smoke server can create telemetry collector/exporter and emit observed snapshot
- `BaseSessionSendPolicyTests`
  - confirms send path behavior was not accidentally touched

### 9.3 Performance Validation

10K load validation is not required for this feature because this is a project boundary and namespace migration. If build/tests pass and JSONL contract tests remain stable, runtime performance should be unchanged.

10K test can be scheduled after this feature only if the implementation accidentally changes telemetry counter collection semantics. That is explicitly out of scope for the first pass.

## 10. Risks And Mitigations

| Risk | Mitigation |
|------|------------|
| Circular project reference | Keep `LibNetworks` independent from `LibTestTelemetry`; only `LibTestTelemetry` can reference `LibNetworks` in phase 1 |
| JSONL compatibility break | Preserve record property names and serializer options; run observed metrics tests |
| Too much migration at once | Do not move `IServerTelemetry`, collector, snapshot, or `LatencyStats` in this feature |
| Ambiguous `using LibNetworks.Telemetry` after move | Split observed contract types to `LibTestTelemetry` and engine hook types to `LibNetworks.Telemetry` |
| Tests become noisy due to mixed namespaces | Update tests by concern: observed contract tests import both namespaces only where needed |

## 11. Non-Goals Confirmed

- Do not change network send/receive behavior.
- Do not change backpressure thresholds.
- Do not change load validation thresholds.
- Do not change benchmark artifact schema.
- Do not move `LatencyStats` in this pass.
- Do not add cloud deployment or runner automation.
- Do not rename existing executable test projects in this pass.
- Do not rename all telemetry classes.

## 12. Follow-Up Candidates

After phase 1 succeeds, the next possible feature is:

```text
$pdca pm extract-runtime-telemetry-sink-from-network-core
```

That follow-up can introduce a smaller engine-facing sink interface, for example:

```text
INetworkTelemetrySink
```

If that interface contains only `Record*` methods and excludes `CreateSnapshot()`/`Reset()`, then `ServerTelemetryCollector` and `ServerTelemetrySnapshot` can move to `LibTestTelemetry` or a later runtime telemetry library without forcing `LibNetworks` to depend on the telemetry contract library.

Another follow-up candidate is:

```text
$pdca pm clarify-test-tool-project-names
```

That feature can rename executable test tooling projects after `LibTestTelemetry` extraction is stable.

## 13. Acceptance Criteria

- [ ] `LibTestTelemetry` boundary is implemented as designed.
- [ ] `LibNetworks` no longer contains `ObservedMetricsSnapshot` or observed JSON serializer contract.
- [ ] `LibNetworks` still owns engine telemetry hook interfaces and send policy types.
- [ ] Existing observed metrics JSONL payload is unchanged.
- [ ] Release build passes.
- [ ] Release tests pass.

## 14. Next Phase

Recommended next command:

```text
$pdca do extract-telemetry-contracts-from-network-core
```
