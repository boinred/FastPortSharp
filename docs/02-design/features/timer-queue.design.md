# timer-queue - Design Document

> Version: 1.0.0 | Date: 2026-05-06 | Status: Completed
> Level: Starter | Plan: docs/01-plan/features/timer-queue.plan.md

---

## 1. Overview

`timer-queue`는 FastPortSharp의 application-level timeout 처리를 중앙화하는 기능이다.

첫 적용 대상은 cloud load validation에서 확인된 stale/half-open server session cleanup이다. TCP keepalive는 kernel-level liveness hint로 유지하되, server가 원하는 시간 안에 idle session을 정리하는 책임은 application-level `TimerQueue`와 `SessionIdleTracker`가 담당한다.

설계의 핵심 원칙은 다음과 같다.

- `TimerQueue`는 범용 primitive이며 session 정책을 알지 않는다.
- `SessionIdleTracker`는 smoke/load validation server 정책이며 `TimerQueue`를 사용한다.
- `BaseSession`은 마지막 successful receive timestamp와 disconnect reason hook만 제공한다.
- per-session `System.Threading.Timer`는 만들지 않는다.
- idle cleanup은 `RequestDisconnect()`의 idempotency를 사용해서 race를 안전하게 흡수한다.

## 2. Current State

현재 관련 코드 흐름은 다음과 같다.

| Area | Current code | Notes |
|------|--------------|-------|
| Socket receive buffer | `LibNetworks/Sessions/BaseSession.cs` | 8 KiB `m_ReceivedSocketBuffers`로 `ReceiveAsync` 완료를 받는다. |
| Receive completion | `OnSocketEventsReceivedCompleted` | 성공 시 `m_ReceivedBuffers.Write(...)` 후 parser task를 깨우고 `RequestReceived()`를 다시 호출한다. |
| Packet parsing | `DoWorkReceivedBuffers` | completed packet 기준으로 `OnNetworkPacketReceived(basePacket)` hook을 호출한다. |
| Disconnect | `RequestDisconnect()` | 중복 호출 방지, send pending cleanup, socket shutdown/close, channel complete를 수행한다. |
| Keepalive | `SocketOptionName.KeepAlive` | OS probe에 의존하며 application timeout을 보장하지 않는다. |
| Server telemetry | `LibTestTelemetry` | current sessions, socket errors, send queue counters는 있으나 disconnect reason과 idle cleanup reason은 없다. |
| Session ownership | `BaseListener`/`IClientSessionFactory` | listener는 session manager를 아직 소유하지 않고 accepted session을 factory로 만든다. |

## 3. Requirements

### 3.1 Functional Requirements

- `TimerQueue`는 one-shot callback을 due time 순서대로 실행한다.
- `TimerQueue`는 periodic callback을 중복 실행 없이 반복한다.
- `TimerQueueHandle.Cancel()` 또는 `Dispose()` 이후 아직 due가 아닌 callback은 실행되지 않는다.
- `TimerQueue.DisposeAsync()` 이후 새 schedule은 실패하거나 명확한 exception을 던진다.
- `BaseSession`은 successful socket receive마다 마지막 receive timestamp를 갱신한다.
- `SessionIdleTracker`는 registered session을 periodic scan하고 idle timeout 초과 시 `RequestDisconnect(IdleTimeout)`를 호출한다.
- `SessionIdleTracker`는 disconnected session을 unregister해서 stale registry entry를 남기지 않는다.
- server observed metrics는 idle timeout disconnect를 fault socket error와 구분해서 노출한다.

### 3.2 Non-Functional Requirements

- 10K session path에서 per-session timer object를 만들지 않는다.
- receive hot path에서는 atomic timestamp write 이상을 하지 않는다.
- idle scan은 configurable interval로 수행하며 callback 안에서 long-running await를 하지 않는다.
- wall-clock jump 영향을 피하기 위해 monotonic timestamp를 사용한다.
- 기존 `RequestDisconnect()` caller와 override 호환성을 최대한 유지한다.

## 4. Architecture

### 4.1 Component Layout

```text
LibCommons
  Timers/
    TimerQueue.cs
    ITimerQueue.cs
    TimerQueueOptions.cs
    IMonotonicTimeSource.cs

LibNetworks
  Sessions/
    NetworkDisconnectReason.cs
    BaseSession.cs

FastPortTestSmokeServer
  Sessions/
    SessionIdleTracker.cs
    SessionIdleTrackerOptions.cs
    FastPortTestSmokeClientSession.cs
    FastPortTestSmokeClientSessionFactory.cs

LibTestTelemetry
  ServerTelemetry.cs
  ObservedMetrics.cs
```

### 4.2 Runtime Flow

```text
server start
-> DI creates app-wide TimerQueue singleton
-> DI creates SessionIdleTracker singleton
-> SessionIdleTracker schedules one periodic scan
-> listener accepts socket
-> factory creates FastPortTestSmokeClientSession
-> session.OnAccepted registers itself with SessionIdleTracker
-> BaseSession successful receive updates LastReceivedTimestamp
-> periodic scan reads registered session timestamps
-> idle age > configured timeout
-> session.RequestDisconnect(NetworkDisconnectReason.IdleTimeout)
-> telemetry records disconnect reason and idle timeout count
-> session.OnDisconnected unregisters itself
```

### 4.3 Ownership Boundaries

| Component | Owns | Does not own |
|-----------|------|--------------|
| `TimerQueue` | due ordering, cancel/dispose, periodic reschedule | session list, socket cleanup, telemetry policy |
| `BaseSession` | socket receive timestamp, disconnect idempotency, disconnect reason hook | session registry, timeout policy |
| `SessionIdleTracker` | registered sessions, idle timeout decision, periodic scan | socket internals, packet parsing |
| `ServerTelemetryCollector` | reason counters and observed snapshot fields | timer scheduling |

## 5. TimerQueue Design

### 5.1 Public API

```csharp
namespace LibCommons.Timers;

public interface ITimerQueue : IDisposable, IAsyncDisposable
{
    ITimerQueueHandle Schedule(TimeSpan delay, Action callback);

    ITimerQueueHandle SchedulePeriodic(TimeSpan interval, Action callback);
}

public interface ITimerQueueHandle : IDisposable
{
    bool IsCanceled { get; }

    bool Cancel();
}

public sealed class TimerQueueOptions
{
    public int MaxCallbacksPerWake { get; init; } = 1024;
}
```

API notes:

- `Action` callback만 허용한다. Timer callback이 오래 걸리면 전체 due processing이 지연되므로 async work는 callback 밖에서 별도 worker로 넘긴다.
- `Schedule(TimeSpan.Zero, callback)`은 가능한 한 다음 worker wake에서 실행한다.
- negative delay/interval은 `ArgumentOutOfRangeException`으로 거부한다.
- periodic timer는 callback이 끝난 뒤 `now + interval`로 다음 due를 잡는다. 밀린 tick을 catch-up 실행하지 않는다.
- callback exception은 queue worker를 죽이지 않고 failure counter/hook으로 기록한다.

### 5.2 Internal Scheduler

`TimerQueue`는 내부적으로 due timestamp 기준 min-heap을 사용한다.

```text
PriorityQueue<TimerQueueEntry, long dueTimestamp>
```

Entry shape:

```text
TimerQueueEntry
- long Id
- long DueTimestamp
- TimeSpan? Period
- Action Callback
- int IsCanceled
- int IsRunning
```

Synchronization:

- heap mutation은 single lock으로 보호한다.
- 새 timer가 현재 earliest due보다 빠르면 worker를 깨운다.
- cancel은 handle state를 canceled로 바꾸고 lazy removal을 사용한다.
- worker는 due entry를 pop할 때 canceled entry를 버린다.

### 5.3 Time Source

Production은 monotonic source를 사용한다.

```csharp
public interface IMonotonicTimeSource
{
    long GetTimestamp();

    TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp);

    long Add(long timestamp, TimeSpan delay);

    TimeSpan GetDelay(long nowTimestamp, long dueTimestamp);
}
```

`StopwatchMonotonicTimeSource`는 `Stopwatch.GetTimestamp()`와 `Stopwatch.Frequency`를 사용한다.

테스트는 짧은 실제 delay integration test로 due ordering/cancel/periodic reschedule을 검증한다. `SessionIdleTracker` 정책 테스트는 fake `ITimerQueue`와 manual monotonic time source를 사용한다.

### 5.4 Worker Policy

- worker는 due entry가 없으면 signal을 기다린다.
- next due가 있으면 `Task.Delay(delay, cancellationToken)`와 schedule signal 중 먼저 오는 이벤트를 기다린다.
- 한 번 깨어났을 때 최대 `MaxCallbacksPerWake`개까지 처리한다.
- callback은 순차 실행한다.
- periodic callback은 callback 성공/실패와 관계없이 canceled가 아니면 다음 due로 재등록한다.
- dispose 시 worker cancellation 후 heap을 비우고 대기 중 callback은 실행하지 않는다.

## 6. BaseSession Changes

### 6.1 Last Receive Timestamp

`BaseSession`에 successful socket receive timestamp를 추가한다.

```csharp
private long m_LastReceivedTimestamp;

public long LastReceivedTimestamp => Volatile.Read(ref m_LastReceivedTimestamp);

protected virtual long GetNetworkTimestamp() => Stopwatch.GetTimestamp();
```

Update point:

```text
OnSocketEventsReceivedCompleted
-> e.SocketError == Success
-> e.BytesTransferred > 0
-> buffer != null
-> write receive buffers
-> UpdateLastReceivedTimestamp()
-> OnNetworkBytesReceived(e.BytesTransferred)
-> RequestReceived()
```

Important detail:

- timestamp는 completed packet이 아니라 successful socket bytes 기준이다.
- partial packet만 받고 멈춘 client도 idle 기준에서 최근 activity로 취급된다.
- constructor 또는 `OnAccepted()` 시점에 initial timestamp를 설정해서 방금 accepted된 idle session이 즉시 expired 되지 않게 한다.

### 6.2 Disconnect Reason

기존 caller 호환을 위해 overload를 추가한다.

```csharp
public bool RequestDisconnect()
{
    return RequestDisconnect(NetworkDisconnectReason.Unknown);
}

public bool RequestDisconnect(NetworkDisconnectReason reason)
{
    ...
    OnNetworkSessionDisconnected(reason);
    ...
    return true;
}

protected virtual void OnNetworkSessionDisconnected(NetworkDisconnectReason reason)
{
    OnNetworkSessionDisconnected();
}
```

`NetworkDisconnectReason` initial set:

```csharp
public enum NetworkDisconnectReason
{
    Unknown = 0,
    RemoteClosed = 1,
    ReceiveSocketError = 2,
    ReceiveRequestError = 3,
    SendSocketError = 4,
    SendZeroBytes = 5,
    IdleTimeout = 6,
    LocalShutdown = 7
}
```

First implementation must at least use `IdleTimeout` for timer cleanup. Existing disconnect paths can remain `Unknown` initially if broad reason mapping would make the change too large. However, receive zero bytes and send socket errors should be mapped if the call site change is straightforward.

## 7. SessionIdleTracker Design

### 7.1 API

```csharp
public sealed class SessionIdleTracker : IAsyncDisposable
{
    public void Register(FastPortTestSmokeClientSession session);

    public void Unregister(long sessionId);

    public int ScanExpired();
}

public sealed class SessionIdleTrackerOptions
{
    public bool Enabled { get; init; } = false;

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan ScanInterval { get; init; } = TimeSpan.FromSeconds(5);
}
```

### 7.2 Registry

`SessionIdleTracker` uses:

```text
ConcurrentDictionary<long, TrackedSession>
```

Tracked fields:

```text
TrackedSession
- long SessionId
- FastPortTestSmokeClientSession Session
- long RegisteredTimestamp
```

The tracker reads `Session.LastReceivedTimestamp` during scan. It does not need to receive every activity event unless later measurements show dictionary lookup on receive is acceptable and needed.

### 7.3 Scan Algorithm

```text
now = timeSource.GetTimestamp()
foreach tracked session:
    if session.IsDisconnected:
        unregister
        continue

    last = session.LastReceivedTimestamp
    idleAge = timeSource.GetElapsedTime(last, now)
    if idleAge <= options.IdleTimeout:
        continue

    telemetry.RecordIdleTimeoutDisconnectCandidate(idleAge)
    session.RequestDisconnect(NetworkDisconnectReason.IdleTimeout)
```

Race handling:

- If receive updates timestamp while scan is reading, either old or new timestamp is observed. A false positive is still possible at the exact boundary, so timeout should include enough grace for load scenarios.
- `RequestDisconnect` idempotency prevents duplicate cleanup.
- `OnDisconnected` unregisters the session even if scan already removed it.

### 7.4 Configuration

FastPortTestSmokeServer configuration section:

```json
{
  "SessionIdleCleanup": {
    "Enabled": true,
    "IdleTimeoutSeconds": 120,
    "ScanIntervalSeconds": 5
  }
}
```

Default recommendation:

- Library default: disabled unless explicitly wired.
- Smoke/cloud validation server: enabled by config.
- High-load cloud 10K first pass: `IdleTimeoutSeconds = 120`, `ScanIntervalSeconds = 5`.
- Short smoke cleanup test: override to `IdleTimeoutSeconds = 5` or lower only for targeted cleanup validation.

## 8. Telemetry Design

### 8.1 ServerTelemetry API

Add reason-aware methods without removing current methods.

```csharp
void RecordSessionDisconnected(string reason);

void RecordIdleTimeoutDisconnect(TimeSpan idleAge);
```

Existing `RecordSessionDisconnected()` remains and delegates to reason `"unknown"`.

### 8.2 Snapshot Fields

Add to `ServerTelemetrySnapshot` and `ServerObservedMetricsSnapshot`:

```text
DisconnectCountsByReason
IdleTimeoutDisconnects
MaxIdleTimeoutAgeMs
```

Observed metrics JSON uses camelCase:

```json
{
  "serverObserved": {
    "disconnectCountsByReason": {
      "idle-timeout": 1
    },
    "idleTimeoutDisconnects": 1,
    "maxIdleTimeoutAgeMs": 610000
  }
}
```

### 8.3 Reason Naming

Use stable lower-kebab strings in telemetry:

| Enum | Telemetry reason |
|------|------------------|
| `Unknown` | `unknown` |
| `RemoteClosed` | `remote-closed` |
| `ReceiveSocketError` | `receive-socket-error` |
| `ReceiveRequestError` | `receive-request-error` |
| `SendSocketError` | `send-socket-error` |
| `SendZeroBytes` | `send-zero-bytes` |
| `IdleTimeout` | `idle-timeout` |
| `LocalShutdown` | `local-shutdown` |

## 9. Implementation Order

1. Add `NetworkDisconnectReason` and `RequestDisconnect(reason)` compatibility overload.
2. Add `BaseSession.LastReceivedTimestamp` and update it on successful socket receive.
3. Add `OnNetworkBytesReceived(int bytes)` hook only if tests or tracker wiring require an explicit event; otherwise tracker reads timestamp directly.
4. Add `LibCommons.Timers` primitive classes and TimerQueue tests.
5. Add `SessionIdleTracker` and options in `FastPortTestSmokeServer`.
6. Wire tracker into DI and `FastPortTestSmokeClientSession.OnAccepted`/`OnDisconnected`.
7. Add reason-aware telemetry counters and observed JSON fields.
8. Add smoke/integration tests for idle cleanup and no false disconnect while active.
9. Update load-validation docs/runbook with cleanup acceptance checks.
10. Run local build/tests, then cloud smoke/staged validation.

## 10. Test Plan

### 10.1 Unit Tests

TimerQueue:

- `TimerQueue_Schedule_ExecutesOneShotTimersInDueOrder`
- `TimerQueue_Cancel_PreventsPendingCallback`
- `TimerQueue_SchedulePeriodic_RepeatsUntilCanceled`
- `TimerQueue_DisposeAsync_PreventsPendingCallbacks`
- `TimerQueue_CallbackException_DoesNotStopWorker`

BaseSession:

- `BaseSession_SuccessfulReceive_UpdatesLastReceivedTimestamp`
- `BaseSession_RequestDisconnect_WithReason_RecordsDisconnectReason`
- `BaseSession_RequestDisconnect_IsStillIdempotentWithReason`

SessionIdleTracker:

- `SessionIdleTracker_ScanExpired_DisconnectsIdleSession`
- `SessionIdleTracker_ScanExpired_DoesNotDisconnectActiveSession`
- `SessionIdleTracker_Unregister_RemovesDisconnectedSession`

Telemetry:

- `ServerTelemetryCollector_DisconnectReason_TracksIdleTimeout`
- `ServerObservedMetricsSnapshot_MapsDisconnectReasonCounters`
- `ObservedMetricsJson_SerializesIdleFields`

### 10.2 Local Validation

```text
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln -c Release --no-build
```

### 10.3 Cloud Validation

Smoke cleanup validation:

```text
FASTPORT_RUNNER_MODE=local FASTPORT_ENDPOINT_TYPE=public-ip FASTPORT_SERVER_HOST=40.82.153.1 FASTPORT_SERVER_PORT=6628 scripts/cloud/runner-smoke.sh
```

Focused/staged validation:

```text
FASTPORT_RUNNER_MODE=local FASTPORT_ENDPOINT_TYPE=public-ip FASTPORT_SERVER_HOST=40.82.153.1 FASTPORT_SERVER_PORT=6628 scripts/cloud/runner-10k.sh
```

Acceptance:

```text
currentSessions == 0 within configured idle cleanup timeout
pendingSendRequests == 0
socketErrorCount does not increase because of idle cleanup itself
disconnectCountsByReason["idle-timeout"] explains timer-driven cleanup
timerQueueCallbackFailures == 0
```

## 11. Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| Idle timeout false positive under high RTT | High | Use conservative default timeout, validate with 10K cloud run, and only lower timeout in targeted smoke cleanup tests. |
| Idle test clients stop sending application packets while sessions are still established | Medium | Keep heartbeat as a client/protocol-level feature; FastPortTestLoadRunner sends a lightweight heartbeat every `30s` by default when no client packet was written. |
| Timer callback blocks the queue | Medium | Keep `Action` callbacks short and move expensive work outside timer queue. Track max callback lateness. |
| Heap lock contention under high schedule/cancel rate | Medium | Idle cleanup uses one periodic timer, not per-session timers. TimerQueue hot path remains outside packet receive. |
| Telemetry field growth breaks old readers | Low | Add optional/defaulted record fields and keep existing names unchanged. |
| BaseSession API break | Medium | Keep `RequestDisconnect()` and old `OnNetworkSessionDisconnected()` compatibility path. |
| Session registry leaks disconnected sessions | Medium | Unregister in `OnDisconnected` and prune disconnected sessions during each scan. |

## 12. Open Questions

- Idle timeout is based on successful receive bytes. Application-level heartbeat should be implemented by clients/protocols, and received heartbeat packets naturally refresh the same activity timestamp.
- Should `BaseListener.RequestShutdown()` pass `LocalShutdown` to active sessions once a session manager exists?
- Should TimerQueue runtime counters be exported later, or are local `ExecutedCallbackCount`/`FailedCallbackCount` properties sufficient for now?
- Should production server projects use this tracker immediately, or should first rollout stay limited to smoke/load validation server?

## 13. Next Phase

Recommended next command:

```text
$pdca do timer-queue
```
