# Load Validation Benchmark Results

> Last updated: 2026-05-06

`artifacts/load-validation/` is git ignored, so selected load-validation results are summarized here when they are used as a baseline or comparison point.

## Scope

This document tracks same-machine `FastPortTestLoadValidation` results for the 10,000-session load path. It is separate from `docs/baseline-benchmark-results.md`, which records component-level micro benchmarks.

## Latest 10K Comparison

Current diagnostic: `artifacts/load-validation/adaptive-pacing-operation-duration-s5/summary.md`

Rejected candidates:

- `artifacts/load-validation/adaptive-pacing-stability-restore-s5/summary.md`
- `artifacts/load-validation/adaptive-pacing-header-pressure-s5/summary.md`

| Metric | Reference: `s5-session-rtt-validation` | Duplex lifecycle: `adaptive-pacing-duplex-cancel-s5` | Diagnostic: `adaptive-pacing-operation-duration-s5` | Rejected: `adaptive-pacing-stability-restore-s5` | Rejected: `adaptive-pacing-header-pressure-s5` |
|--------|---------------------------------------:|------------------------------------------------:|---------------------------------------------:|----------------------------------------------:|-------------------------------------------:|
| Result | Passed under old thresholds | Failed under hard guardrails | Failed under hard guardrails | Failed under hard guardrails | Failed under hard guardrails |
| Peak sessions | `10,000 / 10,000` | `9,830 / 10,000` | `9,802 / 10,000` | `9,690 / 10,000` | `9,698 / 10,000` |
| Final disconnects | `0` | `1,856` | `2,152` | `5,936` | `2,453` |
| Max pending request count | `36,695` | `38,012` | `37,544` | `38,123` | `40,453` |
| Max pending send requests | `1,095` | `1,016` | `986` | `1,194` | `1,438` |
| Server send backpressure events | `1,583` | `2,986` | `3,850` | `3,225` | `1,863` |
| Max send buffer bytes | `64,204` | `63,122` | `87,228` | `69,042` | `88,030` |
| `send\|IOException\|NoBufferSpaceAvailable` | `1,639` | `1,396` | `1,742` | `1,340` | `1,217` |
| `receive\|IOException\|TimedOut` | `184` | `460` | `410` | `4,554` | `1,236` |
| Socket error rate | `0.13%` | `0.14%` | `0.17%` | `0.18%` | `0.18%` |
| Max TPS | `9,371.08` | `9,058.89` | `9,254.96` | `8,176.32` | `9,735.19` |
| RTT P95 | `19,210.39ms` | `16,310.30ms` | `14,980.04ms` | `20,569.94ms` | `23,529.55ms` |
| RTT P99 | `24,863.90ms` | `18,863.93ms` | `22,016.89ms` | `32,961.34ms` | `29,609.81ms` |
| Max scheduler drift | `12.12ms` | `10.64ms` | `34.35ms` | `502.25ms` | `57.59ms` |
| Session RTT p95-of-session-P95 | `18,211.02ms` | `15,818.94ms` | `17,044.66ms` | `17,924.93ms` | `27,259.05ms` |
| Pacing wait count / avg | `570,841 / 2,857.09ms` | `948,833 / 2,410.34ms` | `665,825 / 2,515.80ms` | `578,784 / 2,739.45ms` | `568,466 / 2,263.84ms` |
| Observed pacing window | `1-5` | `1-7` | `1-6` | `1-5` | `1-6` |
| Window +/- | `7 / 7,753` | `5,359 / 703` | `4,960 / 1,900` | `9 / 5,750` | `6,326 / 9,050` |
| `receive-header` avg / max | Not tracked | Not tracked | `1,474.78ms / 52,261.89ms` | `1,519.41ms / 73,423.99ms` | `1,480.13ms / 59,731.73ms` |
| `receive-body` avg / max | Not tracked | Not tracked | `8.34ms / 3,512.89ms` | `8.30ms / 1,134.88ms` | `10.75ms / 3,635.43ms` |
| `send-write` avg / max | Not tracked | Not tracked | `0.07ms / 67.28ms` | `0.09ms / 398.59ms` | `0.10ms / 106.58ms` |

Current interpretation: operation-duration telemetry narrowed the current failure. The client write path is not stalling long enough to explain the receive timeout (`send-write` max `67.28ms`). The dominant signal is waiting for the next response header: `receive-header` max reaches `52,261.89ms`, with the first timeout wave appearing after the late-ramp backlog has already pushed `receive-header` above `25s`.

The stability-restore candidate returned to older static adaptive defaults (`MaxWindow=16`, target/high `12s/20s`, increase every `256`) and was rejected. It reduced send-side `NoBufferSpaceAvailable`, but final disconnects rose to `5,936` and receive timeouts rose to `4,554`; static threshold-only tuning is now low confidence.

The header-pressure candidate reacted to long `receive-header` waits by reducing the adaptive window. It also failed: `NoBufferSpaceAvailable` dropped to `1,217`, but receive timeouts rose to `1,236`, final disconnects rose to `2,453`, RTT P95/P99 regressed, and pending requests/session reached `4.05`. Header-wait-only client feedback is also low confidence.

Guardrail update:

- `FinalDisconnectCount > 0` fails validation.
- `receive|IOException|TimedOut > 0` fails validation.
- `artifacts/load-validation/adaptive-pacing-guardrail-smoke/summary.md` passed with the stricter evaluator.
- `artifacts/load-validation/adaptive-pacing-duplex-cancel-smoke/summary.md` passed with the load-runner duplex cancellation fix.
- `artifacts/load-validation/adaptive-pacing-operation-duration-smoke/summary.md` passed with operation-duration telemetry.
- `artifacts/load-validation/adaptive-pacing-operation-duration-s5/summary.md` failed and should be treated as the current diagnostic artifact.
- `artifacts/load-validation/adaptive-pacing-stability-restore-smoke/summary.md` passed, but `adaptive-pacing-stability-restore-s5` failed badly and should not be used as the retained default.
- `artifacts/load-validation/adaptive-pacing-header-pressure-smoke/summary.md` passed, but `adaptive-pacing-header-pressure-s5` failed and the candidate was reverted.

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

## Packet Assembly Copy Follow-up

This diagnostic checks whether 8 KiB socket receive fragmentation is likely to be the current focused 10K bottleneck.

| Scenario | Packet Size | Fragment Shape | Observed Cost |
|----------|------------:|----------------|---------------|
| One-shot receive | `16 KiB` | Full packet in one append | About `1.08us` per packet, about `16.6 KiB` allocation per packet. |
| 8 KiB fragmented receive | `16 KiB` | Two 8 KiB fragments | About `1.11us` per packet, about `16.7 KiB` allocation per packet. |
| One-shot receive | `64 KiB` | Full packet in one append | About `5.36us` per packet, about `65.8 KiB` allocation per packet. |
| 8 KiB fragmented receive | `64 KiB` | Eight 8 KiB fragments | About `5.34us` per packet, about `66.0 KiB` allocation per packet. |

Follow-up interpretation:

- The 8 KiB receive boundary itself is not a material cost in the local in-memory probe.
- The measurable cost is the payload-sized copy/allocation when `ArrayPoolCircularBuffers` extracts a completed packet and `BasePacket` copies the payload into a new byte array.
- This is unlikely to explain the current 100s RTT tail by itself, so pacing, stuck-session cleanup, and phase-duration decomposition remain higher-priority diagnostics.
- Treat `BasePacket` payload ownership/copy reduction and server-side parse/assembly duration telemetry as follow-up optimization candidates before changing packet ownership semantics.

## Current Improvement Priority

The priority after the 2026-05-06 cloud and packet-assembly diagnostics is:

| Rank | Area | Reason | Completion Signal |
|-----:|------|--------|-------------------|
| 1 | `TimerQueue` and idle/stale session cleanup | Cloud validation showed server `currentSessions` can remain after the client runner exits. TCP keepalive alone cannot guarantee application-level cleanup within a known time window. | After runner completion, `currentSessions = 0` and `pendingSendRequests = 0` within the configured idle cleanup timeout. |
| 2 | Receive timeout and RTT tail decomposition | The current 10K failure shape is dominated by client receive timeouts and 100s-class RTT tail, not server socket errors. | Separate timings for connect, send, receive header, receive body, server receive, parse, and send phases. |
| 3 | Cloud 10K stability revalidation | Local and cloud results diverged, so the Azure server/local runner path is the practical stability target. | Smoke passes and staged 1K/3K/5K/10K ladder clearly identifies the first failing scale. |
| 4 | Server disconnect reason and idle timeout telemetry | TimerQueue cleanup must be distinguishable from fault disconnects, or the next analysis will blur normal cleanup and errors. | Export idle-timeout disconnect count/reason plus session age or last-receive age. |
| 5 | Pending send and send rejection reason detail | Pending send residue is no longer the primary issue, but rejected sends still need reason classification when they appear. | Split rejection by disconnected-after-enqueue, queue-full, and other policy reasons. |
| 6 | Packet assembly copy/allocation optimization | The local probe shows 8 KiB fragmentation itself is low cost, but completed packet extraction still allocates/copies payload-sized buffers. | Add server-side parse/assembly duration telemetry, then decide whether payload ownership/copy reduction is justified. |
| 7 | `remove-server-telemetry-from-network-base-classes` PDCA closure | The architecture cleanup remains useful, but it is lower priority than the current cloud lifecycle and RTT stability failures. | Finish analysis/report or explicitly defer until after TimerQueue validation. |

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
| Adaptive pacing threshold tuning | Changed adaptive-window defaults to max window `8`, RTT target `14,000ms`, RTT high `24,000ms`, increase every `128`; added hard guardrails, duplex phase cancellation, and client operation-duration telemetry; rejected the older `16/12s/20s/256` stability-restore candidate and the header-wait pressure candidate. | Allow limited window recovery while exposing real receive-path failures and separating client write, response header wait, and response body read durations. | Retained focused 10K still fails hard guardrails with `9,802 / 10,000` peak sessions and `2,152` final disconnects; rejected candidates worsened receive timeouts/disconnects despite reducing send-side NoBuffer. |
| BaseSession send queue | Replaced the send hot-path `IBuffers`/signal path with a Channel item queue, explicit byte budget, FIFO batching, and ArrayPool-backed coalesced multi-segment sends. | Reduce send-path lock contention while preserving logical completion accounting and small-packet coalescing. | Focused 10K passed at `9,975 / 10,000`; final disconnects, server send backpressure, send-side NoBuffer, and drift improved, but TPS, RTT P99, socket error rate, pending send depth, and receive timeouts still miss acceptance targets. |
| Cloud server / local runner split | Ran the smoke server on Azure `Standard_B2s` and the load runner from the local Mac against the public endpoint. | Remove same-machine local noise and create a more realistic external RTT/load path. | Smoke passed, but focused 10K failed at `9,337 / 10,000` peak sessions with `752` final disconnects, receive timeouts/resets, and very high RTT tail. |

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

## Cloud Server / Local Runner Baseline

Artifact summary:

- `artifacts/load-validation/cloud-server-runner-split/s5-random-10k/summary.md`
- Collected server artifacts: `artifacts/load-validation/cloud-server-runner-split/collected/server/`
- Started: `2026-05-05T14:09:26.7729900+09:00`
- Completed: `2026-05-05T14:16:32.8300100+09:00`
- Stage: `s5-random-10k`
- Target: `10,000`
- Peak: `9,337`
- Peak ratio: `93.37%`
- Final disconnects: `752`
- Max TPS: `1,085.41`
- Max pending request count: `29,294`
- Max drift: `32.97ms`
- RTT P95: `106,216.65ms`
- RTT P99: `274,206.02ms`
- Session RTT p95-of-p95: `222,702.93ms`
- Socket error rate: `0.28%`
- Socket classification: `receive|IOException|ConnectionReset = 495`
- Socket classification: `receive|IOException|TimedOut = 257`
- Socket classification: `connect|SocketException|TimedOut = 56`
- Operation duration: `send-write avg=0.12ms max=24.07ms`
- Operation duration: `receive-header avg=3,269.27ms max=384,958.03ms`
- Operation duration: `receive-body avg=2,571.06ms max=396,937.01ms`

Server-side collected metrics show a different pressure shape than the client-side failure:

- Max server current sessions: `9,159`
- Server socket errors: `0`
- Server send backpressure events: `0`
- Server rejected sends: `1`
- Max pending server send requests: `155`
- Max server send buffer bytes: `62,049`

Interpretation:

The first cloud split 10K result is not comparable as a win/loss against same-machine local runs because the network path changed. It is useful as a failed external-path baseline: client send writes remain fast, server send pressure is low, but receive waits, disconnects, and RTT tail dominate. The next optimization should focus on receive timeout/reset behavior, connection lifecycle cleanup, and cloud RTT tail rather than server send-buffer pressure.

## Cloud Staged RTT Tail Validation

Artifact summary:

- Smoke: `artifacts/load-validation/cloud-staged-rtt-tail-validation/smoke/summary.md`
- 1K fixed: `artifacts/load-validation/cloud-staged-rtt-tail-validation/s1-fixed-1k/summary.md`
- 1K random: `artifacts/load-validation/cloud-staged-rtt-tail-validation/s2-random-1k/summary.md`
- 3K random: `artifacts/load-validation/cloud-staged-rtt-tail-validation/s3-random-3k/summary.md`

These runs validate the cloud server/local runner path after the receive close and phase completion diagnostics were added. The server was restarted before each load stage.

| Metric | `s1-fixed-1k` | `s2-random-1k` | `s3-random-3k` |
|--------|--------------:|---------------:|---------------:|
| Result | Passed | Passed | Passed by hard guardrail |
| Peak sessions | `1000/1000` | `1000/1000` | `2994/3000` |
| Final disconnects | `0` | `0` | `0` |
| Socket error rate | `0.00%` | `0.00%` | `0.003%` |
| Max TPS | `1,214.22` | `1,045.30` | `1,097.13` |
| Max pending requests | `982` | `1,820` | `10,372` |
| RTT P95 | `1,139.18ms` | `4,560.84ms` | `30,431.21ms` |
| RTT P99 | `2,799.12ms` | `10,673.63ms` | `107,050.72ms` |
| Session RTT p95-of-p95 | `2,474.44ms` | `11,679.07ms` | `118,358.76ms` |
| Slowest session P95 | `31,025.53ms` | `74,855.55ms` | `208,252.42ms` |
| Receive header max | `32,634.68ms` | `47,584.00ms` | `212,445.14ms` |
| Receive body max | `16,485.75ms` | `66,810.64ms` | `205,681.72ms` |
| Send write max | `14.74ms` | `14.89ms` | `69.44ms` |

The staged ladder should stop at `s3-random-3k` for now. Although the validation summary still passes by hard guardrails, the 3K random run already reproduces the cloud tail/lifecycle shape: `connect|SocketException|TimedOut = 6`, RTT P99 above `100s`, operation receive waits above `200s`, and server sessions lingering after runner completion (`currentSessions = 768` immediately after completion, then `50` after an additional delay). 5K/10K would likely amplify the same failure shape rather than answer a new question.

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
- cloud server/local runner smoke validation: `artifacts/load-validation/cloud-server-runner-split/smoke/summary.md`
- cloud server/local runner focused 10K validation: `artifacts/load-validation/cloud-server-runner-split/s5-random-10k/summary.md`
- cloud staged RTT tail validation: `artifacts/load-validation/cloud-staged-rtt-tail-validation/s3-random-3k/summary.md`
