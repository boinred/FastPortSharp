# server-send-backpressure-queue-drain-optimization - Design Document

> Version: 1.0.0 | Date: 2026-04-29 | Status: Draft
> Level: Starter | Plan: docs/01-plan/features/server-send-backpressure-queue-drain-optimization.plan.md

---

## 1. Overview

이번 feature는 10K 부하에서 확인된 server send backlog와 socket/send buffer pressure를 줄이기 위한 첫 번째 최적화 단계다.

서버 병목 기준선:

| Metric | Baseline |
|--------|---------:|
| Peak current sessions | 8,611 / 10,000 |
| Peak session ratio | 86.11% |
| Final disconnect count | 1,855 |
| Max pending request count | 52,820 |
| Max pending send requests | 180,466 |
| Server send backpressure events | 878,503 |
| Max send buffer bytes | 2,649,731 |
| `send|IOException|NoBufferSpaceAvailable` | 6,586 |

핵심 문제는 server response 생성 속도와 send queue drain 속도가 균형을 이루지 못한다는 점이다. 현재 backpressure는 계측되지만 send queueing 동작을 충분히 제어하지 않는다.

## 2. Design Decision

### 2.1 Primary Optimization

1차 최적화는 다음 두 가지를 같이 적용한다.

1. `BaseSession` send loop를 single-flight chunked drain으로 바꾼다.
2. `BaseSession` send queue에 bounded enqueue guard를 둔다.

이 선택의 이유:

- `BaseSession`의 send loop는 현재 readable send buffer 전체를 한 번에 `Peek`해서 socket send에 넘긴다.
- high load에서 큰 queued buffer가 그대로 large socket write가 되고, OS/socket buffer pressure를 키울 수 있다.
- send completion보다 enqueue가 빠를 때 queue size가 계속 커지므로, enqueue 단계에서 명시적인 high watermark가 필요하다.
- application-level response drop/defer만 먼저 적용하면 네트워크 엔진의 send drain 문제가 남는다.

### 2.2 Deferred Alternatives

다음은 이번 1차 구현에서 제외한다.

- protocol-level ACK/window flow control
- client send rate 변경
- OS/kernel socket tuning
- multi-machine load generation
- full session lifecycle rewrite
- `FastPortSmokeServer` 전용 receive stop/read pause 정책

Receive pause는 TCP backpressure를 만들 수 있지만, 현재 client send-side `NoBufferSpaceAvailable`이 이미 관측되었기 때문에 첫 시도에서는 더 직접적인 server send queue 제어를 우선한다.

## 3. Current Send Path

### 3.1 Enqueue

`FastPortSmokeClientSession.OnReceived`는 echo response를 만들고 `SendMessage`를 호출한다.

```text
FastPortSmokeClientSession.OnReceived
  -> SendMessage
  -> BaseSession.RequestSendMessage
  -> BaseSession.RequestSendBuffers
  -> m_SendBuffers.Write
  -> ServerTelemetry.RecordSendRequested
```

### 3.2 Drain

`BaseSession.DoWorkSendBuffers`는 send buffer에 읽을 수 있는 byte가 있으면 전체 queued bytes를 복사하고 socket send를 요청한다.

```text
DoWorkSendBuffers
  -> queuedBytes = m_SendBuffers.CanReadSize
  -> byte[] sendBuffers = new byte[queuedBytes]
  -> m_SendBuffers.Peek(ref sendBuffers)
  -> socket.SendAsync(m_SockenEventsSent)
  -> OnSocketEventsSentCompleted
  -> ServerTelemetry.RecordSent
  -> m_SendBuffers.Drain(bytesTransferred)
```

### 3.3 Observed Problems

- queued bytes 전체를 한 번에 send buffer로 만든다.
- send loop가 completion 기반으로 명확히 single-flight 제어되지 않는다.
- telemetry의 `PendingSendRequests`는 enqueue당 증가하지만 socket completion 기준 감소라 request 단위 pending을 정확히 반영하기 어렵다.
- high watermark 초과 시 `RecordSendBackpressure`만 하고 queueing은 계속된다.

## 4. Target Architecture

### 4.1 Ownership

| Area | Module | Responsibility |
|------|--------|----------------|
| generic send queue guard | `LibNetworks/Sessions/BaseSession` | max queued bytes 초과 enqueue 거부 |
| single-flight chunked drain | `LibNetworks/Sessions/BaseSession` | one socket send at a time, bounded chunk size |
| request completion accounting | `LibNetworks/Sessions/BaseSession` + `LibNetworks/Telemetry` | request length queue로 pending request 정확도 개선 |
| rejected send telemetry | `LibNetworks/Telemetry` | rejected request/bytes visibility |
| smoke response policy | `FastPortSmokeServer` | send rejection 시 response dropped 로그/telemetry 해석 |
| validation comparison | `FastPortLoadValidation` | 기존 server-merged summary 사용 |

### 4.2 Session Send Options

새 옵션 record를 `LibNetworks/Sessions`에 추가한다.

```csharp
public sealed record SessionSendOptions(
    int MaxQueuedBytes = 1024 * 1024,
    int SendChunkBytes = 64 * 1024);
```

초기값:

- `MaxQueuedBytes`: 1 MiB
- `SendChunkBytes`: 64 KiB

이 값은 현재 baseline의 max send buffer `2,649,731 bytes`를 1차 목표인 `<= 1,000,000 bytes` 아래로 제어하기 위한 보수적 시작점이다.

Constructor compatibility:

- 기존 `BaseSession`, `BaseSessionClient`, `BaseSessionServer` 생성자는 유지한다.
- 새 overload는 `SessionSendOptions`를 선택적으로 받는다.
- 기본 생성자 경로는 `SessionSendOptions` 기본값을 사용한다.

## 5. Send Queue Guard

### 5.1 New API Shape

`BaseSession` 내부 protected send API를 bool-returning 형태로 확장한다.

```csharp
protected bool TryRequestSendBuffers(ReadOnlySpan<byte> buffers);
protected bool TryRequestSendMessage<T>(int packetId, IMessage<T> message)
    where T : IMessage<T>;
```

기존 API는 유지한다.

```csharp
protected void RequestSendBuffers(ReadOnlySpan<byte> buffers);
protected void RequestSendMessage<T>(int packetId, IMessage<T> message)
    where T : IMessage<T>;
```

기존 API는 내부적으로 `Try...`를 호출하고 실패를 무시한다. 새 코드, 특히 `FastPortSmokeClientSession`, 은 bool을 확인한다.

### 5.2 Guard Logic

Pseudo flow:

```text
packetBytes = add packet size header
queuedBytes = m_SendBuffers.CanReadSize

if queuedBytes + packetBytes.Length > MaxQueuedBytes:
    ServerTelemetry.RecordSendBackpressure()
    ServerTelemetry.RecordSendRejected(packetBytes.Length, queuedBytes)
    return false

m_SendBuffers.Write(packetBytes)
TrackPendingSendRequest(packetBytes.Length)
ServerTelemetry.RecordSendRequested(packetBytes.Length, queuedBytesAfterWrite)
SignalSendLoop()
return true
```

Notes:

- This is a guard, not a retry loop.
- Rejected sends are not counted as successful throughput.
- Rejected sends must be visible in `summary.json`.

### 5.3 Smoke Server Behavior

`FastPortSmokeClientSession.SendMessage` changes from `void` to `bool`.

`OnReceived` behavior:

```text
if !SendMessage(...):
    log debug/warning at controlled level
    return
```

The response is dropped under overload. This is acceptable for this diagnostic feature only because:

- the drop is explicitly counted as rejected send telemetry
- client pending requests will reveal unanswered requests
- the goal is to prevent unbounded server queue/socket pressure first

## 6. Single-Flight Chunked Drain

### 6.1 Replace Polling Send Loop

The current send loop polls every 1ms. The target loop should be signal-driven.

Fields:

```csharp
private readonly SemaphoreSlim m_SendSignal = new(0, 1);
private int m_SendSignalPosted;
```

Signal helper:

```text
if Interlocked.Exchange(m_SendSignalPosted, 1) == 0:
    m_SendSignal.Release()
```

Drain loop:

```text
while !cancelled:
    await m_SendSignal.WaitAsync(cancellationToken)
    Interlocked.Exchange(m_SendSignalPosted, 0)

    while m_SendBuffers.CanReadSize > 0:
        socket = m_Socket
        if socket is null or disconnected: break

        readSize = min(m_SendBuffers.CanReadSize, SendChunkBytes)
        buffer = rent/read readSize bytes
        sentBytes = await socket.SendAsync(buffer[0..readSize], SocketFlags.None, cancellationToken)
        if sentBytes <= 0: disconnect

        m_SendBuffers.Drain(sentBytes)
        CompletePendingSendRequests(sentBytes)
        ServerTelemetry.RecordSent(sentBytes)
        ServerTelemetry.RecordSendBufferSample(m_SendBuffers.CanReadSize)
```

### 6.2 Why Use Memory-Based Send

Prefer:

```csharp
await socket.SendAsync(ReadOnlyMemory<byte>, SocketFlags, CancellationToken)
```

over reusing a single `SocketAsyncEventArgs`.

Reason:

- it naturally enforces await-based single-flight behavior
- it avoids reusing one `SocketAsyncEventArgs` while a previous send may still be pending
- partial send handling is straightforward

### 6.3 Chunk Size

Initial `SendChunkBytes`: 64 KiB.

Reason:

- much smaller than observed max queued buffer
- large enough to avoid one syscall per small packet
- common size for network buffer batching

If RTT or CPU worsens, tune to 128 KiB in a later iteration.

## 7. Pending Request Accounting

### 7.1 Problem

Current telemetry increments `PendingSendRequests` per `RequestSendBuffers`, but completion is coupled to socket send completion. A single socket send may contain multiple queued responses, so request-level pending can become inaccurate.

### 7.2 Request Size Queue

`BaseSession` tracks queued response lengths.

```csharp
private readonly Queue<int> m_PendingSendRequestBytes = new();
private int m_CurrentSendRequestRemainingBytes;
private readonly object m_PendingSendRequestsLock = new();
```

On successful enqueue:

```text
enqueue packet byte length
ServerTelemetry.RecordSendRequested(...)
```

On drain:

```text
remainingSentBytes = sentBytes
while remainingSentBytes > 0:
    if current request remaining == 0:
        current request remaining = dequeue next request length
    consume = min(current request remaining, remainingSentBytes)
    current request remaining -= consume
    remainingSentBytes -= consume
    if current request remaining == 0:
        ServerTelemetry.RecordSendCompleted()
```

This makes `PendingSendRequests` represent queued response packets rather than socket send operations.

## 8. Telemetry Changes

### 8.1 IServerTelemetry

Add:

```csharp
void RecordSendRejected(int bytes, int queuedBytes);
```

### 8.2 ServerTelemetrySnapshot

Add optional/default fields:

```csharp
long SendRejectedRequests = 0;
long SendRejectedBytes = 0;
```

### 8.3 ServerObservedMetricsSnapshot

Add:

```csharp
long SendRejectedRequests = 0;
double SendRejectedRequestsPerSecond = 0;
long SendRejectedBytes = 0;
double SendRejectedBytesPerSecond = 0;
```

### 8.4 Summary Fields

Extend `LoadValidationStageSummary` and Markdown/JSON summary with:

- `MaxSendRejectedRequests`
- `MaxSendRejectedBytes`
- `MaxSendRejectedRequestsPerSecond`

These are required to avoid hiding failure by dropping responses.

## 9. Implementation Order

1. Add `SessionSendOptions`.
2. Add send rejected telemetry counters and observed DTO fields.
3. Add request-size pending accounting in `BaseSession`.
4. Add `TryRequestSendBuffers` and `TryRequestSendMessage`.
5. Replace send loop with signal-driven single-flight chunked drain.
6. Update `FastPortSmokeClientSession.SendMessage` to return bool and observe rejection.
7. Extend `FastPortLoadValidation` summary with send rejected fields.
8. Add unit tests for telemetry and send policy behavior.
9. Run build/test.
10. Run reduced smoke server-merged validation.
11. Run focused `s5-random-10k` comparison.

## 10. Test Plan

### 10.1 Unit Tests

Add tests under `LibCommonTest`.

- `ServerTelemetryCollector` tracks rejected send requests and bytes.
- `ServerObservedMetricsSnapshot.FromTelemetry` computes rejected send rates.
- `ObservedMetricsJson` preserves rejected send fields.
- `LoadValidationSummaryWriter` includes rejected send fields.
- `LoadValidationEvaluator` includes max rejected send values.
- `BaseSession` send queue guard rejects enqueue when queued bytes exceeds `MaxQueuedBytes`.
- `BaseSession` chunked drain records send completion per queued request.

If direct `BaseSession` socket testing is too heavy, introduce a narrow internal test seam for send queue policy/accounting only. Do not rewrite the session lifecycle for testability.

### 10.2 Verification Commands

Build/test:

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
dotnet build FastPortCharp.sln -c Release
```

Reduced smoke:

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer \
  --Logging:LogLevel:Default Warning \
  --Logging:LogLevel:Microsoft Warning \
  --Telemetry:Output artifacts/load-validation/send-backpressure-smoke/server.metrics.jsonl \
  --Telemetry:IntervalSeconds 1
```

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile smoke \
  --output artifacts/load-validation/send-backpressure-smoke \
  --server-metrics artifacts/load-validation/send-backpressure-smoke/server.metrics.jsonl
```

Focused 10K:

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

## 11. Acceptance Criteria

- Debug build passes.
- Full test suite passes.
- Release build passes.
- Reduced smoke server-merged validation passes.
- Focused 10K comparison artifacts are produced.
- `MaxSendBufferBytes` is bounded near or below 1 MiB.
- `MaxPendingSendRequests` is materially lower than 180,466.
- `NoBufferSpaceAvailable` count is materially lower than 6,586.
- If responses are rejected, `SendRejectedRequests` and `SendRejectedBytes` are visible in summary.
- Existing observed metrics envelope remains backward-compatible.

## 12. Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| Response rejection improves server metrics but worsens client pending requests | High | Compare both client pending and rejected send counters |
| Chunked send increases CPU/syscall overhead | Medium | Start with 64 KiB and adjust only after measurement |
| Request completion accounting becomes inconsistent with buffer drain | High | Unit-test request-size queue accounting separately |
| BaseSession behavior change affects other hosts | High | Keep constructor compatibility and protocol-neutral semantics |
| 10K remains host-resource limited | Medium | Evaluate relative improvement against same-machine baseline |

## 13. Deferred Work

- Receive pause / stop reading policy.
- Config file exposure for `SessionSendOptions`.
- Protocol-level flow control.
- Adaptive high watermark based on send completion rate.
- Multi-machine validation.
