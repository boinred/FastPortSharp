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
Tests: passed, 136/136
```

Verification commands:

```text
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln -c Release --no-build
```

Cloud validation update, 2026-05-09:

```text
Topology: Azure smoke server + local Docker Desktop runners
Server: ListenBacklog=10500, SessionIdleCleanup enabled
Idle cleanup: IdleTimeoutSeconds=90, ScanIntervalSeconds=5
Client load: 10 Docker containers x 1,000 sessions
Payload: random:128-2048
Pacing: fixed-window=1
Ramp-up: 120s
Duration: 3m
```

Cloud result:

| Metric | Result |
|--------|--------|
| Docker runner exits | 10/10 exit 0 |
| Client target sessions | 10,000 |
| Client final current sessions | 9,941 |
| Client connect failures | 34 |
| Client final TPS | 7,121.83 |
| Client average RTT P50 | 212.87 ms |
| Client average RTT P95 | 3,061.79 ms |
| Client average RTT P99 | 14,735.45 ms |
| Server accepted sessions | 9,964 |
| Server final current sessions | 0 |
| Server send backpressure | 0 |
| Server send rejected requests | 1 |
| Server idle timeout disconnects | 77 |
| Server remote-closed disconnects | 9,887 |
| Server max idle timeout age | 107,273 ms |

Artifacts:

- Client runner files: `artifacts/load-validation/timer-cleanup-docker-20260509-client-3/`
- Server metrics: `artifacts/load-validation/timer-cleanup-docker-20260509-client-3/server.metrics.jsonl`

The cloud test verified that sessions converge back to `currentSessions=0` after runner exit. A shorter 30s idle timeout was rejected during the same validation because it disconnected active Docker-ramp sessions too aggressively. The 90s timeout still produced 77 idle-timeout disconnects, so high-load validation should treat timeout configuration as part of the test condition.

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

- TimerQueue runtime counters are local properties only and are not exported in observed JSONL.
- Production server projects are not wired to `ITimerQueue` yet.
- A production idle-timeout default is not selected yet. The 2026-05-09 Docker run shows 30s is too aggressive for this test topology, while 90s is usable for cleanup validation but still affects a small number of sessions.

## 7. Next Steps

1. Commit the local implementation.
2. Choose separate idle cleanup defaults for smoke/load validation and production.
3. Keep Docker 10x1000 as the high-load cleanup validation path.
4. Confirm `currentSessions = 0` and `pendingSendRequests = 0` within configured idle cleanup timeout after future runner exits.
