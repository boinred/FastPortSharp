# Gap Analysis: cloud-server-runner-split-load-validation

> Date: 2026-05-05 | Design: docs/02-design/features/cloud-server-runner-split-load-validation.design.md

---

## Match Rate: 94%

The implementation matches the updated design intent for server-only cloud runbooks, cloud helper scripts, Azure discovery, runtime command wrappers, OS readiness capture, redacted artifact manifests, and actual Azure server/local runner validation.

The feature is ready to report as a cloud validation environment feature, but the first focused 10K cloud-server/local-runner result is a failed performance baseline. The failure should be carried into a follow-up optimization item rather than treated as a cloud setup blocker.

Scoring basis after runtime validation: 17 of 18 design checkpoints are implemented or verified. The remaining gap is that the 10K summary did not merge the collected server metrics into the validation summary during the run; server metrics were collected afterward under the standardized artifact directory.

## Summary

`cloud-server-runner-split-load-validation` now uses a server-only cloud baseline by default. The Do/Act phases correctly avoided engine changes and focused on repeatable operational scaffolding.

Implemented local assets now cover:

- Azure-first runbook and discovery flow.
- Historical OCI/free-tier context and capacity notes.
- Azure VM metadata readiness helper.
- Server SSH and local runner prerequisite readiness helper.
- Local runner to server TCP connectivity helper.
- Server/runner artifact collection helper.
- Server and runner execution wrappers.
- OS readiness capture on server and runner roles.
- Role-specific manifest JSON/Markdown generation.
- Secret-safe documentation boundaries.

Runtime proof now exists. Smoke passed against the Azure server public endpoint, and focused 10K completed with artifacts. The focused 10K result is intentionally recorded as a failed baseline because peak session ratio, final disconnects, receive timeouts, and RTT tail missed guardrails.

## Runtime Validation Result

Azure server/local runner validation was executed on 2026-05-05.

| Check | Result |
|-------|--------|
| Azure VM readiness | Passed for `Standard_B2s` server in `koreacentral` |
| SSH readiness | Passed with PEM key auth |
| TCP connectivity to `6628` | Passed from local runner |
| Smoke validation | Passed |
| Focused 10K validation | Failed, completed with artifacts |
| Artifact collection | Passed |

Focused 10K artifact summary:

- Summary: `artifacts/load-validation/cloud-server-runner-split/s5-random-10k/summary.md`
- Run ID: `20260505-140926-staged`
- Target: `10,000`
- Peak: `9,337`
- Peak ratio: `93.37%`
- Max TPS: `1,085.41`
- Final disconnects: `752`
- Socket errors: `receive|IOException|ConnectionReset = 495`, `receive|IOException|TimedOut = 257`, `connect|SocketException|TimedOut = 56`
- RTT P95/P99: `106,216.65ms` / `274,206.02ms`
- Per-session RTT p95-of-p95: `222,702.93ms`
- `send-write` average/max: `0.12ms` / `24.07ms`
- `receive-header` average/max: `3,269.27ms` / `384,958.03ms`
- `receive-body` average/max: `2,571.06ms` / `396,937.01ms`

Collected server artifact summary:

- Server metrics: `artifacts/load-validation/cloud-server-runner-split/collected/server/server/server.metrics.jsonl`
- Max server current sessions: `9,159`
- Server total accepted sessions at collection time: `9,200`
- Server total disconnected sessions at collection time: `9,149`
- Server socket errors: `0`
- Server send backpressure: `0`
- Server rejected sends: `1`
- Max pending server send requests: `155`
- Max server send buffer bytes: `62,049`

Post-run server observation:

- Latest server telemetry still showed `currentSessions = 51` and `pendingSendRequests = 27` after runner exit.
- The Azure smoke server should be restarted before any follow-up validation run.

## Re-Analysis After Act Iteration

Re-analysis on 2026-05-05 after the runtime hardening helpers confirmed the initial decision boundary:

- Local scripts and runbooks now cover the missing verification commands.
- No Azure server VM or local-runner runtime evidence is available yet.
- Match rate remains `89%`, below the report threshold.
- The next PDCA action stays `iterate` only if we add more local hardening; otherwise this feature is blocked on user-created Azure runtime validation.

Re-analysis after the Azure runtime pass changes the decision boundary:

- Azure VM readiness, SSH readiness, TCP connectivity, smoke validation, focused 10K validation, and artifact collection have all executed.
- The cloud environment feature is no longer blocked on VM setup.
- The first cloud focused 10K result is a valid failed baseline and points to receive-path/connection-stability work, not cloud provisioning work.
- Match rate is now `94%`; report can proceed if the report explicitly records the failed 10K and server cleanup requirement.

## Implemented Items

- [x] Azure target topology documented with server VM and local runner separation.
- [x] GitHub Actions cloud deployment was explicitly excluded for the public repository.
- [x] Azure CLI discovery is implemented as read-only in `scripts/cloud/azure-discover.sh`.
- [x] OCI A1 capacity status and Azure transition are documented in the Do notes.
- [x] Server start wrapper exists in `scripts/cloud/server-start.sh`.
- [x] Runner smoke wrapper exists in `scripts/cloud/runner-smoke.sh`.
- [x] Runner focused 10K wrapper exists in `scripts/cloud/runner-10k.sh`.
- [x] Server and runner scripts capture OS readiness through `scripts/cloud/os-readiness.sh`.
- [x] Server script configures `FastPortTestSmokeServer` for `0.0.0.0:6628`.
- [x] Runner scripts require `FASTPORT_SERVER_HOST`, preserving the explicit endpoint target assumption.
- [x] Server telemetry output path is standardized under `artifacts/load-validation/cloud-server-runner-split/server/`.
- [x] Runner output paths are standardized for smoke and `s5-random-10k`.
- [x] `scripts/cloud/write-manifest.sh` writes redacted JSON/Markdown manifests before runtime execution.
- [x] Manifest output avoids tenant IDs, subscription IDs, OCIDs, credentials, keys, and concrete host/IP values.
- [x] Azure and generic cloud runbooks document the smoke-first, 10K-second workflow.
- [x] `scripts/cloud/azure-vm-readiness.sh` verifies Azure VM metadata after user-created resources exist.
- [x] `scripts/cloud/ssh-readiness.sh` verifies server SSH and local runner prerequisites by default.
- [x] `scripts/cloud/runner-connectivity.sh` verifies local runner to server TCP `6628` connectivity.
- [x] `scripts/cloud/collect-artifacts.sh` collects server and local runner artifacts into a local ignored output directory.
- [x] Local verification covered shell syntax, manifest JSON validity, whitespace checks, and Release test execution.
- [x] Azure server VM readiness was verified after the user-created VM became available.
- [x] Server SSH and local runner prerequisites were verified with PEM key auth.
- [x] Local runner to Azure server TCP `6628` connectivity was verified.
- [x] `scripts/cloud/server-start.sh` was run on the Azure server VM.
- [x] `scripts/cloud/runner-smoke.sh` passed against the Azure server.
- [x] `scripts/cloud/runner-10k.sh` completed against the Azure server and produced a failed baseline.
- [x] `scripts/cloud/collect-artifacts.sh` copied server and local runner artifacts into `artifacts/load-validation/cloud-server-runner-split/collected/`.

## Missing Items

- [ ] Focused 10K validation does not yet merge server metrics into `summary.md`; server metrics were collected afterward.
- [ ] Azure server had lingering sessions after the failed 10K run and should be restarted before the next validation.
- [ ] A follow-up performance feature should address cloud receive timeouts, connection resets, and RTT tail.

## Changed Items

- [x] Provider changed from OCI-first execution to Azure-first execution because OCI A1 capacity is blocked.
- [x] Resource provisioning remains manual/user-driven rather than scripted, matching the later security and cost decision.
- [x] Manifest implementation writes role-specific files such as `manifest.server.json`, `manifest.runner-smoke.json`, and `manifest.runner-10k.json` instead of a single `manifest.json`. This is better aligned with separate server and local runner execution.
- [x] Runtime helper script names are concrete (`server-start.sh`, `runner-smoke.sh`, `runner-10k.sh`) instead of the earlier placeholder names (`run-smoke.sh`, `run-10k.sh`).
- [x] Manifest records size/location/build/git/runtime metadata with redacted endpoint state. Concrete host/IP and account identifiers remain intentionally omitted even after the server VM exists.

## Risk Assessment

| Risk | Current State | Impact |
|------|---------------|--------|
| Runner VM cost or quota is unclear | Avoided by default | Local runner is the default; cloud runner is optional later |
| Server VM is not yet available | Closed | Azure `Standard_B2s` server VM was verified |
| Public endpoint path is unverified | Closed | Smoke and focused 10K reached the Azure server |
| Server metrics collection is not proven | Partially closed | Server metrics were collected after the run but not merged into `summary.md` |
| Focused 10K cloud baseline fails | Open | Receive timeouts, resets, disconnects, and RTT tail require follow-up tuning |
| Lingering server sessions after failed 10K | Open | Restart server before the next validation to avoid stale state |

## Recommendations

1. Move this feature to report with the cloud split validation result recorded as a failed 10K baseline.
2. Restart the Azure smoke server before any follow-up load run because the failed 10K left stale sessions.
3. Split the performance work into a new follow-up focused on cloud receive timeout/reset behavior and RTT tail.
4. Add server metrics merge support for the server/local split path if combined summaries are required for future reports.

## Verification

Already completed during Do phase:

- `bash -n scripts/cloud/*.sh`
- `FASTPORT_CLOUD_OUTPUT=/tmp/fastport-cloud-manifest-test scripts/cloud/write-manifest.sh runner-smoke`
- `jq empty /tmp/fastport-cloud-manifest-test/manifest.runner-smoke.json`
- `git diff --check`
- `dotnet test FastPortCharp.sln -c Release --no-build`

Additional checks for this analysis:

- `jq empty docs/.pdca-status.json`
- `git diff --check`
- `test -s docs/03-analysis/cloud-server-runner-split-load-validation.analysis.md`

Act iteration added the following checks:

- `bash -n scripts/cloud/*.sh`
- `git diff --check`

Runtime validation added the following checks:

- `scripts/cloud/azure-vm-readiness.sh`
- `scripts/cloud/ssh-readiness.sh`
- `scripts/cloud/runner-connectivity.sh`
- `scripts/cloud/runner-smoke.sh`
- `scripts/cloud/runner-10k.sh`
- `scripts/cloud/collect-artifacts.sh`
- `dotnet build FastPortCharp.sln -c Release`
- `dotnet test FastPortCharp.sln -c Release --no-build`

## Next Steps

- [ ] Report `cloud-server-runner-split-load-validation` with the failed 10K cloud baseline.
- [ ] Restart the Azure smoke server before follow-up validation.
- [ ] Start a follow-up optimization item for cloud receive timeout/reset and RTT tail behavior.
