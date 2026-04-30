# basesession-send-channel-queue-lock-reduction - Plan Document

> Version: 1.0.0 | Date: 2026-04-30 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`basesession-send-channel-queue-lock-reduction`는 `BaseSession`의 server send path에서 남아 있는 lock, copy, wake-up 비용을 줄이기 위한 구조 개선 feature다.

현재 send path는 `SocketAsyncEventArgs`/async socket 기반이지만, queued response 처리 자체는 `IBuffers` circular buffer와 `SemaphoreSlim` wake-up을 조합한다. 이 구조는 10K load validation에서 세션 유지와 server send backlog 제어까지는 성공했지만, IOCP급 low-lock send engine에 비해 다음 비용이 남아 있다.

- enqueue 시 `m_SendBuffers.CanReadSize` 확인 후 `Write`
- send worker에서 `CanReadSize -> Peek -> SendAsync -> Drain`
- `ArrayPoolCircularBuffers`의 `Write`, `Peek`, `Drain` lock
- send chunk마다 `byte[]` allocation/copy
- `m_SendSignal`/`m_SendSignalPosted` 기반 single-flight wake-up 관리

이번 feature의 목적은 send queue를 byte circular buffer 중심에서 message/item queue 중심으로 재설계할지 판단하고, 구현한다면 lock contention과 copy/allocation 비용을 줄이는 방향으로 `BaseSession` send architecture를 개선하는 것이다.

### 1.2 Background

직전 `adaptive-client-send-pacing-and-rtt-stability` 결과는 현재 구조가 10K 세션을 유지할 수 있음을 보여줬다.

| Metric | Current Adaptive Window |
|--------|------------------------:|
| Peak sessions | `10,000 / 10,000` |
| Final disconnects | `0` |
| Max pending request count | `36,384` |
| Max pending send requests | `212` |
| Server send backpressure events | `501` |
| Max send buffer bytes | `63,233` |
| `send|IOException|NoBufferSpaceAvailable` | `1,415` |
| Socket error rate | `0.05%` |
| Max TPS | `13,034.37` |
| RTT P95 | `16,234.27ms` |
| RTT P99 | `18,420.99ms` |
| Max scheduler drift | `320.86ms` |

해석:

- server send backlog는 이전보다 크게 낮아졌다.
- client send-buffer pressure도 adaptive pacing으로 통제 가능한 범위에 들어왔다.
- 남은 약점은 RTT P95와 scheduler drift이며, `BaseSession` send path 내부 비용이 tail latency와 scheduling pressure에 기여할 가능성이 있다.

### 1.3 PM Framing

이번 단계의 판단 질문은 다섯 가지다.

1. `m_SendSignal`을 제거하는 것이 목표인가, 아니면 send queue 구조를 바꿔 signal 필요성을 자연스럽게 줄이는 것이 목표인가?
2. `IBuffers` byte queue의 lock/copy 비용이 실제 10K tail latency와 scheduler drift에 의미 있게 영향을 주는가?
3. `Channel<SendItem>` 또는 custom MPSC queue가 현재 correctness invariant를 유지하면서 lock 횟수를 줄일 수 있는가?
4. send completion accounting, queue bound, transient backpressure retry 정책을 구조 변경 후에도 그대로 보존할 수 있는가?
5. 구조 변경이 server-side 지표를 개선하되 client pacing 결과를 망가뜨리지 않는가?

핵심 결론은 `m_SendSignal` 단독 제거가 아니다. 현재 구조에서 signal은 polling을 피하기 위한 wake-up 장치다. 구조 개선은 signal을 억지로 없애기보다, producer/consumer queue가 wake-up과 single-consumer semantics를 자연스럽게 제공하도록 만드는 방향이어야 한다.

## 2. Goals

### 2.1 Primary Goals

- [x] `BaseSession` send path의 현재 lock/copy/wake-up 병목을 명확히 정의한다.
- [ ] `IBuffers` send queue를 `Channel<SendItem>` 또는 custom MPSC queue로 대체할 수 있는지 설계한다.
- [ ] send enqueue path에서 `CanReadSize -> Write -> CanReadSize` 반복과 lock 횟수를 줄인다.
- [ ] send worker에서 `Peek -> new byte[] -> SendAsync -> Drain` copy/allocation 경로를 줄인다.
- [ ] 기존 correctness invariant를 유지한다: socket이 반환한 `sentSize`만큼만 logical send item을 완료 처리한다.
- [ ] queue bound, rejected-send telemetry, pending-send completion accounting을 유지한다.
- [ ] transient `NoBufferSpaceAvailable`/`WouldBlock` retry semantics를 유지한다.
- [ ] focused 10K validation에서 TPS, RTT tail, scheduler drift, socket error rate를 기존 adaptive baseline과 비교한다.

### 2.2 Non-Goals

- 이번 단계에서 receive path를 함께 재설계하지 않는다.
- 이번 단계에서 protocol payload/header format을 변경하지 않는다.
- 이번 단계에서 client pacing policy를 기본값으로 바꾸지 않는다.
- 이번 단계에서 OS/kernel socket tuning을 변경하지 않는다.
- 이번 단계에서 multi-machine load generation을 만들지 않는다.
- 이번 단계에서 IOCP 전용 Windows-only implementation으로 분기하지 않는다.
- 이번 단계에서 response를 silent drop하여 지표를 좋게 보이게 만들지 않는다.

## 3. Scope

### 3.1 In Scope

- `LibNetworks/Sessions/BaseSession` send queue 구조 분석과 개선
- `SendCompletionTracker`와 send item completion accounting 보존
- `SessionSendOptions` queue bound/drain budget/backoff option과의 호환성 검토
- `IServerTelemetry` send requested/completed/rejected/backpressure/drain-yield sample 보존
- partial send 처리 설계
- queue close/disconnect/cancellation 동작 검증
- unit tests for enqueue rejection, partial send, completion accounting, transient send backpressure, disconnect cancellation
- smoke/reduced/focused 10K validation 비교

### 3.2 Out of Scope

- broad session lifecycle rewrite
- receive buffer parser rewrite
- application-level flow-control protocol
- external telemetry backend integration
- public API breaking change
- platform-specific IOCP benchmark harness

## 4. Success Criteria

### 4.1 Functional Criteria

- [ ] send queue 구조와 invariants가 design 문서에 명확히 정의된다.
- [ ] send enqueue는 queue bound를 초과할 때 `false`를 반환하고 rejected-send telemetry를 기록한다.
- [ ] partial send 후 남은 byte는 같은 logical send item의 offset으로 보존된다.
- [ ] `RecordSendCompleted`는 logical response가 완전히 drain된 경우에만 증가한다.
- [ ] transient send backpressure는 drain/completion 없이 retry된다.
- [ ] disconnect/cancellation 시 send worker가 정상 종료된다.
- [ ] 기존 public send methods의 호출 계약이 유지된다.

### 4.2 Performance Criteria

Baseline: `artifacts/load-validation/s5-adaptive-pacing-window/summary.md`

| Metric | Baseline | Target |
|--------|---------:|-------:|
| Peak session ratio | `100.00%` | `>= 99.00%` |
| Final disconnect count | `0` | `<= 100` |
| Max TPS | `13,034.37` | no material regression, ideally higher |
| Max pending request count | `36,384` | no material increase |
| Max pending send requests | `212` | no material increase |
| Server send backpressure events | `501` | no material increase |
| Max send buffer bytes | `63,233` | no material increase |
| `NoBufferSpaceAvailable` count | `1,415` | `<= 1,500` first target |
| Socket error rate | `0.05%` | `<= 0.10%` |
| RTT P95 | `16,234.27ms` | lower, first target `<= 14,000ms` |
| RTT P99 | `18,420.99ms` | `<= 20,000ms` |
| Max scheduler drift | `320.86ms` | lower, first target `<= 200ms` |

The first target is intentionally pragmatic. This is a structural change, so correctness and no-regression come before chasing a single headline TPS number.

## 5. Candidate Design Directions

### 5.1 Bounded `Channel<SendItem>`

Use a per-session bounded channel of logical send items.

Potential shape:

```csharp
private readonly Channel<SendItem> m_SendQueue;
private long m_QueuedSendBytes;

private sealed class SendItem
{
    public byte[] Buffer;
    public int Offset;
    public int Remaining;
    public int RequestBytes;
}
```

Expected benefit:

- queue provides async wait semantics, so manual `SemaphoreSlim` signal may be removed;
- single consumer owns partial send offsets;
- enqueue path can use `Interlocked` byte accounting before channel write;
- no `Peek`/`Drain` lock pair per send operation.

Risks:

- bounded byte capacity is not native to `Channel<T>`, so byte budget must be implemented carefully;
- failed channel write must roll back byte budget;
- channel close/disconnect semantics must not strand pending completion accounting.

### 5.2 Custom MPSC Queue + Async Signal

Use `ConcurrentQueue<SendItem>` plus one async signal.

Expected benefit:

- simpler byte budget and enqueue rollback;
- preserves explicit single-flight drain behavior;
- less lock contention than `IBuffers`.

Risks:

- still needs a signal object;
- easy to reintroduce wake-up races;
- may not improve enough over the current signal-driven implementation.

### 5.3 Keep `IBuffers`, Rent Send Chunks

Keep the current byte queue but replace `new byte[readSize]` with `ArrayPool<byte>` and reduce `CanReadSize` calls.

Expected benefit:

- lower-risk tactical improvement;
- reduces GC pressure.

Risks:

- does not address lock count or `Peek/Drain` structural cost;
- may be too small to move RTT tail or scheduler drift.

## 6. Measurement Plan

### 6.1 Required Validation Runs

1. unit tests for queue and partial-send behavior;
2. `dotnet test FastPortCharp.sln --no-build` or full build+test if needed;
3. reduced smoke validation;
4. focused 10K adaptive baseline-compatible run with the new send queue;
5. optional uncapped run to detect whether server-side queue change affects client `NoBufferSpaceAvailable`.

Suggested output directories:

- `artifacts/load-validation/send-channel-queue-smoke`
- `artifacts/load-validation/s5-send-channel-queue-adaptive`
- `artifacts/load-validation/s5-send-channel-queue-uncapped`

### 6.2 Required Comparison Fields

- peak session ratio
- final disconnect count
- max pending request count
- max pending send requests
- server send backpressure events
- rejected send requests/bytes
- max send buffer bytes
- `send|IOException|NoBufferSpaceAvailable`
- socket error rate
- max TPS
- RTT P50/P95/P99
- max scheduler drift
- allocation/GC data if available from runtime counters

## 7. Schedule

| Phase | Target Date | Status |
|-------|------------|--------|
| Plan | 2026-04-30 | Completed |
| Design | 2026-04-30 | Pending |
| Implementation | TBD | Pending |
| Analyze | TBD | Pending |
| Report | TBD | Pending |

## 8. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Partial send accounting regression | High | Medium | Add fake socket/send override tests that return partial byte counts |
| Byte budget leak on enqueue failure/cancellation | High | Medium | Centralize budget reserve/commit/rollback and test failure paths |
| Channel full mode blocks application processing | Medium | Medium | Prefer non-blocking `TryWrite` after byte-budget reservation; reject explicitly on pressure |
| Removing `m_SendSignal` introduces missed wake-up | High | Low/Medium | Use channel reader await semantics or keep explicit signal in MPSC variant |
| More objects per send item increase GC pressure | Medium | Medium | Pool send item objects only if measurement shows pressure; avoid premature pooling |
| Structure improves server path but worsens client pacing metrics | Medium | Medium | Compare against adaptive baseline, not just isolated microbenchmarks |
| Large refactor destabilizes session lifecycle | High | Medium | Keep change scoped to send queue internals and preserve public APIs |

## 9. Architecture Considerations

- `LibNetworks` should own transport-level queueing and send completion semantics.
- Application sessions should continue to call `TryRequestSendBuffers` / `TryRequestSendMessage` without knowing queue internals.
- `SessionSendOptions.MaxQueuedBytes` remains the primary memory bound.
- `SendChunkBytes`, `MaxDrainBytesPerSignal`, and `MaxDrainOperationsPerSignal` may need reinterpretation under item-queue semantics.
- Telemetry names should remain stable so historical benchmark comparison remains valid.
- If `m_SendSignal` is removed, the replacement must prove equivalent wake-up behavior under concurrent producers.

## 10. Open Questions For Design

1. Should the first implementation choose `Channel<SendItem>` or custom MPSC queue?
2. Should send item buffers be raw `byte[]`, `IMemoryOwner<byte>`, or `ReadOnlyMemory<byte>` over rented arrays?
3. Do we keep per-session dedicated send task, or can the queue be drained directly from async send continuation later?
4. How should drain budget apply when one logical send item is larger than `SendChunkBytes`?
5. How do we expose queue depth: bytes only, item count, or both?
6. Do current tests have enough hooks to simulate partial send and transient send failure?

## 11. References

- `LibNetworks/Sessions/BaseSession.cs`
- `LibCommons/ArrayPoolCircularBuffers.cs`
- `LibNetworks/Sessions/SendCompletionTracker.cs`
- `LibNetworks/Sessions/SessionSendOptions.cs`
- `docs/load-validation-benchmark-results.md`
- `docs/archive/2026-04/server-send-backpressure-queue-drain-optimization/server-send-backpressure-queue-drain-optimization.report.md`
- `docs/archive/2026-04/client-send-buffer-pressure-receive-flow-control/client-send-buffer-pressure-receive-flow-control.report.md`
- `docs/archive/2026-04/adaptive-client-send-pacing-and-rtt-stability/adaptive-client-send-pacing-and-rtt-stability.report.md`

## 12. Next Phase

Recommended next command:

```bash
$pdca design basesession-send-channel-queue-lock-reduction
```
