# fastport-smoke-server - Plan Document

> Version: 1.0.0 | Date: 2026-04-27 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`fastport-smoke-server`는 기본 네트워크 엔진 역할의 `FastPortServer`와 분리된 테스트용 서버를 추가해, 실제 LoadRunner 연결을 처리할 때 서버 관점의 session/packet/socket 상태를 수집하고 자동 integration smoke test로 검증하기 위한 기능이다.

이 기능은 `FastPortLoadRunner` foundation 이후 단계다. LoadRunner는 client-observed metrics를 만들 수 있게 되었지만, 아직 서버가 accept/disconnect/socket error/packet 흐름을 구조적으로 노출하지 않는다. 따라서 MAUI dashboard와 10,000 session staged load validation 전에 서버 관측 지점과 smoke 자동화를 먼저 만든다.

핵심 방향은 `LibNetworks`에 generic telemetry primitive를 두고, echo/protocol/telemetry smoke 구현은 `FastPortSmokeServer` 프로젝트에 둔다. `FastPortServer`는 바로 사용할 수 있는 기본 네트워크 서버 샘플로 유지한다.

### 1.2 Background

이전 PDCA `fastport-loadrunner`의 완료 보고서에서 후속 항목으로 아래 세 가지가 남았다.

- `FastPortServer` echo smoke 자동화
- 서버 측 accept/disconnect/socket error telemetry
- 1k/3k/5k/10k staged load validation

현재 관련 코드 경계는 다음과 같다.

| Area | Current Code | Notes |
|------|--------------|-------|
| Server startup | `FastPortServer/FastPortServerBackgroundService.cs` | 기본 서버 host/port 구성 |
| Accept path | `LibNetworks/BaseListener.cs` | accept success/error 지점 존재 |
| Session lifecycle | `LibNetworks/Sessions/BaseSession.cs` | receive/send/disconnect/socket error 지점 존재 |
| Echo smoke logic | `FastPortSmokeServer/Sessions/FastPortSmokeClientSession.cs` | `EchoRequest` 수신 후 `EchoResponse` 송신 |
| Client metrics | `FastPortLoadRunner/Metrics.cs` | client-observed metrics only |

## 2. Goals

### 2.1 Primary Goals

- [x] 서버 관점 telemetry 수집 모델을 정의한다.
- [x] accept success/failure, disconnect, connected session count를 thread-safe counter로 수집한다.
- [x] server-observed send/recv bytes 및 packets를 수집한다.
- [x] socket error count와 error rate를 수집한다.
- [x] packet parse failure 또는 protocol mismatch 같은 smoke failure 원인을 관측 가능하게 만든다.
- [x] `FastPortSmokeServer`를 test 환경에서 deterministic하게 시작/종료할 수 있는 경계를 만든다.
- [x] LoadRunner 또는 test client를 이용해 small-session echo smoke test를 자동화한다.
- [x] fixed 1K payload와 random 4K~16K payload smoke scenario를 최소 범위로 검증한다.
- [x] smoke 결과가 client metrics와 server telemetry를 함께 검증하도록 만든다.
- [x] `FastPortServer`에서 smoke protocol 책임을 제거하고 기본 서버 경계를 유지한다.

### 2.2 Non-Goals

- 이번 단계에서 MAUI dashboard UI를 만들지 않는다.
- 이번 단계에서 10,000 session 실측을 완료하지 않는다.
- 이번 단계에서 distributed load runner를 만들지 않는다.
- 이번 단계에서 게임별 protocol/template 구조화를 완료하지 않는다.
- 이번 단계에서 production-grade metrics backend, database, OpenTelemetry exporter를 붙이지 않는다.

## 3. Scope

### 3.1 In Scope

- `LibNetworks` 또는 server 공용 영역에 telemetry counter/snapshot 구조 추가
- `BaseListener` accept success/error instrumentation
- `BaseSession` send/receive/disconnect/socket error instrumentation
- `FastPortSmokeServer` 프로젝트 추가 및 telemetry collector DI 연결
- `FastPortServer`는 기본 server host/session 경계로 단순화
- 테스트가 사용할 수 있는 server startup/ready/stop helper
- integration smoke test 추가
  - small fixed payload echo smoke
  - random 4K~16K payload smoke
- smoke test에서 아래 조건 검증
  - server accepts > 0
  - client receives > 0
  - server receive/send packets > 0
  - connected sessions returns to 0 after stop/disconnect
  - socket errors remain within expected threshold
- LoadRunner JSONL 또는 console output과 server telemetry field 이름 정렬

### 3.2 Out of Scope

- dashboard 화면 구현
- 장시간 soak test
- 1,000 이상 load validation
- OS limit tuning 자동화
- 외부 metrics 시스템 연동

## 4. Work Plan

| Step | Task | Notes |
|------|------|-------|
| 1 | telemetry model 설계 | `ServerTelemetrySnapshot`, counter naming, reset/snapshot policy |
| 2 | collector 구현 위치 결정 | `LibNetworks` 공용 collector 우선 검토 |
| 3 | accept path instrumentation | `BaseListener.OnSocketEventsAcceptCompleted` success/error |
| 4 | session lifecycle instrumentation | `BaseSession.RequestDisconnect`, receive/send completed path |
| 5 | FastPortSmokeServer DI wiring | collector singleton 연결, test에서 조회 가능하게 구성 |
| 6 | server test harness 설계 | dynamic port, ready check, graceful stop |
| 7 | integration smoke test 구현 | fixed payload 및 random payload 시나리오 |
| 8 | LoadRunner/server metric contract 정렬 | MAUI dashboard에서 읽을 이름과 의미 정리 |
| 9 | 검증 및 분석 | build/test, smoke reliability, gap analysis |

## 5. Success Criteria

- [x] `dotnet build FastPortCharp.sln`이 경고/오류 없이 통과한다.
- [x] `dotnet test FastPortCharp.sln --no-build`가 통과한다.
- [x] smoke test가 별도 수동 서버 실행 없이 `FastPortSmokeServer`를 시작하고 종료한다.
- [x] fixed payload smoke가 성공한다.
  - 예: 10 sessions, `fixed:1024`, short duration
- [x] random large payload smoke가 성공한다.
  - 예: 2~10 sessions, `random:4096-16384`, short duration
- [x] server telemetry snapshot에 아래 값이 포함된다.
  - connected sessions
  - accepted sessions
  - disconnected sessions
  - received packets/bytes
  - sent packets/bytes
  - socket errors
  - parse/protocol errors
- [x] smoke test가 client-observed metrics와 server-observed telemetry를 함께 assert한다.
- [x] 기존 `FastPortLoadRunner` 단위 테스트가 계속 통과한다.

## 6. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| 서버 startup이 고정 포트 `6628`에 묶여 테스트 충돌 발생 | Medium | High | test harness에서 dynamic/free port injection 가능하게 설계 |
| telemetry instrumentation이 hot path 성능을 왜곡 | Medium | Medium | `Interlocked` counter 중심, per-packet allocation/logging 금지 |
| BaseSession send/receive 경로의 byte/packet 의미가 client metrics와 다름 | Medium | Medium | `serverObserved*`, `clientObserved*` 명명으로 의미 구분 |
| integration smoke가 timing 의존으로 flaky해짐 | High | Medium | ready check, timeout, retry 정책 명시 |
| 4K~16K payload가 기존 8K receive buffer와 충돌 | High | Medium | random large payload smoke를 별도 case로 분리하고 실패 원인을 telemetry로 노출 |
| telemetry collector가 engine code와 game logic을 결합 | Medium | Low | `LibNetworks` primitive는 generic하게 두고 echo/protocol smoke는 `FastPortSmokeServer`로 분리 |

## 7. Architecture Considerations

- telemetry collector는 game-specific protocol을 모르면 안 된다.
- packet parse/protocol error는 smoke server session implementation에서 기록하되 counter name은 공통 dashboard가 이해할 수 있어야 한다.
- server test harness는 future game server template에서도 재사용 가능해야 한다.
- `FastPortServer`는 새 게임 서버가 참조할 수 있는 최소 엔진 사용 예제로 남긴다.
- LoadRunner metrics와 server telemetry는 같은 JSON naming convention(camelCase)을 유지한다.
- 다음 MAUI dashboard는 이 feature의 snapshot contract를 기반으로 설계한다.

## 8. References

- `docs/archive/2026-04/fastport-loadrunner/fastport-loadrunner.report.md`
- `FastPortLoadRunner/Metrics.cs`
- `FastPortLoadRunner/LoadSession.cs`
- `FastPortServer/FastPortServerBackgroundService.cs`
- `FastPortSmokeServer/Sessions/FastPortSmokeClientSession.cs`
- `LibNetworks/BaseListener.cs`
- `LibNetworks/Sessions/BaseSession.cs`
- `docs/loadrunner-os-limits.md`
