# Gap Analysis: fastport-smoke-server

> Date: 2026-04-27 | Design: docs/02-design/features/fastport-smoke-server.design.md

---

## Match Rate: 95%

The implementation matches the revised scope: server-observed telemetry now exists in `LibNetworks`, `FastPortSmokeServer` owns echo/protocol smoke behavior, host/port can be overridden for tests, and `LibCommonTest` verifies real fixed and large-random payload echo smoke paths through `FastPortLoadRunner`.

The architectural gap found after the first implementation has been fixed: `FastPortServer` no longer carries the smoke echo/protocol telemetry responsibility and can remain a basic network engine host. The remaining differences are semantic or polish-level, not blockers: `sentPackets` currently means socket send completions rather than exact FastPort packet count, and protocol/parse error paths are instrumented but not directly failure-case tested.

Match rate calculation:

```text
Implemented design items: 36
Total design items:       38
Match rate:               36 / 38 = 95%
```

## Summary

`fastport-smoke-server` is ready to move to report for this scope. It closes the main follow-up from `fastport-loadrunner`: the engine layer can now expose accept/disconnect/session/packet/socket telemetry, and integration smoke tests can start/stop `FastPortSmokeServer` without a manual process.

The feature does not yet attempt high-scale staged validation. That remains a later load-validation scope after this telemetry contract is accepted.

## Implemented Items

- [x] `IServerTelemetry` abstraction added in `LibNetworks.Telemetry`.
- [x] `ServerTelemetryCollector` added with `Interlocked` counters.
- [x] `ServerTelemetrySnapshot` added with accepted/disconnected/connected session fields.
- [x] `ServerTelemetrySnapshot` includes received/sent packets and bytes.
- [x] `ServerTelemetrySnapshot` includes accept/socket/parse/protocol error counters.
- [x] `SocketErrorRate` is calculated from packets and socket errors.
- [x] `Reset()` clears telemetry counters.
- [x] `NullServerTelemetry` preserves backward-compatible constructor paths.
- [x] `BaseListener` records accept success.
- [x] `BaseListener` records accept errors.
- [x] `BaseListener` records socket errors on listener failure paths.
- [x] `BaseSession` records first disconnect transition.
- [x] `BaseSession` records parsed received packets/bytes.
- [x] `BaseSession` records successful send completions/bytes.
- [x] `BaseSession` records socket errors on receive/send paths.
- [x] `BaseSessionClient` and `BaseSessionServer` accept injected telemetry.
- [x] `BaseMessageListener` accepts injected telemetry.
- [x] `FastPortSmokeServer` project added for telemetry smoke behavior.
- [x] `FastPortSmokeServerOptions` added with production defaults.
- [x] `FastPortSmokeServerBackgroundService` uses configured host/port.
- [x] `FastPortSmokeServer.Program` registers telemetry and options.
- [x] `FastPortSmokeServer` passes telemetry to `BaseMessageListener`.
- [x] `FastPortSmokeClientSessionFactory` passes telemetry into server-side sessions.
- [x] `FastPortSmokeClientSession` records parse errors.
- [x] `FastPortSmokeClientSession` records protocol errors for unexpected protocol ids.
- [x] `FastPortServer` simplified back to a basic network host without smoke protocol handling.
- [x] `LibCommonTest` references `FastPortSmokeServer` and `LibNetworks`.
- [x] Unit tests cover snapshot derived connected sessions.
- [x] Unit tests cover telemetry reset.
- [x] Unit tests cover socket error rate.
- [x] Unit tests cover `FastPortSmokeServerOptions` defaults.
- [x] Integration smoke test starts `FastPortSmokeServer` on a dynamic port.
- [x] Integration smoke test performs ready probing and telemetry reset.
- [x] Fixed 1K payload smoke test passes.
- [x] Random 4K-16K payload smoke test passes.
- [x] Smoke assertions check both LoadRunner client metrics and server telemetry.

## Missing Items

- [ ] Direct negative tests for parse error and protocol error increments are not added.
- [ ] Server telemetry is not serialized to JSON yet. This was a future extension, not required for this phase.
- [ ] 1,000 / 3,000 / 5,000 / 10,000 staged load validation is still out of scope.

## Changed Items (Deviations from Design)

- [ ] `sentPackets` is implemented as socket send completion count, not exact FastPort packet count. Current smoke tests are valid, but a later dashboard should label this precisely or add a packet-enqueue counter.
- [ ] `receivedBytes` uses parsed packet size, not raw socket receive bytes. This matches the design allowance to choose one meaning, but raw bytes would need a separate counter if required later.
- [ ] `FastPortSmokeServerTestHost` is implemented as a nested test helper inside `FastPortSmokeServerTests`, not as a reusable shared test utility.

## Verification Results

- [x] `dotnet build FastPortCharp.sln`
  - Result: success
  - Warnings: 0
  - Errors: 0

- [x] `dotnet test FastPortCharp.sln --no-build`
  - Result: success
  - Passed: 56
  - Failed: 0

## Item-by-Item Design Comparison

| Design Item | Status | Evidence |
|-------------|--------|----------|
| Generic telemetry lives in `LibNetworks` | Match | `LibNetworks/Telemetry/ServerTelemetry.cs` |
| `IServerTelemetry` abstraction | Match | `IServerTelemetry` |
| Thread-safe low-overhead counters | Match | `ServerTelemetryCollector` uses `Interlocked` |
| `ServerTelemetrySnapshot` model | Match | `ServerTelemetrySnapshot` |
| Derived connected sessions | Match | `CreateSnapshot()` |
| Socket error rate | Match | `CreateSnapshot()` |
| Reset policy | Match | `Reset()` and smoke ready reset |
| BaseListener accept success | Match | `BaseListener.OnSocketEventsAcceptCompleted` |
| BaseListener accept/socket errors | Match | `BaseListener.StartAccept`, `Accept`, completion handler |
| BaseSession disconnect instrumentation | Match | `BaseSession.RequestDisconnect` |
| BaseSession receive packet/byte instrumentation | Match | `BaseSession.DoWorkReceivedBuffers` |
| BaseSession send byte instrumentation | Match | `BaseSession.OnSocketEventsSentCompleted` |
| BaseSession socket error instrumentation | Match | receive/send exception paths |
| Dedicated smoke server project | Match | `FastPortSmokeServer/FastPortSmokeServer.csproj` |
| Base server remains protocol-light | Match | `FastPortServer/Sessions/FastPortClientSession.cs` |
| Protocol parse error instrumentation | Match | `FastPortSmokeClientSession.TryParseEchoRequest` |
| Protocol id error instrumentation | Match | `FastPortSmokeClientSession.OnReceived` |
| Server options replace fixed host/port | Match | `FastPortSmokeServerOptions`, `FastPortSmokeServerBackgroundService` |
| Production DI registration | Match | `FastPortSmokeServer/Program.cs` |
| Dynamic-port test host | Match | `FastPortSmokeServerTests.FastPortSmokeServerTestHost` |
| Ready check | Match | `WaitUntilReadyAsync()` |
| Reset after ready probe | Match | `telemetry.Reset()` |
| LoadRunner smoke client | Match | `RunSmokeAsync()` constructs `LoadRunner` |
| Fixed 1K smoke | Match | `FastPortSmokeServer_FixedPayload_EchoesAndRecordsTelemetry` |
| Random 4K-16K smoke | Match | `FastPortSmokeServer_RandomLargePayload_EchoesAndRecordsTelemetry` |
| Client metrics assertions | Match | `AssertClientMetrics` |
| Server telemetry assertions | Match | `AssertServerTelemetry` |
| Unit test coverage | Match | `ServerTelemetryTests` |
| Exact packet count for `sentPackets` | Changed | socket send completion count |
| Reusable standalone test host utility | Changed | nested helper in smoke test |
| Negative parse/protocol tests | Missing | no malformed packet test |

## Recommendations

1. Proceed to `$pdca report fastport-smoke-server` for this phase.
2. In the next MAUI/dashboard design, name `sentPackets` carefully or add a separate exact packet counter.
3. Add malformed packet/protocol mismatch tests when failure-mode validation becomes important.
4. Keep 1k/3k/5k/10k staged load validation as a separate PDCA scope.

## Next Steps

- [ ] Run `$pdca report fastport-smoke-server`.
- [ ] Commit the feature after report/archive or before starting the next implementation scope.
