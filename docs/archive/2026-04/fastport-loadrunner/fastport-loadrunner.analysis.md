# Gap Analysis: fastport-loadrunner

> Date: 2026-04-27 | Design: fastport-loadrunner.design.md

---

## Match Rate: 91%

Current implementation matches the core LoadRunner design: CLI parsing, load scenario modeling, payload generation, TCP session lifecycle, EchoRequest/EchoResponse packet framing, metrics aggregation, console output, JSONL output, focused unit tests, and operational OS-limit guidance are implemented.

The remaining gaps are now concentrated around runtime validation and server-side telemetry: server echo integration was not successfully verified in this execution session, server-side accept/disconnect/socket error telemetry is not yet connected, and high-session manual validation is not performed.

Match rate calculation:

```text
Implemented design items: 31
Total design items:       34
Match rate:               31 / 34 = 91%
```

## Summary

`FastPortLoadRunner` has moved beyond a placeholder and is now a working TCP load runner shell with real session creation, metrics output, parser/payload/metrics unit coverage, and documented host tuning guidance. It is not yet ready to be treated as a validated 10,000-session tool because server integration and load-scale validation are still open.

## Implemented Items

- [x] `LoadRunnerOptions` parses the main CLI contract.
- [x] `LoadScenario` converts parsed options into an executable scenario.
- [x] `PayloadProfile` supports `fixed:<bytes>`.
- [x] `PayloadProfile` supports `random:<min>-<max>`.
- [x] `PayloadGenerator` creates payloads based on profile.
- [x] `LoadRunner` controls lifecycle and duration.
- [x] Session ramp-up is implemented inside `LoadRunner`.
- [x] `LoadSession` owns TCP connect/send/receive loops.
- [x] Per-session cancellation is wired through `CancellationToken`.
- [x] Request id sequence is implemented per session.
- [x] FastPort packet framing is implemented as `[2-byte size][4-byte protocol id][protobuf bytes]`.
- [x] `EchoRequest` serialization is implemented.
- [x] `EchoResponse` parsing is implemented.
- [x] RTT calculation uses client send timestamp and receive timestamp.
- [x] `MetricsCollector` aggregates counters thread-safely.
- [x] `MetricsSnapshot` includes target sessions, connected sessions, packets, bytes, TPS, RTT, accept/disconnect, and socket errors.
- [x] Console reporter prints compact interval metrics.
- [x] JSONL reporter writes one metrics snapshot per line.
- [x] `--metrics-interval` option is implemented.
- [x] `--output` option is implemented.
- [x] `FastPortLoadRunner/README.md` documents current usage and next steps.
- [x] `fastport-loadrunner.do.md` tracks implementation status.
- [x] Unit tests cover CLI parser defaults and full scenario parsing.
- [x] Unit tests cover invalid option validation.
- [x] Unit tests cover fixed and random payload profile parsing.
- [x] Unit tests cover payload generator size behavior.
- [x] Unit tests cover metrics total counters and interval rates.
- [x] JSONL output uses camelCase property names for dashboard-friendly ingestion.
- [x] OS limit guidance is documented for macOS, Linux, and Windows.

## Missing Items

- [ ] Automated integration smoke test with `FastPortServer` is not added.
- [ ] Server echo smoke verification did not succeed in this execution session.
- [ ] Server-side accept/disconnect/socket error telemetry is not connected.
- [ ] 1,000 / 3,000 / 5,000 / 10,000 session manual load validation is not performed.

## Changed Items (Deviations from Design)

- [ ] `SessionRampUp` was designed as a separate component, but the ramp-up logic is currently implemented inside `LoadRunner`.
- [ ] `AcceptCount` and `DisconnectCount` currently reflect client-side session connection/disconnection events, not server-side accept/disconnect telemetry.
- [ ] `PayloadGenerator` uses zero-filled byte arrays. The design only required payload size behavior, but random byte content may be useful later for parser/data-path validation.

## Verification Results

- [x] `dotnet build FastPortCharp.sln`
  - Result: success
  - Warnings: 0
  - Errors: 0

- [x] `dotnet test FastPortCharp.sln --no-build`
  - Result: success
  - Passed: 50
  - Failed: 0
  - New coverage: `LoadRunnerOptions`, `PayloadProfile`, `PayloadGenerator`, `MetricsCollector`

- [x] `dotnet run --no-build --project FastPortLoadRunner -- --help`
  - Result: success
  - Confirmed options: `--host`, `--port`, `--sessions`, `--payload`, `--rate`, `--ramp-up`, `--duration`, `--metrics-interval`, `--output`

- [x] No-server smoke run
  - Command: `dotnet run --no-build --project FastPortLoadRunner -- --sessions 1 --payload fixed:128 --rate 1 --ramp-up 1s --duration 2s --metrics-interval 1s --output /tmp/fastport-loadrunner-iterate-smoke.jsonl`
  - Result: success
  - Expected behavior: no listener, metrics report `errors=100%`
  - JSONL file was written and contains camelCase metrics properties.

- [ ] Server echo smoke run
  - Result: not verified
  - Prior attempt did not produce a successful connection/echo path in this execution session.

## Item-by-Item Design Comparison

| Design Item | Status | Evidence |
|-------------|--------|----------|
| CLI options reproduce scenarios | Match | `LoadRunnerOptions.TryParse` |
| 1,000 to 10,000 sessions via ramp-up | Partial | code supports arbitrary sessions, scale not validated |
| `fixed:8192` payload | Match | `PayloadProfile.Fixed` |
| `random:4096-16384` payload | Match | `PayloadProfile.TryParse`, `GetNextSize` |
| LoadRunner separated from server logic | Match | `FastPortLoadRunner` is independent project |
| `LoadScenario` model | Match | `LoadRunnerOptions.cs` |
| `PayloadGenerator` | Match | `LoadRunner.cs` |
| `MetricsCollector` | Match | `Metrics.cs` |
| `MetricsSnapshot` | Match | `Metrics.cs` |
| Console reporter | Match | `ConsoleMetricsReporter` |
| JSONL reporter | Match | `JsonMetricsReporter` |
| Separate `SessionRampUp` component | Changed | logic folded into `LoadRunner` |
| TCP `LoadSession` | Match | `LoadSession.cs` |
| Echo protobuf request/response | Match | `EchoRequest`, `EchoResponse.Parser` |
| RTT p50/p95/p99 | Match | `MetricsCollector.CalculateRtt` |
| Send/recv bytes/sec | Match | `MetricsSnapshot` interval deltas |
| Send/recv packets/sec | Match | `MetricsSnapshot` interval deltas |
| Socket error rate | Match | `SocketErrorRate` |
| Server-side telemetry | Missing | client-side metrics only |
| Unit tests | Match | `LibCommonTest/FastPortLoadRunnerTests.cs` |
| Integration smoke test | Missing | manual attempt incomplete |
| OS limit docs | Match | `docs/loadrunner-os-limits.md` |

## Recommendations

1. Fix or verify the server echo smoke path before running high-session tests.
2. Add an automated integration smoke test around `FastPortServer` once the server start/health path is deterministic in the test environment.
3. Add server-side telemetry hooks for accept/disconnect/socket errors so the later MAUI dashboard can distinguish client-observed and server-observed metrics.
4. Run staged load validation at 1,000 / 3,000 / 5,000 / 10,000 sessions after applying host OS limit guidance.
5. Extract ramp-up logic to a small `SessionRampUp` class only if the next iteration needs independent testing or multiple ramp-up strategies.

## Next Steps

- [ ] Proceed to `$pdca report fastport-loadrunner` for the LoadRunner foundation scope.
- [ ] Track server-side telemetry and MAUI dashboard work as follow-up PDCA scopes.
