# multi-accept-outstanding - Plan Document

> Version: 1.0.0 | Date: 2026-05-09 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`multi-accept-outstanding`는 `LibNetworks.BaseListener`의 accept pump가 동시에 여러 `AcceptAsync` 요청을 outstanding 상태로 유지할 수 있는지 검토하고, 필요할 경우 작은 단위로 구현하기 위한 계획이다.

현재 `listener-backlog-increase` 이후 `ListenBacklog=10500` 조건에서 10K Docker/cloud 근사 테스트는 대부분의 연결을 받아냈지만, 아직 일부 connect failure와 RTT tail이 남아 있다. send buffer pooling 이후 서버 send path는 `sendBackpressure=0`, `sendRejected=0`, `sendBufferBytes=0`으로 안정화됐으므로, 다음 병목 후보는 accept completion 이후의 처리 경로와 accept repost 구조다.

이번 feature의 목적은 단일 outstanding accept 구조가 10K ramp-up에서 accept 처리율을 제한하는지 분리해서 확인하고, 성능 이득이 명확한 경우에만 multi-accept 구조를 적용하는 것이다.

### 1.2 Background

현재 listener 흐름은 다음과 같다.

```text
BaseListener.StartAccept(ip, port, backlog)
-> Bind
-> Listen(backlog)
-> m_SocketEvent.Completed += OnSocketEventsAcceptCompleted
-> Accept(m_SocketEvent)

OnSocketEventsAcceptCompleted
-> validate completion
-> create session
-> Task.Run(clientSession.OnAccepted)
-> Accept(m_SocketEvent)
```

중요한 제약은 `m_SocketEvent` 하나를 재사용하기 때문에 동시에 outstanding 상태인 accept 요청도 하나라는 점이다. accept completion callback 안에서 session 생성, hook 호출, logging, `Task.Run` scheduling을 수행한 뒤에야 다음 accept가 repost된다.

최근 관측 기준은 다음과 같다.

| Area | Current observation |
|------|---------------------|
| Listen backlog | `4096`에서 시작해 cloud test 중 `10500`까지 검증 |
| Docker 10 x 1000 validation | connect success `9959`, connect failure `41` |
| Server final accepted/disconnected | `accepted=9959`, `disconnected=9959` |
| Server send pressure | `sendBackpressure=0`, `sendRejected=0` |
| Send buffer pooling effect | max send buffer bytes per queued item 수준으로 안정 |
| Remaining concern | connect failure 일부, accept/repost 간격, RTT tail |

이 결과는 multi-accept가 필수라는 증거는 아니다. 따라서 구현 전에 단일 accept의 실제 한계와 multi-accept의 부작용을 design에서 먼저 좁혀야 한다.

## 2. Goals

### 2.1 Primary Goals

- [ ] 현재 `BaseListener` accept pump가 단일 outstanding accept 구조임을 명확히 문서화한다.
- [ ] multi-accept outstanding이 필요한 조건을 design에서 정의한다.
- [ ] 구현 시 기본 동작은 기존과 동일하게 유지한다.
- [ ] `OutstandingAccepts` 또는 동등한 설정값으로 accept concurrency를 조절할 수 있게 한다.
- [ ] `SocketAsyncEventArgs`를 accept 개수만큼 안전하게 소유하고 재사용하는 구조를 설계한다.
- [ ] accept completion 실패, null socket, listener shutdown 시에도 accept loop가 예측 가능하게 동작하도록 한다.
- [ ] cloud/Docker 테스트에서 `1`, `2`, `4`, 필요 시 `8` outstanding accept를 비교한다.
- [ ] connect failure, accepted sessions, accept path latency, server send pressure, RTT tail을 before/after로 기록한다.

### 2.2 Non-Goals

- packet parser, receive path, send queue 구조를 이번 feature에서 변경하지 않는다.
- session lifecycle manager를 새로 만들지 않는다.
- `Task.Run(clientSession.OnAccepted)` 구조를 같은 변경에서 대체하지 않는다.
- OS kernel tuning 또는 Azure networking 설정 변경을 포함하지 않는다.
- 무조건 multi-accept를 기본값으로 켜지 않는다.
- RTT tail 전체를 이 feature 하나로 해결한다고 가정하지 않는다.

## 3. Scope

### 3.1 In Scope

- `LibNetworks/BaseListener.cs`
  - 현재 단일 `m_SocketEvent` accept 구조 분석
  - accept args 다중 보유 구조 설계
  - configurable outstanding accept count 설계
  - shutdown 중 repost 방지 규칙 설계
  - accept completion 실패 시 repost 여부 정책 설계
- `LibNetworks/BaseSocket.cs`
  - listener 전용 accept args 소유권을 `BaseListener`로 옮길지 검토
  - 기존 session socket event와 listener accept event의 책임 분리 검토
- `FastPortTestSmokeServer`
  - smoke/cloud test에서 outstanding accept count를 설정할 surface 검토
  - `appsettings.json` 또는 환경변수 override 검토
- `FastPortTests`
  - default outstanding accept count가 기존 동작과 호환되는지 검증
  - invalid count normalization 검증
  - accept repost가 중단되지 않는지 가능한 범위에서 검증
- validation
  - Docker 10 x 1000 closed-loop test 재사용
  - cloud server + local Docker runner 방식 재사용
  - outstanding accept count별 비교표 작성

### 3.2 Out of Scope

- send buffer pooling 추가 변경
- client pacing policy 변경
- load runner payload/rate 변경
- production-grade session manager 도입
- Linux `somaxconn`, `tcp_max_syn_backlog` 자동 변경
- telemetry contract 대규모 변경

## 4. Candidate Approach

### 4.1 Conservative Default

첫 구현은 기본값을 `1`로 유지한다.

```text
DefaultOutstandingAccepts = 1
```

이렇게 하면 기존 동작과 위험도가 거의 같다. cloud/Docker 테스트에서만 설정값을 `2`, `4`, `8`로 올려 비교한다.

### 4.2 Accept Args Ownership

`BaseListener`가 accept 전용 `SocketAsyncEventArgs` 배열 또는 pool을 소유하는 방향을 검토한다.

```text
BaseListener
  - SocketAsyncEventArgs[] acceptArgs
  - StartAccept posts N AcceptAsync calls
  - each completion clears AcceptSocket
  - each completion reposts the same args when listener is running
```

핵심 불변식은 다음과 같다.

- 하나의 `SocketAsyncEventArgs`는 동시에 하나의 `AcceptAsync`에만 사용한다.
- completion 처리 후 `AcceptSocket = null`로 초기화한 뒤 repost한다.
- shutdown이 시작되면 새 accept repost를 하지 않는다.
- completion error가 일시적이면 listener socket 상태에 따라 repost 가능성을 design에서 결정한다.

### 4.3 Configuration Surface

smoke server에 다음 설정을 추가하는 방향을 design에서 검토한다.

```text
FastPortTestSmokeServer:OutstandingAccepts
FastPortTestSmokeServer__OutstandingAccepts=4
```

값은 작은 범위로 normalize한다.

```text
minimum: 1
suggested test values: 1, 2, 4, 8
hard upper bound candidate: 64
```

상한은 실수로 매우 큰 값을 넣어 `SocketAsyncEventArgs`와 accept callback을 과도하게 생성하는 일을 막기 위한 안전장치다.

## 5. Success Criteria

- [ ] plan/design에서 multi-accept가 필요한 판단 기준이 명확하다.
- [ ] default outstanding accept count는 기존 동작과 동일한 `1`이다.
- [ ] explicit 설정으로 outstanding accept count를 바꿀 수 있다.
- [ ] `SocketAsyncEventArgs` 재사용에서 동시 사용 race가 없다.
- [ ] listener shutdown 중 accept repost가 중단된다.
- [ ] accept completion error 또는 null socket 상황에서 listener가 불필요하게 죽지 않는지 정책이 정의된다.
- [ ] `dotnet build FastPortSharp.sln -c Release`가 통과한다.
- [ ] `dotnet test FastPortSharp.sln -c Release`가 통과한다.
- [ ] Docker 10 x 1000 validation에서 default `1`이 기존 결과와 회귀하지 않는다.
- [ ] `2`, `4`, 필요 시 `8` outstanding accept 비교 결과가 문서화된다.
- [ ] connect failure가 줄거나 accept path latency가 개선될 때만 default 변경을 검토한다.
- [ ] `sendBackpressure=0`, `sendRejected=0` 상태가 유지된다.

## 6. Schedule

| Phase | Target Date | Status |
|-------|-------------|--------|
| Plan | 2026-05-09 | Completed |
| Design | 2026-05-09 | Pending |
| Implementation | TBD | Pending |
| Check | TBD | Pending |
| Report | TBD | Pending |

## 7. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| multi-accept가 실제 병목이 아님 | Medium | Medium | default를 `1`로 유지하고, 설정 기반 실험으로만 비교한다. |
| `SocketAsyncEventArgs` 동시 재사용 race | High | Medium | args별 outstanding 상태를 엄격히 유지하고, completion 후 같은 args만 repost한다. |
| accept completion error 후 accept loop 중단 | High | Medium | error phase별 repost 정책을 design에서 명시하고 테스트한다. |
| shutdown 중 repost로 ObjectDisposedException 증가 | Medium | Medium | `m_bIsRunning` 확인 후 repost하고, socket dispose 경로를 분리한다. |
| accept 수 증가가 session scheduling 병목을 숨김 | Medium | Medium | accept-to-session-created, accept-to-OnAccepted-started, first receive latency를 같이 본다. |
| 너무 큰 outstanding count로 callback storm 발생 | Medium | Low | 설정 상한을 두고 실험값은 `1`, `2`, `4`, `8`로 제한한다. |
| cloud variability로 효과 해석이 흔들림 | Medium | Medium | 동일 Docker runner 방식, 동일 payload/rate/duration 조건으로 before/after를 비교한다. |

## 8. Verification Plan

Local verification:

```text
dotnet build FastPortSharp.sln -c Release
dotnet test FastPortSharp.sln -c Release
```

Docker/cloud validation:

```text
sessions: 10 containers x 1000 sessions
payload: random:128-2048
rate: 1000
pacing-policy: fixed-window
pacing-fixed-window: 1
ramp-up: 120s
duration: 3m
listen-backlog: 10500
outstanding-accepts: 1, 2, 4, 8
```

Comparison metrics:

| Metric | Reason |
|--------|--------|
| connect success/failure | accept pump가 connection establishment에 미치는 영향 |
| total accepted/disconnected | server가 실제로 받은 connection 수 |
| accept path latency | accept completion 이후 session 준비 병목 분리 |
| first socket receive latency | accept 이후 receive 시작 지연 확인 |
| RTT P50/P95/P99 | gameplay 왕복 지연 tail 확인 |
| server send backpressure/rejected | send path 회귀 확인 |
| CPU/load average if available | accept concurrency 부작용 확인 |

## 9. References

- `LibNetworks/BaseListener.cs`
- `LibNetworks/BaseSocket.cs`
- `FastPortTestSmokeServer/FastPortTestSmokeServerOptions.cs`
- `FastPortTestSmokeServer/appsettings.json`
- `docs/01-plan/features/listener-backlog-increase.plan.md`
- `docs/02-design/features/listener-backlog-increase.design.md`
- `docs/load-validation-benchmark-results.md`
- `artifacts/load-validation/send-buffer-pooling-docker-20260509-101500-client-2/`
- `artifacts/load-validation/send-buffer-pooling-docker-20260509-101500-collected/`
