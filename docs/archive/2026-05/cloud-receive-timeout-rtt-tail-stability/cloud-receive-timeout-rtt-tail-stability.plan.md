# cloud-receive-timeout-rtt-tail-stability - Plan Document

> Version: 1.0.0 | Date: 2026-05-05 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`cloud-receive-timeout-rtt-tail-stability`는 Azure server/local runner 10K 검증에서 확인된 receive timeout, connection reset, final disconnect, RTT tail 문제를 좁은 범위로 분석하고 개선하기 위한 feature다.

이번 단계의 목적은 다시 send-buffer 최적화부터 시작하는 것이 아니다. 직전 cloud baseline에서 client `send-write`와 server send pressure는 낮았고, 실패 신호는 receive wait와 connection lifecycle에 집중되어 있었다. 따라서 이 feature는 cloud 경로에서 발생하는 receive timeout/reset과 극단 RTT tail을 설명 가능한 지표로 분해하고, 테스트 서버/러너/검증 도구 중 어느 레이어를 먼저 고쳐야 하는지 결정한다.

### 1.2 Background

직전 `cloud-server-runner-split-load-validation`은 cloud validation 환경 자체는 완료했지만 focused 10K는 실패 baseline으로 기록되었다.

Baseline artifact:

```text
artifacts/load-validation/cloud-server-runner-split/s5-random-10k/summary.md
```

Baseline result:

| Metric | Value |
|--------|------:|
| Run ID | `20260505-140926-staged` |
| Target sessions | `10,000` |
| Peak sessions | `9,337` |
| Peak ratio | `93.37%` |
| Max TPS | `1,085.41` |
| Final disconnects | `752` |
| Socket error rate | `0.28%` |
| RTT P95 | `106,216.65ms` |
| RTT P99 | `274,206.02ms` |
| Session RTT p95-of-p95 | `222,702.93ms` |

Socket classification:

| Class | Count |
|-------|------:|
| `receive\|IOException\|ConnectionReset` | `495` |
| `receive\|IOException\|TimedOut` | `257` |
| `connect\|SocketException\|TimedOut` | `56` |

Operation duration:

| Operation | Average | Max |
|-----------|--------:|----:|
| `send-write` | `0.12ms` | `24.07ms` |
| `receive-header` | `3,269.27ms` | `384,958.03ms` |
| `receive-body` | `2,571.06ms` | `396,937.01ms` |

Collected server metrics showed:

- server socket errors: `0`
- server send backpressure events: `0`
- server rejected sends: `1`
- max pending server send requests: `155`
- max server send buffer bytes: `62,049`
- post-run lingering state: `currentSessions = 51`, `pendingSendRequests = 27`

This points away from server send-buffer pressure as the first explanation. The unresolved questions are whether the tail is caused by test server response processing, receive loop lifecycle, local runner limits, public network path, timeout policy, or stale server session cleanup.

## 2. Goals

### 2.1 Primary Goals

- [x] Define the cloud receive-timeout/RTT-tail problem as a separate PDCA feature.
- [x] Preserve the failed cloud 10K result as the starting baseline.
- [x] Define diagnostic scope across `FastPortTestSmokeServer`, `FastPortTestLoadRunner`, `FastPortTestLoadValidation`, and cloud helper scripts.
- [x] Define success criteria that separate environment validation from performance success.
- [x] Decide that engine changes are not the first target unless diagnostics prove the issue is in `LibNetworks`/`BaseSession`.

### 2.2 Non-Goals

- Do not repeat broad server send queue/backpressure optimization in this feature.
- Do not change `LibNetworks` or `BaseSession` as the first step without evidence.
- Do not build MAUI dashboard UI in this feature.
- Do not add GitHub Actions deployment automation.
- Do not create new paid cloud resources or expand beyond the existing Azure server/local runner path without a separate decision.
- Do not treat public internet RTT as a pure server-capacity metric.
- Do not remove the hard validation guardrails for receive timeouts and final disconnects.

## 3. Scope

### 3.1 In Scope

- Cloud test hygiene:
  - restart/cleanup step before validation;
  - verify server listen state and zero or expected active sessions before each run;
  - collect server artifacts after each run.
- Receive timeout/reset diagnostics:
  - distinguish header wait, body wait, connection reset, receive timeout, connect timeout, cancellation, and orderly close;
  - preserve socket error phase/type/code/class fields in summaries.
- Server-side correlation:
  - correlate client receive waits with server accepted/disconnected sessions;
  - inspect whether server stops sending while connections remain open;
  - expose enough server-side lifecycle counters to explain lingering sessions.
- Runner-side correlation:
  - verify local runner CPU/memory/socket/ephemeral-port pressure during cloud runs;
  - identify whether local runner becomes the bottleneck before the server.
- Validation thresholds:
  - keep final disconnects and receive timeouts as hard failures;
  - record whether a candidate improves failure shape even if it still fails 10K.
- Documentation:
  - update benchmark results and PDCA docs with any new cloud baseline or rejected candidate.

### 3.2 Out of Scope

- Full production deployment hardening.
- Multi-region cloud benchmarking.
- Cloud runner VM creation unless local-runner bottleneck is proven and user explicitly approves the next topology.
- Protocol redesign.
- Game server template work.
- Telemetry library extraction, which remains tracked by `extract-telemetry-contracts-from-network-core`.

## 4. Success Criteria

- [ ] Design identifies the first diagnostic changes and explicitly states whether they are in test server, load runner, validation summary, scripts, or engine.
- [ ] Before the next cloud run, Azure smoke server restart/cleanup is documented and verified.
- [ ] Smoke validation still passes after any code or script changes.
- [ ] Focused 10K run either improves the baseline failure shape or produces a clear stop-condition report.
- [ ] Receive timeout and connection reset counts are explained with stronger telemetry than the current aggregate summary.
- [ ] Server-side lingering sessions are either eliminated or explained by explicit lifecycle counters.
- [ ] `dotnet build FastPortCharp.sln -c Release` passes.
- [ ] `dotnet test FastPortCharp.sln -c Release --no-build` passes.
- [ ] `bash -n scripts/cloud/*.sh`, `jq empty docs/.pdca-status.json`, and `git diff --check` pass.
- [ ] No public IPs, private keys, tenant IDs, subscription IDs, or generated artifacts are committed.

## 5. Schedule

| Phase | Target Date | Status |
|-------|------------|--------|
| Plan | 2026-05-05 | Completed |
| Design | 2026-05-05 | Pending |
| Implementation | TBD | Pending |
| Check | TBD | Pending |
| Report | TBD | Pending |

## 6. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Azure `Standard_B2s` is too small for 10K | False attribution to code | High | Treat results as shape-specific baseline; record VM size and avoid production capacity claims |
| Local Mac runner is bottleneck | Server diagnosis may be wrong | Medium | Capture local runner readiness and operation-duration telemetry; add cloud runner only if evidence requires it |
| Public network RTT dominates | RTT tail may not be server-fixable | Medium | Separate network/path interpretation from server processing metrics |
| Stale server sessions distort follow-up runs | Next test starts from contaminated state | High | Restart server before load runs and verify session counters before start |
| Timeout guardrails block "partial win" interpretation | Candidate may look like failure only | Medium | Keep guardrails hard, but compare failure shape against baseline |
| More telemetry changes perturb performance | Measurement changes can alter result | Medium | Keep instrumentation minimal and validate smoke before 10K |
| Engine changes are applied too early | Scope creep and regression risk | Medium | Require diagnostic evidence before touching `LibNetworks`/`BaseSession` |

## 7. Architecture Considerations

### 7.1 Likely First Investigation Areas

- `FastPortTestSmokeServer` response lifecycle and disconnect behavior.
- `FastPortTestLoadRunner` receive loop timeout/reset classification.
- `FastPortTestLoadValidation` summary output for cloud split runs.
- `scripts/cloud/server-start.sh`, `runner-smoke.sh`, `runner-10k.sh`, and artifact collection flow.

### 7.2 Initial Hypotheses

- The server may leave sessions open after the runner exits or after timeout/reset paths.
- The runner may time out while waiting for response headers after backlog accumulates.
- `Standard_B2s` CPU/network capacity may be insufficient for 10K external-path echo workload.
- Public endpoint RTT and packet loss may produce a different tail shape than same-machine validation.
- Existing summary lacks enough server-side lifecycle detail to explain why server socket errors remain `0` while client receives resets/timeouts.

## 8. References

- `docs/archive/2026-05/cloud-server-runner-split-load-validation/cloud-server-runner-split-load-validation.report.md`
- `docs/archive/2026-05/cloud-server-runner-split-load-validation/cloud-server-runner-split-load-validation.analysis.md`
- `docs/load-validation-benchmark-results.md`
- `docs/azure-server-runner-split-load-validation-runbook.md`
- `docs/cloud-server-runner-split-load-validation-runbook.md`
- `FastPortTestSmokeServer/`
- `FastPortTestLoadRunner/`
- `FastPortTestLoadValidation/`
- `scripts/cloud/`

## 9. Open Questions

- Is the dominant tail caused by server processing, local runner receive scheduling, public network path, or VM capacity?
- Why does the server report `0` socket errors while the client reports resets/timeouts?
- What lifecycle path leaves `currentSessions = 51` after runner exit?
- Should the next 10K use the same `s5-random-10k` stage, or a smaller 1K/3K/5K ladder first?
- Do we need a cloud runner VM only after local runner pressure is proven?

## 10. Next Phase

Recommended next command:

```text
$pdca design cloud-receive-timeout-rtt-tail-stability
```
