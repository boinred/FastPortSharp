# basesession-send-channel-queue-lock-reduction - Design Document

> Version: 1.0.0 | Date: 2026-04-30 | Status: Completed
> Level: Starter | Plan: docs/01-plan/features/basesession-send-channel-queue-lock-reduction.plan.md

---

## 1. Overview

`basesession-send-channel-queue-lock-reduction`는 `BaseSession`의 send queue를 `IBuffers` byte circular buffer에서 `Channel<SendQueueItem>` 기반 logical item queue로 바꾼다.

목표는 세 가지다.

1. send hot path에서 `ArrayPoolCircularBuffers.Write/Peek/Drain` lock을 제거한다.
2. send worker의 `Peek -> new byte[] -> SendAsync -> Drain` copy/allocation 경로를 제거한다.
3. `m_SendSignal`/`m_SendSignalPosted` 수동 wake-up을 Channel reader 대기로 대체한다.

이번 설계는 receive path를 건드리지 않는다. `m_ReceivedBuffers`와 `m_ReceivedPackets` 구조는 그대로 둔다.

## 2. Current Architecture

현재 send 흐름:

```text
TryRequestSendBuffers
  -> packet byte[] 생성
  -> m_SendBuffers.CanReadSize 확인
  -> m_SendBuffers.Write(packet)
  -> SendCompletionTracker.Enqueue(packet.Length)
  -> m_SendSignal.Release()

DoWorkSendBuffers
  -> m_SendSignal.WaitAsync()
  -> while m_SendBuffers.CanReadSize > 0
       -> queuedBytes = m_SendBuffers.CanReadSize
       -> sendBuffers = new byte[readSize]
       -> m_SendBuffers.Peek(ref sendBuffers)
       -> Socket.SendAsync(sendBuffers)
       -> m_SendBuffers.Drain(sentSize)
       -> SendCompletionTracker.Complete(sentSize)
```

문제 지점:

- `CanReadSize`, `Write`, `Peek`, `Drain`이 send queue 상태를 반복 조회한다.
- `ArrayPoolCircularBuffers`는 `Write`, `Peek`, `Drain`마다 lock을 잡는다.
- send worker는 매 chunk마다 새 `byte[]`를 만든다.
- `SendCompletionTracker`는 logical request completion을 맞추기 위해 별도 lock을 사용한다.
- `m_SendSignal`은 필요했던 wake-up 장치지만, queue 자체가 async wait를 제공하면 제거 가능하다.

## 3. Proposed Architecture

### 3.1 Send Queue Shape

`BaseSession`에 send 전용 Channel과 byte budget을 추가한다.

```csharp
private readonly Channel<SendQueueItem> m_SendQueue;
private long m_QueuedSendBytes;

private sealed class SendQueueItem
{
    public SendQueueItem(byte[] buffer)
    {
        Buffer = buffer;
        Length = buffer.Length;
    }

    public byte[] Buffer { get; }
    public int Offset { get; private set; }
    public int Length { get; }
    public int Remaining => Length - Offset;
    public bool IsComplete => Offset >= Length;

    public ReadOnlyMemory<byte> GetNextChunk(int maxBytes)
    {
        int size = Math.Min(Remaining, maxBytes);
        return Buffer.AsMemory(Offset, size);
    }

    public void Advance(int sentBytes)
    {
        Offset += sentBytes;
    }
}
```

Channel 생성:

```csharp
m_SendQueue = Channel.CreateUnbounded<SendQueueItem>(new UnboundedChannelOptions
{
    SingleReader = true,
    SingleWriter = false,
    AllowSynchronousContinuations = false
});
```

설계 의도:

- 실제 bound는 Channel item count가 아니라 `m_QueuedSendBytes`가 담당한다.
- `SingleReader = true`로 partial send offset 소유권을 send worker에 고정한다.
- `SingleWriter = false`로 session 외부/파생 클래스에서 동시에 send 요청이 들어와도 허용한다.
- unbounded Channel을 사용해 Channel 자체의 item-count full 상태와 byte-budget full 상태가 엇갈리는 문제를 피한다.

### 3.2 Constructor Compatibility

기존 생성자 시그니처는 유지한다.

```csharp
BaseSession(..., IBuffers receivedBuffers, IBuffers sendbuffers, ...)
```

단, `sendbuffers`는 send hot path에서 더 이상 사용하지 않는다. 이유는 다음과 같다.

- `BaseSessionClient`, `BaseSessionServer`, smoke server factory, tests의 생성자 호출부를 한 번에 깨지 않기 위함
- 이번 feature의 범위를 send internals로 제한하기 위함
- 후속 cleanup에서 obsolete constructor 또는 factory contract 정리를 별도 feature로 분리하기 위함

## 4. Enqueue Design

### 4.1 Byte Budget Reservation

enqueue는 먼저 byte budget을 예약한다.

```csharp
private bool TryReserveQueuedSendBytes(int bytes, out int queuedBefore, out int queuedAfter)
{
    while (true)
    {
        long current = Volatile.Read(ref m_QueuedSendBytes);
        long next = current + bytes;

        queuedBefore = ToTelemetryQueuedBytes(current);
        queuedAfter = ToTelemetryQueuedBytes(next);

        if (next > m_SendOptions.NormalizedMaxQueuedBytes)
        {
            return false;
        }

        if (Interlocked.CompareExchange(ref m_QueuedSendBytes, next, current) == current)
        {
            return true;
        }
    }
}
```

`ToTelemetryQueuedBytes`는 telemetry API가 `int`를 받으므로 `int.MaxValue`로 clamp한다. 실제 max queued bytes option은 `int`라 정상 설정에서는 clamp가 발생하지 않는다.

### 4.2 TryRequestSendBuffers Flow

새 enqueue 흐름:

```text
TryRequestSendBuffers(buffers)
  -> validate buffers length
  -> packet byte[] 생성
  -> TryReserveQueuedSendBytes(packet.Length)
     -> 실패: RecordSendBackpressure + RecordSendRejected 후 false
  -> SendQueueItem 생성
  -> m_SendQueue.Writer.TryWrite(item)
     -> 실패: byte budget rollback + RecordSendRejected 후 false
  -> RecordSendRequested(packet.Length, queuedAfter)
  -> queuedAfter > threshold 이면 RecordSendBackpressure
  -> true
```

중요한 순서:

- `RecordSendRequested`는 Channel write가 성공한 뒤에만 호출한다.
- byte budget 예약 후 Channel write 실패 시 반드시 rollback한다.
- Channel writer가 닫힌 경우에는 enqueue 실패로 처리한다.
- `SendCompletionTracker.Enqueue`는 호출하지 않는다. logical item 자체가 completion 단위가 된다.

### 4.3 Queue Rejection Semantics

기존 정책을 유지한다.

| Condition | Return | Telemetry |
|-----------|--------|-----------|
| zero length | `false` | no send request |
| packet too large | `false` | no send request |
| byte budget exceeded | `false` | send backpressure + rejected |
| channel closed/disconnected | `false` | rejected |
| write accepted | `true` | send requested |

## 5. Send Worker Design

### 5.1 Worker Loop

`m_SendSignal.WaitAsync`는 제거한다. send worker는 Channel reader를 기다린다.

```text
while not cancelled:
  await m_SendQueue.Reader.WaitToReadAsync(cancellationToken)

  drainedBytesThisCycle = 0
  sendOperationsThisCycle = 0

  while m_SendQueue.Reader.TryRead(out item):
    while item.Remaining > 0:
      if drain budget exhausted:
        RecordSendDrainYield(current queued bytes)
        await Task.Yield()
        reset cycle budget

      socket = m_Socket
      if socket is null or not connected:
        return

      readSize = min(item.Remaining, SendChunkBytes, remaining budget bytes)
      sentSize = await SendSocketAsync(socket, item.Buffer.AsMemory(item.Offset, readSize), token)

      if transient backpressure:
        RecordSocketError + RecordSendBackpressure
        WaitTransientSendBackoffAsync()
        continue without advancing item

      if sentSize <= 0:
        RequestDisconnect()
        return

      item.Advance(sentSize)
      ReleaseQueuedSendBytes(sentSize)
      RecordSent(sentSize)
      RecordSendBufferSample(current queued bytes)

      if item complete:
        RecordSendCompleted()
```

### 5.2 Drain Budget Semantics

기존 `MaxDrainBytesPerSignal`/`MaxDrainOperationsPerSignal`은 `MaxDrainBytesPerCycle`/`MaxDrainOperationsPerCycle`처럼 동작한다.

이름은 이번 feature에서 변경하지 않는다. 옵션 이름 변경은 public-ish surface를 건드리므로 별도 cleanup 대상이다.

Budget exhaustion 시:

- current item은 requeue하지 않는다.
- current item offset을 보존한 상태로 `Task.Yield()` 한다.
- yield 이후 cycle counter를 reset하고 같은 item을 계속 보낸다.
- queue에 남은 byte가 있으면 `RecordSendDrainYield(queuedBytes)`를 기록한다.

이 방식은 current item의 순서를 보존하면서 한 세션 send worker가 긴 backlog를 연속 점유하는 시간을 줄인다.

### 5.3 Partial Send Invariant

핵심 invariant:

```text
queued bytes 감소량 == Socket.SendAsync returned sentSize
item offset 증가량 == Socket.SendAsync returned sentSize
RecordSendCompleted 호출 시점 == item.Offset == item.Length
```

금지:

- `sentSize`보다 많은 byte를 완료 처리하지 않는다.
- transient `NoBufferSpaceAvailable`/`WouldBlock`에서는 offset 또는 queued byte를 변경하지 않는다.
- item이 끝나기 전에 `RecordSendCompleted`를 호출하지 않는다.

## 6. Telemetry Design

Telemetry method 이름은 유지한다.

| Telemetry | New Meaning |
|-----------|-------------|
| `RecordSendRequested(bytes, queuedBytes)` | logical send item accepted |
| `RecordSendCompleted()` | logical send item fully sent |
| `RecordSendRejected(bytes, queuedBytes)` | logical send item rejected before Channel acceptance |
| `RecordSendBackpressure()` | byte budget exceeded, threshold exceeded, or transient socket send pressure |
| `RecordSendDrainYield(queuedBytes)` | cycle budget exhausted while queued bytes remain |
| `RecordSendBufferSample(queuedBytes)` | `m_QueuedSendBytes` snapshot |
| `RecordSent(bytes)` | socket send operation completed with `bytes` |

주의:

- `RecordSent`는 현재처럼 socket send chunk 단위로 유지한다.
- `RecordSendCompleted`는 request/item 단위로 유지한다.
- `SendCompletionTracker` lock은 send hot path에서 제거한다.

## 7. Error Handling

### 7.1 Transient Send Backpressure

유지할 retry 대상:

```csharp
SocketError.NoBufferSpaceAvailable
SocketError.WouldBlock
```

처리:

- `RecordSocketError()`
- `RecordSendBackpressure()`
- `WaitTransientSendBackoffAsync()`
- current item offset 유지
- queued bytes 유지
- completion 미기록

### 7.2 Non-Transient Socket Error

처리:

- `RecordSocketError()`
- `RequestDisconnect()`
- send worker 종료

남은 Channel item은 전송되지 않는다. disconnect 이후 pending telemetry 정리는 이번 feature에서 보정하지 않는다. 기존 구조도 disconnect 중 pending send를 성공으로 만들지 않았다.

### 7.3 Cancellation and Disconnect

`RequestDisconnect()`에 send Channel close를 추가한다.

```csharp
m_SendQueue.Writer.TryComplete();
m_ReceivedPackets.Writer.TryComplete();
```

`DoWorkSendBuffers`는 다음 예외를 정상 종료로 취급한다.

- `OperationCanceledException`
- `ChannelClosedException`
- `ObjectDisposedException`

## 8. Data Model

### 8.1 New Private Type

`SendQueueItem`

| Field/Property | Type | Meaning |
|----------------|------|---------|
| `Buffer` | `byte[]` | complete packet bytes including header |
| `Offset` | `int` | next byte offset to send |
| `Length` | `int` | total packet length |
| `Remaining` | `int` | `Length - Offset` |
| `IsComplete` | `bool` | all bytes sent |

### 8.2 Changed Fields

Remove from send path:

```csharp
private readonly IBuffers m_SendBuffers;
private readonly SemaphoreSlim m_SendSignal;
private readonly SendCompletionTracker m_SendCompletionTracker;
private int m_SendSignalPosted;
```

Add:

```csharp
private readonly Channel<SendQueueItem> m_SendQueue;
private long m_QueuedSendBytes;
```

`SendCompletionTracker` type can remain in the repository for now because existing unit tests cover it and removing it is not required for behavior. It should no longer be used by `BaseSession`.

## 9. API Contract

No public API break.

| API | Contract |
|-----|----------|
| `RequestSendBuffers(ReadOnlySpan<byte>)` | fire-and-forget wrapper remains |
| `TryRequestSendBuffers(ReadOnlySpan<byte>)` | returns `true` only after queue acceptance |
| `RequestSendString(string)` | unchanged |
| `RequestSendMessage<T>(int, IMessage<T>)` | unchanged |
| `TryRequestSendMessage<T>(int, IMessage<T>)` | unchanged |
| constructors with `sendBuffers` | remain source compatible |

Potential future cleanup:

- add constructor overloads without `sendBuffers`
- mark old constructor parameter as obsolete or update factories
- remove unused `SendCompletionTracker` if no longer needed by tests

## 10. Implementation Order

1. Add `SendQueueItem` private type to `BaseSession`.
2. Add `m_SendQueue` and `m_QueuedSendBytes`.
3. Initialize Channel in constructor.
4. Replace `TryRequestSendBuffers` queue write logic with byte budget reservation and Channel `TryWrite`.
5. Rewrite `DoWorkSendBuffers` to read from Channel and send item chunks by offset.
6. Add helpers:
   - `TryReserveQueuedSendBytes`
   - `ReleaseQueuedSendBytes`
   - `GetQueuedSendBytesSnapshot`
   - `RollbackQueuedSendBytes`
7. Remove `SignalSendLoop`, `TrackPendingSendRequest`, and `CompletePendingSendRequests` from `BaseSession`.
8. Complete send Channel in `RequestDisconnect`.
9. Update unit tests.
10. Run build/tests and load validation.

## 11. Test Plan

### 11.1 Unit Tests

Update `LibCommonTest/BaseSessionSendPolicyTests.cs`.

Required tests:

1. `TryRequestSendBuffers` rejects packet over byte budget.
2. `DoWorkSendBuffers` sends accepted item and records send completion once.
3. Partial send does not complete item until all bytes are sent.
4. Partial send decrements queued bytes only by `sentSize`.
5. Transient `NoBufferSpaceAvailable` does not advance offset or complete item.
6. Drain budget exhaustion records `SendDrainYield`.
7. Channel closed/disconnect exits send worker cleanly.
8. Multiple accepted items preserve FIFO completion order.

Existing helper `TestSession.SendSocketAsync` override is sufficient for partial send and transient backpressure tests.

### 11.2 Command Validation

Run at minimum:

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
```

Then smoke/reduced validation if build/test pass:

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer \
  --Logging:LogLevel:Default Warning \
  --Logging:LogLevel:Microsoft Warning \
  --Telemetry:Output artifacts/load-validation/send-channel-queue-smoke/server.metrics.jsonl \
  --Telemetry:IntervalSeconds 1
```

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile smoke \
  --stage smoke-random-25 \
  --output artifacts/load-validation/send-channel-queue-smoke \
  --server-metrics artifacts/load-validation/send-channel-queue-smoke/server.metrics.jsonl
```

Focused 10K comparison:

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer \
  --Logging:LogLevel:Default Warning \
  --Logging:LogLevel:Microsoft Warning \
  --Telemetry:Output artifacts/load-validation/s5-send-channel-queue-adaptive/server.metrics.jsonl \
  --Telemetry:IntervalSeconds 1
```

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-send-channel-queue-adaptive \
  --server-metrics artifacts/load-validation/s5-send-channel-queue-adaptive/server.metrics.jsonl
```

### 11.3 Performance Acceptance

Compare against `artifacts/load-validation/s5-adaptive-pacing-window/summary.md`.

Required no-regression checks:

- peak sessions stay `>= 99%`;
- final disconnects stay `<= 100`;
- socket error rate stays `<= 0.10%`;
- `NoBufferSpaceAvailable` stays `<= 1,500`;
- RTT P99 stays `<= 20,000ms`;
- max pending send requests and max send buffer bytes do not materially regress.

Target improvements:

- max scheduler drift moves below `200ms`;
- RTT P95 moves toward or below `14,000ms`;
- max TPS does not decrease materially from `13,034.37`.

## 12. Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| Byte budget leak when Channel write fails | queued bytes telemetry and rejection behavior become wrong | rollback helper and unit test writer-closed path |
| Completion count changes because item queue no longer uses `SendCompletionTracker` | pending send requests may become inaccurate | complete once per `SendQueueItem`, add partial-send test |
| Channel unbounded item count causes memory concern | byte budget still bounds accepted bytes | reject before write by byte budget |
| Send worker monopolizes execution while draining large item | tail latency and scheduler drift may worsen | preserve drain cycle budget and `Task.Yield()` |
| Constructor keeps unused `sendbuffers` parameter | API looks confusing | document as compatibility debt and defer cleanup |
| `RecordSent` packet count changes under chunk behavior | benchmark comparison may be noisy | keep `RecordSent` per socket send operation as current behavior |

## 13. Deferred Items

- Pool send item buffers with `ArrayPool<byte>` or `IMemoryOwner<byte>`.
- Remove `sendBuffers` constructor parameter from session factories.
- Rename `MaxDrainBytesPerSignal` to cycle-oriented naming.
- Add runtime allocation counters to load validation summary.
- Explore direct SAEA send reuse after item queue correctness is stable.

## 14. Design Decision

Choose **`Channel<SendQueueItem>` with explicit byte budget** for the first implementation.

Rejected alternatives:

- `ConcurrentQueue<SendItem> + SemaphoreSlim`: reduces `IBuffers` lock but keeps the manual wake-up race surface.
- `IBuffers + ArrayPool rented chunks`: lower risk but does not remove the structural `Peek/Drain` locks.
- Channel bounded by item count only: does not enforce memory pressure accurately because item sizes vary.

This decision directly targets the current bottleneck while keeping correctness constraints testable.

## 15. Next Phase

Recommended next command:

```bash
$pdca do basesession-send-channel-queue-lock-reduction
```
