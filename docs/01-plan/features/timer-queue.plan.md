# timer-queue - Plan Document

> Version: 1.0.0 | Date: 2026-05-06 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`timer-queue`는 FastPortSharp 서버가 세션 idle timeout, heartbeat timeout, future delayed/repeating work를 한 곳에서 관리할 수 있게 만드는 기능이다.

현재 서버는 socket receive/send completion과 TCP keepalive에 의존해서 세션 종료를 감지한다. 하지만 cloud load validation에서 runner 종료 이후에도 server-side `currentSessions`가 남는 half-open/stale session 상태가 확인되었다. TCP keepalive는 OS/kernel 정책과 probe 주기에 의존하므로, application이 원하는 시간 안에 stale session을 정리한다는 보장을 주지 않는다.

이번 feature의 목표는 `BaseSession` receive path에서 마지막 activity 시각을 갱신하고, 중앙 `TimerQueue`가 주기적으로 stale session을 검사해서 안전하게 `RequestDisconnect()`를 호출할 수 있는 기반을 마련하는 것이다.

### 1.2 Background

현재 코드 기준의 관련 상태는 다음과 같다.

| Area | Current behavior | Gap |
|------|------------------|-----|
| `BaseSession` receive | socket receive completion 후 `m_ReceivedBuffers`에 append하고 packet parsing worker를 깨운다. | 마지막 수신 시각을 세션 lifecycle policy로 사용하지 않는다. |
| `BaseSession.RequestDisconnect()` | disconnect 중복 방지, send queue cleanup, socket shutdown/close를 수행한다. | stale/idle session을 시간 기준으로 호출하는 scheduler가 없다. |
| TCP keepalive | `SocketOptionName.KeepAlive`가 켜져 있고 `Socket+Extensions.SetKeepAlive` helper가 있다. | keepalive는 빠른 application-level cleanup 또는 load-test 기준의 결정적 timeout으로 보기 어렵다. |
| Telemetry | connected/disconnected/current sessions, socket errors, send queue counters가 있다. | idle timeout disconnect reason, session age, timer queue latency/callback count가 없다. |
| Cloud validation | runner 종료 후 `currentSessions`가 즉시 0이 되지 않는 사례가 있었다. | stale session cleanup 시간을 검증 가능한 지표로 만들 필요가 있다. |

TimerQueue가 없으면 heartbeat/idle cleanup을 넣을 때 각 세션마다 `Timer`를 만들거나 receive path에서 직접 시간을 검사하는 구조로 흐르기 쉽다. 이는 10K 세션에서 timer object 수, callback storm, lock contention, false disconnect 위험을 키운다.

## 2. Goals

### 2.1 Primary Goals

- [ ] 중앙 `TimerQueue` abstraction을 추가한다.
- [ ] one-shot timer, periodic timer, cancel/dispose semantics를 정의한다.
- [ ] monotonic time source를 사용해서 wall-clock jump 영향을 피한다.
- [ ] 10K 세션에서도 per-session `System.Threading.Timer`를 만들지 않는 구조를 설계한다.
- [ ] `BaseSession` 또는 session lifecycle layer가 마지막 packet receive/activity 시각을 기록할 수 있게 한다.
- [ ] `TimerQueue` 기반 stale session scanner가 configured idle timeout 초과 세션에 `RequestDisconnect()`를 호출할 수 있게 한다.
- [ ] idle timeout disconnect reason과 timer execution latency를 telemetry/export에 반영할 수 있는 확장 지점을 마련한다.
- [ ] cloud load validation에서 runner 종료 후 server `currentSessions`가 configured timeout 안에 0으로 수렴하는지 검증한다.

### 2.2 Non-Goals

- TCP keepalive를 제거하지 않는다.
- send queue/channel 구조를 변경하지 않는다.
- packet parser 또는 `BasePacket` payload ownership을 변경하지 않는다.
- adaptive client pacing/window 정책을 변경하지 않는다.
- client reconnect policy를 이번 feature의 필수 구현 범위에 넣지 않는다.
- 범용 cron/job scheduler를 만들지 않는다.
- long-running callback 실행 framework를 만들지 않는다.

## 3. Scope

### 3.1 In Scope

- `LibNetworks` 또는 `LibCommons`
  - `TimerQueue` 위치 결정
  - timer handle/cancel/dispose contract 정의
  - monotonic time provider abstraction 검토
  - unit-test 가능한 virtual/fake clock 전략 설계
- `LibNetworks/Sessions/BaseSession.cs`
  - last activity 또는 last received packet timestamp 기록 지점 검토
  - disconnect idempotency와 timer callback race 검토
  - stale session disconnect hook/event shape 검토
- `FastPortTestSmokeServer`
  - idle timeout 설정 주입 후보 검토
  - server metrics export에 idle disconnect reason/counter 추가 후보 검토
- `FastPortTests`
  - timer ordering/cancel/periodic/dispose tests
  - stale session cleanup tests
  - no false disconnect while packets continue to arrive test
- `scripts/cloud` and validation docs
  - cloud server pre-run/post-run `currentSessions=0` 확인 절차 유지
  - stale cleanup timeout 검증 항목 추가

### 3.2 Out of Scope

- load runner scenario rewrite
- Azure/OCI provisioning changes
- `SocketAsyncEventArgs` receive/send loop rewrite
- receive buffer size tuning
- packet assembly copy optimization
- production observability backend integration

## 4. Candidate Architecture

### 4.1 Preferred Direction

첫 구현은 central priority queue 기반 `TimerQueue`로 둔다.

```text
TimerQueue
- Schedule(delay, callback)
- SchedulePeriodic(interval, callback)
- Cancel(handle)
- DisposeAsync()

SessionIdleTracker
- Register(BaseSession session)
- UpdateActivity(sessionId, timestamp)
- Unregister(sessionId)
- ScanExpired(now)
```

`TimerQueue`는 due time 순서로 callback을 깨우고, idle session 검사는 많은 개별 timer 대신 하나의 periodic scan으로 처리한다. 이렇게 하면 10K 세션에서 timer 개수가 세션 수만큼 늘지 않고, receive path는 마지막 activity timestamp만 갱신한다.

서버 stale cleanup 흐름은 다음과 같다.

```text
packet received
-> session last activity timestamp update
-> TimerQueue periodic scan
-> now - last activity > idle timeout
-> session.RequestDisconnect()
-> telemetry records idle timeout disconnect reason
```

`RequestConnect`는 서버 stale cleanup 동작이 아니다. 서버는 stale session을 `RequestDisconnect()`로 정리하고, client reconnect가 필요하면 별도 client lifecycle feature에서 다룬다.

### 4.2 Time Source

TimerQueue와 idle scanner는 wall clock이 아니라 monotonic time을 사용한다.

Candidate:

- `TimeProvider` if target frameworks and tests can use it cleanly.
- `Stopwatch.GetTimestamp()` wrapper if lower-level allocation and framework compatibility are preferred.

Design phase에서 current target framework와 test ergonomics를 기준으로 결정한다.

### 4.3 Callback Policy

Timer callback은 queue thread를 오래 점유하지 않아야 한다. Idle scan은 짧은 작업으로 유지하고, expensive work는 별도 execution path로 넘긴다.

Design phase에서 다음 항목을 확정한다.

- callback exception handling
- callback overlap 허용 여부
- periodic callback 지연 시 catch-up 여부
- dispose 중 pending callback 처리
- queue latency telemetry sample 방식

## 5. Success Criteria

- [ ] TimerQueue가 one-shot timer를 due time 순서대로 실행한다.
- [ ] TimerQueue cancel/dispose 이후 callback이 실행되지 않는다.
- [ ] periodic timer가 중복 실행 없이 반복 실행된다.
- [ ] TimerQueue unit tests가 fake/controlled time으로 deterministic하게 통과한다.
- [ ] idle session tracker가 receive activity가 없는 세션에 `RequestDisconnect()`를 호출한다.
- [ ] active packet receive가 계속되는 세션은 idle timeout으로 끊기지 않는다.
- [ ] stale disconnect reason 또는 counter가 smoke/server telemetry에서 확인 가능하다.
- [ ] cloud smoke/staged validation에서 runner 종료 후 server `currentSessions`가 configured timeout 안에 0으로 수렴한다.
- [ ] 10K path에서 per-session `System.Threading.Timer`가 생성되지 않는다.
- [ ] `dotnet build FastPortSharp.sln -c Release`가 통과한다.
- [ ] `dotnet test FastPortSharp.sln -c Release --no-build`가 통과한다.

## 6. Schedule

| Phase | Target Date | Status |
|-------|-------------|--------|
| Plan | 2026-05-06 | Completed |
| Design | TBD | Pending |
| Implementation | TBD | Pending |
| Check | TBD | Pending |
| Report | TBD | Pending |

## 7. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| TimerQueue callback이 오래 걸려 due timer 전체가 밀림 | High | Medium | callback은 짧게 유지하고 long-running work는 별도 execution path로 넘긴다. Queue latency telemetry를 둔다. |
| idle timeout이 high RTT/load 중 정상 세션을 끊음 | High | Medium | timeout/grace를 configurable하게 두고 receive activity 기준을 명확히 한다. 첫 rollout은 smoke server/load validation 설정으로 제한한다. |
| receive timestamp update와 timer scan race | Medium | High | timestamp는 atomic write/read 가능한 형태로 저장하고 `RequestDisconnect()` idempotency에 의존한다. |
| 10K 세션 scan이 lock contention을 만든다 | Medium | Medium | per-session timer 대신 periodic scan을 사용하고, scan budget 또는 sharding을 design에서 검토한다. |
| wall-clock 변경으로 timer가 조기/지연 실행됨 | Medium | Low | monotonic clock을 사용한다. |
| TimerQueue가 너무 범용 scheduler로 확장됨 | Medium | Medium | 이번 feature는 idle/stale cleanup과 delayed/repeating timer primitive만 다룬다. |
| TCP keepalive와 application timeout 의미가 혼재됨 | Medium | Medium | keepalive는 kernel-level liveness hint, TimerQueue idle timeout은 application-level cleanup policy로 문서화한다. |

## 8. Test Plan

Required:

```text
dotnet build FastPortSharp.sln -c Release
dotnet test FastPortSharp.sln -c Release --no-build
```

Focused tests:

- `TimerQueue` one-shot due-order execution
- `TimerQueue` cancel before due
- `TimerQueue` periodic no-overlap behavior
- `TimerQueue` dispose drains/stops safely
- concurrent schedule/cancel stress test
- idle tracker disconnects stale sessions
- idle tracker does not disconnect sessions with recent receive activity
- telemetry exposes idle timeout disconnect count/reason

Cloud validation:

```text
FASTPORT_RUNNER_MODE=local FASTPORT_ENDPOINT_TYPE=public-ip FASTPORT_SERVER_HOST=40.82.153.1 FASTPORT_SERVER_PORT=6628 scripts/cloud/runner-smoke.sh
FASTPORT_RUNNER_MODE=local FASTPORT_ENDPOINT_TYPE=public-ip FASTPORT_SERVER_HOST=40.82.153.1 FASTPORT_SERVER_PORT=6628 scripts/cloud/runner-10k.sh
```

Post-run acceptance:

```text
currentSessions == 0 within configured idle cleanup timeout
pendingSendRequests == 0
socketErrorCount does not increase because of idle cleanup itself
idle timeout disconnect count is explainable and bounded
```

## 9. References

- `LibNetworks/Sessions/BaseSession.cs`
- `LibNetworks/BaseSocket.cs`
- `LibNetworks/Extensions/Socket+Extensions.cs`
- `FastPortTestSmokeServer/FastPortTestSmokeServer.cs`
- `FastPortTestSmokeServer/Sessions/FastPortTestSmokeClientSession.cs`
- `LibTestTelemetry/ServerTelemetry.cs`
- `LibTestTelemetry/ObservedMetrics.cs`
- `FastPortTests/BaseSessionSendPolicyTests.cs`
- `FastPortTests/FastPortTestSmokeServerTests.cs`
- `docs/load-validation-benchmark-results.md`
- `docs/azure-server-runner-split-load-validation-runbook.md`
- `docs/archive/2026-05/cloud-receive-timeout-rtt-tail-stability/cloud-receive-timeout-rtt-tail-stability.plan.md`

## 10. Next Phase

Recommended next command:

```text
$pdca design timer-queue
```
