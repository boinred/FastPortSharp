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
| Test coverage | Added direct tests for queue rejection and completion accounting, plus telemetry/load-summary updates. | Lock in the new send policy behavior. | `dotnet test FastPortCharp.sln --no-build` passed `80` tests. |

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

## Verification Commands

The current implementation was checked with:

- `dotnet build FastPortCharp.sln`
- `dotnet test FastPortCharp.sln --no-build` (`80` tests passed)
- `dotnet build FastPortCharp.sln -c Release`
- reduced smoke validation: `artifacts/load-validation/send-backpressure-iterate-smoke/summary.md`
- focused 10K validation: `artifacts/load-validation/s5-send-backpressure-iterate2/summary.md`
