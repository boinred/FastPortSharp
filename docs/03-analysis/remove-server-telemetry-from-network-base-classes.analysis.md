# Gap Analysis: remove-server-telemetry-from-network-base-classes

> Date: 2026-05-06 | Design: docs/02-design/features/remove-server-telemetry-from-network-base-classes.design.md

---

## Match Rate: 100%

Implemented design items: 34 / 34

## Summary

Implementation matches the design. `LibNetworks` no longer owns or references the server telemetry contract, while `FastPortTestSmokeServer` and test-only sessions bridge the new `LibNetworks` hooks to `LibTestTelemetry.ServerTelemetryCollector`.

The network send/receive/accept algorithms were not changed. The implementation replaces telemetry calls with no-op protected hooks in the same branches where the old `Record*` calls existed.

## Implemented Items

- [x] `IServerTelemetry` moved to `LibTestTelemetry`.
- [x] `ServerTelemetryCollector` moved to `LibTestTelemetry`.
- [x] `ServerTelemetrySnapshot` moved to `LibTestTelemetry`.
- [x] `NullServerTelemetry` removed.
- [x] `LibNetworks/Telemetry/ServerTelemetry.cs` deleted.
- [x] `LibTestTelemetry/ObservedMetrics.cs` no longer imports `LibNetworks.Telemetry`.
- [x] `LibTestTelemetry/LibTestTelemetry.csproj` no longer references `LibNetworks`.
- [x] `LibNetworks` source has no `LibTestTelemetry` dependency.
- [x] `LibNetworks` source has no server telemetry type references.
- [x] `BaseListener` telemetry constructor and field removed.
- [x] `BaseListener` added `OnAcceptSucceeded`, `OnAcceptFailed`, and `OnListenerSocketError` hooks.
- [x] `BaseListener` accept success/error/socket-error call sites map to hooks.
- [x] `BaseMessageListener` telemetry overload removed.
- [x] `BaseSession` telemetry constructors and `ServerTelemetry` protected property removed.
- [x] `BaseSession` added network observation hooks for disconnect, socket error, received packet, sent bytes, send request, send completion, backpressure, rejection, drain yield, and buffer sample.
- [x] `BaseSession` old `Record*` call sites replaced with matching hook calls.
- [x] `BaseSession` send queue accounting and send/receive algorithms preserved.
- [x] `BaseSessionClient` telemetry overloads removed.
- [x] `BaseSessionServer` telemetry overloads removed.
- [x] `FastPortTestSmokeServer` keeps injected `IServerTelemetry` and records accept/socket events by overriding listener hooks.
- [x] `FastPortTestSmokeClientSession` stores injected `IServerTelemetry` privately and calls base constructor without telemetry.
- [x] `FastPortTestSmokeClientSession` overrides all `BaseSession` network hooks needed by server telemetry.
- [x] Protocol and parse errors remain in smoke session protocol-specific code.
- [x] `FastPortTestSmokeClientSessionFactory` uses `LibTestTelemetry.IServerTelemetry`.
- [x] `FastPortTestSmokeServer/Program.cs` registers `LibTestTelemetry.IServerTelemetry`.
- [x] `ServerTelemetryTests` import collector/snapshot/exporter from `LibTestTelemetry`.
- [x] `FastPortTestSmokeServerTests` validate smoke telemetry through the new namespace and bridge.
- [x] `BaseSessionSendPolicyTests` use a test-only hook bridge rather than base constructor telemetry.
- [x] `ObservedMetricsTests` use `LibTestTelemetry.ServerTelemetrySnapshot`.
- [x] `FastPortTestLoadRunner` depends on `LibTestTelemetry` and not `LibNetworks`.
- [x] `FastPortTestLoadValidation` depends on `LibTestTelemetry` and not `LibNetworks`.
- [x] `dotnet build FastPortCharp.sln -c Release` passes.
- [x] `dotnet test FastPortCharp.sln -c Release --no-build` passes.
- [x] Static boundary checks pass.

## Missing Items

None.

## Changed Items

- [x] `LibNetworks` public/protected constructor surface intentionally changed by removing telemetry overloads. This matches the design's intended repo-local API break.
- [x] `LibTestTelemetry/ServerTelemetry.cs` now has the telemetry namespace and omits the prior no-op implementation. This matches the `NullServerTelemetry` removal decision.

## Verification

### Build

```text
dotnet build FastPortCharp.sln -c Release
```

Result:

```text
Build succeeded. Warnings: 0, Errors: 0
```

### Tests

```text
dotnet test FastPortCharp.sln -c Release --no-build
```

Result:

```text
Passed: 117, Failed: 0, Skipped: 0
```

### Static Boundary Checks

```text
rg -n "IServerTelemetry|ServerTelemetryCollector|ServerTelemetrySnapshot|NullServerTelemetry" LibNetworks
rg -n "LibTestTelemetry" LibNetworks
rg -n "LibNetworks\.Telemetry|NullServerTelemetry" --glob '*.cs'
rg -F "ProjectReference Include=\"..\\LibNetworks\\LibNetworks.csproj\"" LibTestTelemetry/LibTestTelemetry.csproj
```

Result:

```text
no matches
```

## Risk Review

| Risk | Result |
|------|--------|
| Hook placement drift from old telemetry calls | Mitigated by 1:1 replacement in existing branches. |
| Smoke telemetry fidelity loss | Mitigated by smoke session/listener bridge and passing smoke tests. |
| Accidental project reference cycle | Not present; `LibTestTelemetry` no longer references `LibNetworks`. |
| JSONL contract break | Not observed; observed metrics tests pass. |
| Send/receive behavior regression | Not observed; release build and full test suite pass. |

## Recommendations

1. Proceed to report phase.
2. Keep the next review focused on whether the new hook names are acceptable as the stable protected extension surface.

## Next Steps

- [x] Proceed to report if no additional implementation changes are requested.

Recommended next command:

```text
$pdca report remove-server-telemetry-from-network-base-classes
```
