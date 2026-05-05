# throughput-pacing-server-processing-decomposition - Design Document

> Version: 1.0.0 | Date: 2026-05-01 | Status: Completed
> Level: Starter | Plan: docs/01-plan/features/throughput-pacing-server-processing-decomposition.plan.md

---

## 1. Overview

This feature decomposes the current 10K RTT tail into measurable client, server, and socket-pressure segments.

It is a diagnostic feature. It should not change `LibNetworks` behavior unless a missing metric requires a small and bounded telemetry hook.

The immediate reason for this work is that OCI split-machine validation is blocked by Free Tier A1 capacity. Local same-machine data is still useful if the output is treated as decomposition guidance, not as production capacity proof.

## 2. Current Signal Coverage

### 2.1 Client Observed Metrics

`FastPortLoadRunner` already records:

| Segment | Existing Signal | Source |
|---------|-----------------|--------|
| connection pressure | `connectAttemptCount`, `connectFailureCount`, `currentSessions`, `disconnectCount` | `MetricsCollector` |
| packet throughput | `sentPacketsPerSecond`, `receivedPacketsPerSecond`, `tps` | `MetricsCollector.CreateSnapshot` |
| byte throughput | `sentBytesPerSecond`, `receivedBytesPerSecond` | `MetricsCollector.CreateSnapshot` |
| global RTT | `rttAverageMs`, `rttP50Ms`, `rttP95Ms`, `rttP99Ms` | `MetricsCollector.RecordRtt` |
| per-session RTT tail | `sessionRtt` summary and slowest sessions | `MetricsCollector.CreateSessionRttSummary` |
| global pending request depth | `pendingRequestCount`, `maxPendingRequestCount` | `RecordSentPacket` / `RecordReceivedPacket` |
| pacing pressure | `pacingWaitCount`, `pacingAverageWaitMs`, pacing window min/max/increase/decrease | `OutstandingRequestPacer` |
| scheduler pressure | `schedulerDriftAverageMs`, `schedulerDriftMaxMs` | reporter clock |
| socket error classification | phase/type/code/class counters | `RecordSocketError` |

### 2.2 Server Observed Metrics

`LibNetworks.Telemetry` already records:

| Segment | Existing Signal | Source |
|---------|-----------------|--------|
| accept/disconnect | `acceptedSessions`, `disconnectedSessions`, `connectedSessions` | `ServerTelemetryCollector` |
| receive throughput | `receivedPackets`, `receivedBytes` | `RecordReceived` |
| send throughput | `sentPackets`, `sentBytes` | `RecordSent` |
| send queue pressure | `sendRequests`, `pendingSendRequests`, `maxPendingSendRequests` | `RecordSendRequested` / `RecordSendCompleted` |
| backpressure | `sendBackpressureEvents` | `RecordSendBackpressure` |
| rejected sends | `sendRejectedRequests`, `sendRejectedBytes` | `RecordSendRejected` |
| drain fairness pressure | `sendDrainYieldCount`, `maxSendDrainYieldQueuedBytes` | `RecordSendDrainYield` |
| queued bytes | `sendBufferBytes`, `maxSendBufferBytes` | `RecordSendBufferSample` |
| socket/protocol health | socket/parse/protocol errors | `ServerTelemetryCollector` |

### 2.3 Summary Aggregation

`FastPortLoadValidation` already calculates:

- max TPS
- max RTT P95/P99
- max pending request count
- max pending send requests
- max send backpressure events
- max send buffer bytes
- max pacing average wait
- min/max pacing window
- socket error counts by phase/class
- per-session RTT tail summary
- merged server/client sample counts and merge skew

## 3. Missing Decomposition Signals

The current data can show that pressure exists, but not always where time is spent.

| Missing Signal | Why It Matters | Proposed Boundary |
|----------------|----------------|-------------------|
| client write duration | Distinguishes pacing wait from slow `NetworkStream.WriteAsync`/flush | `FastPortLoadRunner.LoadSession.SendLoopAsync` |
| client receive read duration | Distinguishes network/server delay from local read wait | `FastPortLoadRunner.LoadSession.ReceiveLoopAsync` |
| server echo handler processing duration | Distinguishes server app processing from engine send pressure | `FastPortSmokeServer` only |
| server send queue residency | Distinguishes pending send count from actual enqueue-to-completion age | `LibNetworks.Sessions` telemetry hook if needed |
| server receive-to-send request count alignment | Shows whether received packets convert into send requests at expected rate | derived from existing merged metrics |

The first implementation should prefer derived analysis from existing metrics. Add direct timing metrics only if the design-to-code gap analysis shows the derived model is insufficient.

## 4. Decomposition Model

### 4.1 Pipeline

```text
client send scheduler
  -> client pacing gate
  -> client WriteAsync/FlushAsync
  -> socket/network
  -> server receive
  -> server echo parse/build
  -> server send queue enqueue
  -> server send drain
  -> socket/network
  -> client receive/read
  -> RTT sample
```

### 4.2 Derived Diagnostic Rules

| Finding | Likely Pressure Boundary |
|---------|--------------------------|
| high `pacingAverageWaitMs`, window collapses to 1, pending request bounded | client pacing intentionally throttling |
| high `pendingRequestCount`, low pacing wait | client is outpacing response path |
| high server `pendingSendRequests`, `sendBackpressureEvents`, `sendDrainYieldCount` | server send drain/socket pressure |
| high `NoBufferSpaceAvailable` with high send buffer bytes | OS/socket send pressure |
| high scheduler drift with otherwise stable counters | local machine scheduling noise |
| broad per-session RTT tail close to global P95 | systemic pressure, not isolated slow sessions |
| few slow sessions dominate slowest list but global P95 lower | isolated session/outlier issue |
| received pps much lower than sent pps with growing pending | response path bottleneck |
| server received packets high but send requests/completions lag | server processing or send enqueue boundary |

## 5. Implementation Design

### 5.1 Phase 1: Diagnostic Summary Mapping

Update the validation summary interpretation without changing runtime behavior:

- Add a decomposition checklist in docs or summary notes.
- Map each existing summary field to the model segment.
- Identify the strongest current bottleneck candidate from existing `summary.json`.

Expected first read of the current 10K artifact:

- broad pressure is present because global RTT P95 and per-session P95-of-P95 are close.
- client pacing is active because max average pacing wait is high and pacing window reaches `1`.
- server send pressure is present but not explosive because max pending send requests is `1,095` and max send buffer bytes is `64,204`.
- socket pressure is still present because `send|IOException|NoBufferSpaceAvailable` remains non-zero.

### 5.2 Phase 2: Minimal Missing Metrics

If existing metrics cannot isolate the boundary, add only these metrics first:

| Metric | Location | Shape |
|--------|----------|-------|
| `writeDurationAverageMs`, `writeDurationMaxMs` | `FastPortLoadRunner` | aggregate counters |
| `readDurationAverageMs`, `readDurationMaxMs` | `FastPortLoadRunner` | aggregate counters |
| `echoProcessingAverageMs`, `echoProcessingMaxMs` | `FastPortSmokeServer` telemetry | aggregate counters |

Do not add per-packet JSON rows. Keep the existing sample interval model.

### 5.3 Phase 3: Optional Engine Hook

Only if Phase 1 and Phase 2 are insufficient, add a small server send queue residency hook:

- attach enqueue timestamp to send queue item
- record aggregate send queue age when a send item completes
- export average/max queue age

This is more invasive because it touches `LibNetworks.Sessions.BaseSession`, so it must be deferred until the lower-risk metrics prove insufficient.

## 6. Files And Ownership

Likely files if implementation proceeds:

| File | Change Type |
|------|-------------|
| `FastPortLoadRunner/Metrics.cs` | add client read/write duration aggregates if needed |
| `FastPortLoadRunner/LoadSession.cs` | measure write/read waits if needed |
| `FastPortLoadRunner/ObservedMetricsExtensions.cs` | map new client fields if needed |
| `FastPortLoadValidation/LoadValidationEvaluator.cs` | surface derived decomposition fields if needed |
| `FastPortLoadValidation/LoadValidationSummaryWriter.cs` | summary output if model fields are added |
| `FastPortSmokeServer/` | smoke-server processing timing if needed |
| `LibNetworks/Telemetry/ServerTelemetry.cs` | only if server timing or queue residency is required |

## 7. Verification Plan

### 7.1 No-Code Verification

Use the current latest artifact:

```bash
jq '.stages[0] | {
  maxTps,
  maxRttP95Ms,
  maxRttP99Ms,
  maxPendingRequestCount,
  maxPendingSendRequests,
  maxPacingAverageWaitMs,
  minObservedPacingWindow,
  maxObservedPacingWindow,
  maxSendBackpressureEvents,
  maxSendBufferBytes,
  maxSessionRttP95OfP95Ms,
  socketErrorCountsByClass
}' artifacts/load-validation/s5-session-rtt-validation/summary.json
```

### 7.2 Post-Implementation Verification

If code changes are added:

```bash
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln --no-build
```

Then run a low-risk smoke validation before any 10K run:

```bash
dotnet run -c Release --project FastPortLoadValidation -- \
  --profile smoke \
  --output artifacts/load-validation/throughput-decomposition-smoke \
  --server-metrics artifacts/load-validation/throughput-decomposition-smoke/server.metrics.jsonl
```

Only after smoke passes, run focused 10K locally if needed:

```bash
dotnet run -c Release --project FastPortLoadValidation -- \
  --profile staged \
  --stage s5-random-10k \
  --pacing-policy adaptive-window \
  --output artifacts/load-validation/throughput-decomposition-s5 \
  --server-metrics artifacts/load-validation/throughput-decomposition-s5/server.metrics.jsonl
```

## 8. Decision Criteria

The report should recommend exactly one next optimization lane.

| Evidence | Next Lane |
|----------|-----------|
| pacing wait/window dominates | `adaptive-client-pacing-threshold-tuning` |
| server processing duration dominates | `server-processing-throughput-tuning` |
| client read/write duration dominates | load runner / OS socket tuning |
| server queue age/backpressure dominates | `send-throughput-drain-fairness-optimization` |
| timeout errors dominate with broad pending depth | `receive-timeout-tail-flow-control` |

## 9. Risks

| Risk | Mitigation |
|------|------------|
| Added timing metrics perturb 10K workload | aggregate only, sample interval output only |
| Same-machine noise misleads the model | mark findings as local decomposition until cloud split validation succeeds |
| Scope drifts into optimization | require report before behavior changes |
| Engine hook gets too invasive | defer queue residency until client/smoke-server metrics are insufficient |

## 10. References

- `docs/01-plan/features/throughput-pacing-server-processing-decomposition.plan.md`
- `docs/load-validation-benchmark-results.md`
- `artifacts/load-validation/s5-session-rtt-validation/summary.json`
- `FastPortLoadRunner/Metrics.cs`
- `FastPortLoadRunner/LoadSession.cs`
- `FastPortLoadValidation/LoadValidationEvaluator.cs`
- `LibNetworks/Telemetry/ServerTelemetry.cs`
