# Completion Report: cloud-server-runner-split-load-validation

> Date: 2026-05-05 | Status: Completed With Failed 10K Baseline | Match Rate: 94%

---

## 1. Summary

`cloud-server-runner-split-load-validation` completed the cloud validation environment work.

The accepted topology is:

```text
Azure server VM -> FastPortTestSmokeServer
local Mac -> FastPortTestLoadValidation / FastPortTestLoadRunner
local Mac -> Azure public endpoint restricted by NSG
```

This feature did not attempt engine optimization. It established a repeatable cloud-server/local-runner validation path, verified smoke connectivity, ran focused 10K, collected artifacts, and documented the first cloud baseline.

The focused 10K result is a failed performance baseline, not a cloud setup failure. It should be used to open a narrower follow-up for receive timeout/reset behavior and RTT tail.

## 2. Related Documents

- Plan: `docs/01-plan/features/cloud-server-runner-split-load-validation.plan.md`
- Design: `docs/02-design/features/cloud-server-runner-split-load-validation.design.md`
- Do notes: `docs/02-design/features/cloud-server-runner-split-load-validation.do.md`
- Analysis: `docs/03-analysis/cloud-server-runner-split-load-validation.analysis.md`
- Azure runbook: `docs/azure-server-runner-split-load-validation-runbook.md`
- Generic cloud runbook: `docs/cloud-server-runner-split-load-validation-runbook.md`
- Benchmark summary: `docs/load-validation-benchmark-results.md`

## 3. Completed Items

### 3.1 Cloud Topology

- [x] Changed the default topology to cloud server + local runner.
- [x] Kept cloud runner VM as an optional controlled baseline, not the default.
- [x] Documented why public endpoint testing better reflects external clients than same-machine local validation.
- [x] Kept GitHub Actions deployment out of scope because the repository is public.

### 3.2 Azure Runtime Path

- [x] Verified Azure CLI access.
- [x] Verified the user-created Azure server VM.
- [x] Verified PEM key based SSH readiness.
- [x] Verified local runner TCP connectivity to server port `6628`.
- [x] Started `FastPortTestSmokeServer` on the Azure server VM.
- [x] Ran local smoke validation against the Azure server.
- [x] Ran focused `s5-random-10k` validation against the Azure server.

### 3.3 Scripts

- [x] Added Azure discovery helper.
- [x] Added Azure VM readiness helper.
- [x] Added SSH readiness helper.
- [x] Added runner connectivity helper.
- [x] Added artifact collection helper.
- [x] Added redacted cloud manifest writer.
- [x] Updated server, smoke runner, and 10K runner scripts for server/local split execution.
- [x] Updated runner scripts to build explicitly, then run validation with `dotnet run --no-build`.

### 3.4 Validation Tooling

- [x] Added `--runner-no-build` to `FastPortTestLoadValidation`.
- [x] Propagated `--no-build` to the generated `FastPortTestLoadRunner` command when requested.
- [x] Added unit coverage for `--runner-no-build` command generation.
- [x] Preserved the default validation behavior for non-cloud CLI usage.

### 3.5 Documentation

- [x] Updated the Azure runbook.
- [x] Updated the generic cloud runbook.
- [x] Updated Do notes with Azure runtime validation.
- [x] Updated analysis with the cloud 10K baseline.
- [x] Updated benchmark summary with the cloud server/local runner result.
- [x] Kept account identifiers, public IPs, private keys, tenant IDs, subscription IDs, and generated artifacts out of committed docs.

## 4. Quality Metrics

| Metric | Result |
|--------|--------|
| Match rate | 94% |
| Cloud smoke validation | Passed |
| Cloud focused 10K validation | Failed baseline, completed with artifacts |
| Release build | Passed |
| Release tests | Passed |
| Test count | 113 passed, 0 failed, 0 skipped |
| Script syntax validation | Passed |
| JSON validation | Passed |
| `git diff --check` | Passed |

The remaining implementation gap is server metrics merge during the split run. Server metrics were collected afterward, but the 10K `summary.md` itself does not include merged server metrics.

## 5. Runtime Outcome

Smoke validation passed against the Azure server public endpoint.

Focused 10K artifact:

```text
artifacts/load-validation/cloud-server-runner-split/s5-random-10k/summary.md
```

Focused 10K result:

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

Collected server metrics:

```text
artifacts/load-validation/cloud-server-runner-split/collected/server/server/server.metrics.jsonl
```

Server-side summary:

| Metric | Value |
|--------|------:|
| Max server current sessions | `9,159` |
| Final total accepted sessions | `9,200` |
| Final total disconnected sessions | `9,149` |
| Server socket errors | `0` |
| Server send backpressure events | `0` |
| Server rejected sends | `1` |
| Max pending server send requests | `155` |
| Max server send buffer bytes | `62,049` |

## 6. Interpretation

The cloud split path changes the meaning of the benchmark. It is not a direct win/loss comparison against same-machine local 10K runs because RTT now includes an external network path and local-to-cloud connectivity.

The important signal is the failure shape:

- client `send-write` is still fast;
- server socket errors are `0`;
- server send backpressure is `0`;
- server rejected sends are almost nonexistent;
- receive waits, disconnects, resets, and RTT tail dominate the failure.

The next optimization should not start with another server send-buffer pressure pass. It should start with receive timeout/reset behavior, connection lifecycle cleanup, and cloud RTT tail analysis.

## 7. Deviations

`dotnet run` without `--no-build` stalled in this local environment before the validation process printed the stage command. The cloud runner scripts now perform explicit Release builds and use `dotnet run --no-build`.

This is an execution-path hardening change. It does not alter load-runner scenario semantics.

The 10K validation summary did not merge server metrics because the server and runner are split across machines. Server metrics were collected afterward by `scripts/cloud/collect-artifacts.sh`.

## 8. Lessons Learned

### Keep

- Keep server-only cloud as the default validation topology.
- Keep local runner as the default while cloud runner cost/quota is not justified.
- Keep redacted manifests and artifact collection scripts.
- Keep smoke-first validation before any 10K run.

### Problem

- A passed cloud smoke does not imply a stable 10K cloud path.
- The first cloud 10K left server-side stale state after runner exit.
- Split-machine validation needs a better server metrics merge flow.
- Same-machine results were useful for engine tuning, but they hid external receive-path and connection-lifecycle behavior.

### Try

- Add a server restart/cleanup step before every cloud load validation.
- Add a follow-up feature for cloud receive timeout/reset and RTT tail.
- Add split-run server metrics merge support if future reports require combined summaries.

## 9. Residual Risks

- Azure server showed lingering `currentSessions = 51` and `pendingSendRequests = 27` after failed 10K.
- The root cause of cloud receive timeouts and connection resets is still open.
- A local Mac runner may still be part of the bottleneck; a cloud runner VM may be needed later as a controlled comparison.
- `Standard_B2s` may be too small for a clean 10K server baseline.
- Public internet RTT/path variability remains part of the cloud-server/local-runner result.

## 10. Final Decision

Complete this feature as a validation-environment feature.

Do not treat the failed focused 10K as a reason to keep this feature open. The failure is now a measured baseline and should move to a narrower performance feature.

## 11. Verification

Verification commands used during this feature:

```text
bash -n scripts/cloud/*.sh
jq empty docs/.pdca-status.json
git diff --check
dotnet build FastPortCharp.sln -c Release
dotnet test FastPortCharp.sln -c Release --no-build
scripts/cloud/azure-vm-readiness.sh
scripts/cloud/ssh-readiness.sh
scripts/cloud/runner-connectivity.sh
scripts/cloud/runner-smoke.sh
scripts/cloud/runner-10k.sh
scripts/cloud/collect-artifacts.sh
```

Latest Release test result:

```text
Passed: 113
Failed: 0
Skipped: 0
```

## 12. Next Steps

- [ ] Archive this feature:

  ```text
  $pdca archive cloud-server-runner-split-load-validation
  ```

- [ ] Restart the Azure smoke server before any follow-up load validation.
- [ ] Start a follow-up performance feature:

  ```text
  $pdca pm cloud-receive-timeout-rtt-tail-stability
  ```
