# listener-backlog-increase - Plan Document

> Version: 1.0.0 | Date: 2026-05-07 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`listener-backlog-increase`는 `LibNetworks.BaseListener`의 TCP listen backlog를 현재 hard-coded `Listen(100)`에서 설정 가능한 값으로 바꾸고, smoke server 기본 backlog를 10K cloud load validation에 적합한 `4096`으로 늘리는 기능이다.

현재 closed-loop 근사 테스트(`random:128-2048`, `fixed-window=1`, `rate=1000`, `10,000 sessions`, `120s ramp-up`, `3m duration`)에서 서버 send backpressure는 0으로 유지됐지만, TCP connect 단계에서 `1,057`건의 `SocketException TimedOut`가 발생했다. 서버 accept 수는 `8,942`, client peak connection은 `8,943`으로 멈췄다.

이번 feature의 목적은 accept backlog가 10K ramp-up의 connection establishment를 제한하는지 작게 검증하고, cloud test에서 backlog 값을 명시적으로 조절할 수 있게 만드는 것이다.

### 1.2 Background

현재 코드와 테스트 관측은 다음과 같다.

| Area | Current state | Evidence |
|------|---------------|----------|
| Listener backlog | `LibNetworks/BaseListener.cs`에서 `m_Socket.Listen(100)` 사용 | runtime `ss` 출력도 `LISTEN ... 100` |
| Cloud closed-loop result | peak `8,943 / 10,000`, connect timeout `1,057` | `artifacts/load-validation/cloud-closedloop-269e982/s5-closedloop-10k/s5-closedloop-10k.metrics.jsonl` |
| Timeout shape | all failed connect events are `SocketException TimedOut`, duration about `75,000ms` | `s5-closedloop-10k.connect-events.jsonl` |
| Server send pressure | `sendBackpressureEvents=0`, `sendRejectedRequests=0` | `cloud-closedloop-269e982/collected/server/server/server.metrics.jsonl` |
| Server accept count | `totalAcceptedSessions=8942` | server metrics |

이 관측은 packet echo 처리나 send queue보다 TCP connect/accept path가 먼저 제한되는 상황을 가리킨다. 특히 backlog `100`은 10K 연결 ramp-up 테스트에 작을 가능성이 높다.

## 2. Goals

### 2.1 Primary Goals

- [ ] `BaseListener`의 backlog를 caller가 전달할 수 있게 한다.
- [ ] smoke server 설정 기본값은 `ListenBacklog=4096`으로 둔다.
- [ ] 환경변수 `FastPortTestSmokeServer__ListenBacklog`로 cloud test 중 backlog 값을 바꿀 수 있게 한다.
- [ ] `SocketOptionName.MaxConnections` 또는 `Socket.MaxConnections` 계열 값은 플랫폼별 의미를 확인한 뒤 design에서 대안으로 평가한다.
- [ ] backlog 값을 magic number로 남기지 않고 options/configuration surface로 정리한다.
- [ ] 기존 listener start/accept 동작을 유지한다.
- [ ] closed-loop 3분 cloud test에서 connect timeout 감소 여부를 재검증한다.

### 2.2 Non-Goals

- accept loop 구조를 이번 plan에서 바로 재작성하지 않는다.
- per-accept logging policy를 같은 변경에 섞지 않는다. 필요하면 별도 feature로 분리한다.
- server send queue, packet parser, BaseSession receive/send 흐름을 변경하지 않는다.
- LoadRunner closed-loop 전용 모드를 새로 구현하지 않는다.
- Azure VM, NSG, OS kernel tuning을 이번 feature의 필수 구현 범위에 넣지 않는다.
- 10K full success를 backlog 변경만으로 보장한다고 가정하지 않는다.

## 3. Scope

### 3.1 In Scope

- `LibNetworks/BaseListener.cs`
  - `m_Socket.Listen(100)` hard-coded backlog 제거
  - `StartAccept(string ip, int port, int backlog)` overload 추가
  - 기존 `StartAccept(string ip, int port)` 호출자는 default backlog로 위임
- `FastPortTestSmokeServer`
  - `FastPortTestSmokeServerOptions.ListenBacklog` 추가
  - `appsettings.json`에 `ListenBacklog: 4096` 추가
  - `Program.cs`에서 `FastPortTestSmokeServer:ListenBacklog` 값을 읽어 options로 전달
  - `FastPortTestSmokeServerBackgroundService`에서 `StartAccept(host, port, backlog)` 호출
- `FastPortTests`
  - listener backlog 값을 검증할 수 있는 단위 테스트 가능성 검토
  - socket backlog는 OS observable state라 직접 unit test가 어렵다면 build/test와 cloud validation으로 검증
- `scripts/cloud` and artifacts
  - 기존 cloud server/runner split 절차 재사용
  - closed-loop 조건으로 before/after 비교
- documentation
  - `Listen(100)` 변경 의도와 connect timeout 관측 근거 기록

### 3.2 Out of Scope

- accept concurrency model 변경
- accept path log level 최적화
- Linux `somaxconn`, `tcp_max_syn_backlog`, `tcp_abort_on_overflow` 자동 조정
- multi-runner split test
- load runner reconnect/retry policy
- OS/kernel backlog tuning 자동화

## 4. Candidate Approach

### 4.1 Preferred Direction

구현은 설정 가능성을 포함하되, 의미를 좁게 유지한다.

```text
BaseListener
- private const int C_DefaultListenBacklog = 4096;
- StartAccept(ip, port) -> StartAccept(ip, port, C_DefaultListenBacklog)
- StartAccept(ip, port, backlog) -> m_Socket.Listen(backlog)

FastPortTestSmokeServer
- appsettings.json: FastPortTestSmokeServer.ListenBacklog = 4096
- env override: FastPortTestSmokeServer__ListenBacklog=4096
```

이 방식은 기존 `StartAccept(ip, port)` 호출자를 깨지 않으면서 smoke/cloud test에서는 backlog를 명시적으로 제어할 수 있게 한다. cloud validation에서 timeout이 의미 있게 줄면 backlog가 주요 병목이었다는 가설을 강하게 지지한다.

### 4.2 Alternative

Design 단계에서 다음 대안을 비교한다.

```text
m_Socket.Listen(Socket.MaxConnections)
```

또는 framework/runtime에서 제공하는 max backlog 계열을 검토한다. 다만 `Socket.MaxConnections`는 플랫폼/런타임별 실제 의미가 다를 수 있고 OS limit에 의해 clamp될 수 있으므로, 첫 변경으로는 명시적인 `4096`이 더 해석하기 쉽다.

### 4.3 Configuration Option

이번 feature에서 smoke server 설정 surface를 추가한다.

```text
StartAccept(string ip, int port, int backlog)
FastPortTestSmokeServer__ListenBacklog=4096
```

production server의 설정 surface까지 확장할지는 별도 feature에서 결정한다. 현재 목표는 smoke/cloud validation에서 backlog를 명시적으로 바꿔 재측정할 수 있게 하는 것이다.

## 5. Success Criteria

- [ ] `LibNetworks/BaseListener.cs`에서 `Listen(100)`이 제거된다.
- [ ] `StartAccept(string ip, int port, int backlog)` overload가 추가된다.
- [ ] 기존 `StartAccept(ip, port)` 호출자는 수정 없이 동작한다.
- [ ] `FastPortTestSmokeServerOptions.ListenBacklog`가 추가된다.
- [ ] `FastPortTestSmokeServer/appsettings.json`에 `ListenBacklog: 4096`이 추가된다.
- [ ] 환경변수 `FastPortTestSmokeServer__ListenBacklog`로 cloud run의 backlog를 override할 수 있다.
- [ ] `dotnet build FastPortSharp.sln -c Release`가 통과한다.
- [ ] 관련 tests 또는 전체 `dotnet test FastPortSharp.sln -c Release --no-build`가 통과한다.
- [ ] cloud closed-loop 3분 재테스트를 같은 조건으로 실행한다.
- [ ] 재테스트에서 connect timeout이 기존 `1,057`보다 의미 있게 감소한다.
- [ ] 서버 metrics에서 `sendBackpressureEvents=0`, `sendRejectedRequests=0` 유지 여부를 확인한다.
- [ ] 재테스트 결과를 before/after로 문서화한다.

## 6. Schedule

| Phase | Target Date | Status |
|-------|-------------|--------|
| Plan | 2026-05-07 | Completed |
| Design | 2026-05-07 | Pending |
| Implementation | 2026-05-07 | Pending |
| Check | 2026-05-07 | Pending |
| Report | TBD | Pending |

## 7. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| backlog 증가만으로 timeout이 줄지 않음 | Medium | Medium | 결과가 그대로면 local NAT/Azure/OS queue limit을 분리하기 위해 multi-runner 또는 OS counter 수집을 다음 feature로 진행한다. |
| backlog 값이 OS limit에 의해 clamp됨 | Medium | Medium | runtime `ss -ltnp` Recv-Q/Send-Q와 Linux sysctl 값을 같이 확인한다. |
| 설정 파싱 오류로 backlog가 0 이하가 됨 | Medium | Low | invalid/zero/negative 값은 default `4096`으로 normalize한다. |
| `Socket.MaxConnections` 의미가 플랫폼별로 다름 | Medium | Medium | 첫 구현은 명시적 `4096`을 우선하고, MaxConnections는 design에서 대안으로만 평가한다. |
| backlog 증가가 accept path log 비용을 숨김 | Low | Medium | 이번 feature는 backlog만 바꾸고, log level 최적화는 별도 변경으로 분리한다. |
| 너무 큰 backlog가 connection storm을 늦게 실패하게 만듦 | Medium | Low | 4096으로 시작하고, timeout/error/accept latency를 재측정한다. |
| cloud run 변동성으로 before/after 비교가 흔들림 | Medium | Medium | 동일 payload, 동일 ramp-up, 동일 duration, 동일 runner/server VM 조건을 유지한다. |

## 8. Verification Plan

Local verification:

```text
dotnet build FastPortSharp.sln -c Release
dotnet test FastPortSharp.sln -c Release --no-build
```

Cloud verification:

```text
sessions=10000
payload=random:128-2048
rate=1000
pacing-policy=fixed-window
pacing-fixed-window=1
ramp-up=120s
duration=3m
```

Compare against baseline:

| Metric | Baseline |
|--------|----------|
| Peak current sessions | 8,943 |
| Connect timeouts | 1,057 |
| Final connected sessions | 8,935 |
| Steady avg TPS | 6,738 |
| Steady avg RTT P50 | 308ms |
| Steady avg RTT P95 | 3.28s |
| Server send backpressure events | 0 |
| Server send rejected requests | 0 |

## 9. References

- `LibNetworks/BaseListener.cs`
- `artifacts/load-validation/cloud-closedloop-269e982/s5-closedloop-10k/s5-closedloop-10k.metrics.jsonl`
- `artifacts/load-validation/cloud-closedloop-269e982/s5-closedloop-10k/s5-closedloop-10k.connect-events.jsonl`
- `artifacts/load-validation/cloud-closedloop-269e982/collected/server/server/server.metrics.jsonl`
