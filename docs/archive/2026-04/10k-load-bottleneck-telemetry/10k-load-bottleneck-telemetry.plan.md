# 10k-load-bottleneck-telemetry - Plan Document

> Version: 1.0.0 | Date: 2026-04-28 | Status: Draft
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`10k-load-bottleneck-telemetry`는 10,000 concurrent session 부하에서 발생한 RTT 급증과 peak session 미달 원인을 찾기 위한 관측 지표를 보강하는 기능이다.

직전 staged load validation은 전체 pass였지만, `s5-random-10k` 단계에서 target 10,000 중 peak 9,767까지만 도달했고 RTT P95/P99가 초 단위로 상승했다. 이 단계의 목적은 엔진을 바로 튜닝하기 전에 병목 위치를 특정할 수 있는 telemetry와 재측정 절차를 확보하는 것이다.

### 1.2 Background

현재 검증 결과는 다음과 같다.

| Stage | Payload | Target | Peak | Peak Ratio | Max TPS | Socket Error | Result |
|-------|---------|-------:|-----:|-----------:|--------:|-------------:|--------|
| `s1-fixed-1k` | fixed 8K | 1,000 | 1,000 | 100.00% | 1,024.69 | 0.00% | Passed |
| `s2-random-1k` | random 4K-16K | 1,000 | 1,000 | 100.00% | 1,026.66 | 0.00% | Passed |
| `s3-random-3k` | random 4K-16K | 3,000 | 3,000 | 100.00% | 3,095.41 | 0.00% | Passed |
| `s4-random-5k` | random 4K-16K | 5,000 | 5,000 | 100.00% | 5,850.43 | 0.00% | Passed |
| `s5-random-10k` | random 4K-16K | 10,000 | 9,767 | 97.67% | 8,785.21 | 0.03% | Passed |

10K 세부 지표:

| Metric | Value |
|--------|------:|
| Target sessions | 10,000 |
| Peak current sessions | 9,767 |
| Final disconnect count | 233 |
| Total sent packets | 1,351,275 |
| Total received packets | 1,349,391 |
| Max sent bytes/sec | 82.41 MB/s |
| Max received bytes/sec | 90.15 MB/s |
| Max RTT P95 | 8,738.94 ms |
| Max RTT P99 | 10,137.04 ms |
| JSON samples | 421 |

해석:

- 10K stage는 현재 pass 기준은 만족했다.
- 하지만 5K까지는 target session 100% 도달, 10K에서만 233 session이 peak에 도달하지 못했다.
- RTT가 8-10초까지 증가했으므로 단순 pass/fail보다 병목 위치 분석이 필요하다.
- 현재 telemetry는 결과 지표는 보여주지만 queue/backpressure/worker delay 같은 원인 지표가 부족하다.

## 2. Goals

### 2.1 Primary Goals

- [ ] 10K stage에서 peak session이 10,000에 도달하지 못하는 원인 후보를 관측 가능하게 만든다.
- [ ] RTT P95/P99 급증이 client-side scheduling, server receive/send path, send queue, logging, socket backlog 중 어디에서 발생하는지 좁힌다.
- [ ] 10K 재측정 시 비교 가능한 baseline report를 생성한다.
- [ ] MAUI dashboard 전 단계로 사용할 수 있는 bottleneck-focused telemetry contract를 정의한다.

### 2.2 Non-Goals

- 이번 단계에서 MAUI dashboard UI를 구현하지 않는다.
- 이번 단계에서 game server template 구조화를 진행하지 않는다.
- 아직 RTT hard fail threshold를 최종 확정하지 않는다.
- 무작정 성능 최적화를 먼저 적용하지 않는다. 원인 지표를 먼저 확보한다.
- `FastPortServer`에 smoke/load-test 전용 protocol을 넣지 않는다.

## 3. Scope

### 3.1 In Scope

- 10K stage 결과 분석 문서화
- 부하 병목 원인 가설 정리
- 추가 telemetry 설계
- server-side 관측 지표 후보:
  - current sessions
  - accept rate / accept error rate
  - disconnect rate
  - socket error rate
  - receive packet/byte rate
  - send completion rate
  - pending send count
  - send queue depth
  - max send queue depth
  - packet parse backlog or receive loop delay
- client-side 관측 지표 후보:
  - connect attempt count
  - connect success/failure count
  - active session count
  - pending request count
  - RTT histogram window
  - scheduler delay or timer drift
- logging off 조건 재측정 계획
- 10K 단일 stage 재실행 절차
- 결과 비교 기준 정의

### 3.2 Out of Scope

- production monitoring system 연동
- distributed multi-machine load generation
- HTTP/WebSocket dashboard endpoint 구현
- MAUI dashboard 화면 구현
- OS kernel tuning 자동화
- protocol payload format 변경

## 4. Hypotheses

| Hypothesis | Signal Needed | Why It Matters |
|------------|---------------|----------------|
| Server console logging이 hot path를 막는다 | logging off 전후 RTT/peak 비교 | 10K 테스트에서 server accept/disconnect 로그가 매우 많았다. |
| Send path backpressure가 누적된다 | pending send count, send queue depth | RTT가 초 단위로 증가하면 response write가 밀렸을 가능성이 있다. |
| Client scheduler/timer가 10K에서 밀린다 | client timer drift, pending request count | RTT 계산은 client send/receive timestamp 기준이다. |
| Accept/connect ramp-up이 10K에서 부족하다 | connect failures, accept rate, accept backlog | peak 9,767은 233 session이 target에 못 닿은 상태다. |
| Socket resource pressure가 발생한다 | socket error rate, disconnect reason | socket error는 낮지만 0은 아니며 disconnect 233이 있었다. |
| Large random payload가 buffer pressure를 만든다 | receive loop delay, bytes/sec, packet backlog | 4K-16K random payload는 8K receive buffer와 reassembly 경로를 압박한다. |

## 5. Success Criteria

- [ ] 10K 병목 분석에 필요한 추가 metric 목록이 설계 문서에서 확정된다.
- [ ] 최소 하나 이상의 server-side bottleneck metric이 observed JSONL 또는 summary에 포함된다.
- [ ] 최소 하나 이상의 client-side bottleneck metric이 observed JSONL 또는 summary에 포함된다.
- [ ] logging off 조건의 10K 재측정 결과가 기존 결과와 비교된다.
- [ ] 10K 재측정 report에 다음 값이 포함된다.
  - peak current sessions
  - peak session ratio
  - final disconnect count
  - max socket error rate
  - max TPS
  - RTT P95/P99
  - pending send / queue 관련 지표
- [ ] 기존 smoke/staged validation command가 깨지지 않는다.
- [ ] `dotnet build FastPortCharp.sln` 통과
- [ ] `dotnet test FastPortCharp.sln --no-build` 통과

## 6. Schedule

| Phase | Target Date | Status |
|-------|-------------|--------|
| Plan | 2026-04-28 | In Progress |
| Design | 2026-04-28 | Pending |
| Implementation | TBD | Pending |
| Analyze | TBD | Pending |
| Report | TBD | Pending |

## 7. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| 추가 telemetry가 hot path 성능을 더 악화시킴 | High | Medium | `Interlocked` counter 중심, per-packet allocation/logging 금지 |
| 병목이 로컬 머신/OS 한계라 코드 지표만으로 불충분 | Medium | Medium | `ulimit`, CPU, process scheduling, logging on/off 비교를 같이 기록 |
| 10K 재측정 시간이 길어 반복 비용이 큼 | Medium | High | 단일 `s5-random-10k` stage 재실행과 smoke regression을 분리 |
| metric 이름이 dashboard contract와 충돌 | Medium | Low | `serverObserved*`, `clientObserved*`, `bottleneck*` 의미를 명확히 분리 |
| RTT threshold를 성급히 고정함 | Medium | Medium | 이번 단계에서는 threshold보다 원인 지표와 baseline 비교를 우선 |

## 8. Architecture Considerations

- `LibNetworks`에는 protocol-neutral telemetry primitive만 둔다.
- `FastPortSmokeServer`는 smoke protocol과 server telemetry wiring을 담당한다.
- `FastPortLoadRunner`는 client-observed bottleneck metric을 JSONL에 포함할 수 있다.
- `FastPortLoadValidation`은 기존 pass/fail 기준을 유지하되, 새 bottleneck metric을 summary에 표시한다.
- generated artifacts는 계속 `artifacts/load-validation/` 아래에 두고 git에 포함하지 않는다.

## 9. Measurement Plan

### 9.1 Baseline

기준 결과:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --output artifacts/load-validation/staged-local \
  --continue-on-failure
```

기준 10K 결과:

- peak sessions: 9,767 / 10,000
- peak ratio: 97.67%
- max TPS: 8,785.21
- socket error rate: 0.03%
- disconnect count: 233
- RTT P95/P99: 8,738.94 ms / 10,137.04 ms

### 9.2 First Re-Test Candidate

logging을 줄인 조건에서 10K 단일 stage를 재측정한다.

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-logging-off
```

### 9.3 Comparison

비교 항목:

- peak session ratio가 97.67%보다 개선되는가
- disconnect count가 233보다 감소하는가
- RTT P95/P99가 초 단위에서 내려오는가
- socket error rate가 0.03%보다 낮아지는가
- TPS가 8,785보다 개선되는가

## 10. References

- `artifacts/load-validation/staged-local/summary.md`
- `artifacts/load-validation/staged-local/summary.json`
- `docs/staged-load-validation-test-guide.md`
- `docs/archive/2026-04/staged-load-validation/staged-load-validation.report.md`
- `docs/archive/2026-04/telemetry-export-metric-contract/telemetry-export-metric-contract.report.md`
- `docs/archive/2026-04/fastport-smoke-server/fastport-smoke-server.report.md`
- Confluence: `https://novanexus.atlassian.net/wiki/x/EwBJ`
