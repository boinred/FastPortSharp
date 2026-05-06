# timer-queue - Completion Report

> Date: 2026-05-06 | Status: Completed | Match Rate: 91%

---

## 1. Summary

`timer-queue` implemented a reusable application-wide timer queue and used it to add server-side idle/stale session cleanup for the smoke/load validation server.

The core scheduler is intentionally generic and lives in `LibCommons.Timers`. It can be registered as a DI singleton and reused by future heartbeat, retry, timeout, and cleanup policies. Session cleanup is implemented separately through `SessionIdleTracker`, so the timer queue itself remains free of session/socket/telemetry policy.

## 2. Related Documents

- Plan: `docs/01-plan/features/timer-queue.plan.md`
- Design: `docs/02-design/features/timer-queue.design.md`
- Implementation log: `docs/02-design/features/timer-queue.do.md`
- Analysis: `docs/03-analysis/timer-queue.analysis.md`

## 3. Completed Items

- Reusable `ITimerQueue` abstraction.
- `TimerQueue` one-shot scheduling.
- `TimerQueue` periodic scheduling.
- Timer cancellation/dispose behavior.
- Monotonic `StopwatchMonotonicTimeSource`.
- DI singleton wiring in `FastPortTestSmokeServer`.
- `BaseSession.LastReceivedTimestamp`.
- `BaseSession.RequestDisconnect(NetworkDisconnectReason reason)`.
- `NetworkDisconnectReason`.
- `SessionIdleTracker` with one periodic scan for all sessions.
- `FastPortTestSmokeClientSession` registration/unregistration.
- Idle timeout disconnect reason mapping.
- Server telemetry disconnect reason counters.
- Server observed idle timeout metrics.
- TimerQueue, BaseSession, SessionIdleTracker, telemetry, and observed metrics tests.

## 4. Quality Metrics

```text
Match rate: 91%
Build: passed, 0 warnings, 0 errors
Tests: passed, 130/130
```

Verification commands:

```text
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln -c Release --no-build
```

## 5. Key Decisions

### 5.1 Generic Scheduler

`TimerQueue` was implemented in `LibCommons.Timers`, not `LibNetworks`, so it can be used outside network session code.

### 5.2 DI Singleton Over Static Global

The official usage path is DI singleton registration. Static global access was not added because it would make lifecycle, testing, and replacement harder.

### 5.3 Policy Separation

`SessionIdleTracker` owns idle cleanup. `TimerQueue` only owns due ordering, periodic reschedule, cancellation, and worker lifetime.

### 5.4 Reason-Aware Disconnect

`RequestDisconnect()` remains compatible. `RequestDisconnect(NetworkDisconnectReason reason)` lets timeout policy and telemetry distinguish `idle-timeout` cleanup from socket faults and remote close.

## 6. Remaining Gaps

- Cloud smoke/staged/10K validation has not been run after implementation.
- TimerQueue runtime counters are local properties only and are not exported in observed JSONL.
- Production server projects are not wired to `ITimerQueue` yet.

## 7. Next Steps

1. Commit the local implementation.
2. Deploy to the cloud smoke server.
3. Run smoke validation first.
4. Run staged/10K validation.
5. Confirm `currentSessions = 0` and `pendingSendRequests = 0` within configured idle cleanup timeout after runner exit.
