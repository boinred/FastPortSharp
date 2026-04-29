# client-send-buffer-pressure-receive-flow-control - Design Document

> Version: 1.0.0 | Date: 2026-04-29 | Status: Draft
> Level: Starter | Plan: docs/01-plan/features/client-send-buffer-pressure-receive-flow-control.plan.md

---

## 1. Overview

This feature targets the remaining `send|IOException|NoBufferSpaceAvailable` issue observed after `server-send-backpressure-queue-drain-optimization`.

The previous feature fixed server send backlog materially:

| Metric | Previous Baseline | Current Baseline |
|--------|------------------:|-----------------:|
| Peak sessions | 8,611 / 10,000 | 10,000 / 10,000 |
| Final disconnects | 1,855 | 0 |
| Max pending send requests | 180,466 | 905 |
| Max send buffer bytes | 2,649,731 | 195,683 |

But the dominant client socket error remains:

| Metric | Current Baseline | Target |
|--------|-----------------:|-------:|
| `send|IOException|NoBufferSpaceAvailable` | 7,344 | <= 5,000 |
| Max pending request count | 36,653 | <= 25,000 |

The design must reduce client/kernel send-buffer pressure without undoing the server send backlog improvement and without violating TCP partial-send correctness.

## 2. Design Decisions

### 2.1 Preserve Send Correctness

`Socket.SendAsync` success means bytes were accepted by the local OS socket buffer, not that the remote peer consumed the complete logical response.

The send queue must keep this invariant:

```text
drain bytes == sentSize returned from Socket.SendAsync
```

Do not drain the whole queued buffer unless `sentSize == queuedBytes`.

Reason:

- TCP send may be partial.
- Draining more than `sentSize` can lose unsent response bytes.
- Retrying the whole original buffer after partial success can duplicate bytes.

The current code already follows this invariant with:

```text
Peek(chunk)
Socket.SendAsync(chunk)
Drain(sentSize)
CompletePendingSendRequests(sentSize)
```

This feature must preserve that behavior.

### 2.2 Do Not Revert To Full-Queue Send

The previous implementation sent one bounded chunk at a time. Reverting to "send all currently queued bytes" risks larger socket writes and can increase `NoBufferSpaceAvailable`.

This feature keeps chunked send and adds pacing around it.

### 2.3 Primary Change: Budgeted Send Drain

Current behavior:

```text
wait for send signal
while send queue has bytes:
    send up to SendChunkBytes
    drain sentSize
```

Target behavior:

```text
wait for send signal
drainedBytesThisWake = 0
sendOperationsThisWake = 0

while send queue has bytes:
    if wake budget exhausted:
        record send drain yield
        resignal send loop
        yield cooperatively
        break

    send up to SendChunkBytes
    drain sentSize
    update budget counters
```

This caps response burst size per signal while still letting the queue make progress.

### 2.4 Secondary Diagnostic: Client Outstanding Request Cap

The load runner currently sends at the configured per-session rate regardless of outstanding request count.

That behavior is useful for stress testing, but it can also create a client-side send-buffer pressure pattern that no server-only send-drain change can fully remove.

Add an optional load-runner cap:

```text
--max-pending-requests-per-session N
```

When set, each `LoadSession` waits before sending a new request if its own outstanding request count is already `N`.

This is diagnostic and optional:

- uncapped run remains the primary server stress comparison;
- capped run tells us whether client pacing directly reduces `NoBufferSpaceAvailable`.

### 2.5 Deferred Change: Socket Receive Pause

Pausing socket receive can reduce server application pressure, but it can also push TCP backpressure to clients and increase client-side `NoBufferSpaceAvailable`.

For this iteration, receive pause is not the first implementation. It remains a design fallback if budgeted drain and client pacing data show a clear need.

If added later, it must be high/low watermark based and telemetry-visible.

## 3. Target Architecture

| Area | Module | Responsibility |
|------|--------|----------------|
| send drain budget | `LibNetworks/Sessions/BaseSession` | enforce max bytes/operations per send wake |
| send options | `LibNetworks/Sessions/SessionSendOptions` | configure chunk size, wake budget, transient backoff |
| send drain telemetry | `LibNetworks/Telemetry` | expose budget-yield counts and queued bytes at yield |
| load-runner pacing | `FastPortLoadRunner/LoadSession` | optional per-session outstanding request cap |
| load-runner scenario config | `FastPortLoadRunner` scenario/options parsing | carry `MaxPendingRequestsPerSession` |
| validation summary | `FastPortLoadValidation` | compare NoBuffer, pending requests, pending send, drain yields |

## 4. API And Data Model

### 4.1 SessionSendOptions Extension

Extend `SessionSendOptions`.

```csharp
public sealed record SessionSendOptions(
    int MaxQueuedBytes = 1024 * 1024,
    int SendChunkBytes = 64 * 1024,
    int MaxDrainBytesPerSignal = 256 * 1024,
    int MaxDrainOperationsPerSignal = 4,
    int TransientSendBackoffMs = 1)
{
    public static SessionSendOptions Default { get; } = new();

    public int NormalizedMaxQueuedBytes => Math.Max(1, MaxQueuedBytes);
    public int NormalizedSendChunkBytes => Math.Max(1, SendChunkBytes);
    public int NormalizedMaxDrainBytesPerSignal => Math.Max(NormalizedSendChunkBytes, MaxDrainBytesPerSignal);
    public int NormalizedMaxDrainOperationsPerSignal => Math.Max(1, MaxDrainOperationsPerSignal);
    public int NormalizedTransientSendBackoffMs => Math.Max(0, TransientSendBackoffMs);
}
```

Default behavior changes from "drain until empty" to "drain up to `256 KiB` or `4` send operations per wake".

Reasonable first defaults:

| Option | Value | Rationale |
|--------|------:|-----------|
| `SendChunkBytes` | 64 KiB | Keep current chunk size for first isolation |
| `MaxDrainBytesPerSignal` | 256 KiB | 4 chunks per wake |
| `MaxDrainOperationsPerSignal` | 4 | Bound per-wake socket send count |
| `TransientSendBackoffMs` | 1 ms | Preserve current transient retry delay |

### 4.2 Server Telemetry Extension

Add telemetry for budgeted drain behavior.

```csharp
void RecordSendDrainYield(int queuedBytes);
```

Add fields:

```csharp
long SendDrainYieldCount;
long MaxSendDrainYieldQueuedBytes;
```

Add observed metrics fields:

```csharp
long SendDrainYieldCount;
double SendDrainYieldCountPerSecond;
long MaxSendDrainYieldQueuedBytes;
```

Purpose:

- prove that budget is actually active;
- correlate drain yielding with NoBuffer and pending request changes;
- avoid interpreting lower send pressure as a hidden stall.

### 4.3 LoadRunner Scenario Extension

Add optional scenario field:

```csharp
int? MaxPendingRequestsPerSession
```

CLI:

```text
--max-pending-requests-per-session 4
```

Behavior:

```text
if cap is set:
    while outstandingRequestsForThisSession >= cap:
        wait briefly

send request
outstandingRequestsForThisSession++

on valid response:
    outstandingRequestsForThisSession--
```

The global `MetricsCollector.PendingRequestCount` remains unchanged. This new per-session count only gates client sending.

### 4.4 Load Validation Summary Extension

Add optional summary fields if telemetry is extended:

- `MaxSendDrainYieldCount`
- `MaxSendDrainYieldQueuedBytes`
- `MaxSendDrainYieldCountPerSecond`

Markdown summary can keep the existing table concise, but JSON should preserve the detailed fields.

## 5. Send Drain Algorithm

### 5.1 Budgeted Drain Loop

Pseudo flow:

```text
await sendSignal
reset signal posted flag

drainedBytesThisWake = 0
sendOperationsThisWake = 0

while send buffer has bytes:
    if drainedBytesThisWake >= MaxDrainBytesPerSignal:
        yield and resignal
        break

    if sendOperationsThisWake >= MaxDrainOperationsPerSignal:
        yield and resignal
        break

    readSize = min(queuedBytes, SendChunkBytes, remaining byte budget)
    peek readSize
    sentSize = await Socket.SendAsync(peekedBytes)
    drain sentSize
    drainedBytesThisWake += sentSize
    sendOperationsThisWake++
```

Important details:

- `readSize` should not exceed the remaining byte budget.
- `Drain(sentSize)` remains the only queue removal.
- If queue still has bytes after budget exhaustion, record `RecordSendDrainYield(m_SendBuffers.CanReadSize)` and resignal.
- Use cooperative yielding (`await Task.Yield()` or a zero/short delay) instead of blocking the send task.
- Keep cancellation behavior unchanged.

### 5.2 Transient Socket Backpressure

Current transient handling:

```text
NoBufferSpaceAvailable or WouldBlock:
    RecordSocketError
    RecordSendBackpressure
    delay 1 ms
    continue
```

Target:

```text
delay TransientSendBackoffMs
do not drain
do not complete pending request
retry later
```

If `TransientSendBackoffMs == 0`, use cooperative yield rather than delay.

## 6. Client Pacing Algorithm

### 6.1 Per-Session Outstanding Count

Add a private field to `LoadSession`.

```csharp
private long _outstandingRequests;
```

On successful write:

```text
_outstandingRequests++
metricsCollector.RecordSentPacket(packet.Length)
```

On valid response parse:

```text
_outstandingRequests--
metricsCollector.RecordReceivedPacket(packetSize)
```

This preserves global pending request metrics and adds local gating only.

### 6.2 Send Gate

Before creating/sending the next packet:

```text
while cap is set and outstandingRequests >= cap:
    await Task.Delay(min(interval, 1ms))
```

This should be cancellation-aware.

Recommended first cap for diagnostic run:

```text
--max-pending-requests-per-session 4
```

Why `4`:

- allows pipelining;
- prevents unbounded per-session request backlog;
- should still generate sustained 10K pressure.

## 7. Validation Matrix

Run variants in this order.

| Variant | Server Change | Client Cap | Purpose |
|---------|---------------|-----------:|---------|
| smoke-budgeted-drain | budgeted drain | none | basic correctness |
| s5-budgeted-drain | budgeted drain | none | server-only effect |
| s5-client-cap-4 | budgeted drain | 4 | pacing diagnostic |
| s5-client-cap-1 | budgeted drain | 1 | conservative pacing upper bound |

Output directories:

- `artifacts/load-validation/receive-flow-control-smoke`
- `artifacts/load-validation/s5-budgeted-drain`
- `artifacts/load-validation/s5-client-cap-4`
- `artifacts/load-validation/s5-client-cap-1`

Primary comparison remains against:

- `artifacts/load-validation/s5-send-backpressure-iterate2/summary.md`

## 8. Test Plan

### 8.1 Unit Tests

Add or update tests for:

- `SessionSendOptions` normalization for new fields.
- send drain budget:
  - budget exhaustion records drain yield;
  - queue remains when only part of the budget is drained;
  - pending request completion still follows fully drained logical request bytes.
- transient send backoff:
  - no drain on transient socket error;
  - no pending completion on transient socket error.
- load-runner pacing:
  - per-session outstanding count gates sends when cap is reached;
  - response parse decrements outstanding count.

If direct socket partial-send testing is hard with `Socket`, keep the invariant covered through a small helper or extracted drain accounting unit with deterministic inputs.

### 8.2 Runtime Verification

Commands:

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
dotnet build FastPortCharp.sln -c Release
```

Smoke:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile smoke \
  --output artifacts/load-validation/receive-flow-control-smoke \
  --server-metrics artifacts/load-validation/receive-flow-control-smoke/server.metrics.jsonl
```

Focused 10K:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-budgeted-drain \
  --server-metrics artifacts/load-validation/s5-budgeted-drain/server.metrics.jsonl
```

Client cap diagnostic:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-client-cap-4 \
  --server-metrics artifacts/load-validation/s5-client-cap-4/server.metrics.jsonl \
  --runner-args "--max-pending-requests-per-session 4"
```

If `FastPortLoadValidation` cannot forward runner args yet, add explicit stage or options support in the design implementation.

## 9. Success Evaluation

A successful server-only run should show:

- peak session ratio `>= 99.00%`;
- final disconnect count `<= 100`;
- max pending send requests `<= 5,000`;
- max send buffer bytes `<= 1,000,000`;
- lower `NoBufferSpaceAvailable` than `7,344`, ideally `<= 5,000`.

If server-only budgeted drain does not lower NoBuffer but client cap does, the next conclusion is:

```text
The remaining NoBuffer is mostly load-generator/client pacing pressure, not server send backlog.
```

If neither lowers NoBuffer:

```text
Investigate OS/socket limits, NetworkStream write behavior, and client send buffer sizing.
```

If server-only budget lowers NoBuffer but worsens RTT badly:

```text
Tune MaxDrainBytesPerSignal and MaxDrainOperationsPerSignal upward.
```

## 10. Implementation Order

1. Extend `SessionSendOptions` with budget/backoff fields.
2. Add telemetry fields for send drain yield.
3. Implement budgeted drain loop in `BaseSession`.
4. Update observed metrics and load-validation summary mapping.
5. Add unit tests for option normalization, telemetry, and budget behavior.
6. Add optional per-session outstanding request cap to `FastPortLoadRunner`.
7. Add load-validation runner-arg or stage support if needed.
8. Run build/tests, smoke, and focused comparison runs.
9. Analyze whether server-only budget or client pacing moves `NoBufferSpaceAvailable`.

## 11. Deferred Items

### 11.1 Receive Pause

Receive pause is intentionally deferred from the first implementation.

Only add it if:

- budgeted drain and client pacing data show that server application receive is still the dominant pressure point;
- thresholds can be expressed with high/low watermarks;
- telemetry can report pause/resume counts and duration.

Potential future API:

```csharp
int ReceivePauseQueuedSendBytesHighWatermark = 768 * 1024;
int ReceiveResumeQueuedSendBytesLowWatermark = 256 * 1024;
int ReceivePausePendingSendRequestsHighWatermark = 2_000;
```

This should be a separate design decision because pausing socket receive can push backpressure directly to clients.

## 12. Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| Budgeted drain reduces throughput too much | RTT and pending requests may rise | tune budget upward and compare capped/uncapped runs |
| Client cap hides server bottleneck | results may look better for the wrong reason | keep uncapped server-only run as primary comparison |
| Telemetry table becomes too wide | markdown less readable | preserve details in JSON and keep markdown concise |
| Receive pause worsens client NoBuffer | client send buffer fills faster | defer receive pause until data supports it |
| Partial-send invariant regresses | data corruption | add explicit tests and keep `Drain(sentSize)` as the only removal path |

## 13. References

- Plan: `docs/01-plan/features/client-send-buffer-pressure-receive-flow-control.plan.md`
- Previous report: `docs/archive/2026-04/server-send-backpressure-queue-drain-optimization/server-send-backpressure-queue-drain-optimization.report.md`
- Benchmark summary: `docs/load-validation-benchmark-results.md`
- Current send loop: `LibNetworks/Sessions/BaseSession.cs`
- Send options: `LibNetworks/Sessions/SessionSendOptions.cs`
- Load runner session: `FastPortLoadRunner/LoadSession.cs`
- Load runner metrics: `FastPortLoadRunner/Metrics.cs`
