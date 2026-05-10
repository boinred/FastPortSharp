# FastPortSharp Handoff

> Last updated: 2026-05-05
> Branch: `main`
> Remote baseline before this handoff update: `42db502 Archive 10K session RTT validation handoff`

## Current State

- Active PDCA features: `cloud-server-runner-split-load-validation`, `adaptive-client-pacing-threshold-tuning`
- Primary feature: `adaptive-client-pacing-threshold-tuning`
- Latest archived PDCA feature: `throughput-pacing-server-processing-decomposition`
- Project level detected by bkit: `Starter`
- Current recommended action: stop iterating `adaptive-client-pacing-threshold-tuning` in its current scope and switch target to server/test-server response processing or cloud split validation.

The latest completed work rejected both a stability-restore threshold candidate and a header-wait pressure candidate. The retained adaptive client pacing defaults still fail the hard 10K guardrails, and the remaining signal is a late-ramp `receive-header` wait/backlog problem rather than client write blocking.

## Remaining Work Snapshot

As of 2026-05-05, the remaining work is:

1. Do not continue client threshold-only/header-wait-only iteration under `adaptive-client-pacing-threshold-tuning`.
2. Continue `$pdca do cloud-server-runner-split-load-validation` when OCI A1 capacity becomes available, or open a server/test-server response-processing optimization feature.
3. Build `fastport-game-server-template-foundation` after the current performance path is better understood.
4. Start MAUI dashboard work after telemetry and server template boundaries are stable.
5. Add operational validation beyond same-machine 10K, especially split server/runner validation, soak tests, and OS/socket limit isolation.

Cloud validation is currently waiting on OCI A1 capacity. The next immediate local action is to switch target rather than run another client pacing iterate:

```text
$pdca do cloud-server-runner-split-load-validation
```

## Latest Completed Work

### Adaptive Client Pacing Threshold Tuning

- PDCA status: `check active; iterate stop condition reached`
- Match rate: `86%`
- Started: 2026-05-03
- Plan document: `docs/01-plan/features/adaptive-client-pacing-threshold-tuning.plan.md`
- Design document: `docs/02-design/features/adaptive-client-pacing-threshold-tuning.design.md`
- Do notes: `docs/02-design/features/adaptive-client-pacing-threshold-tuning.do.md`
- Analysis document: `docs/03-analysis/adaptive-client-pacing-threshold-tuning.analysis.md`
- Baseline artifact: `artifacts/load-validation/s5-session-rtt-validation/summary.json`
- First candidate artifact: `artifacts/load-validation/adaptive-pacing-threshold-s5/summary.json`
- Fallback candidate artifact: `artifacts/load-validation/adaptive-pacing-threshold-fallback-s5/summary.json`
- Baseline max TPS: `9,371.08`
- Baseline RTT P95/P99: `19,210.39ms` / `24,863.90ms`
- Baseline pacing average wait max: `2,857.09ms`
- Baseline pacing window range: `1-5`
- Baseline pending requests/session: `3.67`

Implemented scope:

- Tuned load-runner adaptive pacing thresholds before touching `LibNetworks`.
- First selected defaults: max window `8`, RTT target `16,000ms`, RTT high `28,000ms`, increase every `128`; min/initial remain `1`/`4`.
- Iterated fallback defaults: max window `8`, RTT target `14,000ms`, RTT high `24,000ms`, increase every `128`; min/initial remain `1`/`4`.
- Updated runner and validation defaults plus CLI usage text.
- Added direct default-value tests for runner/validation and strengthened validation command propagation assertions.
- Verified with Release build/test, smoke validation, and focused same-machine 10K.
- Kept cloud split validation separate until OCI A1 capacity is available.

Verification:

| Check | Result |
|-------|--------|
| Release build | Passed, 0 warnings/errors |
| Release tests | Passed, 106 tests |
| Smoke validation | Passed |
| Focused 10K validation | Passed |
| Fallback smoke validation | Passed |
| Fallback focused 10K validation | Passed |

Focused 10K comparison:

| Metric | Baseline | First Candidate | Fallback Candidate |
|--------|---------:|----------------:|-------------------:|
| Peak sessions | `10,000 / 10,000` | `10,000 / 10,000` | `10,000 / 10,000` |
| Final disconnects | `0` | `82` | `90` |
| Max TPS | `9,371.08` | `8,188.50` | `8,554.46` |
| RTT P95 | `19,210.39ms` | `18,166.43ms` | `16,746.72ms` |
| RTT P99 | `24,863.90ms` | `26,430.64ms` | `21,643.96ms` |
| Per-session P95-of-P95 | `18,211.02ms` | `19,777.54ms` | `18,653.15ms` |
| Pending requests/session | `3.67` | `3.71` | `3.72` |
| Pending server send requests | `1,095` | `994` | `945` |
| Server send backpressure events | `1,583` | `3,678` | `3,056` |
| Pacing average wait max | `2,857.09ms` | `3,052.51ms` | `2,711.68ms` |
| Pacing window range | `1-5` | `1-7` | `1-7` |
| `send|IOException|NoBufferSpaceAvailable` | `1,639` | `1,657` | `1,497` |
| `receive|IOException|TimedOut` | `184` | `768` | `939` |
| Max scheduler drift | `12.12ms` | `43.30ms` | `15.23ms` |

Interpretation:

- The fallback candidate is better than the first candidate on TPS, RTT P95/P99, per-session tail, pending send, server backpressure, `NoBufferSpaceAvailable`, scheduler drift, and pacing wait.
- Against the original baseline, fallback improves RTT P95/P99, pending server send, send buffer, pacing wait, and `NoBufferSpaceAvailable`.
- It is still not a clean production claim because disconnects, receive timeouts, server backpressure, socket error rate, and pending request depth regress.
- Latest analysis classifies the match rate as `86%`; do not report this as a completed optimization.

### Throughput/Pacing/Server Processing Decomposition

- Analysis document: `docs/archive/2026-05/throughput-pacing-server-processing-decomposition/throughput-pacing-server-processing-decomposition.analysis.md`
- Report document: `docs/archive/2026-05/throughput-pacing-server-processing-decomposition/throughput-pacing-server-processing-decomposition.report.md`
- Archive path: `docs/archive/2026-05/throughput-pacing-server-processing-decomposition/`
- Match rate: `92%`
- Missing item: the script does not yet emit a full metric-to-pipeline segment coverage matrix.
- Decision: archived because the missing item is optional diagnostic output polish, not a runtime gap.
- Recommended next optimization lane: server/test-server response processing or cloud split validation; `adaptive-client-pacing-threshold-tuning` has reached its stop condition.

Key diagnostic interpretation from `artifacts/load-validation/s5-session-rtt-validation/summary.json`:

| Segment | Finding | Evidence |
|---------|---------|----------|
| RTT tail shape | systemic broad pressure | global P95/per-session P95 gap `5.20%` |
| Client pacing | pacing actively throttling | max average wait `2,857.09ms`, window `1-5` |
| Client outstanding depth | broad outstanding backlog | `3.67` pending requests/session |
| Server send path | server send pressure visible | pending send `1,095`, backpressure `1,583` |
| Socket pressure | socket pressure visible | `NoBufferSpaceAvailable=1,639`, receive timeouts `184` |
| Local scheduler | scheduler drift low | max drift `12.12ms` |

### LoadRunner 10K Session RTT Validation

- Archived PDCA documents under:
  - `docs/archive/2026-04/loadrunner-10k-session-rtt-validation/`
- Updated `docs/load-validation-benchmark-results.md` with the latest 10K comparison and session RTT interpretation.
- Verified `docs/.pdca-status.json` has no active feature after archive.

Final focused 10K artifact:

- `artifacts/load-validation/s5-session-rtt-validation/summary.md`

Important result:

| Metric | Value |
|--------|------:|
| Run ID | `20260430-172637-staged` |
| Result | Passed |
| Peak sessions | `10,000 / 10,000` |
| Final disconnects | `0` |
| Max TPS | `9,371.08` |
| Max pending request count | `36,695` |
| Max pending send requests | `1,095` |
| Server send backpressure events | `1,583` |
| Max send buffer bytes | `64,204` |
| RTT P95 | `19,210.39ms` |
| RTT P99 | `24,863.90ms` |
| Socket error rate | `0.13%` |
| `send|IOException|NoBufferSpaceAvailable` | `1,639` |
| `receive|IOException|TimedOut` | `184` |
| Max scheduler drift | `12.12ms` |

Session RTT finding:

| Session RTT Metric | Value |
|--------------------|------:|
| Tracked sessions | `10,000` |
| Eligible sessions | `9,922` |
| P50 of session P95 | `13,663.21ms` |
| P95 of session P95 | `18,211.02ms` |
| P99 of session P95 | `23,295.81ms` |
| Max session P95 | `38,710.49ms` |
| Max session P99 | `87,523.53ms` |
| Max session max RTT | `93,670.83ms` |

Interpretation:

- Global RTT P95 is `19,210.39ms`.
- P95 of per-session P95 is `18,211.02ms`.
- These values are close enough to treat the current tail as broad high-load pressure.
- Slow outlier sessions still exist, but they are not the only source of the tail.

## Current Benchmark Interpretation

The current implementation can pass focused same-machine 10K validation, but it is not yet good enough for a real-time game workload.

What improved compared with the original server-merged failure path:

- Peak sessions now reach `10,000 / 10,000`.
- Final disconnects are down to `0`.
- Server pending send depth is no longer exploding into hundreds of thousands.
- Server send buffer bytes remain bounded.
- Per-session RTT telemetry is now available for tail analysis.

What remains weak:

- RTT P95 is still about `19.2s`.
- RTT P99 is still about `24.9s`.
- Max TPS is still too low for high-frequency movement workloads.
- Send-side `NoBufferSpaceAvailable` is reduced but still present.
- Server send backpressure and receive timeout still appear under 10K pressure.

## Recommended Roadmap

### 1. Server/Test-Server Response Processing Or Cloud Split Validation

Recommended command:

```text
$pdca do cloud-server-runner-split-load-validation
```

Goal:

- Validate whether the late-ramp `receive-header` wait/backlog persists when server and runner are split.
- If cloud capacity remains blocked, open a local server/test-server response-processing optimization feature.
- Avoid another client threshold-only/header-wait-only pacing pass until server-side response latency and same-machine scheduling noise are separated.

Current decision:

- `adaptive-client-pacing-threshold-tuning` should not receive another client-side iterate candidate in its current scope.
- Cloud split artifacts should be fed through the same decomposition script once OCI capacity is available.

### 2. Follow-up Optimization

The latest adaptive-client diagnostics point away from client write blocking and toward response-header wait/backlog.

Likely candidates:

- `server-processing-throughput-tuning`
- `receive-timeout-tail-flow-control`
- `send-throughput-drain-fairness-optimization`

### 3. Game Server Template Foundation — COMPLETED (2026-05)

Feature: `game-server-template-from-network-engine` — archived at
`docs/archive/2026-05/game-server-template-from-network-engine/`.

Delivered:

- `LibCommons` / `LibNetworks` carry local package metadata (`PackageId=FastPort.Common`/`FastPort.Networks`, MIT, repo URL, tags) for identification only; `GeneratePackageOnBuild=false`.
- New `FastPortGameServerTemplate` project: Generic Host + Serilog + Protobuf (`Sample.proto`, `Grpc.Tools`, `GrpcServices=None`) + GameServer/HostedService + GameSession/Factory + IPacketHandler/EchoHandler/PacketDispatcher + IGameServerTelemetry/Null impl.
- New `FastPortGameServerTemplate.SampleClient` project: full Protobuf echo round-trip verification client (EchoRequest 1001 → EchoResponse 1002, loopback RTT ~14ms).
- Full solution build and 139-test suite remain green.

**NuGet upload to nuget.org is out of scope** — consumers use FastPortSharp as a GitHub Template Repository (clone / fork) or via ProjectReference within the same solution.

Follow-up cycle `game-server-template-scaffold-scripts` (2026-05) added cross-platform `scripts/scaffold-game-server.{sh,ps1}` so users can bootstrap a self-contained, token-renamed checkout in one command. Validated by 7 golden-file cases under `tests/scaffold/` and a 3-OS GitHub Actions matrix (`.github/workflows/scaffold.yml`) that asserts byte-identical sha256 across ubuntu / macos / windows for both bash and PowerShell flavors.

Out of scope (separate-cycle candidates only): room/matchmaking, auth, game-level
heartbeat, game loop / tick, UDP, Unity client SDK, MAUI dashboard.

### 4. MAUI Dashboard

Start after the telemetry and server template boundaries are stable.

Likely candidates:

- `maui-telemetry-dashboard-foundation`
- `maui-load-validation-run-viewer`
- `maui-run-comparison-report-export`

The dashboard should consume the existing observed metric envelope:

- root `timestamp`
- `clientObserved`
- `serverObserved`

Initial views should focus on:

- TPS
- RTT P95/P99
- per-session RTT tail
- pending request/send
- pacing window/wait
- socket error classification
- server backpressure/send buffer
- stage pass/fail comparison

## Important Architecture Decisions

- `LibNetworks` should stay protocol-neutral.
- Smoke/load-test behavior belongs in `FastPortTestSmokeServer`, `FastPortTestLoadRunner`, and `FastPortTestLoadValidation`.
- Do not move echo/smoke protocol behavior back into `FastPortServer`.
- `FastPortServer` should remain a basic network engine host/sample. Game server bootstrap concerns live in `FastPortGameServerTemplate` (added 2026-05).
- `FastPortGameServerTemplate` depends only on `LibCommons` + `LibNetworks` (engine boundary). It must not reference `FastPortServer`, `FastPortClient`, `Protocols/`, `LibTestTelemetry`, or any test project.
- Engine packages `FastPort.Common` (= `LibCommons`) and `FastPort.Networks` (= `LibNetworks`) carry package metadata locally for identification, but `GeneratePackageOnBuild=false` and **publishing to nuget.org is explicitly out of scope**. The FastPortSharp repository itself acts as the GitHub Template Repository for `FastPortGameServerTemplate`; consumers should clone/fork the repo or use ProjectReference inside the same solution.
- Engine and template are versioned independently within the repo (`engine-v*` vs `template-v*` release tags); coupling is via ProjectReference, not NuGet.
- The scaffold scripts (`scripts/scaffold-game-server.{sh,ps1}`) read blocked tokens from `tests/scaffold/_shared/blocked-tokens.txt` as a single source of truth shared with the negative-case fixtures. The replacement token (`FastPortGameServerTemplate`) is intentionally a unique 26-char compound to make collateral substitution impossible. Cross-OS byte-identical output is enforced via root `.gitattributes` (LF + UTF-8) and the 3-environment CI matrix; do not introduce CRLF-emitting tooling without updating both.
- High-load generated artifacts remain under `artifacts/load-validation/` and should not be committed.
- Same-machine 10K results are useful for comparison, but server/runner split-machine validation is still needed before claiming production capacity.
- GitHub Actions should not deploy to OCI from this public repository; cloud validation should use local scripts plus OCI CLI/SSH unless a separate hardening pass is approved.
- OCI read-only discovery is connected for `us-chicago-1`, but A1 provisioning attempts hit `Out of host capacity` or rate limiting. No A1 instances were created; `fastport-load-vcn` exists.
- Azure CLI access is verified. There is one active `Standard_B2s` reserved VM instance in `koreacentral`, quantity `1`, utilization `0%`, and no existing `koreacentral` VMs were listed.
- For cloud split validation, wait until the user creates the Azure server VM; use the reserved `Standard_B2s` as the server candidate and review runner size/cost separately.

## Verification Recently Run

Latest validation from the active `adaptive-client-pacing-threshold-tuning` feature:

```bash
dotnet build FastPortSharp.sln -c Release
dotnet test FastPortSharp.sln -c Release --no-build
./tests-projects/FastPortTestLoadValidation/bin/Release/net10.0/FastPortTestLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --pacing-policy adaptive-window \
  --output artifacts/load-validation/adaptive-pacing-operation-duration-s5 \
  --server-metrics artifacts/load-validation/adaptive-pacing-operation-duration-s5/server.metrics.jsonl \
  --continue-on-failure
```

Results:

- Release build passed with `0` warnings and `0` errors.
- Test suite passed: `111 / 111`.
- Operation-duration smoke validation passed.
- Focused 10K failed under hard guardrails: peak `9,802 / 10,000`, final disconnects `2,152`, `receive|IOException|TimedOut = 410`, `send|IOException|NoBufferSpaceAvailable = 1,742`.
- Operation-duration telemetry shows `receive-header` avg/max `1,474.78ms / 52,261.89ms`, `receive-body` avg/max `8.34ms / 3,512.89ms`, and `send-write` avg/max `0.07ms / 67.28ms`.
- A stability-restore experiment with older adaptive defaults (`MaxWindow=16`, RTT target/high `12s/20s`, increase every `256`) was tested and rejected:
  - Smoke passed: `artifacts/load-validation/adaptive-pacing-stability-restore-smoke/summary.md`.
  - Focused 10K failed badly: peak `9,690 / 10,000`, final disconnects `5,936`, `receive|IOException|TimedOut = 4,554`, `send|IOException|NoBufferSpaceAvailable = 1,340`, RTT P95/P99 `20,569.94ms / 32,961.34ms`.
  - The rejected candidate was not kept in code; retained defaults remain `MaxWindow=8`, RTT target/high `14s/24s`, increase every `128`.
- A header-wait pressure experiment was tested and rejected:
  - Candidate behavior: reduce adaptive window when `receive-header` wait exceeds RTT target/high thresholds.
  - Smoke passed: `artifacts/load-validation/adaptive-pacing-header-pressure-smoke/summary.md`.
  - Focused 10K failed: peak `9,698 / 10,000`, final disconnects `2,453`, `receive|IOException|TimedOut = 1,236`, `send|IOException|NoBufferSpaceAvailable = 1,217`, RTT P95/P99 `23,529.55ms / 29,609.81ms`.
  - The candidate reduced send-side NoBuffer but worsened the critical receive-timeout/session-loss guardrails, so it was reverted.
- Validation now fails a stage when `FinalDisconnectCount > 0` or `receive|IOException|TimedOut > 0`.
- The load runner no longer lets the send phase keep running after receive completion/failure. This makes session loss visible instead of hiding it behind a still-running send loop.
- The current timeout path is not client write blocking; it is a late-ramp response-header wait/backlog problem.

## Suggested Commands

Check repository state:

```bash
git status --short --branch
```

Inspect PDCA status:

```text
$pdca status
```

Continue active cloud validation when capacity is available:

```text
$pdca do cloud-server-runner-split-load-validation
```

## Notes For Next Session

- Start with `docs/.pdca-status.json`; `adaptive-client-pacing-threshold-tuning` should be active in `check` after the latest analysis, `throughput-pacing-server-processing-decomposition` should be archived, and `cloud-server-runner-split-load-validation` should remain blocked in `do` until OCI A1 capacity is available.
- Treat the latest adaptive threshold 10K results as failed stability experiments, not completed tuning.
- Do not return to static `16/12s/20s/256` adaptive defaults; the hard-guardrail 10K run regressed to `5,936` disconnects and `4,554` receive timeouts.
- Do not reapply the header-wait pressure candidate; it regressed receive timeouts to `1,236` and final disconnects to `2,453`.
- Next optimization target should move away from client threshold-only pacing. Prefer server/test-server response processing or cloud split validation before more client-side tuning.
- Keep server template work after the next decomposition/optimization pass.
- Keep MAUI dashboard after server template boundaries are stable.
- Do not commit `.DS_Store` files.
