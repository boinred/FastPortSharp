# fastport-smoke-server - Do Tracking

> Version: 1.0.0 | Date: 2026-04-27 | Status: Implemented
> Design: docs/02-design/features/fastport-smoke-server.design.md

---

## 1. Implementation Summary

`fastport-smoke-server` 설계에 따라 전용 smoke server, 서버 관점 telemetry collector, integration smoke 자동화를 구현했다.

핵심 구현은 `LibNetworks`에 generic telemetry primitive를 추가하고, `FastPortSmokeServer`가 이를 DI로 연결한 뒤, `LibCommonTest`에서 dynamic port로 smoke server를 시작해 LoadRunner 기반 smoke를 수행하는 구조다. `FastPortServer`는 echo/protocol/telemetry smoke 책임을 제거해 기본 네트워크 서버 샘플로 남겼다.

## 2. Completed Items

- [x] `IServerTelemetry` interface 추가
- [x] `ServerTelemetryCollector` 구현
- [x] `ServerTelemetrySnapshot` 출력 모델 구현
- [x] `NullServerTelemetry` no-op 구현 추가
- [x] `BaseListener` accept success/error instrumentation 추가
- [x] `BaseSession` disconnect/send/receive/socket error instrumentation 추가
- [x] `FastPortSmokeServer` 프로젝트 추가
- [x] `FastPortSmokeServerOptions` 추가
- [x] `FastPortSmokeServerBackgroundService` host/port 구성 추가
- [x] `FastPortSmokeServer` DI에 `IServerTelemetry` 연결
- [x] `FastPortSmokeClientSessionFactory`에서 telemetry를 session으로 전달
- [x] `FastPortSmokeClientSession` parse/protocol error instrumentation 추가
- [x] `FastPortServer`는 기본 session host로 단순화
- [x] `LibCommonTest`에 `FastPortSmokeServer` 및 `LibNetworks` project reference 추가
- [x] telemetry collector unit tests 추가
- [x] dynamic port 기반 `FastPortSmokeServerTestHost` 추가
- [x] fixed 1K payload integration smoke test 추가
- [x] random 4K~16K payload integration smoke test 추가

## 3. Implementation Notes

### 3.1 Telemetry Location

Telemetry primitive는 `LibNetworks.Telemetry` namespace에 위치한다.

```text
LibNetworks/Telemetry/ServerTelemetry.cs
```

이 위치를 선택한 이유는 accept/session/socket 계측 지점이 `LibNetworks`에 있고, 향후 game server template에서도 protocol과 무관하게 재사용해야 하기 때문이다.

### 3.2 Counter Semantics

- `acceptedSessions`: listener accept success count
- `disconnectedSessions`: first `RequestDisconnect` transition count
- `connectedSessions`: `max(0, acceptedSessions - disconnectedSessions)`
- `receivedPackets`: parsed FastPort packet count
- `sentPackets`: socket send completion count
- `receivedBytes`: parsed packet size sum
- `sentBytes`: socket send completion byte sum
- `acceptErrors`: accept error/null socket/startup failure
- `socketErrors`: socket-level error paths
- `parseErrors`: protocol payload parse failure
- `protocolErrors`: unexpected protocol id

### 3.3 Test Harness

`FastPortSmokeServerTests`는 test process 안에서 `IHost`를 직접 구성한다.

- free TCP port 탐색
- `FastPortSmokeServerOptions { Host = "127.0.0.1", Port = freePort }` 주입
- `ServerTelemetryCollector` singleton 주입
- ready probe 후 `Telemetry.Reset()`
- LoadRunner internal engine으로 smoke scenario 실행
- client metrics와 server telemetry를 함께 assert

Ready probe 자체가 accept/disconnect를 만들 수 있으므로 probe 후 reset을 수행한다.

## 4. Verification

- [x] `dotnet build FastPortCharp.sln`
  - Result: success
  - Warnings: 0
  - Errors: 0

- [x] `dotnet test FastPortCharp.sln --no-build`
  - Result: success
  - Passed: 56
  - Failed: 0

## 5. New Tests

### 5.1 Unit Tests

- `ServerTelemetryCollector_CreateSnapshot_ReturnsDerivedConnectedSessions`
- `ServerTelemetryCollector_Reset_ClearsCounters`
- `ServerTelemetryCollector_SocketErrorRate_UsesPacketsAndErrors`
- `FastPortSmokeServerOptions_Defaults_ToProductionAddress`

### 5.2 Integration Smoke Tests

- `FastPortSmokeServer_FixedPayload_EchoesAndRecordsTelemetry`
- `FastPortSmokeServer_RandomLargePayload_EchoesAndRecordsTelemetry`

## 6. Current Limitations

- `sentPackets`는 실제 packet count라기보다 socket send completion count다. 현재 smoke와 dashboard 초안에는 충분하지만, 정확한 packet counter가 필요하면 send buffer에 들어가는 packet 수를 별도로 기록해야 한다.
- `receivedBytes`는 parsed packet size 기준이다. raw socket receive bytes와 구분이 필요하면 별도 counter가 필요하다.
- `FastPortSmokeServerTestHost`는 test project 내부 helper다. 다른 테스트 프로젝트에서 재사용하려면 별도 test utility로 분리할 수 있다.
- 1,000/3,000/5,000/10,000 staged load validation은 아직 수행하지 않았다.

## 7. Next Steps

1. `$pdca analyze fastport-smoke-server`로 설계 대비 구현 gap을 확인한다.
2. 필요하면 `sentPackets` 의미를 더 정확히 하기 위해 packet write counter를 분리한다.
3. 이후 MAUI dashboard 설계에서 server/client metric naming을 확정한다.
