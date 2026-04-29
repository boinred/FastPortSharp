# server-send-backpressure-queue-drain-optimization - Plan Document

> Version: 1.0.0 | Date: 2026-04-29 | Status: Draft
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`server-send-backpressure-queue-drain-optimization`는 10K 부하에서 확인된 server send backlog와 socket/send buffer pressure를 줄이기 위한 최적화 feature다.

직전 server-merged 10K 실행은 connect path가 아니라 send path가 병목임을 보여줬다.

- connect attempts: 10,000
- connect failures: 0
- peak current sessions: 8,611 / 10,000
- final disconnect count: 1,855
- max pending request count: 52,820
- max pending send requests: 180,466
- server send backpressure events: 878,503
- max send buffer bytes: 2,649,731
- dominant client socket error: `send|IOException|NoBufferSpaceAvailable` 6,586회

이번 feature의 목적은 서버 송신 큐가 무한히 누적되는 구조를 줄이고, send completion보다 send request가 빠르게 쌓일 때 명시적으로 backpressure를 적용하여 10K 유지율과 socket 안정성을 개선하는 것이다.

### 1.2 PM Framing

이번 단계의 판단 질문은 세 가지다.

1. server pending send requests를 10K 부하에서 제어 가능한 수준으로 낮출 수 있는가?
2. `NoBufferSpaceAvailable` send-side socket error를 줄일 수 있는가?
3. 최적화가 실제 세션 유지율과 RTT tail latency를 개선하는가?

성공은 단순히 threshold 통과가 아니라 원인 지표가 개선되는지로 판단한다.

## 2. Goals

### 2.1 Primary Goals

- [ ] server send queue가 무한 누적되지 않도록 bounded/backpressure 정책을 도입한다.
- [ ] send request와 send completion의 차이가 커질 때 response 생성/queueing을 제어한다.
- [ ] 10K focused run에서 max pending send requests를 의미 있게 낮춘다.
- [ ] 10K focused run에서 `send|IOException|NoBufferSpaceAvailable` 발생을 줄인다.
- [ ] max send buffer bytes를 줄인다.
- [ ] 기존 smoke/reduced validation과 client/server metrics export/merge compatibility를 유지한다.
- [ ] 최적화 전후를 같은 summary 지표로 비교할 수 있게 한다.

### 2.2 Non-Goals

- 이번 단계에서 protocol payload format을 변경하지 않는다.
- 이번 단계에서 client load generator의 send rate 정책을 바꾸지 않는다.
- 이번 단계에서 OS/kernel socket tuning 자동화를 만들지 않는다.
- 이번 단계에서 multi-machine load generation을 만들지 않는다.
- 이번 단계에서 production-grade flow-control protocol을 새로 설계하지 않는다.
- 이번 단계에서 `FastPortServer` sample host로 smoke echo behavior를 옮기지 않는다.

## 3. Scope

### 3.1 In Scope

- `LibNetworks/Sessions/BaseSession` send queue/drain behavior 분석과 개선
- send queue bound 또는 high-watermark 정책 검토
- send 작업 중복/동시성 제어 검토
- large queued buffer copy 축소 또는 chunked drain 검토
- `RecordSendRequested`, `RecordSendCompleted`, `RecordSendBackpressure`, `RecordSendBufferSample` 계측 유지 또는 보강
- `FastPortSmokeServer` echo response path에서 backpressure 상황의 동작 정책 확정
- reduced smoke validation과 focused 10K validation으로 개선 확인
- unit tests for send queue/backpressure semantics

### 3.2 Out of Scope

- Prometheus/OpenTelemetry integration
- MAUI/web dashboard
- distributed runner
- external load balancer or network stack tuning
- protocol-level ACK/window negotiation
- broad rewrite of session lifecycle

## 4. Success Criteria

### 4.1 Functional Criteria

- [ ] send queue/backpressure policy가 코드와 테스트로 명확해진다.
- [ ] server telemetry counters가 최적화 후에도 정상적으로 증가/감소한다.
- [ ] smoke validation이 통과한다.
- [ ] server metrics merge path가 계속 작동한다.
- [ ] 기존 client-only validation path가 깨지지 않는다.

### 4.2 Performance Criteria

Baseline: `artifacts/load-validation/s5-server-merged/summary.json`

| Metric | Baseline | Target |
|--------|---------:|-------:|
| Peak session ratio | 86.11% | >= 90.00% first target |
| Final disconnect count | 1,855 | <= 1,000 first target |
| Max pending send requests | 180,466 | <= 60,000 first target |
| Server send backpressure events | 878,503 | materially lower or intentionally bounded |
| Max send buffer bytes | 2,649,731 | <= 1,000,000 first target |
| `NoBufferSpaceAvailable` count | 6,586 | <= 2,000 first target |
| Max RTT P95 | 28,754.76 ms | materially lower without hiding failures |

The first target is intentionally pragmatic. If one iteration does not reach full 95% session ratio, it should still prove whether send backlog control is the right direction.

## 5. Measurement Plan

### 5.1 Baseline Command

The baseline was produced with:

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer \
  --Logging:LogLevel:Default Warning \
  --Logging:LogLevel:Microsoft Warning \
  --Telemetry:Output artifacts/load-validation/s5-server-merged/server.metrics.jsonl \
  --Telemetry:IntervalSeconds 1
```

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-server-merged \
  --server-metrics artifacts/load-validation/s5-server-merged/server.metrics.jsonl
```

### 5.2 Comparison Run

After implementation, run the same stage with a new output directory:

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer \
  --Logging:LogLevel:Default Warning \
  --Logging:LogLevel:Microsoft Warning \
  --Telemetry:Output artifacts/load-validation/s5-send-backpressure-optimized/server.metrics.jsonl \
  --Telemetry:IntervalSeconds 1
```

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-send-backpressure-optimized \
  --server-metrics artifacts/load-validation/s5-send-backpressure-optimized/server.metrics.jsonl
```

### 5.3 Required Comparison Fields

- peak session ratio
- final disconnect count
- max pending request count
- max pending send requests
- send requests per second
- send completions per second
- server send backpressure events
- max send buffer bytes
- socket error counts by class
- max RTT P95/P99

## 6. Candidate Hypotheses

### 6.1 Send Buffer Draining Is Over-Accumulating

`BaseSession.DoWorkSendBuffers` currently drains based on async send completion. Under high load, queued bytes and pending send requests may grow faster than completions, creating large socket writes and memory pressure.

Validation signal:

- pending send grows while send completion TPS stays below send request TPS
- send buffer bytes spikes before `NoBufferSpaceAvailable`

### 6.2 Response Generation Ignores Backpressure

`FastPortSmokeClientSession.OnReceived` generates echo responses even if the session send queue is already under pressure.

Validation signal:

- received packets continue increasing while pending send remains very high
- backpressure events explode without reducing response enqueue rate

### 6.3 Current Backpressure Counter Does Not Enforce Policy

Telemetry records backpressure but does not necessarily change behavior.

Validation signal:

- high backpressure events but no reduction in pending send or send buffer bytes

## 7. Schedule

| Phase | Target Date | Status |
|-------|-------------|--------|
| Plan | 2026-04-29 | In Progress |
| Design | 2026-04-29 | Pending |
| Implementation | TBD | Pending |
| Analyze | TBD | Pending |
| Report | TBD | Pending |

## 8. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Backpressure policy hides failures by dropping responses | High | Medium | summary must include drop/defer counters if introduced; do not count dropped responses as successful throughput |
| Send queue bound breaks normal smoke behavior | Medium | Medium | start with conservative thresholds and unit tests |
| Optimization improves server backlog but worsens client pending requests | Medium | Medium | compare both client pending and server pending fields |
| Changing `BaseSession` affects non-smoke users | High | Medium | keep changes protocol-neutral and covered by focused tests |
| Large refactor introduces lifecycle bugs | High | Medium | prefer surgical changes to send scheduling/drain behavior |
| 10K run remains host-resource limited | Medium | High | use relative improvement against same-machine baseline |

## 9. Architecture Considerations

- `LibNetworks` may enforce generic send queue bounds and drain semantics, but must not know echo/load-test protocol details.
- `FastPortSmokeServer` may choose application-level behavior when send queue is overloaded, but should keep that policy explicit.
- If response drop/defer is introduced, telemetry must make it visible.
- Existing `ObservedMetricsSnapshot` envelope must remain stable.
- Existing server-merged validation summary should be the primary comparison output.

## 10. Open Questions For Design

- Should the first optimization be bounded queue, chunked drain, single-flight send scheduling, or application-level response defer?
- What should happen when the send queue exceeds high watermark: skip response, defer, disconnect, or stop reading?
- Which threshold should be fixed vs configurable?
- Should `ServerTelemetrySnapshot` add explicit dropped/deferred response counters?
- Can send buffer copy size be capped without changing packet ordering?

## 11. References

- `HANDOFF.md`
- `docs/04-report/server-telemetry-export-merge-socket-error-classification.report.md`
- `artifacts/load-validation/s5-server-merged/summary.md`
- `artifacts/load-validation/s5-server-merged/summary.json`
- `artifacts/load-validation/s5-server-merged/s5-random-10k.combined.metrics.jsonl`
- `LibNetworks/Sessions/BaseSession.cs`
- `FastPortSmokeServer/Sessions/FastPortSmokeClientSession.cs`
- `LibNetworks/Telemetry/ServerTelemetry.cs`
