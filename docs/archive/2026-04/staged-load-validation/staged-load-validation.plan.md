# staged-load-validation - Plan Document

> Version: 1.0.0 | Date: 2026-04-28 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose
FastPortSmokeServer와 FastPortLoadRunner를 사용해 1,000 / 3,000 / 5,000 / 10,000 concurrent session 단계별 부하 검증을 반복 가능하게 만든다. 목적은 FastPortSharp 엔진의 packet/buffer/session 안정성을 실제 TCP 경로에서 확인하고, observed JSONL metric contract를 기준으로 결과를 판정할 수 있게 하는 것이다.

### 1.2 Background
이전 단계에서 `FastPortLoadRunner`, `FastPortSmokeServer`, server/client observed metric contract, LoadRunner observed JSONL 출력이 준비되었다. 아직 남은 한계는 실제 staged load validation이 실행 가능한 절차/자동화/판정 기준으로 고정되지 않았다는 점이다.

특히 아래 검증은 아직 정식 범위로 처리되지 않았다.

- 1,000 / 3,000 / 5,000 / 10,000 session staged load validation
- fixed 8K payload와 random 4K-16K payload 시나리오 비교
- JSONL 결과에서 session stability, TPS, RTT, socket error rate를 읽어 pass/fail 판단
- 실패 시 어떤 stage에서 어떤 metric이 기준을 벗어났는지 재현 가능한 기록

## 2. Goals

### 2.1 Primary Goals
- [x] stage matrix를 정의한다: 1,000 / 3,000 / 5,000 / 10,000 sessions.
- [x] payload profile을 정의한다: `fixed:8192`, `random:4096-16384`.
- [x] LoadRunner observed JSONL을 결과 판정의 입력으로 사용한다.
- [x] 각 stage의 pass/fail 기준을 문서화한다.
- [x] 자동화 범위를 CI-safe smoke와 manual/high-load validation으로 분리한다.
- [x] validation 결과 파일/디렉터리 구조를 계획한다.

### 2.2 Non-Goals
- MAUI dashboard UI를 구현하지 않는다.
- FastPortServer core engine에 smoke/test protocol을 추가하지 않는다.
- OS kernel/socket tuning을 자동 변경하지 않는다.
- 모든 환경에서 10,000 session 성공을 보장하지 않는다.
- 이번 plan 단계에서 실제 high-load run 결과를 생성하지 않는다.

## 3. Scope

### 3.1 In Scope
- staged load validation 실행 방식 설계
- stage별 LoadRunner command/profile 정의
- FastPortSmokeServer 실행 전제 정의
- JSONL result path 규칙 정의
- result summary format 정의
- pass/fail metric threshold 초안 정의
- CI-safe small validation과 manual high-load validation의 경계 정의

### 3.2 Out of Scope
- MAUI dashboard streaming
- 외부 telemetry HTTP/WebSocket endpoint
- production deployment pipeline
- packet protocol 변경
- engine-level performance optimization
- OS별 ulimit/sysctl 자동 튜닝

## 4. Proposed Stage Matrix

| Stage | Sessions | Payload | Duration | Ramp-up | Purpose |
|-------|----------|---------|----------|---------|---------|
| S1 | 1,000 | `fixed:8192` | 2m | 30s | baseline fixed payload stability |
| S2 | 1,000 | `random:4096-16384` | 2m | 30s | variable packet size stability |
| S3 | 3,000 | `random:4096-16384` | 3m | 60s | medium concurrency pressure |
| S4 | 5,000 | `random:4096-16384` | 5m | 90s | high concurrency pressure |
| S5 | 10,000 | `random:4096-16384` | 5m | 120s | target scale validation |

The exact duration/ramp-up values may be adjusted during design if local machine limits require safer defaults.

## 5. Result Contract

### 5.1 Inputs
- `FastPortSmokeServer` listening on configured host/port
- `FastPortLoadRunner` command with stage-specific sessions/payload/duration/ramp-up
- JSONL output emitted as `ObservedMetricsSnapshot`

### 5.2 Outputs
Suggested result layout:

```text
artifacts/load-validation/{run-id}/
  manifest.json
  s1-fixed-1k.metrics.jsonl
  s2-random-1k.metrics.jsonl
  s3-random-3k.metrics.jsonl
  s4-random-5k.metrics.jsonl
  s5-random-10k.metrics.jsonl
  summary.json
  summary.md
```

### 5.3 Candidate Pass/Fail Criteria
- final `clientObserved.currentSessions` reaches at least 95% of target during steady state.
- `clientObserved.socketErrorRate` remains below configured threshold.
- no sustained disconnect storm after steady state begins.
- `clientObserved.rttP95Ms` and `clientObserved.rttP99Ms` are recorded for trend comparison.
- JSONL parser can read every emitted line without schema errors.

Thresholds should be finalized in design because they depend on machine/network capacity.

## 6. Automation Strategy

### 6.1 CI-safe Scope
CI should keep a small smoke validation only, for example:

- 10-50 sessions
- fixed 1K or 8K payload
- short duration
- schema parsing check

This protects normal test time and avoids flaky resource exhaustion.

### 6.2 Manual/Performance Scope
The full staged matrix should run through an explicit command or script, not as default `dotnet test`.

Candidate interface:

```bash
dotnet run -c Release --project FastPortLoadRunner -- --sessions 10000 --payload random:4096-16384 --duration 5m --ramp-up 120s --output artifacts/load-validation/{run-id}/s5-random-10k.metrics.jsonl
```

Design should decide whether to add a dedicated orchestrator project/script or keep documented commands first.

## 7. Success Criteria

- [ ] A staged load validation design document defines execution flow and thresholds.
- [ ] A repeatable command/script can run stage profiles and write JSONL result files.
- [ ] A result parser summarizes observed JSONL into pass/fail output.
- [ ] CI-safe smoke validation remains lightweight and deterministic.
- [ ] Full staged validation is opt-in and documented.
- [ ] Existing `dotnet test` remains green.

## 8. Schedule

| Phase | Target Date | Status |
|-------|------------|--------|
| Plan | 2026-04-28 | Completed |
| Design | 2026-04-28 | Pending |
| Implementation | TBD | Pending |
| Review | TBD | Pending |

## 9. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| 10,000 sessions fail due to local OS limits rather than engine bugs | High | High | Keep OS prerequisites documented and separate environment failure from engine failure. |
| Long-running load tests make CI flaky | High | High | Keep full matrix opt-in; use only small smoke in CI. |
| Result thresholds are too strict or too loose | Medium | Medium | Start with schema/session/error stability criteria, then tune latency thresholds after baseline runs. |
| JSONL output grows large | Medium | Medium | Use per-stage output files and summary extraction; avoid committing generated artifacts. |
| Server startup/shutdown orchestration is brittle | Medium | Medium | Prefer explicit process lifecycle handling or documented manual server prerequisite in first iteration. |

## 10. References

- `FastPortLoadRunner/`
- `FastPortSmokeServer/`
- `LibNetworks/Telemetry/ObservedMetrics.cs`
- `docs/archive/2026-04/fastport-loadrunner/`
- `docs/archive/2026-04/fastport-smoke-server/`
- `docs/archive/2026-04/telemetry-export-metric-contract/`
- `docs/archive/2026-04/loadrunner-observed-jsonl/`
