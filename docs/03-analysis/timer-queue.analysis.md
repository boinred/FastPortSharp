# Gap Analysis: timer-queue

> Date: 2026-05-06 | Design: docs/02-design/features/timer-queue.design.md

---

## Match Rate: 91%

Implemented items: 20 / 22 design items.

## Summary

`timer-queue` implementation matches the main design direction: reusable app-wide `TimerQueue`, DI singleton wiring, `SessionIdleTracker` as the session cleanup consumer, reason-aware disconnects, last receive timestamp tracking, and idle cleanup telemetry are implemented and covered by tests.

The implementation intentionally places `TimerQueue` in `LibCommons.Timers` instead of `LibNetworks.Timers` to satisfy the latest requirement that the queue be broadly reusable. The remaining gaps are operational validation and optional future export of TimerQueue runtime counters.

## Implemented Items

- [x] `ITimerQueue` abstraction added.
- [x] `ITimerQueueHandle` cancel/dispose contract added.
- [x] `TimerQueue` one-shot scheduling implemented.
- [x] `TimerQueue` periodic scheduling implemented.
- [x] `TimerQueue` cancel prevents pending callback execution.
- [x] `TimerQueue.DisposeAsync()` stops pending callbacks and rejects new schedules.
- [x] Callback exception does not stop the worker.
- [x] Monotonic time source abstraction added with `StopwatchMonotonicTimeSource`.
- [x] `TimerQueue` is reusable outside networking by living in `LibCommons.Timers`.
- [x] `FastPortTestSmokeServer` wires `TimerQueue` as DI singleton.
- [x] `NetworkDisconnectReason` added.
- [x] `BaseSession.RequestDisconnect(NetworkDisconnectReason reason)` added while preserving `RequestDisconnect()`.
- [x] `BaseSession.LastReceivedTimestamp` added.
- [x] Successful socket receive updates last activity timestamp.
- [x] `SessionIdleTracker` added.
- [x] `SessionIdleTracker` uses one periodic timer instead of per-session timers.
- [x] `FastPortTestSmokeClientSession` registers/unregisters with `SessionIdleTracker`.
- [x] Idle timeout calls `RequestDisconnect(NetworkDisconnectReason.IdleTimeout)`.
- [x] Server telemetry records disconnect reason counters and idle timeout cleanup counts.
- [x] Release build and full test suite pass.

## Missing Items

- [ ] Cloud smoke/staged/10K validation has not been run after implementation.
- [ ] TimerQueue runtime counters are not exported in observed JSONL. Local `ExecutedCallbackCount` and `FailedCallbackCount` exist, but no server observed fields are wired for them.

## Changed Items

- [x] TimerQueue namespace changed from the initial design's `LibNetworks.Timers` to `LibCommons.Timers` for broader reuse.
- [x] TimerQueue tests use short real-delay integration tests instead of an internal deterministic scheduler. `SessionIdleTracker` policy tests still use a fake timer queue and manual time source.
- [x] `RequestDisconnect(reason)` returns `bool` so policy code can record idle timeout cleanup only when it actually wins the disconnect race.

## Verification

```text
dotnet build FastPortSharp.sln -c Release
dotnet test FastPortSharp.sln -c Release --no-build
```

Result:

```text
build passed: 0 warnings, 0 errors
test passed: 130/130
```

## Recommendations

1. Proceed to report for local implementation completion because the match rate is above 90%.
2. Run cloud smoke/staged validation as the next operational validation step before claiming the stale-session production symptom is resolved.
3. Defer observed JSONL TimerQueue runtime counters unless cloud validation shows timer callback latency/failure visibility is needed.

## Next Steps

- [x] Local implementation complete.
- [x] Local build/test complete.
- [ ] Generate completion report: `$pdca report timer-queue`.
- [ ] Run cloud validation separately after commit or before final release.
