# listener-backlog-increase - Design Document

> Version: 1.0.0 | Date: 2026-05-07 | Status: Completed
> Level: Starter | Plan: docs/01-plan/features/listener-backlog-increase.plan.md

---

## 1. Overview

`listener-backlog-increase`는 `LibNetworks.BaseListener`의 TCP listen backlog를 hard-coded `100`에서 caller가 전달하는 값으로 바꾸고, `FastPortTestSmokeServer` 설정 기본값을 `4096`으로 두는 작은 변경이다.

이번 설계는 의도적으로 좁다. accept loop 구조, socket receive/send path, cloud runner script는 바꾸지 않는다. 목표는 smoke/cloud test에서 backlog 값을 명시적으로 제어하고, backlog 값이 connection establishment 병목에 미치는 영향을 before/after로 분리해서 확인하는 것이다.

Baseline cloud closed-loop 테스트 결과는 다음과 같다.

| Metric | Baseline |
|--------|----------|
| Test mode | `random:128-2048`, `fixed-window=1`, `rate=1000`, `10,000 sessions`, `120s ramp-up`, `3m duration` |
| Peak current sessions | `8,943 / 10,000` |
| Final current sessions | `8,935` |
| Connect timeouts | `1,057` |
| Timeout class | `connect|SocketException|TimedOut` |
| Timeout duration | about `75,000ms` |
| Server accepted sessions | `8,942` |
| Server send backpressure | `0` |
| Server send rejected | `0` |

이 설계의 판단 기준은 간단하다. backlog 증가 후 같은 조건에서 connect timeout이 의미 있게 줄면, `Listen(100)`이 10K ramp-up의 주요 제한 요인 중 하나였다고 본다.

## 2. Current State

현재 listener 시작 흐름은 다음과 같다.

```text
BaseListener.StartAccept(ip, port)
-> AddressConverter.TryToEndPoint(ip, port)
-> m_Socket.Bind(endPoint)
-> m_Socket.Listen(100)
-> m_SocketEvent.Completed += OnSocketEventsAcceptCompleted
-> Accept(m_SocketEvent)
```

관련 코드 위치:

| File | Current behavior |
|------|------------------|
| `LibNetworks/BaseListener.cs` | `m_Socket.Listen(100)` hard-coded |
| `FastPortTestSmokeServer/FastPortTestSmokeServerBackgroundService.cs` | `StartAccept(host, port)` 호출 |
| `FastPortTestSmokeServer/appsettings.json` | `Host`, `Port`, `SessionIdleCleanup`은 있지만 `ListenBacklog` 없음 |
| `FastPortTestSmokeServer/Program.cs` | `Host`, `Port`, `Telemetry`, `SessionIdleCleanup` 설정 binding |
| `FastPortTestSmokeServer/FastPortTestSmokeServer.cs` | `BaseMessageListener` 상속, accept telemetry hook override |
| `LibNetworks/BaseMessageListener.cs` | `BaseListener` 기본 생성자/동작 사용 |

현재 `BaseListener` constructor에는 `maxConnectionsCount`가 있지만, 이 값은 listen backlog에 연결되어 있지 않다. 이 feature에서는 기존 constructor 의미를 새로 정의하지 않는다.

## 3. Requirements

### 3.1 Functional Requirements

- `BaseListener`는 caller가 전달한 listen backlog로 socket listen을 시작한다.
- `BaseListener.StartAccept(string ip, int port)`는 기존 caller 호환을 위해 default backlog로 위임한다.
- `FastPortTestSmokeServer`는 `FastPortTestSmokeServer:ListenBacklog` 설정을 읽는다.
- `FastPortTestSmokeServer`의 default listen backlog는 `4096`이다.
- invalid/zero/negative backlog 설정은 default `4096`으로 normalize한다.
- 기존 `StartAccept(string ip, int port)` 호출자는 수정 없이 동작해야 한다.
- endpoint 변환 실패, bind/listen 예외, accept hook 호출 흐름은 기존 동작을 유지해야 한다.
- cloud closed-loop 3분 테스트에서 connect timeout 수를 baseline과 비교할 수 있어야 한다.

### 3.2 Non-Functional Requirements

- 변경 범위는 listener backlog 설정 흐름으로 제한한다.
- 기존 public API를 깨지 않는다.
- smoke server 설정 파일/appsettings surface를 추가한다.
- platform-specific socket option을 추가하지 않는다.
- OS sysctl 또는 Azure networking 설정을 자동 변경하지 않는다.
- backlog 변경 의도는 코드 주석으로 남긴다.

## 4. Architecture

### 4.1 Component Boundary

```text
LibNetworks
  BaseListener
    - owns socket bind/listen/accept loop
    - defines compatibility default listen backlog
    - accepts explicit backlog from caller
    - does not own cloud tuning or OS sysctl

FastPortTestSmokeServer
  Program
    - binds FastPortTestSmokeServer.ListenBacklog
    - normalizes invalid values to default

  FastPortTestSmokeServerOptions
    - carries Host, Port, ListenBacklog

  FastPortTestSmokeServerBackgroundService
    - calls StartAccept(host, port, listenBacklog)

Cloud validation
  scripts/cloud/server-start.sh
  FastPortTestLoadRunner
    - reuses existing server/runner workflow
```

`BaseListener`는 engine-level listener primitive다. 따라서 explicit backlog를 받을 수 있게 하고, smoke server는 그 값을 configuration에서 읽어 전달한다.

### 4.2 Backlog Value Decision

Default configured value:

```text
4096
```

Reasoning:

- baseline `100`은 10K connection ramp-up에는 작다.
- `4096`은 기존보다 충분히 크지만 무제한에 가깝게 키우지는 않는다.
- cloud test에서 `ss -ltnp`로 listen backlog가 설정값으로 보이는지 확인하기 쉽다.
- OS limit에 의해 clamp되더라도 runtime observation으로 확인 가능하다.
- `Socket.MaxConnections` 계열 값보다 before/after 해석이 쉽다.

### 4.3 Alternative: Runtime Max Backlog

대안은 runtime/framework가 제공하는 max backlog 값을 사용하는 것이다.

```text
m_Socket.Listen(Socket.MaxConnections)
```

또는 parameterless `Listen()`/max connection 계열 API가 target runtime에서 적합하면 사용할 수 있다.

이번 feature에서는 채택하지 않는다.

이유:

- 플랫폼별 실제 backlog 의미가 다를 수 있다.
- Linux kernel `somaxconn`에 의해 clamp될 수 있다.
- 값이 너무 커지면 connection storm 상황에서 failure를 늦게 만들 수 있다.
- 현재 목표는 원인 분리이므로 명시적인 `4096`이 더 좋은 실험 변수다.

### 4.4 Configuration Surface

이번 feature에서 다음 configuration surface를 추가한다.

```csharp
public bool StartAccept(string ip, int port, int backlog)
```

Smoke server 설정:

```text
FastPortTestSmokeServer__ListenBacklog=4096
```

`FastPortTestSmokeServer/appsettings.json`에도 같은 기본값을 추가한다.

```json
{
  "FastPortTestSmokeServer": {
    "Host": "0.0.0.0",
    "Port": 6628,
    "ListenBacklog": 4096
  }
}
```

## 5. Implementation Design

### 5.1 BaseListener Constant

`LibNetworks/BaseListener.cs`에 compatibility default backlog 상수를 추가한다.

```csharp
// 목적: 10K ramp-up 중 TCP connect queue 포화를 줄이기 위한 기본 listen backlog
private const int C_DefaultListenBacklog = 4096;
```

기존 코드의 naming style에 `C_MaxConnections`가 있으므로, 같은 파일 안에서는 `C_DefaultListenBacklog` 형태를 사용한다.

### 5.2 StartAccept Overload

기존 caller 호환용 method는 유지하고 explicit backlog overload로 위임한다.

```csharp
public bool StartAccept(string ip, int port)
{
    return StartAccept(ip, port, C_DefaultListenBacklog);
}
```

새 overload는 backlog를 normalize한 뒤 `Listen`에 전달한다.

```csharp
public bool StartAccept(string ip, int port, int backlog)
{
    int normalizedBacklog = NormalizeListenBacklog(backlog);
    ...
    m_Socket.Listen(normalizedBacklog);
}
```

`Listen` 호출 위치는 `Bind` 직후, accept event handler 등록 전 그대로 유지한다.

### 5.3 Backlog Normalization

0 이하 값은 default로 보정한다.

```csharp
private static int NormalizeListenBacklog(int backlog)
{
    return backlog > 0 ? backlog : C_DefaultListenBacklog;
}
```

이렇게 하면 잘못된 appsettings/env 값 때문에 `Listen(0)` 또는 예외성 설정으로 테스트가 흔들리는 일을 줄인다.

### 5.4 FastPortTestSmokeServerOptions

`FastPortTestSmokeServerOptions`에 `ListenBacklog`를 추가한다.

```csharp
public sealed class FastPortTestSmokeServerOptions
{
    public string Host { get; init; } = "0.0.0.0";

    public int Port { get; init; } = 6628;

    public int ListenBacklog { get; init; } = 4096;
}
```

### 5.5 Program Binding

`FastPortTestSmokeServer/Program.cs`에서 `FastPortTestSmokeServer:ListenBacklog`를 읽는다.

```csharp
int listenBacklog = int.TryParse(serverSection["ListenBacklog"], out int configuredListenBacklog)
    ? configuredListenBacklog
    : 4096;
```

`listenBacklog <= 0`이면 options default 또는 `4096`으로 normalize한다.

### 5.6 BackgroundService Call

`FastPortTestSmokeServerBackgroundService`는 options의 backlog를 listener로 전달한다.

```csharp
m_FastPortTestSmokeServer.StartAccept(
    m_Options.Host,
    m_Options.Port,
    m_Options.ListenBacklog);
```

시작 로그에는 backlog도 포함한다.

```text
Host:{Host}, Port:{Port}, ListenBacklog:{ListenBacklog}
```

### 5.7 Constructor Surface

`BaseListener` constructor는 변경하지 않는다.

```csharp
public BaseListener(
    ILogger<BaseListener> logger,
    IClientSessionFactory clientSessionFactory,
    int maxConnectionsCount)
```

`maxConnectionsCount`를 backlog로 재사용하지 않는다. 이름상 "최대 접속 수"이고 실제 session cap과 연계될 가능성이 있으므로, backlog와 의미를 섞지 않는다.

### 5.8 Logging And Telemetry

변경 없음.

이번 feature는 accept path info log 또는 telemetry hook을 수정하지 않는다. backlog 증가 후에도 timeout이 크게 남으면 다음 순서로 별도 feature를 검토한다.

1. accept path info log level 조정
2. accept concurrency 또는 accept loop 병렬화 검토
3. OS backlog/drop counter 수집
4. multi-runner split test

## 6. Data Model

새 persistent 데이터 모델은 없다.

새 configuration field:

| Type | Field | Default | Source |
|------|-------|---------|--------|
| `FastPortTestSmokeServerOptions` | `ListenBacklog` | `4096` | `FastPortTestSmokeServer:ListenBacklog` |

새 runtime metric도 추가하지 않는다. 검증은 기존 metrics와 connect events를 사용한다.

| Existing artifact | Used fields |
|-------------------|-------------|
| client metrics JSONL | `connectFailureCount`, `socketErrorCountsByClass`, `currentSessions`, `tps`, `rttP50Ms`, `rttP95Ms`, `rttP99Ms` |
| connect events JSONL | `status`, `exceptionType`, `socketErrorCode`, `durationMs` |
| server metrics JSONL | `totalAcceptedSessions`, `sendBackpressureEvents`, `sendRejectedRequests`, `socketErrorCountsByClass` |
| remote `ss -ltnp` | listen backlog display |

## 7. API Design

기존 public API는 유지하고 overload를 추가한다.

Existing:

```csharp
public bool StartAccept(string ip, int port)
```

New:

```csharp
public bool StartAccept(string ip, int port, int backlog)
```

New configuration key:

```text
FastPortTestSmokeServer:ListenBacklog
FastPortTestSmokeServer__ListenBacklog
```

## 8. Test Plan

### 8.1 Local Verification

Build:

```text
dotnet build FastPortCharp.sln -c Release
```

Tests:

```text
dotnet test FastPortCharp.sln -c Release --no-build
```

Focused tests are not required for the constant change because socket listen backlog is OS-observable behavior. If a unit test is added later, it should avoid relying on OS-specific backlog reporting.

### 8.2 Cloud Verification

Run the same closed-loop baseline condition.

```text
sessions=10000
payload=random:128-2048
rate=1000
pacing-policy=fixed-window
pacing-fixed-window=1
ramp-up=120s
duration=3m
metrics-interval=1s
```

Server startup check:

```text
ss -ltnp | grep 6628
```

Expected observation:

```text
LISTEN ... 4096 ... :6628
```

Cloud server startup should pass either appsettings default or explicit environment override.

```text
FastPortTestSmokeServer__ListenBacklog=4096
```

### 8.3 Comparison Metrics

Compare new run against baseline:

| Metric | Baseline | Expected after change |
|--------|----------|-----------------------|
| Connect timeouts | `1,057` | lower |
| Peak sessions | `8,943` | higher |
| Final sessions | `8,935` | higher or stable |
| Server accepted sessions | `8,942` | higher |
| Server send backpressure | `0` | remains `0` |
| Server send rejected | `0` | remains `0` |
| RTT P50/P95/P99 | measured | no regression target, interpret with TPS/session count |

### 8.4 Failure Interpretation

If connect timeout remains near baseline:

- backlog was not the dominant limiter, or OS/Azure clamp still applies.
- collect Linux counters:
  - `netstat -s | grep -i listen`
  - `/proc/net/netstat`
  - `sysctl net.core.somaxconn`
  - `sysctl net.ipv4.tcp_max_syn_backlog`
- run `5K + 5K` split runner test to isolate local NAT/runner bottleneck.

If timeout drops but RTT tail worsens:

- backlog allowed more sessions to connect, exposing downstream network/processing tail.
- evaluate accept path logging and runner/client network limits separately.

## 9. Implementation Order

1. Add `C_DefaultListenBacklog = 4096` to `LibNetworks/BaseListener.cs`.
2. Add `StartAccept(string ip, int port, int backlog)` overload.
3. Make existing `StartAccept(string ip, int port)` delegate to the overload.
4. Replace `m_Socket.Listen(100)` with `m_Socket.Listen(normalizedBacklog)`.
5. Add `FastPortTestSmokeServerOptions.ListenBacklog`.
6. Bind `FastPortTestSmokeServer:ListenBacklog` in `Program.cs`.
7. Add `ListenBacklog: 4096` to `FastPortTestSmokeServer/appsettings.json`.
8. Pass `m_Options.ListenBacklog` from background service to `StartAccept`.
9. Run local build.
10. Run local tests.
11. Deploy current branch to cloud test directory.
12. Start smoke server and confirm `ss` listen backlog is no longer `100`.
13. Run closed-loop 3분 cloud test.
14. Summarize before/after metrics.

## 10. Risks And Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| OS clamps backlog below `4096` | connect timeout may remain | inspect `ss` and sysctl values during cloud verification |
| invalid config value creates bad backlog | listener may fail or behave unexpectedly | normalize `<= 0` to `4096` |
| timeout moves from connect phase to RTT tail | test may look better by connection count but worse by tail | compare RTT P50/P95/P99 and TPS together |
| `4096` too conservative for full 10K | some timeout may remain | treat result as first controlled step, consider `8192` or config in follow-up |
| backlog change hides accept log overhead | root cause remains partially hidden | keep log changes out of this feature, inspect after backlog retest |
| local runner/NAT is actual limiter | backlog change may not help | split runner test if timeout remains high |

## 11. Success Criteria Mapping

| Plan criterion | Design answer |
|----------------|---------------|
| Remove `Listen(100)` | Replace with `Listen(normalizedBacklog)` |
| Name default backlog | `C_DefaultListenBacklog = 4096`, smoke server `ListenBacklog=4096` |
| Preserve caller API | `StartAccept(ip, port)` unchanged and delegates to overload |
| Add configuration | `FastPortTestSmokeServer:ListenBacklog` and env override |
| Avoid broad refactor | listener/options/background-service only |
| Validate locally | build and tests |
| Validate in cloud | same closed-loop 3분 scenario |
| Compare timeout | baseline `1,057` vs new run |
| Preserve send queue health | check backpressure/rejected remain `0` |
