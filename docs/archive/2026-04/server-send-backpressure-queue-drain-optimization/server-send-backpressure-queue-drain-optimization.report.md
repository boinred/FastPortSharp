# Completion Report: server-send-backpressure-queue-drain-optimization

> Date: 2026-04-29 | Match Rate: 93%

---

## Summary

`server-send-backpressure-queue-drain-optimization` 기능은 1차 완료 기준을 충족했다.

이번 변경은 10K 부하에서 서버 send backlog가 과도하게 누적되던 문제를 `BaseSession` send path에서 직접 제어한다. 기존 1ms polling + `SocketAsyncEventArgs` drain 흐름을 signal-driven single-flight chunked drain으로 바꾸고, per-session queued bytes cap, bool-returning send API, rejected-send telemetry, load-validation summary fields를 추가했다.

핵심 결과는 focused 10K run이 `8,611 / 10,000` peak에서 `10,000 / 10,000` peak로 개선되고, final disconnects가 `1,855`에서 `0`으로 줄었다는 점이다. 다만 `NoBufferSpaceAvailable`은 `6,586`에서 `7,344`로 증가했으므로, 이 feature는 server send backlog 최적화로 완료하고 client/socket send-buffer pressure는 후속 과제로 남긴다.

## Related Documents

- Plan: `docs/01-plan/features/server-send-backpressure-queue-drain-optimization.plan.md`
- Design: `docs/02-design/features/server-send-backpressure-queue-drain-optimization.design.md`
- Analysis: `docs/03-analysis/server-send-backpressure-queue-drain-optimization.analysis.md`
- Benchmark: `docs/load-validation-benchmark-results.md`

## Completed Items

- Send queue/drain optimization
  - Added `SessionSendOptions` with default `MaxQueuedBytes = 1 MiB` and `SendChunkBytes = 64 KiB`
  - Preserved existing `BaseSession`, `BaseSessionClient`, and `BaseSessionServer` constructor compatibility
  - Added optional `SessionSendOptions?` constructor overloads
  - Added `TryRequestSendBuffers` and `TryRequestSendMessage`
  - Kept existing `RequestSendBuffers` and `RequestSendMessage` as compatibility wrappers
  - Replaced the polling send loop with a `SemaphoreSlim` signal-driven drain
  - Switched the send path to bounded `Socket.SendAsync(ReadOnlyMemory<byte>, SocketFlags, CancellationToken)` chunks
  - Treated server-side transient `NoBufferSpaceAvailable` and `WouldBlock` sends as retryable backpressure
- Send queue policy and accounting
  - Added queued-byte guard before enqueue
  - Added rejected-send recording through `RecordSendRejected`
  - Added request-size completion accounting with `SendCompletionTracker`
  - Decremented pending send requests only after each queued response is fully drained
- Smoke server behavior
  - Changed `FastPortSmokeClientSession.SendMessage` to return `bool`
  - Dropped echo responses explicitly when enqueue is rejected
- Telemetry and load validation
  - Added rejected-send counters to server telemetry snapshots
  - Added rejected-send fields to observed metrics snapshots
  - Added rejected-send max fields to load-validation stage summaries
  - Added `Rejected Send` to Markdown summary output
  - Added benchmark snapshot document for selected 10K load-validation results
- Tests
  - Updated server telemetry tests
  - Updated observed metrics tests
  - Updated load validation evaluator and summary writer tests
  - Added direct queue rejection coverage
  - Added direct send completion accounting coverage

## Verification

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
dotnet build FastPortCharp.sln -c Release
```

Results:

- Debug build passed
- Test suite passed: `80` passed, `0` failed
- Release build passed
- `git diff --check` passed

Reduced smoke validation:

- Artifact: `artifacts/load-validation/send-backpressure-iterate-smoke/summary.md`
- Status: Passed
- `smoke-fixed-10`: peak `10 / 10`, rejected send `0 / 0`
- `smoke-random-25`: peak `25 / 25`, rejected send `0 / 0`

Focused 10K validation:

- Artifact: `artifacts/load-validation/s5-send-backpressure-iterate2/summary.md`
- Status: Passed
- Stage: `s5-random-10k`
- Peak: `10,000 / 10,000`
- Final disconnects: `0`
- Max pending send requests: `905`
- Max send buffer bytes: `195,683`
- Rejected send requests/bytes: `0 / 0`
- Socket classification: `send|IOException|NoBufferSpaceAvailable = 7,344`

## Benchmark Comparison

| Metric | Baseline | Current | Change |
|--------|---------:|--------:|-------:|
| Peak sessions | `8,611 / 10,000` | `10,000 / 10,000` | `+13.89pp` peak ratio |
| Final disconnects | `1,855` | `0` | `-1,855` |
| Max pending request count | `52,820` | `36,653` | `-16,167` (`-30.6%`) |
| Max pending send requests | `180,466` | `905` | `-179,561` (`-99.5%`) |
| Server send backpressure events | `878,503` | `4,153` | `-874,350` (`-99.5%`) |
| Max send buffer bytes | `2,649,731` | `195,683` | `-2,454,048` (`-92.6%`) |
| `send\|IOException\|NoBufferSpaceAvailable` | `6,586` | `7,344` | `+758` (`+11.5%`) |

Conclusion:

- Server send backlog is materially reduced.
- 10K session retention is now stable for the focused stage.
- The dominant remaining socket error moved outside the solved scope of this feature.

## Deviations from Design

- The design suggested renting chunk buffers, but the implementation currently allocates exact-sized chunk buffers per send loop iteration.
  - Reason: this keeps the existing `IBuffers.Peek(ref byte[])` contract intact while enforcing the chunk cap reliably.
  - Follow-up: revisit buffer rental if allocation pressure becomes visible in benchmark or profiler output.
- The focused 10K run did not trigger rejected sends.
  - Reason: transient server send backpressure was retried and the per-session queued-byte cap was not exceeded.
  - Impact: rejected-send telemetry is present and tested, but the current workload validates the non-rejection path.
- `NoBufferSpaceAvailable` did not improve.
  - Reason: server send queue control reduced backlog, but client/kernel send-buffer pressure remains.
  - Impact: treat as residual risk, not as a hidden success.

## Quality Metrics

| Metric | Value |
|--------|------:|
| Final match rate | `93%` |
| PDCA iterations | `1` |
| Unit tests | `80 passed` |
| Focused 10K result | `Passed` |
| Peak session ratio | `100.00%` |
| Final disconnects | `0` |
| Max pending send reduction | `99.5%` |
| Max send buffer reduction | `92.6%` |

## Residual Risk

- `send|IOException|NoBufferSpaceAvailable` remains unresolved and increased from `6,586` to `7,344`.
- `MaxPendingRequestCount` is still elevated at `36,653`, even though it improved from `52,820`.
- Exact-sized chunk allocation is acceptable for correctness but may deserve a later allocation-focused optimization pass.
- The rejected-send path is unit-tested but was not exercised by the latest focused 10K run because queue caps were not exceeded.

## Lessons Learned

1. Server send backlog and client-side send-buffer pressure must be tracked separately. Reducing one does not automatically reduce the other.
2. Pending send request accounting needs request-size completion tracking; socket completion alone is too coarse for logical response backlog.
3. A successful 10K peak ratio is not enough as a completion signal. Socket error classification remains necessary to decide the next bottleneck.
4. Load-validation summaries need explicit rejected-send fields so overload policy is not hidden behind ordinary pass/fail status.

## Recommended Next Step

Commit this feature with the report and benchmark document included, then push.

After that, choose one of the following:

- Archive this feature if the accepted outcome is "server send backlog fixed, NoBuffer remains follow-up."
- Open a follow-up PDCA for receive-side/client send-buffer pressure if `NoBufferSpaceAvailable` must be reduced.

Suggested follow-up feature name:

```text
client-send-buffer-pressure-receive-flow-control
```
