# client-send-buffer-pressure-receive-flow-control - Plan Document

> Version: 1.0.0 | Date: 2026-04-29 | Status: Draft
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`client-send-buffer-pressure-receive-flow-control`는 10K focused run에서 남아 있는 `send|IOException|NoBufferSpaceAvailable` 문제를 줄이기 위한 후속 feature다.

직전 `server-send-backpressure-queue-drain-optimization`은 server send backlog를 크게 줄였지만, client/kernel send-buffer pressure는 해결하지 못했다.

현재 기준선:

| Metric | Current |
|--------|--------:|
| Focused 10K result | Passed |
| Peak current sessions | 10,000 / 10,000 |
| Final disconnect count | 0 |
| Max pending request count | 36,653 |
| Max pending send requests | 905 |
| Server send backpressure events | 4,153 |
| Max send buffer bytes | 195,683 |
| `send|IOException|NoBufferSpaceAvailable` | 7,344 |

이번 feature의 목적은 10K session retention을 유지하면서 client send-side `NoBufferSpaceAvailable`과 elevated pending request를 낮추는 것이다.

### 1.2 PM Framing

이번 단계의 판단 질문은 네 가지다.

1. client `NoBufferSpaceAvailable`은 server send backlog가 아니라 client send pacing/receive pressure 때문에 남아 있는가?
2. server send loop가 한 번 깨어났을 때 큐를 끝까지 비우는 동작이 client receive/send 압박을 키우는가?
3. receive-side pause 또는 flow-control이 `MaxPendingRequestCount`와 client socket errors를 낮추는가?
4. send correctness invariant를 유지하면서 drain budget/chunk tuning만으로 효과를 볼 수 있는가?

중요한 설계 원칙:

- `Socket.SendAsync` 성공은 "상대가 모두 받았다"가 아니라 OS socket send buffer에 일부 또는 전부 복사됐다는 뜻이다.
- TCP send는 partial send가 가능하다.
- 따라서 send queue는 반드시 `sentSize`만큼만 drain해야 한다.
- queued buffer 전체를 한 번에 보내고 성공했다고 전체를 삭제하는 방식은 `sentSize == queuedBytes`가 아닌 한 데이터 유실 위험이 있다.

## 2. Goals

### 2.1 Primary Goals

- [ ] `send|IOException|NoBufferSpaceAvailable` 발생 원인을 client send pacing, server receive pressure, server response burst 중 어디에 더 가깝게 볼 수 있는지 검증한다.
- [ ] current focused 10K 기준 `NoBufferSpaceAvailable = 7,344`를 의미 있게 낮춘다.
- [ ] `MaxPendingRequestCount = 36,653`을 낮춘다.
- [ ] 10K focused run의 peak session ratio와 final disconnect 개선 효과를 유지한다.
- [ ] 기존 server send backlog 개선 효과를 회귀시키지 않는다.
- [ ] send queue drain은 성공한 byte 수만큼만 제거한다는 invariant를 코드/테스트/문서로 유지한다.
- [ ] receive-side flow-control 또는 pacing 정책이 적용된다면 해당 정책의 drop/defer/pause 상태가 telemetry나 summary에서 보이게 한다.

### 2.2 Non-Goals

- 이번 단계에서 queued send buffer 전체를 한 번에 socket send에 밀어 넣는 구조로 되돌리지 않는다.
- 이번 단계에서 `sentSize`보다 많은 byte를 queue에서 제거하지 않는다.
- 이번 단계에서 protocol-level ACK/window negotiation을 새로 만들지 않는다.
- 이번 단계에서 OS/kernel socket tuning 자동화를 만들지 않는다.
- 이번 단계에서 distributed/multi-machine load generation을 만들지 않는다.
- 이번 단계에서 정상 응답을 silent drop하여 pass처럼 보이게 만들지 않는다.
- 이번 단계에서 broad session lifecycle rewrite를 하지 않는다.

## 3. Scope

### 3.1 In Scope

- `LibNetworks/Sessions/BaseSession` send drain loop의 per-wake budget 검토
- `SessionSendOptions.SendChunkBytes` 조정 가능성 검토
- one wake cycle에서 처리할 max bytes 또는 max send operations 제한 검토
- transient send backpressure 이후 backoff 정책 검토
- receive-side pause/read throttle 정책 검토
- `FastPortSmokeServer` echo response burst와 pending request 증가 상관관계 분석
- `FastPortLoadValidation` summary에서 client socket classification, pending request, RTT tail, server send backlog를 함께 비교
- smoke/reduced validation과 focused 10K validation으로 변경 효과 검증
- send correctness invariant 관련 unit tests 보강

### 3.2 Out of Scope

- application protocol 변경
- production-grade backpressure protocol 설계
- external load balancer 또는 NIC/kernel tuning
- client workload의 목적 자체를 바꾸는 payload/protocol 변경
- `NoBufferSpaceAvailable`을 단순 retry로 숨기는 변경
- pass/fail threshold만 완화하는 변경

## 4. Success Criteria

### 4.1 Functional Criteria

- [ ] send queue는 `Socket.SendAsync`가 반환한 `sentSize`만큼만 drain한다.
- [ ] partial send 상황에서 unsent bytes가 유지된다는 test가 존재한다.
- [ ] drain budget 또는 receive flow-control 정책이 설계 문서에 명확히 정의된다.
- [ ] smoke validation이 통과한다.
- [ ] server metrics export/merge path가 계속 작동한다.
- [ ] load validation summary에서 `NoBufferSpaceAvailable`, pending request, pending send, rejected send를 비교할 수 있다.

### 4.2 Performance Criteria

Baseline: `artifacts/load-validation/s5-send-backpressure-iterate2/summary.md`

| Metric | Baseline | First Target |
|--------|---------:|-------------:|
| Peak session ratio | 100.00% | >= 99.00% |
| Final disconnect count | 0 | <= 100 |
| Max pending request count | 36,653 | <= 25,000 |
| Max pending send requests | 905 | <= 5,000 |
| Max send buffer bytes | 195,683 | <= 1,000,000 |
| `NoBufferSpaceAvailable` count | 7,344 | <= 5,000 |
| Socket error rate | 0.55% | materially lower or no worse |
| RTT P95 | 10,611.83 ms | materially lower or no worse |

The first target is intentionally pragmatic. A useful iteration should reduce `NoBufferSpaceAvailable` without losing the 10K peak-session stability gained by the previous feature.

## 5. Candidate Hypotheses

### 5.1 Drain Cycle Burst Pressure

Current send loop sends `SendChunkBytes` chunks repeatedly until the session send queue is empty.

Potential issue:

- even with 64 KiB chunks, one signal can produce a long burst;
- the burst may push responses faster than clients consume them;
- client pending requests and send-side socket pressure may rise even while server pending send stays low.

Candidate controls:

- max bytes per send-loop wake
- max sends per send-loop wake
- short cooperative yield after each budget
- adaptive backoff after transient `NoBufferSpaceAvailable`

### 5.2 Chunk Size Is Still Too Large For 10K Burst

Current default `SendChunkBytes` is 64 KiB.

Candidate experiment:

- compare 64 KiB vs 32 KiB vs 16 KiB
- track `NoBufferSpaceAvailable`, pending request, RTT P95/P99, max pending send, CPU/runtime drift

Risk:

- too-small chunks can increase syscall count and overhead;
- too-large chunks can keep socket buffer pressure high.

### 5.3 Receive-Side Read Pause Can Reduce Request Backlog

If a session has too much pending response/send pressure, continuing to read inbound requests may grow `MaxPendingRequestCount`.

Candidate control:

- pause or delay `RequestReceived()` when pending request/send pressure crosses a threshold;
- resume receive when pressure falls below a low watermark.

Risk:

- receive pause creates TCP backpressure;
- this can reduce request flood, but may increase client send wait time if thresholds are too aggressive.

### 5.4 Client Pacing May Be Required For The Load Generator

The load generator may continue sending requests while responses are slow, making the remaining issue a client-side workload pacing problem rather than a server-only flow-control problem.

Candidate control:

- cap per-session outstanding requests in the load runner;
- compare uncapped load generator behavior with capped behavior.

Risk:

- changing the client load model may make comparison against previous 10K results less direct.

## 6. Measurement Plan

### 6.1 Baseline Command

Use the latest current-code focused 10K result as the baseline:

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer \
  --Logging:LogLevel:Default Warning \
  --Logging:LogLevel:Microsoft Warning \
  --Telemetry:Output artifacts/load-validation/s5-send-backpressure-iterate2/server.metrics.jsonl \
  --Telemetry:IntervalSeconds 1
```

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-send-backpressure-iterate2 \
  --server-metrics artifacts/load-validation/s5-send-backpressure-iterate2/server.metrics.jsonl
```

### 6.2 Comparison Runs

Design should define one or more focused variants before implementation.

Suggested comparison directories:

- `artifacts/load-validation/s5-receive-flow-control-smoke`
- `artifacts/load-validation/s5-receive-flow-control-16k`
- `artifacts/load-validation/s5-receive-flow-control-budgeted-drain`
- `artifacts/load-validation/s5-receive-flow-control-10k`

### 6.3 Required Comparison Fields

- peak session ratio
- final disconnect count
- max pending request count
- max pending send requests
- max send buffer bytes
- server send backpressure events
- rejected send requests/bytes
- `send|IOException|NoBufferSpaceAvailable`
- socket error rate
- RTT P95/P99
- max drift
- server merge matched/unmatched sample counts

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
| Draining more than `sentSize` causes data loss | High | Low | Make `sentSize`-only drain an explicit invariant and add partial-send tests |
| Sending full queued buffer increases `NoBufferSpaceAvailable` | High | Medium | Keep bounded chunking; evaluate budgeted drain instead of full-queue send |
| Receive pause reduces throughput too much | Medium | Medium | Use high/low watermark and compare RTT/pending/error metrics together |
| Client pacing changes benchmark semantics | Medium | Medium | Separate server-flow-control run from client-paced run |
| Smaller chunks increase CPU/syscall overhead | Medium | Medium | Track max drift, RTT tail, and stage duration |
| Fix hides errors by dropping responses | High | Medium | Keep rejected/deferred counters visible in summary |
| Server backlog regresses | High | Low | Keep max pending send and max send buffer success criteria |

## 9. Architecture Considerations

- `LibNetworks` owns generic send/receive pressure mechanics.
- `FastPortSmokeServer` may expose diagnostic response policy only if the behavior is telemetry-visible.
- `FastPortLoadValidation` should remain the source of truth for before/after comparison.
- Flow-control decisions must not depend on protobuf payload details.
- Send completion accounting must continue to use logical request-size tracking.
- Socket error classification remains the primary signal for whether this feature works.

## 10. Open Questions For Design

- Should drain budget be configured by bytes, send operations, elapsed time, or a combination?
- Should receive pause live in `BaseSession`, `FastPortSmokeClientSession`, or a configurable policy object?
- What pressure signal should pause receive: pending request count, pending send requests, queued send bytes, or socket backpressure events?
- What high/low watermark avoids oscillation?
- Should client load runner outstanding-request cap be a separate experiment or part of this feature?
- Should `SessionSendOptions` grow options such as `MaxDrainBytesPerSignal`, `MaxDrainOperationsPerSignal`, or `TransientSendBackoffMs`?

## 11. References

- `docs/archive/2026-04/server-send-backpressure-queue-drain-optimization/server-send-backpressure-queue-drain-optimization.report.md`
- `docs/load-validation-benchmark-results.md`
- `artifacts/load-validation/s5-send-backpressure-iterate2/summary.md`
- `LibNetworks/Sessions/BaseSession.cs`
- `LibNetworks/Sessions/SessionSendOptions.cs`
- `LibNetworks/Sessions/SendCompletionTracker.cs`
- `FastPortSmokeServer/Sessions/FastPortSmokeClientSession.cs`
- `FastPortLoadValidation/LoadValidationSummaryWriter.cs`
