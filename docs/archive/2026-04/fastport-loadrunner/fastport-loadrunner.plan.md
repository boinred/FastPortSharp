# fastport-loadrunner - Plan Document

> Version: 1.0.0 | Date: 2026-04-27 | Status: Completed
> Level: Starter

---

## 1. Overview

### 1.1 Purpose

`FastPortLoadRunner`는 `FastPortServer`에 실제 TCP 세션을 대량으로 연결하고, 게임 서버 엔진으로 사용할 때 필요한 처리량과 안정성을 검증하기 위한 부하 테스트 실행기다.

### 1.2 Background

기존 `FastPortBenchmark`는 `BenchmarkDotNet` 기반 micro benchmark 용도였다. 앞으로는 컴포넌트 단위 성능보다 실제 서버에 1,000~10,000개 이상 세션을 붙이고, 4K~16K 랜덤 메시지 또는 8K 고정 메시지를 보내는 실사용 부하 검증이 더 중요하다.

## 2. Goals

### 2.1 Primary Goals

- [ ] `FastPortLoadRunner`를 실제 TCP 부하 테스트 콘솔 앱으로 구현한다.
- [ ] 세션 수, payload 크기, send rate, ramp-up, duration을 CLI 옵션으로 제어한다.
- [ ] 4K~16K 랜덤 payload와 8K 고정 payload 시나리오를 지원한다.
- [ ] TPS, RTT, latency, send/recv bytes, packets/sec, socket error를 측정한다.
- [ ] 향후 MAUI dashboard가 읽을 수 있는 telemetry 출력 기반을 만든다.

### 2.2 Non-Goals

- 이번 단계에서 MAUI dashboard까지 만들지는 않는다.
- 이번 단계에서 게임 서버 템플릿 구조까지 완성하지는 않는다.
- 기존 `BenchmarkDotNet` micro benchmark를 유지하지 않는다.

## 3. Scope

### 3.1 In Scope

- 기존 `FastPortBenchmark` 제거 및 `FastPortLoadRunner` 프로젝트 전환
- LoadRunner CLI 옵션 정의
- 다중 TCP 세션 생성과 ramp-up
- payload 생성기 구현
- 송신/수신 루프 구현
- 기본 telemetry 수집과 콘솔 출력
- 서버 안정화를 위한 패킷/버퍼/세션 문제 정리

### 3.2 Out of Scope

- 시각화 UI
- 분산 부하 생성
- CI 기반 장시간 부하 테스트
- 게임별 프로토콜/로직 템플릿 완성

## 4. Work Plan

| Step | Task | Notes |
|------|------|-------|
| 1 | 패킷/버퍼/세션 안정화 | length header, partial packet, send loop, disconnect 정리 |
| 2 | `FastPortLoadRunner` 프로젝트 정리 | 기존 micro benchmark 제거, 부하 테스트 앱으로 전환 |
| 3 | LoadRunner 실행 엔진 구현 | 세션 생성, ramp-up, duration, cancellation |
| 4 | payload 시나리오 구현 | `fixed:8192`, `random:4096-16384` |
| 5 | telemetry 수집 | TPS, RTT, latency, bytes/sec, packets/sec, CCU, error rate |
| 6 | MAUI dashboard 준비 | telemetry 출력 형식과 dashboard 연동 경계 정의 |
| 7 | game server template 구조화 | 엔진과 게임 로직 교체 지점 분리 |

## 5. Success Criteria

- [ ] `dotnet build FastPortCharp.sln`이 경고/오류 없이 통과한다.
- [ ] `dotnet test FastPortCharp.sln --no-build`가 통과한다.
- [ ] `FastPortLoadRunner --help`가 부하 테스트 옵션을 보여준다.
- [ ] `--sessions`, `--payload`, `--rate`, `--ramp-up`, `--duration` 옵션이 동작한다.
- [ ] 최소 1,000 세션 부하 테스트를 로컬에서 재현할 수 있다.
- [ ] 10,000 세션 테스트를 위한 OS/환경 제약과 실행 가이드가 문서화된다.

## 6. Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| 단일 머신에서 10,000 세션 생성 한계 | High | High | ramp-up, 다중 프로세스, OS limit 문서화 |
| 송신 루프 중복 `SendAsync` 문제 | High | Medium | 세션 안정화 단계에서 먼저 수정 |
| 큰 payload의 partial packet 처리 실패 | High | Medium | 4K~16K 테스트 케이스 추가 |
| telemetry 비용이 성능을 왜곡 | Medium | Medium | sampling/aggregation 방식 사용 |

## 7. References

- `FastPortLoadRunner/README.md`
- `docs/baseline-benchmark-results.md`
- `docs/latency-performance-report-after-channel.md`
