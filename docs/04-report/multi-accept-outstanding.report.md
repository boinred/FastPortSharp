# Completion Report: multi-accept-outstanding

> Date: 2026-05-09 | Plan: docs/01-plan/features/multi-accept-outstanding.plan.md | Design: docs/02-design/features/multi-accept-outstanding.design.md

---

## Summary

`multi-accept-outstanding`는 완료됐다.

`LibNetworks.BaseListener`에 동시에 여러 `Socket.AcceptAsync`를 outstanding 상태로 유지할 수 있는 overload를 추가했다. 기존 `StartAccept(ip, port)`와 `StartAccept(ip, port, backlog)` 동작은 그대로 유지하고, 새 `StartAccept(ip, port, backlog, outstandingAccepts)`만 추가했다.

기본값은 `OutstandingAccepts=1`로 유지한다. Docker/cloud 10K 비교에서 `OutstandingAccepts=4`는 accept path 평균을 일부 낮췄지만, connect failure와 TPS 관점에서 default를 올릴 만큼의 명확한 이득은 없었다.

## Changed Files

| File | Change |
|------|--------|
| `LibNetworks/BaseListener.cs` | accept 전용 `SocketAsyncEventArgs[]`, outstanding accept count normalization, 신규 `StartAccept` overload |
| `FastPortTestSmokeServer/FastPortTestSmokeServerOptions.cs` | `DefaultOutstandingAccepts`, `OutstandingAccepts` option 추가 |
| `FastPortTestSmokeServer/Program.cs` | `FastPortTestSmokeServer:OutstandingAccepts` binding 추가 |
| `FastPortTestSmokeServer/FastPortTestSmokeServerBackgroundService.cs` | startup log와 listener 호출에 `OutstandingAccepts` 전달 |
| `FastPortTestSmokeServer/appsettings.json` | `OutstandingAccepts: 1` 추가 |
| `FastPortTests/ServerTelemetryTests.cs` | option/configuration 및 normalization coverage 추가 |
| `FastPortTests/FastPortTestSmokeServerTests.cs` | `outstandingAccepts=2` smoke integration coverage 추가 |
| `docs/load-validation-benchmark-results.md` | Docker/cloud `OutstandingAccepts=1` vs `4` 비교 결과 추가 |
| `docs/03-analysis/multi-accept-outstanding.analysis.md` | design/code gap 및 validation 결과 반영 |

## Verification

Local verification:

| Check | Result |
|-------|--------|
| `dotnet build FastPortSharp.sln -c Release` | Passed, warning 0, error 0 |
| `dotnet test FastPortSharp.sln -c Release --no-build` | Passed, 139/139 |

Docker/cloud load condition:

```text
Topology: Azure smoke server + local Docker Desktop runners
Server: ListenBacklog=10500, SessionIdleCleanup IdleTimeoutSeconds=90
Client load: 10 Docker containers x 1,000 sessions
Payload: random:128-2048
Send rate: 1000
Pacing: fixed-window=1
Ramp-up: 120s
Duration: 3m
```

Artifacts:

| Variant | Artifact root |
|---------|---------------|
| `OutstandingAccepts=1` | `artifacts/load-validation/multi-accept-o1-20260509-021411/` |
| `OutstandingAccepts=4` | `artifacts/load-validation/multi-accept-o4-20260509-021411/` |

## Cloud Result

| Metric | `OutstandingAccepts=1` | `OutstandingAccepts=4` |
|--------|-----------------------:|-----------------------:|
| Docker runner exits | `10/10 exit 0` | `10/10 exit 0` |
| Connect completed | `10,000` | `9,997` |
| Connect failures | `0` | `3` |
| Sum TPS | `6,559.79` | `5,941.41` |
| Average RTT P50 | `378.97ms` | `369.48ms` |
| Average RTT P95 | `3,967.57ms` | `3,725.48ms` |
| Average RTT P99 | `17,579.81ms` | `17,809.50ms` |
| Server accepted sessions | `9,999` | `9,997` |
| Server accept errors | `0` | `0` |
| Server socket errors | `149` | `166` |
| Server idle-timeout disconnects | `154` | `172` |
| Server send backpressure events | `0` | `0` |
| Server send rejected requests | `18` | `2` |
| Max pending send requests | `859` | `824` |
| `accept-task-start` avg | `0.616ms` | `0.259ms` |
| `accept-first-socket-receive` avg | `134.037ms` | `126.719ms` |

## Interpretation

The implementation works and the runtime option is safe to keep.

`OutstandingAccepts=4` did reduce accept path averages, especially `accept-task-start`. However, it also produced `3` connect failures where the default `1` produced `0`, lowered aggregate TPS, and slightly worsened average RTT P99. Server send pressure remained clean in both variants with `sendBackpressureEvents=0`.

The default should therefore remain `1`. Multi-accept outstanding can stay as an experimental/runtime tuning option, but this validation does not justify changing production behavior.

## Remaining Gaps

- Accept args disposal is still a cleanup candidate, not part of this feature.
- `BaseSocket.m_SocketEvent` is still present for compatibility and can be cleaned up separately.
- `OutstandingAccepts=2`/`8` cloud comparison is deferred until accept pressure reappears.

## Next Steps

1. Keep `OutstandingAccepts=1` as the default.
2. Commit this feature when ready.
3. Move to the next improvement candidate: listener socket event cleanup or the later pipeline experiment.
