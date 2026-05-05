# cloud-staged-rtt-tail-validation - Plan Document

> Version: 1.0.0 | Date: 2026-05-05 | Status: Draft
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`cloud-staged-rtt-tail-validation` validates the Azure server/local runner path again after the receive close and phase completion diagnostics were added.

The goal is to identify the first cloud load stage where receive timeout, connection reset, final disconnect, or RTT tail becomes unacceptable.

### 1.2 Background

The previous cloud focused 10K run failed:

| Metric | Value |
|--------|------:|
| Artifact | `artifacts/load-validation/cloud-server-runner-split/s5-random-10k/summary.md` |
| Run ID | `20260505-140926-staged` |
| Target sessions | `10,000` |
| Peak sessions | `9,337` |
| Peak ratio | `93.37%` |
| Final disconnects | `752` |
| Socket error rate | `0.28%` |
| RTT P95 | `106,216.65ms` |
| RTT P99 | `274,206.02ms` |

The latest diagnostic feature did not re-run 10K. It added close/phase classification and passed cloud smoke from a clean server process. This feature runs staged validation with those diagnostics.

## 2. Goals

### 2.1 Primary Goals

- [ ] Restart the Azure smoke server before staged validation.
- [ ] Verify the local runner can reach the Azure endpoint.
- [ ] Run `s1-fixed-1k` first and record whether the new diagnostics remain quiet.
- [ ] Continue to `s2-random-1k`, `s3-random-3k`, `s4-random-5k`, and `s5-random-10k` only if earlier stages are stable enough to justify the next step.
- [ ] Record the first failing stage and dominant failure shape.

### 2.2 Non-Goals

- Do not change engine send/receive logic in this feature.
- Do not add paid cloud resources.
- Do not introduce a cloud runner VM unless local runner pressure is proven by this validation.
- Do not weaken receive timeout, final disconnect, or peak session guardrails.

## 3. Scope

### 3.1 In Scope

- Azure server clean start and readiness check.
- Local runner OS readiness capture.
- Cloud staged validation one stage at a time.
- Summary review using `summary.md`, `summary.json`, client metrics, and collected server metrics when available.
- Benchmark/result documentation update if new staged results are produced.

### 3.2 Out of Scope

- Server engine optimization.
- MAUI dashboard work.
- GitHub Actions deployment.
- Multi-region benchmark comparison.

## 4. Success Criteria

- [ ] `s1-fixed-1k` completes with stable peak sessions and no receive timeout/final disconnect regression.
- [ ] Every executed stage has preserved artifacts under `artifacts/load-validation/cloud-staged-rtt-tail-validation/`.
- [ ] The result identifies either a safe next stage or a clear stop condition.
- [ ] `docs/load-validation-benchmark-results.md` is updated if the staged run produces meaningful new runtime data.
- [ ] `jq empty docs/.pdca-status.json` and `git diff --check` pass.

## 5. Execution Order

| Order | Stage | Target | Payload | Action |
|-------|-------|--------|---------|--------|
| 1 | smoke | 10/25 | mixed smoke profile | Run after clean server start |
| 2 | `s1-fixed-1k` | 1,000 | `fixed:8192` | First staged validation |
| 3 | `s2-random-1k` | 1,000 | `random:4096-16384` | Run if s1 is stable |
| 4 | `s3-random-3k` | 3,000 | `random:4096-16384` | Run if 1K random is stable |
| 5 | `s4-random-5k` | 5,000 | `random:4096-16384` | Run if 3K is stable |
| 6 | `s5-random-10k` | 10,000 | `random:4096-16384` | Run only after earlier stages justify it |

## 6. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Azure `Standard_B2s` is too small for high concurrency | False negative at 5K/10K | High | Treat as validation-environment result, not production capacity |
| Local runner is the bottleneck | Server may be blamed incorrectly | Medium | Preserve local OS readiness and operation duration metrics |
| Stale server sessions pollute the next run | Misleading disconnect/RTT tail | Medium | Restart server and confirm listener/current metrics before each load stage |
| Public internet path varies | Non-repeatable RTT tail | Medium | Record run ID and stage sequence; compare failure shape, not only raw RTT |

## 7. References

- `docs/archive/2026-05/cloud-server-runner-split-load-validation/cloud-server-runner-split-load-validation.report.md`
- `docs/archive/2026-05/cloud-receive-timeout-rtt-tail-stability/cloud-receive-timeout-rtt-tail-stability.report.md`
- `artifacts/load-validation/cloud-server-runner-split/s5-random-10k/summary.md`
- `docs/azure-server-runner-split-load-validation-runbook.md`
- `scripts/cloud/runner-smoke.sh`
- `scripts/cloud/runner-10k.sh`
