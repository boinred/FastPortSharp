# remove-server-telemetry-from-network-base-classes - Plan Document

> Version: 1.0.0 | Date: 2026-05-05 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`remove-server-telemetry-from-network-base-classes`는 `LibNetworks`의 engine/base networking classes가 test/server telemetry contract를 직접 소유하지 않도록 분리하는 feature다.

현재 `BaseSession`, `BaseListener`, `BaseMessageListener`, `BaseSessionClient`, `BaseSessionServer`는 `IServerTelemetry`를 constructor surface와 protected property로 노출하고, send/receive/accept/error 경로에서 `Record*`를 직접 호출한다. 이 구조는 테스트용 telemetry collector가 engine base class의 기본 책임처럼 보이게 만든다.

이번 feature의 목표는 engine 동작은 유지하면서 telemetry 기록 책임을 test/smoke server layer로 올릴 수 있는 migration path를 정리하는 것이다.

### 1.2 Background

이전 feature `extract-telemetry-contracts-from-network-core`에서 observed metrics JSONL contract와 exporter는 `LibTestTelemetry`로 이동했다. 하지만 다음 항목은 아직 `LibNetworks`에 남아 있다.

| Area | Current location | Current issue |
|------|------------------|---------------|
| `IServerTelemetry` | `LibNetworks/Telemetry/ServerTelemetry.cs` | engine base constructor/API가 diagnostics interface를 요구한다. |
| `ServerTelemetryCollector` | `LibNetworks/Telemetry/ServerTelemetry.cs` | test/smoke collector 성격이지만 core project에 남아 있다. |
| `ServerTelemetrySnapshot` | `LibNetworks/Telemetry/ServerTelemetry.cs` | snapshot/read model이 engine write path와 결합되어 있다. |
| `NullServerTelemetry` | `LibNetworks/Telemetry/ServerTelemetry.cs` | base class default dependency로 남아 있다. |

현재 base class 내부 telemetry 기록 지점은 다음 범주다.

| Class | Current telemetry events |
|-------|--------------------------|
| `BaseListener` | accept success, accept error, socket error |
| `BaseSession` | disconnect, socket error, receive packet bytes, send requested/completed/rejected, send backpressure, send drain yield, send buffer sample, sent bytes |
| `FastPortTestSmokeClientSession` | protocol error, parse error |

상속은 가능하지만 현재 `BaseListener.OnSocketEventsAcceptCompleted`는 private이고, `BaseSession`의 send/backpressure/socket error 세부 정보도 대부분 private 흐름 안에 있다. 따라서 단순히 subclass override만으로 현재 telemetry fidelity를 재현하려면 protected hook 설계가 필요하다.

## 2. Goals

### 2.1 Primary Goals

- [ ] `LibNetworks` base class constructor에서 `IServerTelemetry` parameter를 제거한다.
- [ ] `BaseSession`의 `protected IServerTelemetry ServerTelemetry` dependency를 제거한다.
- [ ] `BaseListener`/`BaseMessageListener`의 `IServerTelemetry` constructor overload를 제거한다.
- [ ] `BaseSessionClient`/`BaseSessionServer`의 `IServerTelemetry` constructor overload를 제거한다.
- [ ] 현재 telemetry 기록에 해당하는 engine lifecycle/send/receive/error event를 protected virtual hook으로 노출할지 설계한다.
- [ ] `FastPortTestSmokeServer` layer가 hook override 또는 composition으로 `ServerTelemetryCollector`에 기록하게 한다.
- [ ] `ServerTelemetryCollector`, `ServerTelemetrySnapshot`, `NullServerTelemetry`, `IServerTelemetry`의 이동/삭제 범위를 결정한다.
- [ ] `LibNetworks`가 `LibTestTelemetry`를 참조하지 않는 dependency boundary를 유지한다.
- [ ] 기존 observed JSONL/export/merge workflow와 test coverage를 유지한다.

### 2.2 Non-Goals

- 네트워크 send/receive 알고리즘을 변경하지 않는다.
- backpressure threshold 또는 pacing threshold를 변경하지 않는다.
- observed metrics JSON field 이름을 변경하지 않는다.
- `LibTestTelemetry`를 production runtime telemetry library로 확장하지 않는다.
- cloud/10K benchmark를 이 feature의 필수 검증으로 삼지 않는다.
- `LatencyStats` 이동은 이번 feature의 필수 구현 범위에 넣지 않는다.

## 3. Scope

### 3.1 In Scope

- `LibNetworks/Sessions/BaseSession.cs`
  - `IServerTelemetry` dependency 제거
  - telemetry call sites를 protected virtual hook call로 대체하는 설계
  - hook event shape 정의
- `LibNetworks/BaseListener.cs`
  - `IServerTelemetry` dependency 제거
  - accept success/error/socket error hook 설계
- `LibNetworks/BaseMessageListener.cs`
  - telemetry constructor overload 제거
- `LibNetworks/Sessions/BaseSessionClient.cs`
  - telemetry constructor overload 제거
- `LibNetworks/Sessions/BaseSessionServer.cs`
  - telemetry constructor overload 제거
- `FastPortTestSmokeServer`
  - smoke server listener/session/factory에서 telemetry collector 주입 및 기록
  - protocol/parse errors는 test session layer에서 계속 기록
- `LibTestTelemetry`
  - `ServerTelemetryCollector`와 `ServerTelemetrySnapshot` 이동 후보 검토
  - `IServerTelemetry`를 test-only interface로 유지할지, collector concrete type으로 충분한지 결정
- `FastPortTests`
  - send policy/server telemetry/smoke server tests의 dependency update

### 3.2 Out of Scope

- `BaseSession` send queue/channel 구조 변경
- packet parser/protocol schema 변경
- load runner pacing/window tuning
- server template/cloud deployment automation
- MAUI dashboard integration
- unrelated benchmark markdown update

## 4. Candidate Architecture

### 4.1 Preferred Direction

엔진 base class에는 telemetry interface를 두지 않고, event hook만 둔다.

예상 hook 예:

```text
BaseListener
- OnAcceptSucceeded(Socket clientSocket)
- OnAcceptFailed(SocketError? socketError, Exception? exception)

BaseSession
- OnSessionDisconnected()
- OnSocketError(SocketError socketError)
- OnPacketReceived(BasePacket packet)
- OnSendRequested(int bytes, int queuedBytes)
- OnSendCompleted()
- OnSendRejected(int bytes, int queuedBytes)
- OnSendBackpressure(int bytes, int queuedBytes)
- OnSendDrainYield(int queuedBytes)
- OnSendBufferSample(int queuedBytes)
- OnBytesSent(int bytes)
```

`FastPortTestSmokeServer` 또는 전용 test subclasses가 이 hook을 override해서 `ServerTelemetryCollector`에 기록한다.

### 4.2 Alternative

더 보수적인 대안은 `LibNetworks`에 read/control API가 없는 작은 sink만 남기는 것이다.

```text
INetworkTelemetrySink
```

이 경우 `CreateSnapshot()`/`Reset()`은 collector 쪽에만 남기고, engine은 `Record*` write path만 본다.

하지만 사용자가 지적한 것처럼 base class 상속 구조를 활용하면 interface 자체를 engine에서 제거할 수 있다. 이번 feature는 preferred direction을 먼저 설계한다.

## 5. Success Criteria

- [ ] `LibNetworks` source에서 `IServerTelemetry`, `ServerTelemetryCollector`, `ServerTelemetrySnapshot`, `NullServerTelemetry` 직접 정의 또는 사용이 제거된다.
- [ ] `LibNetworks/LibNetworks.csproj`는 `LibTestTelemetry`를 참조하지 않는다.
- [ ] `FastPortTestSmokeServer`는 server telemetry collector/exporter를 계속 생성하고 JSONL export를 유지한다.
- [ ] 기존 server observed metrics test가 통과한다.
- [ ] send policy/backpressure tests가 통과한다.
- [ ] smoke server tests가 통과한다.
- [ ] `dotnet build FastPortCharp.sln -c Release`가 통과한다.
- [ ] `dotnet test FastPortCharp.sln -c Release --no-build`가 통과한다.

## 6. Schedule

| Phase | Target Date | Status |
|-------|-------------|--------|
| Plan | 2026-05-05 | Completed |
| Design | TBD | Pending |
| Implementation | TBD | Pending |
| Check | TBD | Pending |
| Report | TBD | Pending |

## 7. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| protected hook 설계가 과도하게 커짐 | Medium | Medium | 현재 telemetry call site에 대응하는 최소 hook만 추가한다. |
| hook 누락으로 observed metric fidelity가 떨어짐 | High | Medium | 기존 `ServerTelemetryTests`, `BaseSessionSendPolicyTests`, `FastPortTestSmokeServerTests`로 검증한다. |
| base class public constructor/API 변경으로 consumer break 발생 | Medium | Medium | 현재 repo consumers를 모두 갱신하고, old telemetry overload 제거 범위를 design에서 명시한다. |
| accept/socket error 이벤트가 private 흐름에 묶여 있음 | Medium | High | `BaseListener`에 protected virtual event method를 추가한다. |
| send rejected/backpressure queue depth 의미가 바뀜 | High | Low | hook 호출 위치는 기존 `Record*` 호출 위치와 1:1로 매핑한다. |
| `ServerTelemetryCollector` 이동이 순환 참조를 유발함 | High | Low | collector는 `LibTestTelemetry`가 `LibNetworks`를 참조하는 방향으로만 배치한다. |

## 8. Test Plan

Required:

```text
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln -c Release --no-build
```

Focused tests:

- `ServerTelemetryTests`
- `BaseSessionSendPolicyTests`
- `FastPortTestSmokeServerTests`
- `ObservedMetricsTests`

Static checks:

```text
rg -n "IServerTelemetry|ServerTelemetryCollector|ServerTelemetrySnapshot|NullServerTelemetry" LibNetworks
```

Expected after implementation:

```text
no matches
```

## 9. References

- `LibNetworks/Telemetry/ServerTelemetry.cs`
- `LibNetworks/Sessions/BaseSession.cs`
- `LibNetworks/BaseListener.cs`
- `LibNetworks/BaseMessageListener.cs`
- `LibNetworks/Sessions/BaseSessionClient.cs`
- `LibNetworks/Sessions/BaseSessionServer.cs`
- `FastPortTestSmokeServer/FastPortTestSmokeServer.cs`
- `FastPortTestSmokeServer/Sessions/FastPortTestSmokeClientSession.cs`
- `FastPortTestSmokeServer/Sessions/FastPortTestSmokeClientSessionFactory.cs`
- `LibTestTelemetry/ObservedMetrics.cs`
- `FastPortTests/ServerTelemetryTests.cs`
- `FastPortTests/BaseSessionSendPolicyTests.cs`
- `FastPortTests/FastPortTestSmokeServerTests.cs`

## 10. Next Phase

Recommended next command:

```text
$pdca design remove-server-telemetry-from-network-base-classes
```
