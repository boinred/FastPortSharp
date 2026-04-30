# loadrunner-10k-session-rtt-validation - Plan Document

> Version: 1.0.0 | Date: 2026-04-30 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`loadrunner-10k-session-rtt-validation`은 직전 `loadrunner-per-session-rtt-tail-telemetry`에서 추가한 `sessionRtt` 계측을 실제 10K load validation artifact로 검증하는 feature다.

이번 feature는 엔진 최적화가 아니라 측정 검증이다. 목표는 10K RTT tail이 전체 세션에 넓게 퍼진 병목인지, 일부 세션에 집중된 starvation/fairness 문제인지 판정하고 다음 최적화 feature를 좁히는 것이다.

### 1.2 Background

현재 최신 10K 기준 run은 `artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive/summary.md`이며, `basesession-send-channel-queue-lock-reduction` 이후 다음 상태를 보였다.

| Metric | Latest 10K Result |
|--------|------------------:|
| Result | Passed |
| Peak sessions | `9,975 / 10,000` |
| Final disconnects | `2` |
| Max TPS | `7,901.40` |
| Max pending request count | `38,246` |
| Max pending send requests | `1,282` |
| Server send backpressure events | `0` |
| `send\|IOException\|NoBufferSpaceAvailable` | `0` |
| `receive\|IOException\|TimedOut` | `1,266` |
| Socket error rate | `0.12%` |
| RTT P95 | `17,796.60ms` |
| RTT P99 | `27,398.15ms` |
| Max scheduler drift | `19.66ms` |

이 run은 validation threshold는 통과했지만, TPS, RTT P99, socket error rate, pending send depth, receive timeout은 아직 좋지 않다. 특히 기존 RTT P95/P99는 전체 sample 통합 분포라서 slow tail이 전체 세션 현상인지 일부 세션 현상인지 알 수 없었다.

직전 feature에서 다음 데이터가 추가되었다.

- client observed JSONL의 `sessionRtt`
- 세션별 RTT sample bounded collection
- 세션별 RTT P95/P99/max summary
- `p50OfSessionP95Ms`, `p95OfSessionP95Ms`, `p99OfSessionP95Ms`
- slowest session Top N
- low-sample excluded session count
- LoadValidation `Session RTT` summary column

### 1.3 PM Framing

이번 feature가 답해야 할 질문은 세 가지다.

1. 10K RTT tail은 대부분의 세션이 함께 느려지는 global throughput/pacing 문제인가?
2. slowest session Top N에 tail이 집중되는 fairness/starvation/session backlog 문제인가?
3. slow session과 receive timeout/socket classification이 같은 방향을 가리키는가?

이 질문에 답하기 전에는 엔진 send path, client pacing, receive flow-control 중 어디를 고칠지 확정하기 어렵다.

## 2. Goals

### 2.1 Primary Goals

- [ ] Release build 기준으로 `FastPortSmokeServer`와 `FastPortLoadValidation` 실행 준비를 확인한다.
- [ ] focused 10K validation을 실행해 새 `sessionRtt`가 포함된 artifact를 생성한다.
- [ ] `summary.json`, `summary.md`, raw observed JSONL에서 `sessionRtt` 출력이 실제로 존재하는지 확인한다.
- [ ] 10K 결과를 기존 latest candidate와 비교한다.
- [ ] `sessionRtt` 기준으로 RTT tail 분포를 해석한다.
- [ ] slowest session Top N이 전체 RTT P95/P99를 설명하는지 판단한다.
- [ ] low-sample excluded session count가 tail 해석을 왜곡하는지 확인한다.
- [ ] `docs/load-validation-benchmark-results.md`에 새 10K 결과와 해석을 갱신한다.
- [ ] 다음 병목 feature 후보를 하나로 좁힌다.

### 2.2 Non-Goals

- `LibNetworks` 엔진 send/receive path를 변경하지 않는다.
- `FastPortLoadRunner`의 pacing 정책을 변경하지 않는다.
- `FastPortLoadValidation`의 pass/fail threshold를 새로 조정하지 않는다.
- per-session RTT collector 구현을 다시 설계하지 않는다.
- 10K 성능 수치를 이번 feature 안에서 개선하려고 하지 않는다.
- 모든 raw RTT sample을 benchmark 문서에 옮기지 않는다.

## 3. Scope

### 3.1 In Scope

- Release build
  - `dotnet build FastPortCharp.sln -c Release`
- Server runtime
  - `FastPortSmokeServer` Release binary 실행
- 10K validation
  - `FastPortLoadValidation`
  - profile: `staged`
  - stage: `s5-random-10k`
  - adaptive pacing option 유지
  - output: `artifacts/load-validation/s5-session-rtt-validation`
- Artifact inspection
  - `summary.md`
  - `summary.json`
  - stage metrics JSONL
  - `sessionRtt` field presence and values
- Benchmark documentation
  - `docs/load-validation-benchmark-results.md`
- PDCA documents
  - plan/design/do/analyze/report

### 3.2 Out of Scope

- BenchmarkDotNet microbenchmark 재측정
- CI workflow 추가
- OS socket limit 문서 변경
- 서버 프로토콜 변경
- session-level socket correlation 구현 추가
- real-time game workload threshold 확정

## 4. Success Criteria

- [ ] 10K validation artifact가 `artifacts/load-validation/s5-session-rtt-validation`에 생성된다.
- [ ] `summary.md`에 `Session RTT` column 또는 slow session summary가 기록된다.
- [ ] `summary.json`에 `sessionRtt` 기반 stage summary 값이 기록된다.
- [ ] raw observed JSONL 중 client snapshot에 `sessionRtt` object가 포함된다.
- [ ] `trackedSessionCount`, `eligibleSessionCount`, `excludedLowSampleSessionCount`를 확인한다.
- [ ] `p95OfSessionP95Ms`와 `maxSessionP95Ms`의 차이로 tail 집중 여부를 판단한다.
- [ ] slowest session Top N의 P95/P99/max 값을 확인한다.
- [ ] 기존 latest candidate와 새 결과의 주요 지표를 비교한다.
- [ ] `docs/load-validation-benchmark-results.md`에 새 결과와 해석을 반영한다.
- [ ] 다음 feature 후보를 다음 중 하나로 결정한다.
  - global throughput/pacing/server processing decomposition
  - fairness/starvation/session backlog analysis
  - receive-timeout/socket-error correlation

## 5. Schedule

| Phase | Target Date | Status |
|-------|-------------|--------|
| Plan | 2026-04-30 | In Progress |
| Design | 2026-04-30 | Pending |
| Execution | 2026-04-30 | Pending |
| Analysis | 2026-04-30 | Pending |
| Report | 2026-04-30 | Pending |

## 6. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Local machine state differs from previous 10K runs | Result comparison may be noisy | Medium | Compare against latest local baseline and record run timestamp/output path explicitly |
| 10K run takes several minutes or fails due to port/process state | Delays validation | Medium | Verify server process and port before running; keep output directory unique |
| `sessionRtt` appears in raw JSONL but summary aggregation misses it | Feature cannot answer PM question | Low | Inspect both raw JSONL and summary files |
| low-sample sessions distort tail interpretation | Wrong next feature choice | Medium | Use `eligibleSessionCount` and `excludedLowSampleSessionCount` as required interpretation fields |
| 10K result regresses unrelated metrics | Diagnosis may mix telemetry overhead with runtime noise | Medium | Record TPS, socket classification, pending counts, scheduler drift along with session RTT |

## 7. Execution Plan

### 7.1 Preflight

```bash
dotnet build FastPortCharp.sln -c Release
```

Check that no stale smoke server is holding port `6628`. If needed, stop the old process before launching the server.

### 7.2 Server

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer
```

### 7.3 10K Validation

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --pacing-policy adaptive-window \
  --output artifacts/load-validation/s5-session-rtt-validation
```

### 7.4 Artifact Checks

- `artifacts/load-validation/s5-session-rtt-validation/summary.md`
- `artifacts/load-validation/s5-session-rtt-validation/summary.json`
- stage metrics JSONL path listed in the manifest/summary

Required fields:

- `sessionRtt.trackedSessionCount`
- `sessionRtt.eligibleSessionCount`
- `sessionRtt.excludedLowSampleSessionCount`
- `sessionRtt.p50OfSessionP95Ms`
- `sessionRtt.p95OfSessionP95Ms`
- `sessionRtt.p99OfSessionP95Ms`
- `sessionRtt.maxSessionP95Ms`
- `sessionRtt.maxSessionP99Ms`
- `sessionRtt.maxSessionMaxMs`
- `sessionRtt.slowestSessions`

## 8. Interpretation Rules

| Observation | Interpretation | Next Feature Direction |
|-------------|----------------|------------------------|
| `p95OfSessionP95Ms` is close to global RTT P95 and slowest sessions are not extreme outliers | Most sessions are slow | Throughput/pacing/server processing decomposition |
| `maxSessionP95Ms` is much higher than `p95OfSessionP95Ms` and slowest Top N dominates | Tail is concentrated | Fairness/starvation/session backlog analysis |
| Slow sessions also show receive timeout/socket classification spikes | Tail may be socket/receive-path related | receive-timeout/socket-error correlation |
| Many sessions are excluded due to low samples | Per-session RTT is not yet reliable | Measurement reliability / run stability pass |

## 9. References

- `docs/load-validation-benchmark-results.md`
- `docs/staged-load-validation-test-guide.md`
- `docs/archive/2026-04/loadrunner-per-session-rtt-tail-telemetry/loadrunner-per-session-rtt-tail-telemetry.report.md`
- `docs/archive/2026-04/basesession-send-channel-queue-lock-reduction/basesession-send-channel-queue-lock-reduction.report.md`
- `artifacts/load-validation/s5-send-channel-queue-batch-pool-adaptive/summary.md`
