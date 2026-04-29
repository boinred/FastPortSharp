# Completion Report: client-send-buffer-pressure-receive-flow-control

> Date: 2026-04-29 | Level: Starter | Status: Completed

---

## 1. Summary

### 1.1 Feature Overview

`client-send-buffer-pressure-receive-flow-control`는 10K focused run 이후 남아 있던 client-side `send|IOException|NoBufferSpaceAvailable` 압박을 다루기 위한 후속 작업이다.

이번 작업에서는 server send queue를 한 번 깨어났을 때 끝까지 비우는 구조 대신, wake 단위 byte/operation budget으로 나누어 drain하도록 바꾸었다. 동시에 load runner에는 per-session outstanding request cap을 추가해, 남은 client send-buffer pressure가 client pacing 문제인지 진단할 수 있게 했다.

중요한 invariant는 유지했다.

```text
send queue drain bytes == Socket.SendAsync returned sentSize
```

즉, TCP partial send 가능성을 고려해 성공한 byte 수보다 더 많이 queue에서 제거하지 않는다.

### 1.2 Final Match Rate

94% (Target: 90%)

분석 기준으로 17개 설계 항목 중 16개가 match 또는 substantially matched 상태다. 남은 1개는 focused 10K uncapped/capped benchmark evidence와 benchmark 문서 갱신이다.

## 2. Related Documents

| Document | Path |
|----------|------|
| Plan | `docs/01-plan/features/client-send-buffer-pressure-receive-flow-control.plan.md` |
| Design | `docs/02-design/features/client-send-buffer-pressure-receive-flow-control.design.md` |
| Analysis | `docs/03-analysis/client-send-buffer-pressure-receive-flow-control.analysis.md` |
| Smoke validation | `artifacts/load-validation/receive-flow-control-smoke/summary.md` |
| Focused 10K uncapped | `artifacts/load-validation/s5-budgeted-drain/summary.md` |
| Focused 10K client cap 4 | `artifacts/load-validation/s5-client-cap-4/summary.md` |
| Benchmark summary | `docs/load-validation-benchmark-results.md` |

## 3. Completed Items

- [x] `SessionSendOptions`에 `MaxDrainBytesPerSignal`, `MaxDrainOperationsPerSignal`, `TransientSendBackoffMs`를 추가했다.
- [x] `BaseSession` send loop에 per-wake drain budget을 적용했다.
- [x] send queue는 `Socket.SendAsync`의 `sentSize`만큼만 drain하도록 유지했다.
- [x] budget exhaustion 시 send drain yield를 기록하고 send loop를 재신호한다.
- [x] transient `NoBufferSpaceAvailable` / `WouldBlock` 경로는 drain/completion 없이 backoff 후 retry한다.
- [x] server telemetry에 `SendDrainYieldCount`, `MaxSendDrainYieldQueuedBytes`를 추가했다.
- [x] observed server metrics에 drain yield count/rate/max queued bytes를 노출했다.
- [x] `FastPortLoadRunner`에 `--max-pending-requests-per-session` 옵션을 추가했다.
- [x] `LoadSession`에서 per-session outstanding request cap을 기준으로 send를 gate한다.
- [x] `FastPortLoadValidation`이 load runner cap 옵션을 전달하도록 했다.
- [x] load validation JSON/Markdown summary에 drain yield 항목을 추가했다.
- [x] deterministic unit tests로 transient no-drain/no-completion과 load-runner pacing gate를 검증했다.
- [x] Release smoke validation으로 server metrics export/merge path를 확인했다.

## 4. Deviations from Design

| Deviation | Decision | Reason |
|-----------|----------|--------|
| Generic `--runner-args` 대신 explicit `--max-pending-requests-per-session` forwarding 사용 | Accepted | 이번 기능에 필요한 옵션만 타입 안정적으로 전달할 수 있고, 기존 command builder 구조와 더 잘 맞는다. |
| receive pause 미구현 | Accepted | 설계에서 deferred item으로 분리했다. receive pause는 client-side `NoBufferSpaceAvailable`을 악화시킬 수 있으므로 10K 데이터가 필요하다. |
| focused 10K benchmark는 report 작성 이후 실행 | Completed follow-up | uncapped budgeted-drain과 client cap `4` 결과를 `docs/load-validation-benchmark-results.md`에 반영했다. |

## 5. Quality Metrics

| Metric | Value |
|--------|-------|
| Final match rate | 94% |
| PDCA iterations | 1 |
| Debug build | Passed (`dotnet build FastPortCharp.sln`) |
| Unit tests | Passed (`86/86`, `dotnet test FastPortCharp.sln --no-build`) |
| Release build | Passed (`dotnet build FastPortCharp.sln -c Release`) |
| Diff whitespace check | Passed (`git diff --check`) |
| PDCA status JSON check | Passed (`jq empty docs/.pdca-status.json`) |
| Smoke validation | Passed (`artifacts/load-validation/receive-flow-control-smoke/summary.md`) |
| Focused 10K uncapped | Passed (`artifacts/load-validation/s5-budgeted-drain/summary.md`) |
| Focused 10K client cap 4 | Passed with tradeoffs (`artifacts/load-validation/s5-client-cap-4/summary.md`) |

## 6. Smoke Runtime Results

| Stage | Peak | Max Pending Req | Max Pending Send | Backpressure | Rejected Send | Drain Yield | Socket Errors |
|-------|-----:|----------------:|-----------------:|-------------:|--------------:|------------:|--------------:|
| smoke-fixed-10 | 10 / 10 | 2 | 2 | 0 | 0 / 0 | 0 / 0 | 0.00% |
| smoke-random-25 | 25 / 25 | 5 | 4 | 0 | 0 / 0 | 0 / 0 | 0.00% |

Smoke 결과는 correctness와 runtime integration 검증으로만 사용한다. `NoBufferSpaceAvailable` 개선 여부는 focused 10K run에서 판단해야 한다.

## 6.1 Focused 10K Runtime Results

| Metric | Baseline | Budgeted Drain | Client Cap 4 |
|--------|---------:|---------------:|-------------:|
| Peak sessions | 10,000 / 10,000 | 10,000 / 10,000 | 10,000 / 10,000 |
| Final disconnects | 0 | 0 | 26 |
| Max pending request count | 36,653 | 36,166 | 37,509 |
| Max pending send requests | 905 | 675 | 154 |
| Server send backpressure events | 4,153 | 996 | 1,064 |
| Max send buffer bytes | 195,683 | 162,411 | 61,567 |
| `send\|IOException\|NoBufferSpaceAvailable` | 7,344 | 8,370 | 905 |
| Other socket classifications | None material | None material | `receive\|IOException\|TimedOut = 629`, `send\|IOException\|Shutdown = 1` |
| Socket error rate | 0.55% | 0.82% | 0.12% |
| RTT P95 | 10,611.83ms | 9,250.43ms | 18,259.91ms |
| RTT P99 | 12,949.47ms | 10,371.08ms | 62,738.87ms |

Focused 10K conclusion:

- Server-only budgeted drain improves server-side send pressure but does not reduce client NoBuffer.
- Client cap `4` reduces NoBuffer by `87.7%`, but worsens RTT tail and introduces receive timeouts.
- Cap `4` should be treated as diagnostic evidence that client pacing matters, not as a final recommended runtime setting.

## 7. Key Code Changes

| Area | Files |
|------|-------|
| Server send budget | `LibNetworks/Sessions/BaseSession.cs`, `LibNetworks/Sessions/SessionSendOptions.cs` |
| Server telemetry | `LibNetworks/Telemetry/ServerTelemetry.cs`, `LibNetworks/Telemetry/ObservedMetrics.cs` |
| Load runner pacing | `FastPortLoadRunner/LoadRunnerOptions.cs`, `FastPortLoadRunner/LoadSession.cs`, `FastPortLoadRunner/Program.cs` |
| Load validation summary | `FastPortLoadValidation/LoadValidationOptions.cs`, `FastPortLoadValidation/LoadRunnerCommandBuilder.cs`, `FastPortLoadValidation/LoadValidationEvaluator.cs`, `FastPortLoadValidation/LoadValidationStage.cs`, `FastPortLoadValidation/LoadValidationSummaryWriter.cs` |
| Tests | `LibCommonTest/BaseSessionSendPolicyTests.cs`, `LibCommonTest/FastPortLoadRunnerTests.cs`, `LibCommonTest/FastPortLoadValidationTests.cs`, `LibCommonTest/ObservedMetricsTests.cs`, `LibCommonTest/ServerTelemetryTests.cs`, `LibCommonTest/FastPortSmokeServerTests.cs` |

## 8. Learnings

1. Server send backlog와 client send-buffer pressure는 같은 문제가 아니다. 이전 feature는 server pending send를 크게 낮췄지만 client `NoBufferSpaceAvailable`은 별도 pacing 관측이 필요했다.
2. `Socket.SendAsync` 성공은 logical response 전체 전송 완료가 아니다. 따라서 `Drain(sentSize)` invariant를 테스트로 고정하는 것이 핵심 안정장치다.
3. Load generator pacing은 성능 결과 해석에 직접 영향을 준다. uncapped run은 server stress 기준이고, capped run은 client pacing 진단 기준으로 분리해야 한다.
4. Budgeted drain telemetry가 없으면 lower pressure가 실제 yield 때문인지 hidden stall 때문인지 구분하기 어렵다.

## 9. Follow-up Items

- [x] Run focused 10K uncapped budgeted-drain validation.
- [x] Run focused 10K with `--max-pending-requests-per-session 4`.
- [ ] Optionally run focused 10K with cap `1` as conservative pacing bound.
- [x] Update `docs/load-validation-benchmark-results.md` with `NoBufferSpaceAvailable`, max pending request, max pending send, RTT, and drain yield comparison.
- [ ] Decide whether receive pause deserves a separate PDCA only after focused 10K data shows server receive pressure is still dominant.

## 10. Final Status

This PDCA feature is complete for implementation, code-level verification, and focused 10K validation.

The remaining optional work is cap `1` measurement or a separate follow-up PDCA for a more balanced pacing/receive strategy:

```text
Next command: $pdca archive client-send-buffer-pressure-receive-flow-control
```
