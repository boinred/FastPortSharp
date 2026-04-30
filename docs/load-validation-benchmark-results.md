# Load Validation Benchmark Results

> Last updated: 2026-04-30

`artifacts/load-validation/` is git ignored, so selected load-validation results are summarized here when they are used as a baseline or comparison point.

## Scope

This document tracks same-machine `FastPortLoadValidation` results for the 10,000-session load path. It is separate from `docs/baseline-benchmark-results.md`, which records component-level micro benchmarks.

## Latest 10K Comparison

Current candidate: `artifacts/load-validation/s5-session-rtt-validation/summary.md`

| Metric | Reference: `s5-send-channel-queue-batch-pool-adaptive` | Current: `s5-session-rtt-validation` | Change |
|--------|--------------------------------------------------------:|--------------------------------------:|-------:|
| Result | Passed | Passed | Stable |
| Peak sessions | `9,975 / 10,000` | `10,000 / 10,000` | `+0.25pp` |
| Final disconnects | `2` | `0` | Improved |
| Max pending request count | `38,246` | `36,695` | `-1,551` (`-4.1%`) |
| Max pending send requests | `1,282` | `1,095` | `-187` (`-14.6%`) |
| Server send backpressure events | `0` | `1,583` | Regression |
| Max send buffer bytes | `63,364` | `64,204` | `+840` (`+1.3%`) |
| `send\|IOException\|NoBufferSpaceAvailable` | `0` | `1,639` | Regression |
| `receive\|IOException\|TimedOut` | `1,266` | `184` | `-1,082` (`-85.5%`) |
| Socket error rate | `0.12%` | `0.13%` | `+0.01pp` |
| Max TPS | `7,901.40` | `9,371.08` | `+18.6%` |
| RTT P95 | `17,796.60ms` | `19,210.39ms` | `+7.9%` |
| RTT P99 | `27,398.15ms` | `24,863.90ms` | `-9.3%` |
| Max scheduler drift | `19.66ms` | `12.12ms` | Lower |
| Session RTT p95-of-session-P95 | Not tracked | `18,211.02ms` | Newly observed |

Current interpretation: the latest 10K run passes the validation threshold and now includes per-session RTT telemetry. It improves peak sessions, final disconnects, max TPS, pending request depth, pending send depth, receive timeouts, RTT P99, and scheduler drift compared with the previous batch+pool candidate. It is still not a clean performance win because RTT P95, server send backpressure, send-side `NoBufferSpaceAvailable`, and socket error rate remain above the desired direction. The session RTT data suggests the tail is mostly a broad high-load slowdown, with a smaller set of outlier sessions extending the worst tail.

## Initial 10K Breakthrough Comparison

| Metric | Baseline: `s5-server-merged` | Current: `s5-send-backpressure-iterate2` | Change |
|--------|------------------------------|------------------------------------------|--------|
| Result | Failed | Passed | Passes focused 10K validation |
| Peak sessions | `8,611 / 10,000` | `10,000 / 10,000` | `+13.89pp` peak ratio |
| Final disconnects | `1,855` | `0` | `-1,855` |
| Max pending request count | `52,820` | `36,653` | `-16,167` (`-30.6%`) |
| Max pending send requests | `180,466` | `905` | `-179,561` (`-99.5%`) |
| Server send backpressure events | `878,503` | `4,153` | `-874,350` (`-99.5%`) |
| Max send buffer bytes | `2,649,731` | `195,683` | `-2,454,048` (`-92.6%`) |
| Rejected send requests/bytes | Not tracked | `0 / 0` | New telemetry field |
| `send\|IOException\|NoBufferSpaceAvailable` | `6,586` | `7,344` | `+758` (`+11.5%`) |

## Client Send Buffer Pressure Follow-up

These runs validate `client-send-buffer-pressure-receive-flow-control` against the prior focused 10K baseline:

- Baseline: `artifacts/load-validation/s5-send-backpressure-iterate2/summary.md`
- Server-only budgeted drain: `artifacts/load-validation/s5-budgeted-drain/summary.md`
- Client pacing diagnostic: `artifacts/load-validation/s5-client-cap-4/summary.md`

| Metric | Baseline: `s5-send-backpressure-iterate2` | Budgeted Drain | Change | Client Cap 4 | Change |
|--------|------------------------------------------:|----------------:|--------|-------------:|--------|
| Result | Passed | Passed | Stable | Passed | Stable |
| Peak sessions | `10,000 / 10,000` | `10,000 / 10,000` | Stable | `10,000 / 10,000` | Stable |
| Final disconnects | `0` | `0` | Stable | `26` | `+26`, still within threshold |
| Max pending request count | `36,653` | `36,166` | `-487` (`-1.3%`) | `37,509` | `+856` (`+2.3%`) |
| Max pending send requests | `905` | `675` | `-230` (`-25.4%`) | `154` | `-751` (`-83.0%`) |
| Server send backpressure events | `4,153` | `996` | `-3,157` (`-76.0%`) | `1,064` | `-3,089` (`-74.4%`) |
| Max send buffer bytes | `195,683` | `162,411` | `-33,272` (`-17.0%`) | `61,567` | `-134,116` (`-68.5%`) |
| Rejected send requests/bytes | `0 / 0` | `0 / 0` | Stable | `0 / 0` | Stable |
| Drain yield count/queued bytes | Not tracked | `0 / 0` | No budget yield observed | `0 / 0` | No budget yield observed |
| `send\|IOException\|NoBufferSpaceAvailable` | `7,344` | `8,370` | `+1,026` (`+14.0%`) | `905` | `-6,439` (`-87.7%`) |
| Other socket classifications | None material | None material | Stable | `receive\|IOException\|TimedOut = 629`, `send\|IOException\|Shutdown = 1` | New tradeoff |
| Socket error rate | `0.55%` | `0.82%` | `+0.27pp` | `0.12%` | `-0.43pp` |
| Max TPS | `11,094.55` | `8,853.73` | `-20.2%` | `8,392.50` | `-24.4%` |
| RTT P95 | `10,611.83ms` | `9,250.43ms` | `-12.8%` | `18,259.91ms` | `+72.1%` |
| RTT P99 | `12,949.47ms` | `10,371.08ms` | `-19.9%` | `62,738.87ms` | `+384.5%` |
| Max scheduler drift | `12.04ms` | `4.25ms` | Lower | `86.30ms` | Higher |

### Follow-up Interpretation

Budgeted server send drain by itself does not solve the remaining client `NoBufferSpaceAvailable` issue. It improves server-side send pressure (`MaxPendingSendRequests`, send backpressure events, max send buffer bytes) and RTT tail, but `NoBufferSpaceAvailable` increases from `7,344` to `8,370`.

The client cap diagnostic strongly reduces `NoBufferSpaceAvailable` from `7,344` to `905`, which supports the hypothesis that the remaining error is largely client/load-generator pacing pressure. However, cap `4` is not a free win: it introduces receive timeouts, raises final disconnects to `26`, and worsens RTT P95/P99 materially. Treat cap `4` as a diagnostic result, not a production recommendation.

## Adaptive Client Pacing Follow-up

These runs validate `adaptive-client-send-pacing-and-rtt-stability` against the prior focused 10K baseline and the earlier polling cap diagnostic:

- Baseline: `artifacts/load-validation/s5-send-backpressure-iterate2/summary.md`
- Polling cap reference: `artifacts/load-validation/s5-client-cap-4/summary.md`
- Event-driven fixed cap: `artifacts/load-validation/s5-fixed-cap-4-event-gate/summary.md`
- Adaptive window: `artifacts/load-validation/s5-adaptive-pacing-window/summary.md`

| Metric | Baseline | Polling Cap 4 | Event-Gate Cap 4 | Adaptive Window |
|--------|---------:|--------------:|-----------------:|----------------:|
| Result | Passed | Passed | Passed | Passed |
| Peak sessions | `10,000 / 10,000` | `10,000 / 10,000` | `10,000 / 10,000` | `10,000 / 10,000` |
| Final disconnects | `0` | `26` | `89` | `0` |
| Max pending request count | `36,653` | `37,509` | `37,294` | `36,384` |
| Max pending send requests | `905` | `154` | `219` | `212` |
| Server send backpressure events | `4,153` | `1,064` | `1,453` | `501` |
| Max send buffer bytes | `195,683` | `61,567` | `63,586` | `63,233` |
| `send\|IOException\|NoBufferSpaceAvailable` | `7,344` | `905` | `1,149` | `1,415` |
| `receive\|IOException\|TimedOut` | None material | `629` | `5,150` | None material |
| Other socket classifications | None material | `send\|IOException\|Shutdown = 1` | `send\|IOException\|Shutdown = 36` | None material |
| Socket error rate | `0.55%` | `0.12%` | `0.36%` | `0.05%` |
| Max TPS | `11,094.55` | `8,392.50` | `13,118.68` | `13,034.37` |
| RTT P95 | `10,611.83ms` | `18,259.91ms` | `17,832.44ms` | `16,234.27ms` |
| RTT P99 | `12,949.47ms` | `62,738.87ms` | `26,785.33ms` | `18,420.99ms` |
| Max scheduler drift | `12.04ms` | `86.30ms` | `56.04ms` | `320.86ms` |
| Pacing wait count / avg | Not applicable | Not tracked | `272,480 / 2763.47ms` | `656,889 / 2765.26ms` |
| Observed pacing window | Not applicable | fixed `4` | `4-4` | `1-5` |
| Window +/- | Not applicable | Not tracked | `0 / 0` | `142 / 424` |

### Adaptive Pacing Interpretation

The event-driven fixed cap removes the 1ms polling implementation, but it does not remove the cap `4` tradeoff. It keeps `NoBufferSpaceAvailable` low at `1,149`, but still drives RTT P95/P99 above target and increases receive timeouts to `5,150`.

The adaptive window is the better current candidate. It keeps peak sessions at `100%`, leaves final disconnects at `0`, removes material receive timeouts, lowers socket error rate to `0.05%`, and keeps `NoBufferSpaceAvailable = 1,415`, which is below the first target of `3,500`. It also brings RTT P99 under the `20,000ms` first target.

The remaining weakness is RTT P95 and scheduler drift. RTT P95 is still `16,234.27ms`, above the `12,000ms` target, and max scheduler drift rises to `320.86ms`. Adaptive pacing should therefore be kept as an opt-in validation policy for now, with a follow-up tuning pass before considering it as a default.

## BaseSession Send Channel Queue Follow-up

This run validates `basesession-send-channel-queue-lock-reduction` against the previous adaptive-window focused 10K baseline:

- Baseline: `artifacts/load-validation/s5-adaptive-pacing-window/summary.md`
- Send Channel Queue: `artifacts/load-validation/s5-send-channel-queue-adaptive/summary.md`
- Batch + Chunk Cap: `artifacts/load-validation/s5-send-channel-queue-batch-chunk-adaptive/summary.md`
- Batch + Pool Copy: `artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive/summary.md`

| Metric | Adaptive Window | Send Channel Queue | Batch + Chunk Cap | Batch + Pool Copy |
|--------|----------------:|-------------------:|------------------:|------------------:|
| Result | Passed | Passed | Passed | Passed |
| Peak sessions | `10,000 / 10,000` | `10,000 / 10,000` | `10,000 / 10,000` | `9,975 / 10,000` |
| Final disconnects | `0` | `38` | `109` | `2` |
| Max pending request count | `36,384` | `37,587` | `37,190` | `38,246` |
| Max pending send requests | `212` | `917` | `954` | `1,282` |
| Server send backpressure events | `501` | `1,377` | `1,591` | `0` |
| Max send buffer bytes | `63,233` | `63,190` | `64,121` | `63,364` |
| `send\|IOException\|NoBufferSpaceAvailable` | `1,415` | `868` | `1,159` | `0` |
| `receive\|IOException\|TimedOut` | None material | `4,419` | `900` | `1,266` |
| Other socket classifications | None material | None material | None material | `send\|IOException\|Shutdown = 2` |
| Socket error rate | `0.05%` | `0.16%` | `0.18%` | `0.12%` |
| Max TPS | `13,034.37` | `8,878.08` | `9,672.38` | `7,901.40` |
| RTT P95 | `16,234.27ms` | `15,669.20ms` | `17,487.73ms` | `17,796.60ms` |
| RTT P99 | `18,420.99ms` | `18,166.68ms` | `30,073.37ms` | `27,398.15ms` |
| Max scheduler drift | `320.86ms` | `2,639.85ms` | `36.02ms` | `19.66ms` |
| Pacing wait count / avg | `656,889 / 2765.26ms` | `869,353 / 2538.01ms` | `877,962 / 3103.53ms` | `858,852 / 2731.11ms` |
| Observed pacing window | `1-5` | `1-5` | `1-5` | `1-5` |
| Window +/- | `142 / 424` | `262 / 1320` | `901 / 1845` | `1293 / 3791` |

### Send Channel Queue Interpretation

The Channel-based `BaseSession` send queue passes focused 10K and lowers `NoBufferSpaceAvailable`, but the first Channel-only run loses the previous byte queue's small-packet coalescing behavior and drives scheduler drift to `2,639.85ms`.

The first iterate pass adds scatter/gather batching and restores the `SendChunkBytes` cap. That fixes the drift problem (`36.02ms`) and reduces receive timeouts compared with Channel-only (`4,419 -> 900`), but it leaves final disconnects slightly above target and RTT P99 high.

The second iterate pass coalesces multi-segment batches into an ArrayPool-rented buffer before using the memory send overload. That improves final disconnects (`109 -> 2`), server send backpressure (`1,591 -> 0`), send-side `NoBufferSpaceAvailable` (`1,159 -> 0`), and drift (`36.02ms -> 19.66ms`). It is still not a clean performance win: max TPS drops further, pending send depth rises, receive timeouts remain material, socket error rate is still above the `0.10%` target, and RTT P99 remains above the `20,000ms` target.

## Session RTT Validation Follow-up

This run validates whether the latest focused 10K RTT tail is broad or concentrated by using per-session RTT telemetry.

- Baseline: `artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive/summary.md`
- Session RTT validation: `artifacts/load-validation/s5-session-rtt-validation/summary.md`
- Run ID: `20260430-172637-staged`
- Started: `2026-04-30T17:26:37.3803570+09:00`
- Completed: `2026-04-30T17:33:44.1348890+09:00`
- Server metrics: `artifacts/load-validation/s5-session-rtt-validation/server.metrics.jsonl`
- Combined metrics: `artifacts/load-validation/s5-session-rtt-validation/s5-random-10k.combined.metrics.jsonl`

| Metric | Value |
|--------|------:|
| Result | Passed |
| Peak sessions | `10,000 / 10,000` |
| Final disconnects | `0` |
| Max TPS | `9,371.08` |
| Max pending request count | `36,695` |
| Max pending send requests | `1,095` |
| Server send backpressure events | `1,583` |
| Max send buffer bytes | `64,204` |
| `send\|IOException\|NoBufferSpaceAvailable` | `1,639` |
| `receive\|IOException\|TimedOut` | `184` |
| Socket error rate | `0.13%` |
| RTT P95 | `19,210.39ms` |
| RTT P99 | `24,863.90ms` |
| Max scheduler drift | `12.12ms` |
| Merge | `407 / 0` unmatched client samples |

| Session RTT Metric | Value |
|--------------------|------:|
| Tracked sessions | `10,000` |
| Eligible sessions | `9,922` |
| Max excluded low-sample sessions | `773` |
| P50 of session P95 | `13,663.21ms` |
| P95 of session P95 | `18,211.02ms` |
| P99 of session P95 | `23,295.81ms` |
| Max session P95 | `38,710.49ms` |
| Max session P99 | `87,523.53ms` |
| Max session max RTT | `93,670.83ms` |

| Slow Session | Samples | RTT P50 | RTT P95 | RTT P99 | Max RTT |
|--------------|--------:|--------:|--------:|--------:|--------:|
| `7977` | `39 / 39` | `2,893.24ms` | `38,710.49ms` | `54,278.30ms` | `57,058.88ms` |
| `8587` | `26 / 26` | `3,337.31ms` | `31,466.97ms` | `34,156.44ms` | `35,037.68ms` |
| `9484` | `71 / 71` | `7,534.53ms` | `31,103.52ms` | `44,122.22ms` | `45,754.75ms` |
| `6764` | `49 / 49` | `191.68ms` | `30,370.43ms` | `32,200.57ms` | `32,711.67ms` |
| `8095` | `33 / 33` | `355.45ms` | `29,626.03ms` | `30,251.24ms` | `30,398.60ms` |

Session RTT interpretation:

- The global RTT P95 is `19,210.39ms`, while the P95 of per-session P95 is `18,211.02ms`. These are close enough to indicate that the problem is not limited to a tiny set of sessions.
- The max session P95 is `38,710.49ms`, so there are still concentrated outliers. However, the broader distribution is already high before the Top 5 outliers.
- The next feature should focus on throughput/pacing/server processing decomposition first. Socket error correlation remains useful, but the per-session evidence points to broad pressure before isolated starvation.

## Implemented Improvements

| Area | Change | Expected Effect | Observed Result |
|------|--------|-----------------|-----------------|
| Send drain loop | Replaced 1ms polling with a signal-driven single-flight drain. | Avoid idle polling and keep send work serialized per session. | Focused 10K reached `10,000 / 10,000` peak sessions. |
| Send operation shape | Switched the drain path to bounded `Socket.SendAsync(ReadOnlyMemory<byte>, SocketFlags, CancellationToken)` chunks. | Prevent one oversized response backlog from monopolizing send progress. | Max pending send requests dropped from `180,466` to `905`. |
| Queue bound | Added `SessionSendOptions` with default `MaxQueuedBytes = 1 MiB` and `SendChunkBytes = 64 KiB`. | Bound per-session queued response memory under load. | Max send buffer bytes dropped from `2,649,731` to `195,683`. |
| Backpressure handling | Treat server-side transient `NoBufferSpaceAvailable`/`WouldBlock` sends as retryable send backpressure. | Avoid disconnecting sessions on transient kernel send-buffer pressure. | Final disconnects dropped from `1,855` to `0`. |
| Send API contract | Added `TryRequestSendBuffers` and `TryRequestSendMessage`; existing void APIs remain as wrappers. | Let callers observe enqueue rejection without breaking existing callers. | Smoke server can drop echo responses explicitly when enqueue is rejected. |
| Completion accounting | Added request-size based send completion tracking. | Decrement pending send requests only after each queued response is fully drained. | Pending send metrics now represent logical response backlog instead of raw send-loop progress. |
| Telemetry | Added rejected-send request/byte counters to server and observed metrics. | Distinguish intentional queue rejection from socket errors and ordinary backpressure. | Current focused 10K reported `0 / 0` rejected send requests/bytes. |
| Load validation output | Added rejected-send fields to JSON and Markdown summaries. | Keep future benchmark comparisons auditable without parsing raw metrics logs. | `summary.md` now reports the `Rejected Send` column. |
| Test coverage | Added direct tests for queue rejection, completion accounting, FIFO send completion, batch chunk limits, pacing gates, manifest options, and telemetry/load-summary updates. | Lock in the new send policy behavior. | `dotnet test FastPortCharp.sln --no-build` passed `97` tests. |
| Budgeted drain | Added per-wake drain byte/op budget and drain-yield telemetry. | Reduce response burst pressure without violating `Drain(sentSize)`. | Server-side send pressure improved, but client NoBuffer worsened in uncapped 10K. |
| Load-runner pacing | Added `--max-pending-requests-per-session`. | Diagnose whether client pacing drives NoBuffer. | Cap `4` reduced NoBuffer by `87.7%` but worsened RTT tail and introduced receive timeouts. |
| Adaptive client pacing | Added event-driven fixed/adaptive outstanding request pacing with pacing metrics and manifest options. | Lower client send-buffer pressure without cap `4` receive-timeout regression. | Adaptive 10K reduced NoBuffer to `1,415`, removed material receive timeouts, and kept RTT P99 under `20,000ms`; RTT P95/drift still need tuning. |
| BaseSession send queue | Replaced the send hot-path `IBuffers`/signal path with a Channel item queue, explicit byte budget, FIFO batching, and ArrayPool-backed coalesced multi-segment sends. | Reduce send-path lock contention while preserving logical completion accounting and small-packet coalescing. | Focused 10K passed at `9,975 / 10,000`; final disconnects, server send backpressure, send-side NoBuffer, and drift improved, but TPS, RTT P99, socket error rate, pending send depth, and receive timeouts still miss acceptance targets. |

## Current Run Details

Artifact summary:

- `artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive/summary.md`
- Started: `2026-04-30T13:13:13.5118440+09:00`
- Completed: `2026-04-30T13:20:21.0860460+09:00`
- Stage: `s5-random-10k`
- Target: `10,000`
- Peak: `9,975`
- Peak ratio: `99.75%`
- Final disconnects: `2`
- Max TPS: `7,901.40`
- Max pending request count: `38,246`
- Max pending send requests: `1,282`
- Server send backpressure events: `0`
- Rejected send requests/bytes: `0 / 0`
- Max send buffer bytes: `63,364`
- Max drift: `19.66ms`
- RTT P95: `17,796.60ms`
- RTT P99: `27,398.15ms`
- Socket error rate: `0.12%`
- Socket classification: `receive|IOException|TimedOut = 1,266`
- Socket classification: `send|IOException|Shutdown = 2`

## Interpretation

The current BaseSession Channel send queue candidate is functionally correct and can pass the focused 10K validation:

- focused 10K reaches `9,975 / 10,000`, which is above the 99% peak session target;
- rejected send remains `0 / 0`;
- send-side `NoBufferSpaceAvailable` improves from the adaptive-window reference `1,415` to `0`;
- server send backpressure improves from `501` to `0`;
- final disconnects remain within target at `2`;
- max scheduler drift improves from `320.86ms` to `19.66ms`;
- max send buffer bytes stays near the previous adaptive-window level (`63,233 -> 63,364`).

It should not be treated as a clean performance win. Against `s5-adaptive-pacing-window`, the latest run still regresses max TPS, pending request depth, pending send requests, socket error rate, RTT P95/P99, and receive timeouts. The next decision should be to report this feature as a structural refactor with known benchmark tradeoffs, then split a narrower follow-up for send throughput and receive-timeout tail behavior.

## Verification Commands

The current implementation was checked with:

- `dotnet build FastPortCharp.sln`
- `dotnet test FastPortCharp.sln --no-build` (`97` tests passed)
- `dotnet build FastPortCharp.sln -c Release`
- latest reduced smoke validation: `artifacts/load-validation/send-channel-queue-batch-pool-smoke/summary.md`
- reduced smoke validation: `artifacts/load-validation/send-backpressure-iterate-smoke/summary.md`
- focused 10K validation: `artifacts/load-validation/s5-send-backpressure-iterate2/summary.md`
- follow-up focused 10K server-only budgeted drain: `artifacts/load-validation/s5-budgeted-drain/summary.md`
- follow-up focused 10K client cap `4`: `artifacts/load-validation/s5-client-cap-4/summary.md`
- follow-up focused 10K event-driven fixed cap `4`: `artifacts/load-validation/s5-fixed-cap-4-event-gate/summary.md`
- follow-up focused 10K adaptive pacing: `artifacts/load-validation/s5-adaptive-pacing-window/summary.md`
- follow-up focused 10K Channel send queue: `artifacts/load-validation/s5-send-channel-queue-adaptive/summary.md`
- follow-up focused 10K Channel send queue with scatter/gather batching and chunk cap: `artifacts/load-validation/s5-send-channel-queue-batch-chunk-adaptive/summary.md`
- follow-up focused 10K Channel send queue with ArrayPool coalesced batching: `artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive/summary.md`
