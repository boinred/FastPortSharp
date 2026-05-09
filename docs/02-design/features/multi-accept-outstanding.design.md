# multi-accept-outstanding - Design Document

> Version: 1.0.0 | Date: 2026-05-09 | Status: Completed
> Level: Starter | Plan: docs/01-plan/features/multi-accept-outstanding.plan.md

---

## 1. Overview

`multi-accept-outstanding`는 `LibNetworks.BaseListener`가 동시에 여러 `Socket.AcceptAsync` 요청을 outstanding 상태로 유지할 수 있도록 하는 소규모 listener 개선이다.

현재 구현은 listener 전체에서 하나의 `SocketAsyncEventArgs`인 `m_SocketEvent`를 accept에 재사용한다. 따라서 accept completion callback이 session 생성, telemetry hook, per-accept log, `Task.Run(clientSession.OnAccepted)` scheduling을 끝낸 뒤에야 다음 accept가 repost된다. 10K ramp-up처럼 connect가 짧은 시간에 몰리는 테스트에서는 이 단일 outstanding accept 구조가 backlog를 충분히 키운 뒤에도 일부 connect failure나 accept tail을 만들 수 있다.

이번 설계는 기본 동작을 바꾸지 않는다. default outstanding accept count는 `1`로 유지하고, smoke/cloud 검증에서만 설정으로 `2`, `4`, `8`을 비교할 수 있게 만든다. 성능 이득이 명확하지 않으면 default를 올리지 않는다.

## 2. Current State

현재 listener 시작 흐름:

```text
BaseListener.StartAccept(ip, port)
-> StartAccept(ip, port, C_DefaultListenBacklog)

BaseListener.StartAccept(ip, port, backlog)
-> Bind(endPoint)
-> Listen(normalizedBacklog)
-> m_SocketEvent.Completed += OnSocketEventsAcceptCompleted
-> Accept(m_SocketEvent)
```

현재 accept completion 흐름:

```text
OnSocketEventsAcceptCompleted(args)
-> capture acceptCompletedTimestamp
-> if SocketError != Success: record error and return
-> if AcceptSocket == null: record error and return
-> OnAcceptSucceeded(clientSocket)
-> log endpoint
-> m_ClientSessionFactory.Create(clientSocket)
-> OnAcceptSessionCreated(...)
-> Task.Run(clientSession.OnAccepted)
-> Accept(m_SocketEvent)
```

제약:

| Constraint | Impact |
|------------|--------|
| accept args가 하나뿐임 | 동시에 outstanding인 accept도 하나뿐임 |
| failure/null socket에서 repost 없이 return | 일시적 completion failure가 accept loop 중단으로 이어질 수 있음 |
| repost가 callback 끝에 위치 | session 생성/log/Task scheduling 비용만큼 다음 accept가 늦어짐 |
| `m_SocketEvent`가 `BaseSocket`에 있음 | listener accept event와 session socket event 책임이 흐림 |

## 3. Requirements

### 3.1 Functional Requirements

- `BaseListener`는 outstanding accept count를 caller가 지정할 수 있어야 한다.
- 기존 `StartAccept(string ip, int port)` 호출자는 기존처럼 동작해야 한다.
- 기존 `StartAccept(string ip, int port, int backlog)` 호출자는 기존처럼 동작해야 한다.
- default outstanding accept count는 `1`이어야 한다.
- invalid/zero/negative outstanding accept count는 `1`로 normalize한다.
- 너무 큰 outstanding accept count는 안전한 상한으로 clamp한다.
- 각 `SocketAsyncEventArgs`는 동시에 하나의 `AcceptAsync`에만 사용해야 한다.
- accept completion 성공 후 같은 args를 재사용해서 repost해야 한다.
- accept completion failure/null socket 후에도 listener가 running이면 같은 args를 repost해야 한다.
- shutdown이 시작되면 새 accept repost를 하지 않아야 한다.
- smoke server는 `OutstandingAccepts` 설정을 읽고 listener에 전달해야 한다.

### 3.2 Non-Functional Requirements

- 변경은 `BaseListener` accept pump와 smoke server 설정 surface로 제한한다.
- send/receive packet path는 변경하지 않는다.
- session lifecycle manager를 새로 만들지 않는다.
- `Task.Run(clientSession.OnAccepted)` 구조는 이번 feature에서 유지한다.
- telemetry contract를 새로 만들지 않는다.
- 기존 accept telemetry hook 호출 순서는 성공 경로에서 유지한다.
- per-accept log는 새로 늘리지 않는다.

## 4. Architecture

### 4.1 Component Boundary

```text
LibNetworks
  BaseListener
    - owns listener socket bind/listen/accept pump
    - owns accept-only SocketAsyncEventArgs collection
    - normalizes backlog and outstanding accept count
    - reposts accept args while listener is running
    - exposes existing telemetry hooks

  BaseSocket
    - keeps base socket ownership
    - existing m_SocketEvent remains for compatibility unless removed in a separate cleanup

FastPortTestSmokeServer
  FastPortTestSmokeServerOptions
    - adds DefaultOutstandingAccepts
    - adds OutstandingAccepts

  Program
    - reads FastPortTestSmokeServer:OutstandingAccepts
    - normalizes invalid values through smoke server defaults

  FastPortTestSmokeServerBackgroundService
    - logs OutstandingAccepts
    - calls StartAccept(host, port, listenBacklog, outstandingAccepts)

Validation
  Docker/cloud runner
    - compares outstandingAccepts 1, 2, 4, 8
```

### 4.2 Default and Bounds

Recommended constants:

```csharp
// 목적: 기존 단일 accept pump 동작 보존
private const int C_DefaultOutstandingAccepts = 1;

// 목적: 잘못된 설정으로 accept args/callback이 과도하게 늘어나는 상황 방지
private const int C_MaxOutstandingAccepts = 64;
```

Smoke server defaults:

```csharp
public const int DefaultOutstandingAccepts = 1;
public int OutstandingAccepts { get; init; } = DefaultOutstandingAccepts;
```

Test values:

```text
1, 2, 4, 8
```

`16+`은 이번 검증의 기본 실험값에 넣지 않는다. 10K 테스트에서 `4` 또는 `8`로 충분한 개선이 없으면 accept outstanding만의 문제가 아닐 가능성이 높다.

## 5. Public API and Configuration

### 5.1 BaseListener API

Existing overloads remain:

```csharp
public bool StartAccept(string ip, int port);

public bool StartAccept(string ip, int port, int backlog);
```

New overload:

```csharp
public bool StartAccept(string ip, int port, int backlog, int outstandingAccepts);
```

Delegation:

```csharp
public bool StartAccept(string ip, int port)
{
    return StartAccept(ip, port, C_DefaultListenBacklog, C_DefaultOutstandingAccepts);
}

public bool StartAccept(string ip, int port, int backlog)
{
    return StartAccept(ip, port, backlog, C_DefaultOutstandingAccepts);
}
```

### 5.2 Smoke Server Configuration

`FastPortTestSmokeServer/appsettings.json`:

```json
{
  "FastPortTestSmokeServer": {
    "Host": "0.0.0.0",
    "Port": 6628,
    "ListenBacklog": 4096,
    "OutstandingAccepts": 1
  }
}
```

Environment override:

```text
FastPortTestSmokeServer__OutstandingAccepts=4
```

The legacy section fallback remains through `FastPortTestSmokeServerConfiguration.GetServerSection`.

## 6. Implementation Design

### 6.1 Accept Args Ownership

`BaseListener` should own accept-only args rather than relying on inherited `m_SocketEvent` for the listener accept pump.

Candidate field:

```csharp
// 상태: listener accept pump 전용 SocketAsyncEventArgs 목록
private SocketAsyncEventArgs[] m_AcceptSocketEvents = Array.Empty<SocketAsyncEventArgs>();
```

Creation:

```csharp
private SocketAsyncEventArgs[] CreateAcceptSocketEvents(int outstandingAccepts)
{
    var acceptSocketEvents = new SocketAsyncEventArgs[outstandingAccepts];
    for (int index = 0; index < acceptSocketEvents.Length; index++)
    {
        var acceptArgs = new SocketAsyncEventArgs();
        acceptArgs.Completed += OnSocketEventsAcceptCompleted;
        acceptSocketEvents[index] = acceptArgs;
    }

    return acceptSocketEvents;
}
```

The inherited `m_SocketEvent` can remain untouched in this feature. Removing it from `BaseSocket` is a broader cleanup and not required for functional correctness.

### 6.2 Startup Flow

New startup flow:

```text
StartAccept(ip, port, backlog, outstandingAccepts)
-> validate endpoint
-> normalize backlog
-> normalize outstandingAccepts
-> Bind
-> Listen
-> create accept args array
-> post AcceptAsync for each args
-> return true only if startup postings succeed
```

Startup failure policy:

- If endpoint validation fails, preserve existing `start-endpoint` failure behavior.
- If bind/listen fails, preserve existing `start-bind-listen` failure behavior.
- If posting accept fails during startup, record `accept-start`, call `RequestShutdown`, and return `false`.
- If synchronous completion happens during startup, `Accept` handles it through the same completion callback.

### 6.3 Repost Flow

Completion handler should always consider repost in a `finally` style path.

```text
OnSocketEventsAcceptCompleted(args)
-> capture timestamp
-> process success/error/null socket
-> if listener is running: Accept(args)
```

Key rule:

```text
Every accept args that completes must either be reposted while running or retired during shutdown.
```

This avoids the current behavior where a single `SocketError` or null socket can end the accept pump.

### 6.4 Completion Success Path

Success path keeps existing telemetry hook order:

```text
OnAcceptSucceeded(clientSocket)
log endpoint
m_ClientSessionFactory.Create(clientSocket)
OnAcceptSessionCreated(...)
Task.Run(...)
OnAcceptSessionTaskStarted(...)
clientSession.OnAccepted()
```

No new telemetry hook is required for this feature. Existing accept path telemetry remains enough to compare `accept-to-session-created`, `accept-to-OnAccepted-started`, and first receive path.

### 6.5 Completion Error Path

`SocketError != Success`:

```text
OnAcceptFailed("accept-completion", args.SocketError, null)
OnListenerSocketError("accept-completion", args.SocketError, null)
log error
repost same args if running
```

`AcceptSocket == null`:

```text
OnAcceptFailed("accept-completion-null-socket", null, null)
log error
repost same args if running
```

Shutdown race:

```text
if listener is not running, do not repost.
```

If `AcceptAsync` throws after shutdown, `Accept` should record/log through existing hooks and return `false`; no retry loop should be forced.

### 6.6 Normalization

Backlog normalization already exists. Outstanding count should be separately normalized.

```csharp
internal static int NormalizeOutstandingAccepts(int outstandingAccepts)
{
    if (outstandingAccepts <= 0)
    {
        return C_DefaultOutstandingAccepts;
    }

    return Math.Min(outstandingAccepts, C_MaxOutstandingAccepts);
}
```

`internal` is preferred over `private` because `LibNetworks` already exposes internals to `FastPortTests` through `InternalsVisibleTo`.

### 6.7 Shutdown

Existing `RequestShutdown` sets `m_bIsRunning` false and calls `RequestDisconnect`.

Design rule:

- completion handler checks running state before repost.
- `Accept` checks running state before calling `m_Socket.AcceptAsync`.
- disposing individual accept args is optional in this small change; if added, it should happen after socket shutdown and must avoid disposing args that may still be completing.

This design intentionally avoids complex per-args state machines unless tests show shutdown races.

## 7. Test Design

### 7.1 Unit Tests

Add tests in `FastPortTests`:

| Test | Purpose |
|------|---------|
| `BaseListener_NormalizeOutstandingAccepts_UsesDefaultForInvalidValues` | `0`, negative values become `1` |
| `BaseListener_NormalizeOutstandingAccepts_ClampsLargeValues` | value above max becomes max |
| `FastPortTestSmokeServerOptions_DefaultOutstandingAccepts_IsOne` | smoke server default preserves current behavior |
| `FastPortTestSmokeServerConfiguration_ReadsOutstandingAccepts` | config/env style value reaches options |

If direct listener accept count testing is too invasive, keep unit tests focused on normalization/config and rely on smoke/cloud validation for concurrent accept behavior.

### 7.2 Integration Tests

Existing smoke server tests should continue passing. If feasible, add a small integration test that starts a listener with `outstandingAccepts=2` and verifies multiple clients can connect and disconnect.

Avoid brittle assertions on OS-level backlog or exact accept concurrency. The goal is to prove no regression in listener startup and basic connection handling.

### 7.3 Validation Tests

Local verification:

```text
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln -c Release
```

Cloud/Docker validation matrix:

| Variant | ListenBacklog | OutstandingAccepts | Purpose |
|---------|---------------|--------------------|---------|
| baseline | 10500 | 1 | current behavior |
| test-a | 10500 | 2 | low-risk multi-accept |
| test-b | 10500 | 4 | expected practical ceiling |
| test-c | 10500 | 8 | stress comparison if needed |

Runner condition:

```text
sessions: 10 containers x 1000 sessions
payload: random:128-2048
rate: 1000
pacing-policy: fixed-window
pacing-fixed-window: 1
ramp-up: 120s
duration: 3m
```

Metrics:

- connect success/failure
- server accepted/disconnected
- accept error count
- accept-to-session-created
- accept-to-OnAccepted-started
- first socket receive latency
- RTT P50/P95/P99
- sendBackpressure/sendRejected
- server CPU/load if available

## 8. Implementation Order

1. Add `OutstandingAccepts` defaults and config parsing to `FastPortTestSmokeServerOptions` and `Program.cs`.
2. Add `StartAccept(ip, port, backlog, outstandingAccepts)` overload to `BaseListener`.
3. Add outstanding accept normalization and tests.
4. Move listener accept pump from inherited single `m_SocketEvent` usage to `BaseListener` owned accept args array.
5. Rework completion handler to repost the same args when listener is running.
6. Update background service log and `StartAccept` call.
7. Run local build/test.
8. Run Docker/cloud validation with `OutstandingAccepts=1`, then `2` or `4`.
9. Document results in benchmark/report docs.

## 9. Decision Criteria

Default should remain `1` unless validation shows clear benefit.

Suggested promotion criteria:

| Condition | Action |
|-----------|--------|
| `2` or `4` lowers connect failures without RTT/send regression | consider smoke default increase |
| connect failures unchanged | leave default `1`; investigate client/NAT/OS/network tail |
| accept path latency improves but RTT tail does not | keep setting available; do not claim gameplay improvement |
| sendBackpressure or CPU spikes appear | revert default to `1`, keep design as optional experiment only |

Current expected implementation default:

```text
OutstandingAccepts = 1
```

Expected cloud experiment value:

```text
OutstandingAccepts = 4
```

## 10. Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| `SocketAsyncEventArgs` reused while still outstanding | High | Same args repost only after completion; no shared args across outstanding accepts |
| accept error retires one args permanently | Medium | Completion handler reposts on error while running |
| shutdown race logs noisy errors | Medium | Check running before repost and before `AcceptAsync` |
| default behavior changes unexpectedly | High | Default remains `1`; existing overloads delegate to `1` |
| config typo creates too many accepts | Medium | Normalize and clamp to `C_MaxOutstandingAccepts` |
| multi-accept hides real bottleneck | Medium | Compare accept path metrics, RTT, send pressure together |

## 11. References

- `docs/01-plan/features/multi-accept-outstanding.plan.md`
- `LibNetworks/BaseListener.cs`
- `LibNetworks/BaseSocket.cs`
- `FastPortTestSmokeServer/Program.cs`
- `FastPortTestSmokeServer/FastPortTestSmokeServerBackgroundService.cs`
- `FastPortTestSmokeServer/FastPortTestSmokeServerOptions.cs`
- `FastPortTestSmokeServer/appsettings.json`
- `LibNetworks/Properties/AssemblyInfo.cs`
