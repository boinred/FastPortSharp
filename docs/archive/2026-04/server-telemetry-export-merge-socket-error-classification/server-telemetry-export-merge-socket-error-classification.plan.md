# server-telemetry-export-merge-socket-error-classification - Plan Document

> Version: 1.0.0 | Date: 2026-04-29 | Status: Draft
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`server-telemetry-export-merge-socket-error-classification`는 10K 부하에서 발생한 disconnect/backlog 병목을 client-only 지표가 아니라 client/server combined timeline으로 분석할 수 있게 만드는 기능이다.

직전 `10k-load-bottleneck-telemetry` 결과는 다음을 보여줬다.

- connect attempts는 10,000까지 도달했다.
- connect failures는 0이었다.
- peak current sessions는 8,624 / 10,000에 그쳤다.
- final disconnect count는 1,782였다.
- max pending request count는 55,695까지 증가했다.
- RTT P95/P99는 43,268.80 ms / 44,895.97 ms까지 상승했다.
- client JSONL에는 `serverObserved`가 `null`이라 server send backlog와 client backlog를 같은 시간축에서 비교할 수 없었다.

이번 단계의 목적은 “왜 연결 이후 세션이 떨어지고 request backlog가 누적되는지”를 판단할 수 있도록 server telemetry export/merge와 socket error classification을 추가하는 것이다.

### 1.2 PM Framing

사용자가 다음 10K 실행 후 바로 판단해야 하는 질문은 세 가지다.

1. client pending request 증가는 server send path backlog와 같은 시간대에 발생하는가?
2. disconnect/socket error는 connect, send, receive, protocol parse 중 어느 단계에서 발생하는가?
3. 10K 실패가 서버 send path, client read/write path, 또는 로컬 OS/socket resource pressure 중 어디에 가까운가?

이 feature는 튜닝 자체가 아니라 위 질문에 답할 수 있는 관측/분류 데이터를 만드는 PM 관점의 분석 기반 작업이다.

## 2. Goals

### 2.1 Primary Goals

- [ ] `FastPortSmokeServer`의 server observed telemetry를 JSONL로 export할 수 있게 한다.
- [ ] `FastPortLoadValidation`이 client metrics JSONL과 server metrics JSONL을 같은 stage summary에서 같이 다룰 수 있게 한다.
- [ ] combined observed JSONL 또는 merge summary에서 `clientObserved`와 `serverObserved`를 같은 timestamp 근처에서 비교할 수 있게 한다.
- [ ] client socket errors를 최소한 phase/type 단위로 분류한다.
- [ ] 10K focused run summary에 server send backlog, send backpressure, pending request, classified socket error count를 같이 표시한다.
- [ ] 다음 10K 실행에서 disconnect 원인 후보를 server send path vs client socket path로 좁힐 수 있게 한다.

### 2.2 Non-Goals

- 이번 단계에서 성능 최적화를 직접 적용하지 않는다.
- 이번 단계에서 MAUI dashboard를 만들지 않는다.
- 이번 단계에서 multi-machine distributed load generation을 만들지 않는다.
- 이번 단계에서 OS/kernel tuning 자동화를 만들지 않는다.
- 이번 단계에서 RTT hard fail threshold를 확정하지 않는다.
- 이번 단계에서 server process lifecycle을 `FastPortLoadValidation`이 완전히 관리하도록 바꾸는 것은 필수 목표가 아니다.

## 3. Scope

### 3.1 In Scope

- Server telemetry export:
  - `FastPortSmokeServer`가 주기적으로 `ObservedMetricsSnapshot.FromServer(...)` JSONL을 쓸 수 있는 옵션
  - export interval 설정
  - output path 설정
  - graceful shutdown 시 writer flush
- Metrics merge:
  - `FastPortLoadValidation`에서 optional server metrics path를 읽는 경로
  - client/server samples를 timestamp 기준 nearest-neighbor 또는 bounded tolerance로 매칭
  - combined JSONL 또는 merged summary 생성
- Server summary fields:
  - max pending send requests
  - max send buffer bytes
  - max/send backpressure events
  - send requests per second
  - send completions per second
- Socket error classification:
  - connect phase errors
  - send/write phase errors
  - receive/read phase errors
  - protocol/parse errors
  - cancellation vs actual socket/IO errors 분리
  - exception type and socket error code counters
- Validation/report:
  - 10K focused run에서 classified error counts와 server bottleneck fields를 summary에 표시
  - 기존 client-only JSONL과 summary compatibility 유지
- Tests:
  - server JSONL export serialization
  - merge reader/evaluator
  - old client-only metrics compatibility
  - socket error classifier unit tests

### 3.2 Out of Scope

- Full observability stack 연동
- Prometheus/OpenTelemetry exporter
- Web dashboard/API endpoint
- automatic server launch and teardown as the only supported workflow
- server-side per-session full registry redesign
- payload/protocol format change

## 4. Success Criteria

- [ ] `FastPortSmokeServer`가 server observed JSONL을 주기적으로 쓸 수 있다.
- [ ] server JSONL sample에 `pendingSendRequests`, `maxPendingSendRequests`, `sendBackpressureEvents`, `sendBufferBytes`, `maxSendBufferBytes`가 포함된다.
- [ ] `FastPortLoadValidation` summary가 client-only metrics만 있어도 기존처럼 동작한다.
- [ ] `FastPortLoadValidation` summary가 server metrics가 있을 때 merged bottleneck fields를 포함한다.
- [ ] client socket errors가 phase/type/code별 counter로 집계된다.
- [ ] 10K focused validation 산출물에서 client pending request와 server pending send/backpressure를 같은 stage 기준으로 비교할 수 있다.
- [ ] `dotnet build FastPortCharp.sln` 통과
- [ ] `dotnet test FastPortCharp.sln --no-build` 통과
- [ ] focused smoke 또는 reduced run으로 server export/merge path가 검증된다.

## 5. Measurement Plan

### 5.1 Baseline Reference

기존 focused run:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-logging-off
```

주요 결과:

| Metric | Value |
|--------|------:|
| Peak current sessions | 8,624 / 10,000 |
| Peak session ratio | 86.24% |
| Final disconnect count | 1,782 |
| Max socket error rate | 0.19% |
| Max pending request count | 55,695 |
| Max scheduler drift | 28.21 ms |
| Max RTT P95 | 43,268.80 ms |
| Max RTT P99 | 44,895.97 ms |
| Connect attempts | 10,000 |
| Connect failures | 0 |

### 5.2 Target Re-Test

server metrics export를 켠 상태로 같은 focused stage를 재실행한다.

```bash
./FastPortSmokeServer/bin/Release/net10.0/FastPortSmokeServer \
  --Telemetry:Output artifacts/load-validation/s5-server-merged/server.metrics.jsonl

./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-server-merged \
  --server-metrics artifacts/load-validation/s5-server-merged/server.metrics.jsonl
```

위 CLI는 계획 단계의 target shape이며, 최종 이름과 옵션 구조는 design phase에서 확정한다.

## 6. Schedule

| Phase | Target Date | Status |
|-------|-------------|--------|
| Plan | 2026-04-29 | In Progress |
| Design | 2026-04-29 | Pending |
| Implementation | TBD | Pending |
| Analyze | TBD | Pending |
| Report | TBD | Pending |

## 7. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| server JSONL export가 hot path에 영향을 줌 | High | Medium | telemetry snapshot writer는 background interval 기반으로 두고 session hot path에서는 기존 counter만 유지 |
| client/server timestamp merge가 부정확함 | Medium | Medium | tolerance window와 unmatched sample count를 summary에 기록 |
| socket error 분류가 플랫폼별로 다르게 나옴 | Medium | Medium | exception type, socket error code, phase를 모두 보존하고 display name은 보조 정보로 둠 |
| summary table이 너무 넓어짐 | Low | High | Markdown에는 핵심 max fields만 표시하고 JSON에는 상세 counters 포함 |
| high-load run이 로컬 머신 상태에 민감함 | Medium | High | smoke/reduced run으로 contract 검증, 10K는 manual performance validation으로 분리 |
| server process 수동 실행 workflow가 불편함 | Medium | Medium | 이번 단계는 optional path로 두고, 후속 feature에서 managed server lifecycle 검토 |

## 8. Architecture Considerations

- `LibNetworks`에는 protocol-neutral telemetry counter만 유지한다.
- server JSONL export는 `FastPortSmokeServer` application layer에서 담당한다.
- client socket error classification은 `FastPortLoadRunner`에 둔다.
- merge/evaluation은 `FastPortLoadValidation`에 둔다.
- observed JSONL envelope는 기존 `ObservedMetricsSnapshot`을 재사용한다.
- old client-only output은 계속 유효해야 한다.

## 9. Open Questions

- server metrics export path를 `FastPortSmokeServer` CLI/config로 둘지, `FastPortLoadValidation`이 server process를 관리하며 자동 주입할지?
- merged artifact를 별도 `combined.metrics.jsonl`로 쓸지, summary JSON에만 반영할지?
- socket error classification을 enum DTO로 둘지, string key dictionary로 둘지?
- 10K run에서 server metrics interval은 client metrics interval과 동일하게 1초로 둘지?

## 10. References

- `docs/archive/2026-04/10k-load-bottleneck-telemetry/10k-load-bottleneck-telemetry.report.md`
- `docs/archive/2026-04/10k-load-bottleneck-telemetry/10k-load-bottleneck-telemetry.analysis.md`
- `artifacts/load-validation/s5-logging-off/summary.md`
- `artifacts/load-validation/s5-logging-off/summary.json`
- `LibNetworks/Telemetry/ObservedMetrics.cs`
- `LibNetworks/Telemetry/ServerTelemetry.cs`
- `FastPortLoadRunner/Metrics.cs`
- `FastPortLoadValidation/LoadValidationSummaryWriter.cs`
