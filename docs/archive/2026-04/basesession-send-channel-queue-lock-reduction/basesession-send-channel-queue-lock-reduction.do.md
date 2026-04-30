# basesession-send-channel-queue-lock-reduction - Do Document

> Version: 1.0.0 | Date: 2026-04-30 | Status: Completed
> Plan: docs/01-plan/features/basesession-send-channel-queue-lock-reduction.plan.md
> Design: docs/02-design/features/basesession-send-channel-queue-lock-reduction.design.md

---

## 1. Implementation Summary

`BaseSession` send path now uses a logical `Channel<SendQueueItem>` plus explicit `Interlocked` byte budget instead of the previous `IBuffers` send queue and `SemaphoreSlim` wake-up pair.

Implemented changes:

- removed `m_SendBuffers` from the send hot path;
- removed `m_SendSignal` and `m_SendSignalPosted`;
- added `m_SendQueue` and `m_QueuedSendBytes`;
- added private `SendQueueItem` for logical packet buffer, offset, and remaining byte tracking;
- changed enqueue to reserve byte budget before Channel write;
- changed send worker to await Channel reader and send item chunks directly from the original packet buffer;
- added scatter/gather batching so small FIFO send items can be coalesced without restoring the locked `IBuffers` send queue;
- preserved `SendChunkBytes` as the per-socket-send cap for batched sends;
- replaced direct multi-segment socket sends with ArrayPool-backed coalesced memory sends for the latest 10K candidate;
- preserved transient `NoBufferSpaceAvailable` / `WouldBlock` retry behavior;
- preserved queue rejection and send completion telemetry semantics;
- added unit tests for partial send completion, FIFO completion, batch chunk limit, and closed-queue rejection.

## 2. Files Changed

| File | Change |
|------|--------|
| `LibNetworks/Sessions/BaseSession.cs` | Replaced send `IBuffers`/signal path with Channel item queue, byte budget helpers, and scatter/gather batch send |
| `LibCommonTest/BaseSessionSendPolicyTests.cs` | Added partial-send, FIFO, batch chunk-limit, and closed-send-queue tests |
| `docs/02-design/features/basesession-send-channel-queue-lock-reduction.design.md` | Corrected smoke validation stage name |

## 3. Preserved Invariants

- `TryRequestSendBuffers` returns `true` only after queue acceptance.
- Queue bound is enforced by `SessionSendOptions.NormalizedMaxQueuedBytes`.
- `RecordSendRequested` is called once per accepted logical send item.
- `RecordSendCompleted` is called only after the logical send item is fully sent.
- `RecordSent` remains socket-send chunk based.
- Transient send backpressure does not advance item offset or decrement queued bytes.
- Queued byte telemetry decreases by exactly the completed socket send byte count.
- Public send methods and constructor signatures remain source-compatible.

## 4. Verification

### 4.1 Build

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

### 4.2 Unit Tests

```bash
dotnet test FastPortCharp.sln --no-build
```

Result:

- Passed: 97
- Failed: 0
- Skipped: 0

### 4.3 Smoke Validation

Server:

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer \
  --Logging:LogLevel:Default Warning \
  --Logging:LogLevel:Microsoft Warning \
  --Telemetry:Output artifacts/load-validation/send-channel-queue-smoke/server.metrics.jsonl \
  --Telemetry:IntervalSeconds 1
```

Validation:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile smoke \
  --stage smoke-random-25 \
  --output artifacts/load-validation/send-channel-queue-smoke \
  --server-metrics artifacts/load-validation/send-channel-queue-smoke/server.metrics.jsonl
```

Summary:

- Status: Passed
- Peak sessions: 25 / 25
- Peak ratio: 100.00%
- Max TPS: 28.97
- Max pending request count: 5
- Max pending send requests: 5
- Server backpressure: 0
- Rejected send: 0 / 0
- Drain yield: 0 / 0
- Max scheduler drift: 1.84ms
- RTT P95: 10.16ms
- RTT P99: 13.09ms
- Socket errors: 0.00%

Artifact:

- `artifacts/load-validation/send-channel-queue-smoke/summary.md`

### 4.4 Focused 10K Validation

Server:

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer \
  --Logging:LogLevel:Default Warning \
  --Logging:LogLevel:Microsoft Warning \
  --Telemetry:Output artifacts/load-validation/s5-send-channel-queue-adaptive/server.metrics.jsonl \
  --Telemetry:IntervalSeconds 1
```

Validation:

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

Summary:

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

Artifact:

- `artifacts/load-validation/s5-send-channel-queue-adaptive/summary.md`

Interpretation:

- The focused 10K run passed the session target.
- The Channel send queue reduced client `NoBufferSpaceAvailable` and slightly improved RTT P95/P99.
- Throughput, pending send depth, server backpressure, receive timeouts, final disconnects, and scheduler drift regressed.
- Treat this as a mixed validation result that needs an iterate phase before reporting a clean performance win.

### 4.5 Post-Iterate Validation

Iteration changes:

- restored small-packet coalescing with scatter/gather send batches;
- capped each batched socket send by `SessionSendOptions.SendChunkBytes`;
- added direct FIFO completion and batch chunk-limit tests.

Smoke artifact:

- `artifacts/load-validation/send-channel-queue-batch-chunk-smoke/summary.md`

Smoke result:

- Status: Passed
- Peak sessions: 25 / 25
- Socket errors: 0.00%
- RTT P95: 9.48ms
- RTT P99: 14.86ms

Focused 10K artifact:

- `artifacts/load-validation/s5-send-channel-queue-batch-chunk-adaptive/summary.md`

Focused 10K result:

| Metric | Previous Adaptive | Channel-only | Batch + Chunk Cap |
|--------|------------------:|-------------:|------------------:|
| Result | Passed | Passed | Passed |
| Peak sessions | 10,000 / 10,000 | 10,000 / 10,000 | 10,000 / 10,000 |
| Final disconnects | 0 | 38 | 109 |
| Max TPS | 13,034.37 | 8,878.08 | 9,672.38 |
| Max pending send requests | 212 | 917 | 954 |
| Server send backpressure events | 501 | 1,377 | 1,591 |
| `send\|IOException\|NoBufferSpaceAvailable` | 1,415 | 868 | 1,159 |
| `receive\|IOException\|TimedOut` | none material | 4,419 | 900 |
| Socket error rate | 0.05% | 0.16% | 0.18% |
| RTT P95 | 16,234.27ms | 15,669.20ms | 17,487.73ms |
| RTT P99 | 18,420.99ms | 18,166.68ms | 30,073.37ms |
| Max scheduler drift | 320.86ms | 2,639.85ms | 36.02ms |

Interpretation:

- Functional and unit-test gaps are closed.
- Scheduler drift is materially improved compared with both previous runs.
- Performance acceptance is still mixed: NoBuffer and drift improved, but RTT P99, socket error rate, pending send depth, and final disconnect count remain worse than target.

### 4.6 Second Post-Iterate Validation

Iteration change:

- multi-segment batches now copy into an ArrayPool-rented buffer and send through the cancellation-aware `ReadOnlyMemory<byte>` socket path;
- direct scatter/gather socket send remains available to tests through the protected overload shape, but the production multi-segment path is coalesced before send.

Smoke artifact:

- `artifacts/load-validation/send-channel-queue-batch-pool-smoke/summary.md`

Smoke result:

- Status: Passed
- Peak sessions: 25 / 25
- Socket errors: 0.00%
- RTT P95: 9.85ms
- RTT P99: 12.60ms

Focused 10K artifact:

- `artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive/summary.md`

Focused 10K result:

| Metric | Previous Adaptive | Batch + Chunk Cap | Batch + Pool Copy |
|--------|------------------:|------------------:|------------------:|
| Result | Passed | Passed | Passed |
| Peak sessions | 10,000 / 10,000 | 10,000 / 10,000 | 9,975 / 10,000 |
| Final disconnects | 0 | 109 | 2 |
| Max TPS | 13,034.37 | 9,672.38 | 7,901.40 |
| Max pending request count | 36,384 | 37,190 | 38,246 |
| Max pending send requests | 212 | 954 | 1,282 |
| Server send backpressure events | 501 | 1,591 | 0 |
| Max send buffer bytes | 63,233 | 64,121 | 63,364 |
| `send\|IOException\|NoBufferSpaceAvailable` | 1,415 | 1,159 | 0 |
| `receive\|IOException\|TimedOut` | none material | 900 | 1,266 |
| Other socket classifications | none material | none material | `send\|IOException\|Shutdown = 2` |
| Socket error rate | 0.05% | 0.18% | 0.12% |
| RTT P95 | 16,234.27ms | 17,487.73ms | 17,796.60ms |
| RTT P99 | 18,420.99ms | 30,073.37ms | 27,398.15ms |
| Max scheduler drift | 320.86ms | 36.02ms | 19.66ms |

Interpretation:

- ArrayPool coalescing improves reliability compared with the direct scatter/gather batch result: final disconnects, server send backpressure, send-side `NoBufferSpaceAvailable`, and scheduler drift are now within the pragmatic targets.
- The feature still has benchmark tradeoffs: max TPS, pending send depth, socket error rate, receive timeouts, and RTT P95/P99 remain worse than the adaptive-window baseline.
- Treat this as a reportable structural refactor only if the report explicitly carries those remaining performance misses.

## 5. Notes

`SendCompletionTracker` remains in the repository and still has direct unit coverage, but `BaseSession` no longer uses it in the send hot path. Removing it is a cleanup decision for a separate change because this feature intentionally keeps the public/session surface stable.

## 6. Next Phase

Recommended next command:

```bash
$pdca report basesession-send-channel-queue-lock-reduction
```
