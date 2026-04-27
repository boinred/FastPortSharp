# fastport-loadrunner - Do Tracking

> Version: 1.0.0 | Date: 2026-04-27 | Status: Implemented
> Design: fastport-loadrunner.design.md

---

## 1. Implementation Summary

`FastPortLoadRunner`를 계획 출력용 CLI에서 실제 TCP 부하 테스트 실행기로 확장했다.

## 2. Completed Items

- [x] `LoadRunnerOptions`를 별도 파일로 분리
- [x] `LoadScenario` 모델 추가
- [x] `--metrics-interval` 옵션 추가
- [x] `--output` JSONL metrics 옵션 추가
- [x] `PayloadProfile` fixed/random parsing 구현
- [x] `PayloadGenerator` 구현
- [x] `LoadRunner` lifecycle 구현
- [x] session ramp-up 구현
- [x] `LoadSession` TCP connect/send/receive loop 구현
- [x] FastPort packet framing 구현
- [x] `EchoRequest`/`EchoResponse` protobuf 송수신 구현
- [x] client-side RTT 측정 구현
- [x] `MetricsCollector` 구현
- [x] console metrics reporter 구현
- [x] JSONL metrics reporter 구현
- [x] JSONL metrics camelCase 출력 적용
- [x] `FastPortLoadRunner/README.md` 갱신
- [x] CLI/payload/metrics 단위 테스트 추가
- [x] 10,000 세션 OS limit 가이드 추가

## 3. Implementation Notes

### 3.1 Packet Format

LoadRunner는 서버의 기존 `BaseSession.RequestSendMessage` 형식에 맞춰 패킷을 만든다.

```text
[2-byte total packet size][4-byte protocol id][protobuf message bytes]
```

`protocol id`는 `ProtocolId.Tests`를 사용한다.

### 3.2 Timing

RTT는 클라이언트에서 `Stopwatch.GetTimestamp()`로 송신 시각과 수신 시각을 측정한다. 서버가 echo response header에 `client_send_ts`를 보존한다는 전제를 사용한다.

### 3.3 Current Limitation

서버 측 telemetry는 아직 연결하지 않았다. 현재 accept/disconnect/socket error 값은 LoadRunner 클라이언트 관점의 연결/종료/예외 기반 값이다.

## 4. Verification

- [x] `dotnet build FastPortCharp.sln`
- [x] `dotnet test FastPortCharp.sln --no-build`
- [x] `dotnet run --no-build --project FastPortLoadRunner -- --help`
- [x] short no-server smoke run
- [ ] server echo smoke run

### 4.1 Verification Notes

The no-server smoke run completed and reported `errors=100%`, which is expected when no listener is available. A short `FastPortServer` smoke run was attempted, but the server process did not produce logs or accept the LoadRunner connection in this execution session. Server-side integration verification remains open.

## 5. Next Steps

1. 단위 테스트 또는 smoke test 자동화 추가
2. 서버를 띄운 상태에서 소규모 integration test 수행
3. 패킷/버퍼/세션 안정화 항목을 별도 Do 작업으로 진행
4. `$pdca analyze fastport-loadrunner`로 설계 대비 구현 gap 확인
