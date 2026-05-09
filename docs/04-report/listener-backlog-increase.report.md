# Completion Report: listener-backlog-increase

> Date: 2026-05-07 | Plan: docs/01-plan/features/listener-backlog-increase.plan.md | Design: docs/02-design/features/listener-backlog-increase.design.md

---

## Summary

`listener-backlog-increase`는 완료됐다.

`LibNetworks.BaseListener`의 hard-coded `Listen(100)`을 제거하고, caller가 전달한 backlog를 `Socket.Listen(backlog)`에 넘기도록 변경했다. 기존 `StartAccept(string ip, int port)` API는 유지했고, `FastPortTestSmokeServer`에는 `FastPortTestSmokeServer:ListenBacklog` 설정과 기본값 `4096`을 추가했다.

Local build/test와 cloud validation 모두 완료했다. Cloud test에서 listen backlog는 원격 `ss` 기준 `4096`으로 관측됐고, connect timeout은 baseline `1,057`에서 `7`로 줄었다.

## Changed Files

| File | Change |
|------|--------|
| `LibNetworks/BaseListener.cs` | default backlog `4096`, `StartAccept(ip, port, backlog)` overload, `Listen(normalizedBacklog)` 적용 |
| `FastPortTestSmokeServer/FastPortTestSmokeServerOptions.cs` | `DefaultListenBacklog`, `ListenBacklog` option 추가 |
| `FastPortTestSmokeServer/Program.cs` | `FastPortTestSmokeServer:ListenBacklog` binding 및 fallback 처리 |
| `FastPortTestSmokeServer/FastPortTestSmokeServerBackgroundService.cs` | startup log와 listener 호출에 backlog 전달 |
| `FastPortTestSmokeServer/appsettings.json` | `ListenBacklog: 4096` 추가 |
| `FastPortTests/ServerTelemetryTests.cs` | smoke server options/configuration coverage 보강 |
| `docs/01-plan/features/listener-backlog-increase.plan.md` | PDCA plan |
| `docs/02-design/features/listener-backlog-increase.design.md` | PDCA design |
| `docs/03-analysis/listener-backlog-increase.analysis.md` | design/code 및 cloud result 분석 |

## Verification

Local verification:

| Check | Result |
|-------|--------|
| `dotnet build FastPortSharp.sln -c Release` | Passed, warning 0, error 0 |
| `dotnet test FastPortSharp.sln -c Release --no-build` | Passed, 134/134 |

Cloud server verification:

| Check | Result |
|-------|--------|
| Remote clean worktree build | Passed, warning 0, error 0 |
| Startup log | `ListenBacklog:4096` |
| `ss -ltnp \| grep 6628` | `LISTEN 0 4096 ... :6628` |

Cloud load condition:

```text
sessions=10000
payload=random:128-2048
rate=1000
pacing-policy=fixed-window
pacing-fixed-window=1
ramp-up=120s
duration=3m
metrics-interval=1s
```

Artifacts:

| Artifact | Path |
|----------|------|
| Client metrics | `artifacts/load-validation/listener-backlog-20260507-123030-client/medium-gameplay-10k.metrics.jsonl` |
| Connect events | `artifacts/load-validation/listener-backlog-20260507-123030-client/medium-gameplay-10k.connect-events.jsonl` |
| Client stdout | `artifacts/load-validation/listener-backlog-20260507-123030-client/medium-gameplay-10k.stdout.log` |
| Server metrics | `artifacts/load-validation/listener-backlog-20260507-123030-client/server.metrics.jsonl` |

## Cloud Result

| Metric | Baseline | After backlog `4096` |
|--------|----------|----------------------|
| Client peak sessions | `8,943 / 10,000` | `9,993 / 10,000` |
| Client final sessions | `8,935` | `9,993` |
| Connect timeouts | `1,057` | `7` |
| Connect timeout class | `connect\|SocketException\|TimedOut` | `connect\|SocketException\|TimedOut` |
| Client connected count | about `8,943` | `9,993` |
| Server accepted sessions | `8,942` | `9,994` including one connectivity probe |
| Server send backpressure | `0` | `0` |
| Server send rejected | `0` | `0` |
| Client final TPS | not compared | `6,452` |
| Client final RTT P50 | not compared | `163.57ms` |
| Client final RTT P95 | not compared | `2,445.81ms` |
| Client final RTT P99 | not compared | `11,146.18ms` |

Connect event classification:

| Event | Count |
|-------|-------|
| `completed` | `9,993` |
| `faulted SocketException TimedOut` | `7` |

## Interpretation

The backlog increase materially improved connection establishment.

The remaining `7` connect timeouts still took about `75,013ms`, so a small residual connect-tail remains. However, the baseline failure mode was reduced by about `99.3%`, and the test reached `99.93%` of target sessions.

Server send queue health stayed clean:

- `sendBackpressureEvents`: `0`
- `sendRejectedRequests`: `0`
- `maxPendingSendRequests`: `680`

One caveat remains. The final server metrics include `316` `idle-timeout` disconnects and `9,678` `remote-closed` disconnects after/around runner teardown. This does not change the connect backlog conclusion, but future RTT-tail tests should either disable `SessionIdleCleanup` or set its timeout above the full test window to avoid mixing cleanup behavior into latency analysis.

## Follow-Up: 10500 Backlog/SYN Queue Test

Additional runtime-only cloud test was executed with both OS queue limits and app listen backlog set to `10500`.

Server runtime settings:

```text
net.core.somaxconn = 10500
net.ipv4.tcp_max_syn_backlog = 10500
FastPortTestSmokeServer__ListenBacklog=10500
```

Server listen observation:

```text
LISTEN 0 10500 ... :6628
```

Same load condition:

```text
sessions=10000
payload=random:128-2048
rate=1000
pacing-policy=fixed-window
pacing-fixed-window=1
ramp-up=120s
duration=3m
metrics-interval=1s
```

Result:

| Metric | Backlog `4096` | Backlog/SYN `10500` |
|--------|----------------|---------------------|
| Client peak sessions | `9,993 / 10,000` | `10,000 / 10,000` |
| Connect attempts | `10,000` | `10,000` |
| Connect completed | `9,993` | `10,000` |
| Connect timeouts | `7` | `0` |
| Server accepted sessions | `9,994` including probe | `10,000` |
| Server send backpressure | `0` | `0` |
| Server send rejected | `0` | `0` |
| Client final RTT P50 | `163.57ms` | `319.57ms` |
| Client final RTT P95 | `2,445.81ms` | `2,930.41ms` |
| Client final RTT P99 | `11,146.18ms` | `12,938.56ms` |

Kernel counter delta during the `10500` run:

| Counter | Delta |
|---------|-------|
| `ListenOverflows` | `0` |
| `ListenDrops` | `0` |
| `TCPReqQFullDoCookies` | `0` |
| `TCPReqQFullDrop` | `0` |
| `TCPBacklogDrop` | `0` |
| `SyncookiesSent` | `0` |
| `SyncookiesFailed` | `0` |

Artifacts:

| Artifact | Path |
|----------|------|
| Client metrics | `artifacts/load-validation/listener-backlog-10500-20260508-0001-client/medium-gameplay-10k.metrics.jsonl` |
| Connect events | `artifacts/load-validation/listener-backlog-10500-20260508-0001-client/medium-gameplay-10k.connect-events.jsonl` |
| Server metrics | `artifacts/load-validation/listener-backlog-10500-20260508-0001-client/server.metrics.jsonl` |
| Kernel before/after | `artifacts/load-validation/listener-backlog-10500-20260508-0001-client/netstat-before.txt`, `netstat-after.txt` |

Interpretation:

`10500` eliminated the residual connect timeout and removed the observed kernel listen/SYN queue pressure for this run. The remaining issue is no longer connection establishment. The next bottleneck candidate is the connected-session runtime path: accept/session startup cost, receive-header RTT tail, idle cleanup interaction, and send/receive scheduling.

## Follow-Up: 10500 With Idle Cleanup Disabled

Additional runtime-only cloud test was executed with the same `10500` queue settings and `SessionIdleCleanup__Enabled=false`.

Result:

| Metric | Backlog/SYN `10500` | `10500` + idle cleanup disabled |
|--------|---------------------|---------------------------------|
| Client peak sessions | `10,000 / 10,000` | `9,945 / 10,000` |
| Connect attempts | `10,000` | `10,000` |
| Connect completed | `10,000` | `9,945` |
| Connect timeouts | `0` | `55` |
| Server accepted sessions | `10,000` | `9,944` |
| Server idle-timeout disconnects | `378` | `0` |
| Server socket errors | `370` | `0` |
| Server send backpressure | `0` | `0` |
| Server send rejected | `0` | `2` |
| Client final RTT P50 | `319.57ms` | `356.34ms` |
| Client final RTT P95 | `2,930.41ms` | `3,152.34ms` |
| Client final RTT P99 | `12,938.56ms` | `13,302.22ms` |

Kernel counter delta during the idle-cleanup-disabled run:

| Counter | Delta |
|---------|-------|
| `ListenOverflows` | `0` |
| `ListenDrops` | `0` |
| `TCPReqQFullDoCookies` | `0` |
| `TCPReqQFullDrop` | `0` |
| `TCPBacklogDrop` | `0` |
| `SyncookiesSent` | `0` |
| `SyncookiesFailed` | `0` |

Artifacts:

| Artifact | Path |
|----------|------|
| Client metrics | `artifacts/load-validation/listener-backlog-10500-no-idlecleanup-20260508-0001-client/medium-gameplay-10k.metrics.jsonl` |
| Connect events | `artifacts/load-validation/listener-backlog-10500-no-idlecleanup-20260508-0001-client/medium-gameplay-10k.connect-events.jsonl` |
| Server metrics | `artifacts/load-validation/listener-backlog-10500-no-idlecleanup-20260508-0001-client/server.metrics.jsonl` |
| Kernel before/after | `artifacts/load-validation/listener-backlog-10500-no-idlecleanup-20260508-0001-client/netstat-before.txt`, `netstat-after.txt` |

Interpretation:

Disabling idle cleanup removed server-side idle-timeout disconnects and server socket-error counters, but it did not improve the client-visible RTT tail. This run also reintroduced `55` connect timeouts even though server-side listen/SYN queue counters did not move. Therefore, these `55` failures should not be attributed to the server accept queue. The likely explanation is external variability in the local-runner-to-public-cloud connection path, such as local NAT, public network path, or cloud ingress behavior outside the application counters.

This weakens the idle-cleanup-as-primary-cause hypothesis. The next diagnostic should use either repeated runs or a cloud-side runner to separate local public-network variance from server runtime behavior before changing accept code.

## Outcome

`Listen(100)` was a real limiter for the 10K ramp-up test. The selected `4096` backlog is validated as a good next default for the smoke/cloud test path.

Recommended next feature:

```text
cloud-rtt-tail-idle-cleanup-isolation
```

Scope:

- keep backlog at `4096`
- run the same 10K closed-loop condition
- set `SessionIdleCleanup__Enabled=false` or timeout above test duration
- focus on RTT P95/P99 and receive-header operation duration
