# Gap Analysis: basesession-send-channel-queue-lock-reduction

> Date: 2026-04-30 | Design: docs/02-design/features/basesession-send-channel-queue-lock-reduction.design.md

---

## Match Rate: 100%

Implemented items: 22 / 22

This implementation matches the main design intent: `BaseSession` send hot path no longer uses the `IBuffers` send queue, `SemaphoreSlim` send signal, or `SendCompletionTracker` lock. The send path now uses a `Channel<SendQueueItem>` with explicit `Interlocked` byte budget, item-local partial-send offset tracking, FIFO batching, and ArrayPool-backed coalesced socket sends for multi-segment batches.

The previous test coverage gap is closed. The remaining concern is benchmark quality, not design/code match: focused 10K still passes, but the post-iterate result does not satisfy the performance acceptance targets cleanly.

## Summary

The structural refactor is implemented and verified at build, unit, and smoke-validation levels.

Evidence:

- `LibNetworks/Sessions/BaseSession.cs`
  - `m_SendQueue` and `m_QueuedSendBytes` exist.
  - `SendQueueItem` owns `Buffer`, `Offset`, `Length`, `Remaining`, and `IsComplete`.
  - send worker waits on `m_SendQueue.Reader.WaitToReadAsync`.
  - enqueue uses `TryReserveQueuedSendBytes` before `TryWrite`.
  - partial send advances item offset and decrements queued bytes only by the completed byte count.
  - `NoBufferSpaceAvailable` and `WouldBlock` remain transient retry signals.
- `LibCommonTest/BaseSessionSendPolicyTests.cs`
  - byte-budget rejection test remains.
  - drain-yield test remains.
  - transient backpressure no-drain/no-complete test remains.
  - partial-send completion test was added.
  - closed-queue rejection test was added.
  - FIFO multi-item completion test was added.
  - batched send chunk-limit test was added.
- `artifacts/load-validation/send-channel-queue-smoke/summary.md`
  - `smoke-random-25` passed with 25 / 25 peak sessions and 0.00% socket errors.
- `artifacts/load-validation/s5-send-channel-queue-adaptive/summary.md`
  - `s5-random-10k` passed with 10,000 / 10,000 peak sessions.
  - `send|IOException|NoBufferSpaceAvailable` dropped to 868 from the prior adaptive baseline 1,415.
  - RTT P95/P99 improved slightly, but max TPS, scheduler drift, and receive timeout behavior regressed.
- `artifacts/load-validation/s5-send-channel-queue-batch-chunk-adaptive/summary.md`
  - post-iterate `s5-random-10k` passed with 10,000 / 10,000 peak sessions.
  - scatter/gather batching restored chunk coalescing without returning to the locked `IBuffers` send queue.
  - max scheduler drift improved to 36.02ms, but RTT P99, socket error rate, pending send depth, and final disconnect count remain outside the feature acceptance targets.
- `artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive/summary.md`
  - second post-iterate `s5-random-10k` passed with 9,975 / 10,000 peak sessions.
  - ArrayPool-backed coalescing eliminated server send backpressure in the run and reduced final disconnects from 109 to 2.
  - the run still misses clean performance acceptance because max TPS, pending send depth, socket error rate, and RTT P95/P99 remain worse than the adaptive-window reference.

## Design Item Comparison

| # | Design Item | Status | Evidence |
|---:|-------------|--------|----------|
| 1 | Replace send `IBuffers` queue with `Channel<SendQueueItem>` | Match | `m_SendQueue` in `BaseSession`; no `m_SendBuffers` field remains |
| 2 | Keep explicit byte budget separate from Channel item count | Match | `m_QueuedSendBytes`, `TryReserveQueuedSendBytes`, `ReleaseQueuedSendBytes` |
| 3 | Use `SingleReader = true`, `SingleWriter = false`, `AllowSynchronousContinuations = false` | Match | Channel initialization in constructor |
| 4 | Preserve constructor/source compatibility with `sendbuffers` parameter | Match | constructor signatures unchanged |
| 5 | Remove send hot-path `ArrayPoolCircularBuffers.Write/Peek/Drain` lock usage | Match | send path no longer calls `m_SendBuffers.Write`, `Peek`, or `Drain` |
| 6 | Remove `m_SendSignal` and `m_SendSignalPosted` wake-up path | Match | no references remain in `BaseSession` |
| 7 | Remove `SendCompletionTracker` from `BaseSession` hot path | Match | no `SendCompletionTracker` reference remains in `BaseSession` |
| 8 | `TryRequestSendBuffers` validates zero length and oversized packet | Match | validation remains before queue reservation |
| 9 | Queue bound rejection records backpressure and rejected send | Match | reservation failure records both |
| 10 | Channel write failure rolls back byte budget | Match | `TryWrite` failure calls `ReleaseQueuedSendBytes(buffersSize)` |
| 11 | `RecordSendRequested` only after queue acceptance | Match | called after successful `TryWrite` |
| 12 | Send worker waits on Channel reader instead of signal | Match | `WaitToReadAsync` and `TryRead` loop |
| 13 | Send bounded chunks without `IBuffers.Peek` copy/drain | Match | `BuildSendSegments` creates FIFO array segments and multi-segment batches are coalesced through an ArrayPool-rented memory send |
| 14 | Drain budget yields without requeueing current item | Match | `RecordSendDrainYield`, `Task.Yield`, cycle counter reset |
| 15 | Partial send advances offset by returned byte count only | Match | `advancedSize`, `sendItem.Advance`, `ReleaseQueuedSendBytes` |
| 16 | Logical completion recorded only when item is complete | Match | `RecordSendCompleted` inside `if (sendItem.IsComplete)` |
| 17 | Transient backpressure does not advance offset or queued bytes | Match | transient catch records error/backpressure and continues |
| 18 | Non-transient socket error disconnects and exits worker | Match | non-transient catch records error and calls `RequestDisconnect` |
| 19 | `RequestDisconnect` closes send and receive Channels | Match | `m_SendQueue.Writer.TryComplete()` and `m_ReceivedPackets.Writer.TryComplete()` |
| 20 | Build, unit test, and smoke validation executed | Match | build/test passed; smoke summary passed |
| 21 | Dedicated FIFO completion order unit test | Match | `BaseSession_DoWorkSendBuffers_CompletesMultipleAcceptedItemsInFifoOrder` |
| 22 | Focused 10K comparison against adaptive baseline | Match | Channel-only, batch+chunk, and batch+pool focused 10K runs passed and are compared below |

## Implemented Items

- [x] `Channel<SendQueueItem>` introduced as the send queue.
- [x] `m_QueuedSendBytes` introduced as the explicit byte budget.
- [x] `SendQueueItem` tracks logical item offset and completion.
- [x] `m_SendSignal` and `m_SendSignalPosted` removed.
- [x] send-path use of `m_SendBuffers` removed.
- [x] `BaseSession` no longer uses `SendCompletionTracker`.
- [x] queue acceptance happens before `RecordSendRequested`.
- [x] queue bound rejection remains observable via telemetry.
- [x] closed Channel write rolls back byte budget.
- [x] partial send decrements queue depth only by sent bytes.
- [x] transient `NoBufferSpaceAvailable`/`WouldBlock` retry semantics preserved.
- [x] logical send completion is recorded only after full item send.
- [x] `RequestDisconnect` closes the send Channel.
- [x] public send methods and constructors remain source-compatible.
- [x] design stage typo for smoke validation stage was corrected to `smoke-random-25`.
- [x] build/test/smoke verification is documented in the Do document.
- [x] focused 10K adaptive-window validation was executed and compared with the previous adaptive baseline.
- [x] FIFO completion order is directly tested for multiple accepted send items.
- [x] batched scatter/gather send preserves `SendChunkBytes` as the per-socket-send cap.
- [x] multi-segment batched sends use ArrayPool-backed coalescing before the cancellation-aware memory send path.

## Missing Items

None.

## Changed Items

- [x] Design named a separate `RollbackQueuedSendBytes` helper; implementation uses `ReleaseQueuedSendBytes` for both successful send decrement and enqueue rollback.

Reasoning:

This is an acceptable implementation simplification. Both paths perform the same atomic subtraction and telemetry-safe clamp. A separate rollback helper would add API surface without changing behavior.

- [x] Design said `DoWorkSendBuffers` should treat `ChannelClosedException` as normal; implementation does not explicitly catch it.

Reasoning:

The worker uses `WaitToReadAsync` and `TryRead`, so normal writer completion exits without throwing. `OperationCanceledException` and `ObjectDisposedException` are still handled.

- [x] Iterate added scatter/gather batching on top of the Channel item queue.

Reasoning:

The first Channel-only implementation preserved correctness but lost the previous byte queue's ability to coalesce multiple small logical packets into one socket send. The iterate implementation batches available FIFO items into `IList<ArraySegment<byte>>` while preserving logical item completion order and the existing `SendChunkBytes` / drain-budget caps.

- [x] Second iterate replaced direct scatter/gather socket sends with ArrayPool-backed coalesced memory sends for multi-segment batches.

Reasoning:

Direct `Socket.SendAsync(IList<ArraySegment<byte>>)` improved scheduler drift but still left high final disconnects, server send backpressure, and RTT P99. The second iterate keeps FIFO batching and the `SendChunkBytes` cap, but routes multi-segment batches through the same cancellation-aware `ReadOnlyMemory<byte>` send path after copying into a rented buffer. This is an intentional tradeoff: it adds a bounded per-send copy, but reduced final disconnects and server send backpressure in the focused 10K run.

## Verification Results

### Build

```bash
dotnet build FastPortCharp.sln
```

Result:

- Passed
- Warnings: 0
- Errors: 0

```bash
dotnet build FastPortCharp.sln -c Release
```

Result:

- Passed
- Warnings: 0
- Errors: 0

### Unit Tests

```bash
dotnet test FastPortCharp.sln --no-build
```

Result:

- Passed: 97
- Failed: 0
- Skipped: 0

### Smoke Validation

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile smoke \
  --stage smoke-random-25 \
  --output artifacts/load-validation/send-channel-queue-smoke \
  --server-metrics artifacts/load-validation/send-channel-queue-smoke/server.metrics.jsonl
```

Result:

| Metric | Result |
|--------|-------:|
| Status | Passed |
| Peak sessions | 25 / 25 |
| Peak ratio | 100.00% |
| Max TPS | 28.97 |
| Max pending request count | 5 |
| Max pending send requests | 5 |
| Server backpressure | 0 |
| Rejected send | 0 / 0 |
| Drain yield | 0 / 0 |
| Max scheduler drift | 1.84ms |
| RTT P95 | 10.16ms |
| RTT P99 | 13.09ms |
| Socket errors | 0.00% |

### Focused 10K Validation

Command:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-send-channel-queue-adaptive \
  --server-metrics artifacts/load-validation/s5-send-channel-queue-adaptive/server.metrics.jsonl \
  --pacing-policy adaptive-window \
  --pacing-min-window 1 \
  --pacing-initial-window 4 \
  --pacing-max-window 16 \
  --pacing-rtt-target-ms 12000 \
  --pacing-rtt-high-ms 20000 \
  --pacing-increase-every 256
```

Result:

| Metric | Previous Adaptive | Send Channel Queue | Change |
|--------|------------------:|-------------------:|-------:|
| Result | Passed | Passed | same |
| Peak sessions | 10,000 / 10,000 | 10,000 / 10,000 | same |
| Final disconnects | 0 | 38 | regression |
| Max TPS | 13,034.37 | 8,878.08 | -31.9% |
| Max pending request count | 36,384 | 37,587 | +3.3% |
| Max pending send requests | 212 | 917 | +332.5% |
| Server send backpressure events | 501 | 1,377 | +174.9% |
| Max send buffer bytes | 63,233 | 63,190 | flat |
| `send\|IOException\|NoBufferSpaceAvailable` | 1,415 | 868 | -38.7% |
| `receive\|IOException\|TimedOut` | none material | 4,419 | regression |
| Socket error rate | 0.05% | 0.16% | +0.11pp |
| RTT P95 | 16,234.27ms | 15,669.20ms | -3.5% |
| RTT P99 | 18,420.99ms | 18,166.68ms | -1.4% |
| Max scheduler drift | 320.86ms | 2,639.85ms | regression |

Interpretation:

- The run passed the validation thresholds.
- The new send queue reduced client `NoBufferSpaceAvailable` and slightly improved RTT P95/P99.
- It also reduced throughput and introduced receive timeouts and final disconnects.
- This should be treated as a mixed result that needs iteration before reporting a clean performance win.

### Iterate Focused 10K Validation

Command:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-send-channel-queue-batch-chunk-adaptive \
  --server-metrics artifacts/load-validation/s5-send-channel-queue-batch-chunk-adaptive/server.metrics.jsonl \
  --pacing-policy adaptive-window \
  --pacing-min-window 1 \
  --pacing-initial-window 4 \
  --pacing-max-window 16 \
  --pacing-rtt-target-ms 12000 \
  --pacing-rtt-high-ms 20000 \
  --pacing-increase-every 256
```

Result:

| Metric | Previous Adaptive | Channel-only | Batch + Chunk Cap | Acceptance |
|--------|------------------:|-------------:|------------------:|------------|
| Result | Passed | Passed | Passed | Match |
| Peak sessions | 10,000 / 10,000 | 10,000 / 10,000 | 10,000 / 10,000 | Match |
| Final disconnects | 0 | 38 | 109 | Slight miss |
| Max TPS | 13,034.37 | 8,878.08 | 9,672.38 | Miss |
| Max pending request count | 36,384 | 37,587 | 37,190 | Slight regression |
| Max pending send requests | 212 | 917 | 954 | Miss |
| Server send backpressure events | 501 | 1,377 | 1,591 | Miss |
| Max send buffer bytes | 63,233 | 63,190 | 64,121 | Slight regression |
| `send\|IOException\|NoBufferSpaceAvailable` | 1,415 | 868 | 1,159 | Match |
| `receive\|IOException\|TimedOut` | none material | 4,419 | 900 | Miss |
| Socket error rate | 0.05% | 0.16% | 0.18% | Miss |
| RTT P95 | 16,234.27ms | 15,669.20ms | 17,487.73ms | Miss |
| RTT P99 | 18,420.99ms | 18,166.68ms | 30,073.37ms | Miss |
| Max scheduler drift | 320.86ms | 2,639.85ms | 36.02ms | Match |

Interpretation:

- Scatter/gather batching fixes the largest scheduler-drift regression from the Channel-only run.
- The chunk cap fix restores the original per-send size contract and keeps peak sessions at 100%.
- The result is still not a clean performance acceptance pass because RTT P99, socket error rate, pending send depth, and final disconnect count remain worse than the adaptive baseline targets.

### Second Iterate Focused 10K Validation

Command:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive \
  --server-metrics artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive/server.metrics.jsonl \
  --pacing-policy adaptive-window \
  --pacing-min-window 1 \
  --pacing-initial-window 4 \
  --pacing-max-window 16 \
  --pacing-rtt-target-ms 12000 \
  --pacing-rtt-high-ms 20000 \
  --pacing-increase-every 256
```

Result:

| Metric | Previous Adaptive | Batch + Chunk Cap | Batch + Pool Copy | Acceptance |
|--------|------------------:|------------------:|------------------:|------------|
| Result | Passed | Passed | Passed | Match |
| Peak sessions | 10,000 / 10,000 | 10,000 / 10,000 | 9,975 / 10,000 | Match |
| Final disconnects | 0 | 109 | 2 | Match |
| Max TPS | 13,034.37 | 9,672.38 | 7,901.40 | Miss |
| Max pending request count | 36,384 | 37,190 | 38,246 | Miss |
| Max pending send requests | 212 | 954 | 1,282 | Miss |
| Server send backpressure events | 501 | 1,591 | 0 | Match |
| Max send buffer bytes | 63,233 | 64,121 | 63,364 | Slight regression |
| `send\|IOException\|NoBufferSpaceAvailable` | 1,415 | 1,159 | 0 | Match |
| `receive\|IOException\|TimedOut` | none material | 900 | 1,266 | Miss |
| Other socket classifications | none material | none material | `send\|IOException\|Shutdown = 2` | Slight miss |
| Socket error rate | 0.05% | 0.18% | 0.12% | Slight miss |
| RTT P95 | 16,234.27ms | 17,487.73ms | 17,796.60ms | Miss |
| RTT P99 | 18,420.99ms | 30,073.37ms | 27,398.15ms | Miss |
| Max scheduler drift | 320.86ms | 36.02ms | 19.66ms | Match |

Interpretation:

- ArrayPool coalescing materially improves reliability compared with direct scatter/gather batching: final disconnects fell from 109 to 2, server send backpressure fell from 1,591 to 0, and send-side `NoBufferSpaceAvailable` disappeared in this run.
- It does not solve the throughput and receive-side tail problem. Max TPS dropped to 7,901.40, pending send depth rose to 1,282, receive timeouts rose to 1,266, and RTT P99 remains above the 20,000ms target.
- The feature is now a structurally correct low-lock send-queue refactor with useful pressure improvements, but it should still be reported with explicit benchmark tradeoffs or followed by a narrower throughput/tail-latency tuning feature.

## Risk Review

| Risk | Current Status | Notes |
|------|----------------|-------|
| Byte budget leak on Channel write failure | Mitigated | closed-queue rejection test covers rollback |
| Partial send accounting regression | Mitigated | partial-send test verifies pending and queued byte behavior |
| Transient backpressure accidentally drains data | Mitigated | existing transient test still passes |
| FIFO logical completion order | Mitigated | direct FIFO multi-item unit test covers completion behavior |
| Send worker monopolizes execution | Improved | latest post-iterate scheduler drift is 19.66ms, below the 200ms target |
| Performance regression under 10K | Confirmed mixed | Final disconnects, send NoBuffer, server backpressure, and drift now meet targets; TPS, RTT P99, socket error rate, pending send depth, and receive timeouts do not |

## Recommendations

1. Do not report this feature as a clean performance win.
2. Either report it as a structural lock-reduction refactor with known benchmark tradeoffs or split a follow-up focused on send throughput / receive timeout tail behavior.
3. Keep `SendCompletionTracker` cleanup as deferred work; removing it now is not required for the send hot-path change.

## Next Steps

Match rate is 100%, but focused 10K still produced a mixed performance result.

Formal PDCA status:

- code/design match rate is high enough to proceed to report;
- Act iteration has reached the configured maximum iteration count;
- report must explicitly document the performance acceptance misses if generated now.

Engineering recommendation:

- do not report this as a clean performance win;
- report this feature as a structural refactor with known benchmark tradeoffs, then split a follow-up PM for send throughput / receive timeout tail behavior.

Possible next commands:

```bash
$pdca report basesession-send-channel-queue-lock-reduction
```
