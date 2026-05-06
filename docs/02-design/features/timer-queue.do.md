# timer-queue - Implementation Log

> Version: 1.0.0 | Date: 2026-05-06 | Status: Completed
> Design: docs/02-design/features/timer-queue.design.md

---

## 1. Scope Implemented

- Added reusable `LibCommons.Timers` scheduler primitives:
  - `ITimerQueue`
  - `ITimerQueueHandle`
  - `TimerQueue`
  - `TimerQueueOptions`
  - `IMonotonicTimeSource`
  - `StopwatchMonotonicTimeSource`
- Added reason-aware session disconnect support:
  - `NetworkDisconnectReason`
  - `BaseSession.RequestDisconnect(NetworkDisconnectReason reason)`
  - `BaseSession.LastReceivedTimestamp`
  - `BaseSession.MarkNetworkActivity()`
- Added smoke server idle cleanup consumer:
  - `SessionIdleTracker`
  - `SessionIdleTrackerOptions`
  - `IIdleTrackedSession`
- Wired `TimerQueue` as an app-wide DI singleton in `FastPortTestSmokeServer`.
- Added server telemetry fields for idle cleanup and disconnect reason classification.
- Added unit/integration tests for timer scheduling, idle scan, receive timestamp update, disconnect reason, and observed metric mapping.

## 2. Key Implementation Decisions

### 2.1 TimerQueue as General Service

`TimerQueue` was placed in `LibCommons.Timers`, not `LibNetworks`, so it can be reused outside networking code.

The official usage path is DI singleton:

```csharp
services.AddSingleton<IMonotonicTimeSource>(StopwatchMonotonicTimeSource.Instance);
services.AddSingleton(TimerQueueOptions.Default);
services.AddSingleton<TimerQueue>();
services.AddSingleton<ITimerQueue>(provider => provider.GetRequiredService<TimerQueue>());
```

Static global access was not added. This keeps lifetime ownership under the Host/DI container and makes tests/future replacement simpler.

### 2.2 Session Cleanup as Consumer

`SessionIdleTracker` consumes `ITimerQueue` and owns the session idle policy. `TimerQueue` does not know about sockets, sessions, or telemetry.

Runtime flow:

```text
FastPortTestSmokeClientSession.OnAccepted
-> SessionIdleTracker.Register(session)
-> BaseSession receive success updates LastReceivedTimestamp
-> TimerQueue periodic scan calls SessionIdleTracker.ScanExpired
-> idle timeout exceeded
-> session.RequestDisconnect(NetworkDisconnectReason.IdleTimeout)
-> telemetry records idle-timeout reason and idle cleanup count
```

### 2.3 Receive Timestamp Semantics

`LastReceivedTimestamp` updates on successful socket byte receive, not only completed packet parse. This avoids falsely treating a partial packet as idle while bytes are still arriving.

### 2.4 Disconnect Reason Compatibility

Existing callers can still call:

```csharp
RequestDisconnect()
```

New policy code can call:

```csharp
RequestDisconnect(NetworkDisconnectReason.IdleTimeout)
```

The old `OnNetworkSessionDisconnected()` hook remains compatible through the new reason-aware hook.

## 3. Verification

Commands run:

```text
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln -c Release --no-build
```

Results:

```text
build: passed, 0 warnings, 0 errors
test: passed, 130/130
```

## 4. Remaining Follow-up

- Cloud smoke/staged validation has not been run yet after this implementation.
- TimerQueue runtime counters are local properties only; they are not exported in observed JSONL yet.
- Production server projects are not wired to `ITimerQueue` yet. The first rollout is limited to `FastPortTestSmokeServer`.
