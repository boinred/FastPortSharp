# telemetry-export-metric-contract - Plan Document

> Version: 1.0.0 | Date: 2026-04-28 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`telemetry-export-metric-contract`는 FastPortSharp의 server/client observed metric naming을 정리하고, 이후 MAUI dashboard와 staged load validation이 같은 의미의 지표를 읽을 수 있도록 telemetry export 표면을 정의하는 기능이다.

직전 `fastport-smoke-server` 단계에서 서버 관점 telemetry와 integration smoke 자동화는 추가되었지만, 일부 metric의 이름과 의미가 아직 명확하지 않다. 특히 `sentPackets`가 exact packet count인지 socket send completion count인지, `receivedBytes`가 raw socket bytes인지 parsed packet bytes인지가 dashboard 관점에서 애매하다.

이 단계의 목적은 구현 전에 지표 의미를 고정하고, client-observed metric과 server-observed telemetry가 같은 naming convention 아래에서 비교될 수 있게 만드는 것이다.

### 1.2 Background

현재 관련 지표는 두 흐름에 나뉘어 있다.

| Area | Current Type | Current Meaning |
|------|--------------|-----------------|
| Client load metrics | `FastPortLoadRunner.MetricsSnapshot` | LoadRunner가 관측한 session, TPS, RTT, sent/received bytes/packets, socket errors |
| Server telemetry | `LibNetworks.Telemetry.ServerTelemetrySnapshot` | 서버가 관측한 accepted/disconnected sessions, connected sessions, received/sent bytes/packets, socket/parse/protocol errors |
| Smoke validation | `LibCommonTest.FastPortSmokeServerTests` | LoadRunner client metrics와 server telemetry를 함께 assert |

향후 MAUI dashboard는 아래 정보를 실시간으로 표시해야 한다.

- 연결된 세션 수
- TPS
- Latency / RTT
- 초당 send/recv bytes
- 초당 send/recv packets
- CCU
- accept/disconnect 빈도
- socket error 비율
- protocol/parse error count

이 정보를 안정적으로 표시하려면 metric 이름과 단위, 관측 주체가 먼저 고정되어야 한다.

## 2. Goals

### 2.1 Primary Goals

- [x] server-observed metric과 client-observed metric의 명명 규칙을 정의한다.
- [x] packet count, socket completion count, raw byte, parsed packet byte의 의미를 분리한다.
- [x] dashboard/export에 사용할 canonical metric field 목록을 정의한다.
- [x] `FastPortLoadRunner.MetricsSnapshot`과 `ServerTelemetrySnapshot`의 기존 필드 중 유지/변경/추가 후보를 정리한다.
- [x] telemetry export surface의 최소 요구사항을 정의한다.
- [x] MAUI dashboard가 직접 참조할 수 있는 export 포맷 방향을 정한다.
- [x] staged load validation 전에 필요한 metric semantic test 범위를 정한다.

### 2.2 Non-Goals

- 이번 Plan 단계에서 MAUI dashboard UI를 구현하지 않는다.
- 이번 Plan 단계에서 1,000 / 3,000 / 5,000 / 10,000 staged load test를 수행하지 않는다.
- 이번 Plan 단계에서 외부 metrics backend, database, OpenTelemetry exporter를 붙이지 않는다.
- 이번 Plan 단계에서 game server template을 구조화하지 않는다.
- 기존 telemetry code를 바로 대규모로 변경하지 않는다. 변경은 Design/Do 단계에서 확정한다.

## 3. Scope

### 3.1 In Scope

- metric naming convention 정의
  - `clientObserved*`
  - `serverObserved*`
  - `rawSocket*`
  - `parsedPacket*`
  - `sendCompletion*`
- canonical dashboard/export fields 정의
- server snapshot과 client snapshot의 비교 가능 항목 정리
- telemetry export surface 후보 설계
  - in-process snapshot provider
  - JSON snapshot/JSONL writer
  - lightweight HTTP endpoint 또는 stream endpoint 후보
- server telemetry semantic test 계획
- LoadRunner JSONL output과 server telemetry JSON naming 정렬 계획

### 3.2 Out of Scope

- MAUI project 생성
- dashboard chart/grid 구현
- distributed load runner
- long-running soak test
- OS tuning 자동화
- production monitoring backend 연동

## 4. Proposed Metric Contract Direction

### 4.1 Naming Principles

| Prefix / Term | Meaning |
|---------------|---------|
| `clientObserved` | LoadRunner 또는 테스트 클라이언트가 관측한 값 |
| `serverObserved` | 서버/엔진이 관측한 값 |
| `current` | snapshot 시점의 현재 값 |
| `total` | process 또는 collector reset 이후 누적 값 |
| `perSecond` | 이전 snapshot 대비 초당 변화량 |
| `rawSocketBytes` | socket receive/send completion 기준 byte |
| `parsedPacketBytes` | FastPort packet framing 후 packet size 합계 |
| `packetCount` | FastPort packet 단위 count |
| `sendCompletionCount` | socket send completion callback count |

### 4.2 Candidate Canonical Fields

| Field | Owner | Unit | Notes |
|-------|-------|------|-------|
| `timestamp` | shared | ISO-8601 | snapshot time |
| `clientObservedCurrentSessions` | client | count | LoadRunner active sessions |
| `serverObservedCurrentSessions` | server | count | server derived CCU |
| `clientObservedTotalSentPackets` | client | packets | LoadRunner sent packet count |
| `clientObservedTotalReceivedPackets` | client | packets | LoadRunner received response packet count |
| `serverObservedTotalReceivedPackets` | server | packets | parsed FastPort packet count |
| `serverObservedTotalSendCompletions` | server | completions | current `sentPackets` meaning |
| `clientObservedTotalSentBytes` | client | bytes | LoadRunner written bytes |
| `clientObservedTotalReceivedBytes` | client | bytes | LoadRunner received bytes |
| `serverObservedTotalParsedPacketBytes` | server | bytes | current `receivedBytes` meaning |
| `serverObservedTotalSentBytes` | server | bytes | socket send completion bytes |
| `clientObservedTps` | client | packets/sec | successful received responses per second |
| `clientObservedRttAverageMs` | client | ms | LoadRunner RTT |
| `serverObservedAcceptsPerSecond` | server | sessions/sec | derived from accept count delta |
| `serverObservedDisconnectsPerSecond` | server | sessions/sec | derived from disconnect count delta |
| `serverObservedSocketErrorRate` | server | ratio | socket errors / observed operation count |
| `serverObservedParseErrors` | server | count | protocol payload parse failures |
| `serverObservedProtocolErrors` | server | count | wrong protocol id / mismatch |

## 5. Success Criteria

- [ ] Design 문서에서 canonical metric contract가 확정된다.
- [ ] 기존 `MetricsSnapshot`과 `ServerTelemetrySnapshot`의 field 의미가 문서화된다.
- [ ] ambiguous fields(`sentPackets`, `receivedBytes`)의 rename/add strategy가 확정된다.
- [ ] telemetry export surface가 하나 이상 설계된다.
- [ ] export output이 camelCase JSON naming을 유지한다.
- [ ] MAUI dashboard가 읽을 수 있는 snapshot shape가 정의된다.
- [ ] focused unit tests 또는 smoke tests로 metric semantic이 검증된다.
- [ ] `dotnet build FastPortCharp.sln`이 통과한다.
- [ ] `dotnet test FastPortCharp.sln --no-build`가 통과한다.

## 6. Schedule

| Phase | Target Date | Status |
|-------|-------------|--------|
| Plan | 2026-04-28 | Completed |
| Design | TBD | Pending |
| Implementation | TBD | Pending |
| Check | TBD | Pending |
| Report/Archive | TBD | Pending |

## 7. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| 기존 필드 rename이 테스트/consumer를 깨뜨림 | Medium | Medium | backward-compatible field 또는 adapter를 검토 |
| server/client metric 단위가 섞여 dashboard가 잘못 표시함 | High | Medium | field name에 observed owner와 unit 의미를 포함 |
| exact packet count와 socket completion count를 혼동 | Medium | High | `packetCount`와 `sendCompletionCount`를 명시적으로 분리 |
| export API가 엔진에 protocol-specific 의존성을 추가 | High | Low | `LibNetworks` export는 protocol-neutral snapshot만 제공 |
| export 방식이 너무 일찍 특정 UI에 묶임 | Medium | Medium | JSON snapshot contract를 먼저 고정하고 MAUI는 다음 단계로 분리 |

## 8. Architecture Considerations

- `LibNetworks`는 protocol-neutral telemetry와 export contract만 제공한다.
- `FastPortSmokeServer`는 smoke protocol과 server telemetry export wiring을 담당할 수 있다.
- `FastPortLoadRunner`의 JSONL output과 server telemetry JSON output은 같은 naming policy를 사용한다.
- MAUI dashboard는 이 feature의 export contract를 소비하는 별도 단계로 둔다.
- staged load validation은 metric contract가 안정화된 뒤 수행한다.

## 9. References

- `HANDOFF.md`
- `docs/archive/2026-04/fastport-loadrunner/`
- `docs/archive/2026-04/fastport-smoke-server/`
- `FastPortLoadRunner/Metrics.cs`
- `LibNetworks/Telemetry/ServerTelemetry.cs`
- `LibCommonTest/FastPortSmokeServerTests.cs`
