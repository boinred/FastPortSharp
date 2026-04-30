# adaptive-client-send-pacing-and-rtt-stability - Design Document

> Version: 1.0.0 | Date: 2026-04-29 | Status: Completed
> Level: Starter | Plan: docs/01-plan/features/adaptive-client-send-pacing-and-rtt-stability.plan.md

---

## 1. Overview

`adaptive-client-send-pacing-and-rtt-stability`는 10K focused validation에서 남아 있는 client-side `send|IOException|NoBufferSpaceAvailable`을 낮추면서 RTT P95/P99와 receive timeout을 악화시키지 않기 위한 LoadRunner pacing feature다.

이 설계는 server send queue/drain 정책을 다시 바꾸지 않는다. 직전 결과가 보여준 핵심은 다음과 같다.

- server-only budgeted drain은 server send pressure와 RTT tail을 개선했지만 client NoBuffer는 줄이지 못했다.
- fixed cap `4`는 NoBuffer를 `7,344 -> 905`로 줄였지만 RTT P95/P99와 `receive|IOException|TimedOut`을 악화시켰다.
- fixed cap의 현재 구현은 cap에 걸린 세션들이 1ms polling loop로 대기하므로, 10K 세션에서 scheduler drift를 키울 수 있다.

따라서 이번 feature의 설계 방향은 다음 순서다.

1. 기존 fixed outstanding cap을 event-driven gate로 바꿔 polling storm을 제거한다.
2. 그 gate를 재사용해 adaptive per-session outstanding window를 추가한다.
3. pacing policy와 효과를 JSONL/summary에 노출해 cap sweep, adaptive run, uncapped baseline을 같은 표에서 비교한다.

## 2. Architecture

### 2.1 Component Boundaries

| Component | Responsibility |
|-----------|----------------|
| `FastPortLoadRunner` | client-side pacing policy, per-session outstanding request gate, pacing metrics |
| `FastPortLoadValidation` | pacing CLI forwarding, manifest/summary visibility, focused run comparison |
| `LibNetworks.Telemetry` | observed client metric DTO extension for pacing counters |
| `LibNetworks.Sessions` | unchanged for this feature except compatibility checks |
| `FastPortSmokeServer` | unchanged; remains echo/load target with server telemetry export |

This is intentionally LoadRunner-first. The current evidence points to load-generator pressure, so `LibNetworks` should not gain generic runtime flow-control policy until this feature proves the behavior is useful outside validation.

### 2.2 Runtime Flow

Current fixed cap flow:

```text
SendLoop
  -> WaitForPendingRequestBudgetAsync()
     -> while outstanding >= cap: Task.Delay(<=1ms)
  -> WriteAsync()
  -> outstanding++

ReceiveLoop
  -> parse response
  -> outstanding--
```

Target flow:

```text
SendLoop
  -> pacer.WaitForPermitAsync()
     -> returns immediately if inFlight < currentWindow
     -> otherwise awaits a signal from response/window change/cancel
  -> WriteAsync()
  -> pacer.OnRequestSent()
  -> metrics.RecordSentPacket()

ReceiveLoop
  -> parse response and calculate RTT
  -> pacer.OnResponse(rttMs)
     -> inFlight--
     -> maybe adjust adaptive window
     -> signal waiters
  -> metrics.RecordRtt()
  -> metrics.RecordReceivedPacket()
```

If `WriteAsync` fails after a permit is reserved, the session must call `pacer.OnRequestAbandoned()` before recording the socket error path. This prevents the pacing gate from leaking an in-flight permit.

### 2.3 Policy Selection

The runner supports three policy modes.

| Policy | Behavior | Use |
|--------|----------|-----|
| `none` | No outstanding-request gate | baseline and compatibility |
| `fixed-window` | Event-driven version of the existing cap | cap sweep and regression-safe replacement for polling cap |
| `adaptive-window` | AIMD-style per-session outstanding window | target feature behavior |

`--max-pending-requests-per-session <count>` remains supported as a legacy alias for `fixed-window`.

## 3. Data Model

### 3.1 Load Runner Options

Add:

```csharp
internal enum LoadPacingPolicy
{
    None,
    FixedWindow,
    AdaptiveWindow
}

internal sealed record LoadPacingOptions(
    LoadPacingPolicy Policy,
    int? FixedWindow,
    int MinWindow,
    int InitialWindow,
    int MaxWindow,
    double RttTargetMs,
    double RttHighMs,
    int IncreaseEveryResponses);
```

Default:

```text
Policy = none
FixedWindow = null
MinWindow = 1
InitialWindow = 4
MaxWindow = 16
RttTargetMs = 12000
RttHighMs = 20000
IncreaseEveryResponses = 256
```

Validation rules:

- fixed-window requires `FixedWindow > 0`;
- adaptive-window requires `1 <= MinWindow <= InitialWindow <= MaxWindow`;
- `RttTargetMs > 0`;
- `RttHighMs >= RttTargetMs`;
- `IncreaseEveryResponses > 0`;
- `--max-pending-requests-per-session` cannot be combined with `--pacing-policy adaptive-window`.

### 3.2 Load Scenario

`LoadScenario` should replace the nullable cap with `LoadPacingOptions Pacing`.

Compatibility:

- Existing code/tests that pass `MaxPendingRequestsPerSession: null` should map to `LoadPacingOptions.None`.
- Existing cap tests should move to `LoadPacingOptions.FixedWindow(count)`.

### 3.3 Pacing Controller

Add a per-session controller owned by `LoadSession`.

```csharp
internal sealed class OutstandingRequestPacer
{
    ValueTask WaitForPermitAsync(CancellationToken cancellationToken);
    void OnRequestSent();
    void OnRequestAbandoned();
    void OnResponse(double rttMs);
    OutstandingRequestPacingSnapshot CreateSnapshot();
}
```

State:

- current in-flight request count;
- current window;
- min/max observed window;
- stable response count since last window change;
- total wait count;
- total wait elapsed ticks;
- increase count;
- decrease count;
- waiter signal.

The waiter signal should be event-driven, not a polling delay. A `TaskCompletionSource` swapped under a small lock is acceptable. The controller is per-session, so contention should stay low.

### 3.4 Metrics

Extend `MetricsCollector` and `MetricsSnapshot` with pacing counters:

```text
TotalPacingWaitCount
PacingWaitsPerSecond
TotalPacingWaitTimeMs
PacingAverageWaitMs
PacingWindowIncreaseCount
PacingWindowDecreaseCount
MinObservedPacingWindow
MaxObservedPacingWindow
```

Extend `ClientObservedMetricsSnapshot` with the same fields. Keep new fields optional/defaulted at the end of the record to preserve old JSON readers as much as possible.

`LoadValidationStageSummary` should aggregate:

- max pacing wait count;
- max average wait ms;
- max pacing window increase/decrease count;
- min/max observed pacing window.

Markdown summary should include a compact `Pacing` column:

```text
waits=<count>, avg=<ms>ms, win=<min>-<max>, +/-=<inc>/<dec>
```

## 4. Adaptive Window Algorithm

### 4.1 Fixed Window

Fixed policy:

```text
currentWindow = FixedWindow
WaitForPermitAsync blocks while inFlight >= currentWindow
OnResponse decrements inFlight and signals waiters
```

This replaces the old 1ms polling behavior. Logical behavior remains a per-session outstanding-request cap.

### 4.2 Adaptive Window

Adaptive policy starts at `InitialWindow`.

On response:

1. decrement `inFlight`;
2. calculate response RTT from echoed `ClientSendTs`;
3. if `rttMs >= RttHighMs`, set `currentWindow = max(MinWindow, currentWindow / 2)`;
4. else if `rttMs <= RttTargetMs`, increment `stableResponseCount`;
5. if `stableResponseCount >= IncreaseEveryResponses`, set `currentWindow = min(MaxWindow, currentWindow + 1)` and reset the stable counter;
6. signal waiters if `inFlight < currentWindow`.

On request abandoned:

1. decrement `inFlight` if a permit was reserved;
2. signal waiters;
3. do not increase the adaptive window.

Decrease behavior is intentionally faster than increase behavior. This mirrors the result we want: keep throughput when healthy, back off quickly when latency pressure appears.

### 4.3 Why Not Global Policy First

A global window shared across all sessions would be simpler to report but riskier:

- one session's socket error could throttle every other session;
- global lock contention can become visible at 10K sessions;
- per-session request/response RTT is already available locally.

This feature should start per-session. A later feature can add global coordination if per-session adaptation is too noisy.

## 5. CLI and API Contract

### 5.1 FastPortLoadRunner CLI

Add:

```text
--pacing-policy <none|fixed-window|adaptive-window>
--pacing-fixed-window <count>
--pacing-min-window <count>
--pacing-initial-window <count>
--pacing-max-window <count>
--pacing-rtt-target-ms <ms>
--pacing-rtt-high-ms <ms>
--pacing-increase-every <count>
```

Keep:

```text
--max-pending-requests-per-session <count>
```

Compatibility behavior:

- if only `--max-pending-requests-per-session 4` is supplied, parse as `fixed-window` with `FixedWindow = 4`;
- if `--pacing-policy fixed-window` is supplied, require `--pacing-fixed-window` or use `--max-pending-requests-per-session`;
- if `--pacing-policy adaptive-window` is supplied, reject `--max-pending-requests-per-session`.

`Program.PrintPlan` should print the effective pacing policy and window thresholds.

### 5.2 FastPortLoadValidation CLI

Mirror and forward the runner options:

```text
--pacing-policy <none|fixed-window|adaptive-window>
--pacing-fixed-window <count>
--pacing-min-window <count>
--pacing-initial-window <count>
--pacing-max-window <count>
--pacing-rtt-target-ms <ms>
--pacing-rtt-high-ms <ms>
--pacing-increase-every <count>
```

`--max-pending-requests-per-session` stays as a fixed-window shortcut for existing scripts.

Manifest output should include effective pacing options so archived benchmark folders are self-describing.

## 6. Validation Design

### 6.1 Test Matrix

Required local verification:

```text
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
dotnet build FastPortCharp.sln -c Release
```

Runtime validation:

1. reduced smoke run with `--pacing-policy adaptive-window`;
2. focused 10K fixed-window event-driven cap run, preferably cap `4`, to compare against old polling cap behavior;
3. focused 10K adaptive-window run;
4. optional cap sweep if adaptive thresholds are inconclusive.

Suggested output directories:

```text
artifacts/load-validation/adaptive-pacing-smoke
artifacts/load-validation/s5-fixed-cap-4-event-gate
artifacts/load-validation/s5-adaptive-pacing-window
artifacts/load-validation/s5-adaptive-pacing-cap-sweep
```

### 6.2 Success Comparison

Adaptive run should be judged against both baseline and fixed cap `4`.

| Metric | Required Direction |
|--------|--------------------|
| Peak session ratio | stay >= 99% |
| Final disconnect count | stay <= 100 |
| `NoBufferSpaceAvailable` | materially below 7,344; target <= 3,500 |
| Socket error rate | no worse than 0.55% |
| `receive|IOException|TimedOut` | no material increase |
| RTT P95 | target <= 12,000 ms |
| RTT P99 | target <= 20,000 ms |
| Scheduler drift | should not reproduce cap `4` drift spike |
| Max pending send/send buffer | no material server-side regression |

### 6.3 Unit Tests

Add or update tests in `LibCommonTest/FastPortLoadRunnerTests.cs`:

- default options parse as pacing policy `none`;
- legacy `--max-pending-requests-per-session 4` maps to fixed-window;
- adaptive options parse and validate min/initial/max;
- invalid mixed options are rejected;
- fixed-window pacer waits until response signal without polling;
- adaptive pacer increases after stable responses;
- adaptive pacer decreases after high RTT;
- abandoned request releases reserved permit.

Add or update tests in `LibCommonTest/FastPortLoadValidationTests.cs`:

- validation options parse pacing options;
- command builder forwards pacing options;
- summary writer includes pacing fields;
- evaluator aggregates pacing counters from client samples.

Add or update tests in `LibCommonTest/ObservedMetricsTests.cs`:

- new client observed pacing fields serialize/deserialize through `ObservedMetricsJson`;
- `MetricsSnapshot.ToClientObservedMetricsSnapshot()` maps pacing fields.

## 7. Implementation Order

1. Add `LoadPacingPolicy` and `LoadPacingOptions`.
2. Update `LoadRunnerOptions.TryParse`, usage text, `LoadScenario`, and `Program.PrintPlan`.
3. Add `OutstandingRequestPacer` with event-driven fixed-window and adaptive-window behavior.
4. Replace `LoadSession.WaitForPendingRequestBudgetAsync` polling path with pacer calls.
5. Add pacing metrics to `MetricsCollector`, `MetricsSnapshot`, and observed client DTO.
6. Update `FastPortLoadValidation` options, command builder, evaluator, summary writer, and manifest model.
7. Add focused unit tests.
8. Run build/tests.
9. Run reduced smoke validation.
10. Run focused 10K adaptive validation and update `docs/load-validation-benchmark-results.md`.

## 8. Compatibility

- Default runner behavior remains uncapped and unpaced.
- Existing `--max-pending-requests-per-session` scripts keep working, but their wait implementation becomes event-driven.
- Existing JSONL readers should tolerate new client observed fields because fields are appended with defaults.
- Validation without server metrics must continue to work.
- Validation with server metrics merge must continue to expose server send backlog fields.

## 9. Design Decisions

| Decision | Rationale |
|----------|-----------|
| Start in `FastPortLoadRunner` | Current evidence points to load-generator pacing, not another server queue policy. |
| Replace polling gate | Cap `4` reduced NoBuffer but caused scheduler drift; event-driven wait addresses that artifact directly. |
| Keep default policy `none` | Baseline compatibility and stress testing remain available. |
| Support fixed and adaptive policies | Fixed-window is needed for cap sweep and for comparing against previous diagnostic runs. |
| Use per-session window first | Lower contention and direct use of per-session RTT signal. |
| Expose pacing metrics in observed client snapshot | Runtime results must be auditable without reading raw logs. |

## 10. Open Questions

- Should the first focused adaptive run use `InitialWindow = 4, MaxWindow = 16`, or start from `8` to preserve more throughput?
- Should `RttHighMs` be `20,000` based on target P99, or closer to `12,000` based on target P95?
- Should adaptive decrease trigger on single high RTT or require a small burst to avoid overreacting?
- If event-driven fixed cap `4` removes the RTT regression by itself, should adaptive-window still be implemented in the same feature?
- Should validation summary include only aggregate pacing counters, or also final effective window?

## 11. References

- `docs/01-plan/features/adaptive-client-send-pacing-and-rtt-stability.plan.md`
- `docs/load-validation-benchmark-results.md`
- `docs/archive/2026-04/client-send-buffer-pressure-receive-flow-control/client-send-buffer-pressure-receive-flow-control.report.md`
- `FastPortLoadRunner/LoadRunnerOptions.cs`
- `FastPortLoadRunner/LoadSession.cs`
- `FastPortLoadRunner/Metrics.cs`
- `FastPortLoadRunner/ObservedMetricsExtensions.cs`
- `FastPortLoadValidation/LoadValidationOptions.cs`
- `FastPortLoadValidation/LoadRunnerCommandBuilder.cs`
- `FastPortLoadValidation/LoadValidationEvaluator.cs`
- `FastPortLoadValidation/LoadValidationSummaryWriter.cs`
- `LibNetworks/Telemetry/ObservedMetrics.cs`
