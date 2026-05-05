# throughput-pacing-server-processing-decomposition - Plan Document

> Version: 1.0.0 | Date: 2026-05-01 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`throughput-pacing-server-processing-decomposition`은 현재 10K load validation에서 관측되는 큰 RTT tail을 단계별 원인으로 분해하기 위한 진단 feature다.

이번 feature의 목적은 바로 엔진을 최적화하는 것이 아니라, 다음 최적화가 어느 경계에 있어야 하는지 판단할 수 있게 만드는 것이다.

분해 대상은 다음이다.

- client pacing wait
- client pending request depth
- client send/receive throughput
- server receive/parse/echo processing
- server send request/queue/drain pressure
- socket send backpressure
- scheduler drift and sample skew

### 1.2 Background

최신 same-machine 10K focused validation은 통과했지만, real-time game workload 기준으로는 RTT가 너무 크다.

Latest known result:

| Metric | Value |
|--------|------:|
| Peak sessions | `10,000 / 10,000` |
| Final disconnects | `0` |
| Max TPS | `9,371.08` |
| RTT P95 | `19,210.39ms` |
| RTT P99 | `24,863.90ms` |
| Max pending request count | `36,695` |
| Max pending send requests | `1,095` |
| Server send backpressure events | `1,583` |
| `send|IOException|NoBufferSpaceAvailable` | `1,639` |

Per-session RTT telemetry also shows broad pressure:

| Session RTT Metric | Value |
|--------------------|------:|
| Tracked sessions | `10,000` |
| P50 of session P95 | `13,663.21ms` |
| P95 of session P95 | `18,211.02ms` |
| P99 of session P95 | `23,295.81ms` |
| Max session P95 | `38,710.49ms` |

Cloud split server/runner validation is still the preferred environment check, but OCI A1 capacity is currently unavailable. This feature keeps progress moving with local artifacts and minimal additional telemetry.

## 2. Goals

### 2.1 Primary Goals

- [ ] Define a decomposition model for the current 10K RTT tail.
- [ ] Identify which existing metrics already cover each model segment.
- [ ] Identify minimal missing metrics needed before the next optimization.
- [ ] Produce a focused diagnostic path that can run locally while OCI capacity is unavailable.
- [ ] Decide the next optimization target from data, not intuition.

### 2.2 Non-Goals

- Do not change `LibNetworks` send/receive behavior in this feature unless design proves a missing metric requires a surgical hook.
- Do not claim production capacity from same-machine validation.
- Do not implement the game server template.
- Do not start the MAUI dashboard.
- Do not add GitHub Actions based OCI deployment.
- Do not run paid or non-free-tier cloud resources.

## 3. Scope

### 3.1 In Scope

- Review current client/server observed metric schema.
- Map current summary metrics to pipeline segments.
- Review `FastPortLoadRunner`, `FastPortLoadValidation`, `FastPortSmokeServer`, and `LibNetworks.Telemetry` boundaries.
- Design missing telemetry only if needed for decomposition.
- Define one or more local validation commands and artifact checks.
- Update benchmark documentation with diagnostic interpretation only after validation.

### 3.2 Out of Scope

- Cloud VM provisioning beyond the existing free-tier-only path.
- Large engine refactor.
- Protocol redesign.
- UI/dashboard work.
- Long soak testing.

## 4. Success Criteria

- [ ] A design document explains the RTT pressure decomposition model.
- [ ] Each model segment is marked as covered by existing metrics or missing.
- [ ] Any proposed telemetry addition is low overhead, bounded, and has a clear consumer in `FastPortLoadValidation`.
- [ ] Verification plan can run on the current local machine without OCI.
- [ ] The next optimization candidate is narrowed to one of:
  - client pacing threshold tuning
  - server processing throughput tuning
  - receive timeout tail flow control
  - send throughput/drain fairness optimization
- [ ] Generated load artifacts remain under ignored `artifacts/load-validation/`.

## 5. Schedule

| Phase | Target Date | Status |
|-------|------------|--------|
| Plan | 2026-05-01 | Completed |
| Design | 2026-05-01 | In Progress |
| Implementation | TBD | Pending |
| Check | TBD | Pending |
| Report | TBD | Pending |

## 6. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Same-machine benchmark noise hides true server bottleneck | Medium | High | Treat output as decomposition guidance only; validate with cloud later |
| Adding telemetry changes timing | Medium | Medium | Prefer aggregate counters and existing sampling cadence |
| Metrics duplicate existing fields | Low | Medium | Start by mapping current schema before adding code |
| Large scope creep into optimization | High | Medium | Keep this feature diagnostic; defer behavior changes |
| OCI capacity remains unavailable | Medium | High | Continue local decomposition and retry free-tier cloud separately |

## 7. References

- `HANDOFF.md`
- `docs/load-validation-benchmark-results.md`
- `docs/02-design/features/cloud-server-runner-split-load-validation.design.md`
- `docs/02-design/features/cloud-server-runner-split-load-validation.do.md`
- `artifacts/load-validation/s5-session-rtt-validation/summary.md`
- `artifacts/load-validation/s5-session-rtt-validation/summary.json`
- `FastPortLoadRunner/`
- `FastPortLoadValidation/`
- `FastPortSmokeServer/`
- `LibNetworks/Telemetry/`
