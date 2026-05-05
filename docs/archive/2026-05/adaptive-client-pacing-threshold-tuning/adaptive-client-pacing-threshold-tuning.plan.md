# adaptive-client-pacing-threshold-tuning - Plan Document

> Version: 1.0.0 | Date: 2026-05-03 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`adaptive-client-pacing-threshold-tuning`은 현재 focused same-machine 10K load validation에서 관측되는 broad RTT tail과 pacing wait를 줄이기 위한 로컬 최적화 feature다.

이번 feature의 1차 목표는 `LibNetworks` 엔진을 다시 수정하기 전에 `FastPortTestLoadRunner`의 adaptive outstanding-request pacing 정책을 더 정확하게 조정하는 것이다.

핵심 질문은 다음이다.

- 현재 adaptive window가 너무 빨리 `1`까지 내려가서 처리량을 제한하고 있는가?
- RTT high/target threshold가 현재 10K 환경에 맞지 않아 window 회복이 너무 느린가?
- `IncreaseEveryResponses`가 너무 보수적이어서 backlog 해소 후에도 window가 회복되지 않는가?
- pacing tuning만으로 RTT tail과 pending depth를 개선할 수 있는가?

### 1.2 Background

최근 `throughput-pacing-server-processing-decomposition` feature는 기존 10K `summary.json`을 분해했고, 다음 최적화 lane으로 `adaptive-client-pacing-threshold-tuning`을 선택했다.

현재 기준 artifact:

- `artifacts/load-validation/s5-session-rtt-validation/summary.json`
- stage: `s5-random-10k`
- result: passed

기준 수치:

| Metric | Value |
|--------|------:|
| Peak sessions | `10,000 / 10,000` |
| Max TPS | `9,371.08` |
| RTT P95 | `19,210.39ms` |
| RTT P99 | `24,863.90ms` |
| Per-session P95-of-P95 | `18,211.02ms` |
| RTT gap ratio | `5.20%` |
| Max pending requests | `36,695` |
| Pending requests/session | `3.67` |
| Max pending server send requests | `1,095` |
| Server send backpressure events | `1,583` |
| Max server send buffer bytes | `64,204` |
| Pacing average wait max | `2,857.09ms` |
| Pacing window range | `1-5` |
| `send\|IOException\|NoBufferSpaceAvailable` | `1,639` |
| `receive\|IOException\|TimedOut` | `184` |
| Max scheduler drift | `12.12ms` |

Existing adaptive defaults:

| Option | Current Default |
|--------|----------------:|
| `DefaultMinWindow` | `1` |
| `DefaultInitialWindow` | `4` |
| `DefaultMaxWindow` | `16` |
| `DefaultRttTargetMs` | `12,000` |
| `DefaultRttHighMs` | `20,000` |
| `DefaultIncreaseEveryResponses` | `256` |

The current 10K artifact shows that adaptive pacing is active and frequently constrained. The window reaches `1`, max observed window is only `5`, and average pacing wait is high. Server send/socket pressure is still visible, but the lower-risk next step is to tune the load runner pacing policy before another engine send-path change.

## 2. Goals

### 2.1 Primary Goals

- [ ] Review the existing adaptive pacing algorithm and option defaults.
- [ ] Design a focused tuning strategy for `MinWindow`, `InitialWindow`, `MaxWindow`, `RttTargetMs`, `RttHighMs`, and `IncreaseEveryResponses`.
- [ ] Decide whether tuning should change code defaults, validation profile arguments, or both.
- [ ] Keep the first behavior change in `FastPortTestLoadRunner` / `FastPortTestLoadValidation`, not `LibNetworks`.
- [ ] Define a verification sequence that includes build/test, smoke validation, focused 10K validation, and comparison against the current baseline.
- [ ] Define pass/fail comparison metrics for TPS, RTT P95/P99, pacing wait, pending request depth, socket errors, and server send pressure.

### 2.2 Non-Goals

- Do not change `LibNetworks` send queue, socket send, or session locking behavior in this feature.
- Do not change `FastPortTestSmokeServer` echo behavior.
- Do not claim production capacity from same-machine 10K results.
- Do not run paid or non-free-tier cloud resources.
- Do not make adaptive pacing the default production policy without post-change evidence.
- Do not start MAUI dashboard or game server template work.

## 3. Scope

### 3.1 In Scope

- `FastPortTestLoadRunner/OutstandingRequestPacer.cs`
- `FastPortTestLoadRunner/LoadRunnerOptions.cs`
- `FastPortTestLoadRunner` pacing metrics and observed output, if needed for verification.
- `FastPortTestLoadValidation` staged profile invocation and summary comparison, if profile-level tuning is selected.
- `docs/load-validation-benchmark-results.md` update after validation.
- `scripts/load-validation/decompose-summary.sh` reuse for post-change interpretation.

### 3.2 Out of Scope

- `LibNetworks/Sessions/BaseSession` send path changes.
- Server send queue residency telemetry.
- Cloud provisioning changes.
- CI/CD or GitHub Actions deployment.
- Broad benchmark harness refactor.

## 4. Success Criteria

- [ ] Design document explains the selected adaptive pacing tuning strategy.
- [ ] The selected strategy has a clear baseline comparison against `s5-session-rtt-validation`.
- [ ] Code changes, if any, are limited to load-runner/validation pacing behavior.
- [ ] `dotnet build FastPortCharp.sln -c Release` passes.
- [ ] `dotnet test FastPortCharp.sln --no-build` passes.
- [ ] Smoke validation passes before any focused 10K run.
- [ ] Focused 10K validation is run after actual pacing behavior changes.
- [ ] Post-change `summary.json` is compared with the current baseline.
- [ ] Result interpretation states whether this is a true improvement, mixed result, or regression.

### 4.1 Performance Comparison Criteria

The first pass should be considered useful only if it improves pacing behavior without hiding server pressure.

Primary target:

- RTT P95 and/or RTT P99 improves materially from the current `19,210.39ms` / `24,863.90ms` baseline, or pacing wait drops materially without creating new disconnect/timeout regressions.

Guardrails:

- Peak sessions should remain `10,000 / 10,000`.
- Final disconnects should remain near `0`.
- `NoBufferSpaceAvailable` should not regress materially above the current `1,639`.
- `receive|IOException|TimedOut` should not regress materially above the current `184`.
- Pending requests/session should not increase above the current `3.67`.
- Server pending send/backpressure should be interpreted with the same decomposition script.

## 5. Schedule

| Phase | Target Date | Status |
|-------|------------|--------|
| Plan | 2026-05-03 | Completed |
| Design | 2026-05-03 | Pending |
| Implementation | TBD | Pending |
| Check | TBD | Pending |
| Report | TBD | Pending |

## 6. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Same-machine noise hides true pacing impact | High | High | Compare against existing artifact, mark result as local, repeat on cloud split when available |
| More aggressive window recovery increases socket pressure | High | Medium | Keep `NoBufferSpaceAvailable`, send backpressure, pending send, and receive timeout as guardrails |
| Tuning only improves TPS while worsening RTT tail | High | Medium | Treat RTT P95/P99 and session RTT as primary quality metrics |
| Too conservative tuning lowers TPS without improving RTT | Medium | Medium | Compare TPS, pending depth, pacing wait, and RTT together |
| Scope drifts back into engine send path | High | Medium | Explicitly keep `LibNetworks` out of scope for this feature |
| 10K validation consumes time | Medium | High | Run smoke first; run focused 10K only after actual pacing behavior changes |

## 7. Architecture Considerations

- `FastPortTestLoadRunner` owns load-generation pacing. This feature should tune how the client generates pressure, not how the engine handles real server sends.
- `OutstandingRequestPacer` currently halves the window when RTT is above `RttHighMs`, increments by one after `IncreaseEveryResponses` stable responses, and records pacing wait/window metrics.
- `LoadPacingOptions` already exposes CLI knobs for the main tuning dimensions. The design phase should decide whether command/profile tuning is sufficient or code defaults should change.
- `FastPortTestLoadValidation` should remain the comparison owner for staged validation artifacts.
- `scripts/load-validation/decompose-summary.sh` should be reused after validation to keep interpretation consistent with the previous feature.

## 8. Verification Plan

Baseline:

```bash
scripts/load-validation/decompose-summary.sh artifacts/load-validation/s5-session-rtt-validation/summary.json
```

Required after implementation:

```bash
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln --no-build
```

Smoke validation before 10K:

```bash
dotnet run -c Release --project FastPortTestLoadValidation -- \
  --profile smoke \
  --output artifacts/load-validation/adaptive-pacing-threshold-smoke \
  --server-metrics artifacts/load-validation/adaptive-pacing-threshold-smoke/server.metrics.jsonl
```

Focused 10K validation after actual pacing behavior changes:

```bash
dotnet run -c Release --project FastPortTestLoadValidation -- \
  --profile staged \
  --stage s5-random-10k \
  --pacing-policy adaptive-window \
  --output artifacts/load-validation/adaptive-pacing-threshold-s5 \
  --server-metrics artifacts/load-validation/adaptive-pacing-threshold-s5/server.metrics.jsonl
```

Post-run interpretation:

```bash
scripts/load-validation/decompose-summary.sh artifacts/load-validation/adaptive-pacing-threshold-s5/summary.json
```

## 9. References

- `docs/archive/2026-05/throughput-pacing-server-processing-decomposition/throughput-pacing-server-processing-decomposition.report.md`
- `docs/archive/2026-05/throughput-pacing-server-processing-decomposition/throughput-pacing-server-processing-decomposition.analysis.md`
- `docs/load-validation-benchmark-results.md`
- `artifacts/load-validation/s5-session-rtt-validation/summary.json`
- `FastPortTestLoadRunner/OutstandingRequestPacer.cs`
- `FastPortTestLoadRunner/LoadRunnerOptions.cs`
- `FastPortTestLoadRunner/Metrics.cs`
- `FastPortTestLoadValidation/LoadValidationStage.cs`
- `FastPortTestLoadValidation/LoadValidationEvaluator.cs`
- `scripts/load-validation/decompose-summary.sh`
