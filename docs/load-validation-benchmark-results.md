# Load Validation Benchmark Results

> Last updated: 2026-04-29

`artifacts/load-validation/` is git ignored, so selected load-validation results are summarized here when they are used as a baseline or comparison point.

## Scope

This document tracks same-machine `FastPortLoadValidation` results for the 10,000-session load path. It is separate from `docs/baseline-benchmark-results.md`, which records component-level micro benchmarks.

## Latest 10K Comparison

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
| Test coverage | Added direct tests for queue rejection, completion accounting, pacing gates, manifest options, and telemetry/load-summary updates. | Lock in the new send policy behavior. | `dotnet test FastPortCharp.sln --no-build` passed `93` tests. |
| Budgeted drain | Added per-wake drain byte/op budget and drain-yield telemetry. | Reduce response burst pressure without violating `Drain(sentSize)`. | Server-side send pressure improved, but client NoBuffer worsened in uncapped 10K. |
| Load-runner pacing | Added `--max-pending-requests-per-session`. | Diagnose whether client pacing drives NoBuffer. | Cap `4` reduced NoBuffer by `87.7%` but worsened RTT tail and introduced receive timeouts. |
| Adaptive client pacing | Added event-driven fixed/adaptive outstanding request pacing with pacing metrics and manifest options. | Lower client send-buffer pressure without cap `4` receive-timeout regression. | Adaptive 10K reduced NoBuffer to `1,415`, removed material receive timeouts, and kept RTT P99 under `20,000ms`; RTT P95/drift still need tuning. |

## Current Run Details

Artifact summary:

- `artifacts/load-validation/s5-send-backpressure-iterate2/summary.md`
- Started: `2026-04-29T13:27:38.7571200+09:00`
- Completed: `2026-04-29T13:34:47.1330340+09:00`
- Stage: `s5-random-10k`
- Target: `10,000`
- Peak: `10,000`
- Peak ratio: `100.00%`
- Max TPS: `11,094.55`
- Max pending request count: `36,653`
- Max pending send requests: `905`
- Server send backpressure events: `4,153`
- Rejected send requests/bytes: `0 / 0`
- Max drift: `12.04ms`
- RTT P95: `10,611.83ms`
- RTT P99: `12,949.47ms`
- Socket error rate: `0.55%`
- Socket classification: `send|IOException|NoBufferSpaceAvailable = 7,344`

## Interpretation

The send backpressure queue-drain optimization materially fixes the server-side send backlog problem:

- focused 10K now reaches the full `10,000 / 10,000` peak session target;
- final disconnects drop to `0`;
- pending send requests drop from `180,466` to `905`;
- server send backpressure events drop from `878,503` to `4,153`;
- max send buffer bytes stay below the configured `1 MiB` queue cap.

The remaining unresolved benchmark item is `NoBufferSpaceAvailable`. It increased from `6,586` to `7,344`, so the current change should be treated as a server send-backlog fix, not a complete client/socket send-buffer pressure fix.

The follow-up validation confirms that interpretation. Server-only budgeted drain does not lower client NoBuffer, while client pacing does. The tradeoff is that cap `4` reduces NoBuffer at the cost of RTT tail and receive timeout behavior, so the next decision should focus on a more balanced pacing or receive strategy rather than blindly enabling cap `4`.

## Verification Commands

The current implementation was checked with:

- `dotnet build FastPortCharp.sln`
- `dotnet test FastPortCharp.sln --no-build` (`80` tests passed)
- `dotnet build FastPortCharp.sln -c Release`
- reduced smoke validation: `artifacts/load-validation/send-backpressure-iterate-smoke/summary.md`
- focused 10K validation: `artifacts/load-validation/s5-send-backpressure-iterate2/summary.md`
- follow-up focused 10K server-only budgeted drain: `artifacts/load-validation/s5-budgeted-drain/summary.md`
- follow-up focused 10K client cap `4`: `artifacts/load-validation/s5-client-cap-4/summary.md`
- follow-up focused 10K event-driven fixed cap `4`: `artifacts/load-validation/s5-fixed-cap-4-event-gate/summary.md`
- follow-up focused 10K adaptive pacing: `artifacts/load-validation/s5-adaptive-pacing-window/summary.md`
