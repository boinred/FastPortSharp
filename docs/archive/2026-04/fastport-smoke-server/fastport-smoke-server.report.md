# fastport-smoke-server - Completion Report

> Version: 1.0.0 | Date: 2026-04-27 | Status: Completed
> Match Rate: 95%

---

## 1. Summary

`fastport-smoke-server`는 FastPortSharp의 부하 테스트 흐름에서 서버 관측 지표와 integration smoke 자동화를 담당하는 단계다.

이번 작업에서는 `LibNetworks`에 generic telemetry primitive를 추가하고, echo/protocol/telemetry smoke 책임을 새 `FastPortSmokeServer` 프로젝트로 분리했다. `FastPortServer`는 기본 네트워크 엔진 host/sample 역할로 되돌려, 앞으로 다른 게임 서버가 엔진만 가져다 쓰는 구조를 해치지 않도록 정리했다.

## 2. Related Documents

- Plan: `docs/01-plan/features/fastport-smoke-server.plan.md`
- Design: `docs/02-design/features/fastport-smoke-server.design.md`
- Do: `docs/02-design/features/fastport-smoke-server.do.md`
- Analysis: `docs/03-analysis/fastport-smoke-server.analysis.md`

## 3. Completed Items

- `LibNetworks.Telemetry.IServerTelemetry` 추가
- `ServerTelemetryCollector`, `ServerTelemetrySnapshot`, `NullServerTelemetry` 추가
- `BaseListener` accept success/error/socket error instrumentation 추가
- `BaseSession` disconnect, receive, send, socket error instrumentation 추가
- `BaseMessageListener`, `BaseSessionClient`, `BaseSessionServer` telemetry injection path 추가
- `FastPortSmokeServer` 프로젝트 추가
- `FastPortSmokeServerOptions` 및 dynamic host/port 구성 추가
- `FastPortSmokeClientSession`에 `EchoRequest`/`EchoResponse` smoke protocol 처리 추가
- parse/protocol error counter 연결
- `FastPortServer`에서 smoke protocol 및 telemetry DI 책임 제거
- LoadRunner 기반 fixed 1K payload smoke test 추가
- LoadRunner 기반 random 4K~16K payload smoke test 추가
- server telemetry unit tests 추가

## 4. Quality Metrics

| Metric | Result |
|--------|--------|
| Design match rate | 95% |
| Build | Passed |
| Build warnings | 0 |
| Tests | 56 passed, 0 failed |
| Fixed payload smoke | Passed |
| Random 4K~16K smoke | Passed |

Verification commands:

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
```

## 5. Remaining Limits

- `sentPackets`는 현재 exact FastPort packet count가 아니라 socket send completion count다.
- `receivedBytes`는 raw socket bytes가 아니라 parsed packet size 기준이다.
- malformed packet / wrong protocol id에 대한 negative smoke test는 아직 없다.
- 1,000 / 3,000 / 5,000 / 10,000 staged load validation은 다음 범위로 남긴다.
- telemetry를 외부 dashboard가 읽을 JSON/stream 형태로 내보내는 API는 아직 없다.

## 6. Lessons Learned

### Keep

- `LibNetworks`에는 protocol-neutral telemetry primitive만 둔다.
- 실사용 부하 검증은 `FastPortLoadRunner`와 실제 TCP path를 통해 검증한다.
- smoke/test protocol은 별도 server project에 둬서 기본 엔진 서버와 분리한다.

### Problem

- 초기 설계는 `FastPortServer`에 echo smoke logic을 얹는 방향이라, “바로 쓸 수 있는 기본 엔진 서버”라는 목적과 충돌했다.

### Try

- 다음 feature부터는 `FastPortServer`, `FastPortSmokeServer`, future game server template의 책임 경계를 먼저 고정한다.
- MAUI dashboard 설계 전에 server/client metric naming을 exact packet count와 socket completion count 기준으로 정리한다.

## 7. Next Steps

1. `$pdca archive fastport-smoke-server`로 문서를 archive한다.
2. 현재 변경을 commit한다.
3. 다음 PDCA에서 telemetry export 또는 staged load validation 중 하나를 선택한다.
