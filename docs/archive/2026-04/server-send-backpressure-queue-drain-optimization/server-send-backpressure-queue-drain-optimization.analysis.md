# Gap Analysis: server-send-backpressure-queue-drain-optimization

> Date: 2026-04-29 | Design: docs/02-design/features/server-send-backpressure-queue-drain-optimization.design.md

---

## Match Rate: 93%

## Summary

`BaseSession` send path was changed from 1ms polling plus `SocketAsyncEventArgs` to signal-driven, single-flight chunked drain. The implementation also adds per-session send queue bounding, bool-returning send APIs, explicit rejected-send telemetry, and load-validation summary fields.

The reduced smoke validation and focused 10K validation both pass. The major functional improvement is that focused 10K now reaches `10,000 / 10,000` peak sessions with `0` final disconnects, while keeping per-session `MaxSendBufferBytes` near the configured `1 MiB` cap.

The remaining gap is client-side `NoBufferSpaceAvailable`: the design expected the dominant socket error count to decrease materially from the baseline `6,586`, but the latest focused run observed `7,344`. This means the queue drain and cap fix server memory/backlog behavior, but not the client send-buffer pressure by itself.

## Implemented Items

- [x] Added `SessionSendOptions` with default `MaxQueuedBytes = 1 MiB` and `SendChunkBytes = 64 KiB`.
- [x] Preserved existing `BaseSession`, `BaseSessionClient`, and `BaseSessionServer` constructors.
- [x] Added `SessionSendOptions?` constructor overloads.
- [x] Added `TryRequestSendBuffers` and `TryRequestSendMessage`.
- [x] Kept existing `RequestSendBuffers` and `RequestSendMessage` as wrappers.
- [x] Added send queue guard with `RecordSendBackpressure` and `RecordSendRejected`.
- [x] Changed `FastPortSmokeClientSession.SendMessage` to return `bool`.
- [x] Dropped echo responses explicitly when send enqueue is rejected.
- [x] Replaced polling send loop with `SemaphoreSlim` signal-driven drain.
- [x] Switched send path to `Socket.SendAsync(ReadOnlyMemory<byte>, SocketFlags, CancellationToken)`.
- [x] Bounded each send operation to `SendChunkBytes`.
- [x] Added request-size queue accounting so `PendingSendRequests` is decremented per fully drained queued response.
- [x] Added rejected-send counters to `IServerTelemetry`, `ServerTelemetrySnapshot`, and `ServerObservedMetricsSnapshot`.
- [x] Added rejected-send max fields to `LoadValidationStageSummary`, JSON summary, and Markdown summary.
- [x] Updated unit tests for telemetry, observed metrics, evaluator, and summary writer.
- [x] Added direct coverage for the send queue guard rejection path.
- [x] Added direct coverage for request-size completion accounting via `SendCompletionTracker`.
- [x] Verified Debug build, unit tests, Release build, reduced smoke, and focused 10K validation.

## Missing Items

- [ ] `NoBufferSpaceAvailable` did not decrease from the baseline; latest focused run observed `7,344` vs baseline `6,586`.

## Changed Items (Deviations from Design)

- [ ] The design suggested renting chunk buffers. The implementation currently allocates an exact-sized chunk buffer per send loop iteration to keep the existing `IBuffers.Peek(ref byte[])` contract and enforce the chunk cap reliably.
- [ ] The focused 10K run passes current validation thresholds, but `MaxPendingRequestCount` remains elevated at `36,653`.
- [ ] The final focused 10K run did not trigger rejected responses (`0/0`), because transient server send backpressure was retried and the per-session queued byte cap was not exceeded.

## Verification

- [x] `dotnet build FastPortCharp.sln`
- [x] `dotnet test FastPortCharp.sln --no-build` (`80` tests passed)
- [x] `dotnet build FastPortCharp.sln -c Release`
- [x] Reduced smoke: `artifacts/load-validation/send-backpressure-iterate-smoke/summary.md`
  - Status: Passed
  - `smoke-fixed-10`: peak `10 / 10`, rejected send `0/0`
  - `smoke-random-25`: peak `25 / 25`, rejected send `0/0`
- [x] Focused 10K: `artifacts/load-validation/s5-send-backpressure-iterate2/summary.md`
  - Status: Passed
  - Peak: `10,000 / 10,000`
  - Final disconnects: `0`
  - Max pending send: `905`
  - Max send buffer bytes: `195,683`
  - Rejected send: `0 / 0`
  - Socket classification: `send|IOException|NoBufferSpaceAvailable = 7,344`
- [x] Benchmark snapshot updated: `docs/load-validation-benchmark-results.md`

## Recommendations

1. Treat the remaining `NoBufferSpaceAvailable` issue as a follow-up design problem rather than another blind retry inside this feature.
2. Investigate receive-side pressure next: receive pause/flow control, client send pacing, or adaptive rejection based on pending send requests rather than queued bytes alone.
3. Clamp or annotate anomalous per-second TPS samples in `LoadValidationSummaryWriter` if future summaries show outlier rates.

## Next Steps

- [ ] `$pdca report server-send-backpressure-queue-drain-optimization` with the residual `NoBufferSpaceAvailable` risk called out
- [ ] Open a follow-up PDCA for receive-side/client send-buffer pressure if that criterion must be fully closed
