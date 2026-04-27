# FastPortLoadRunner

FastPort 서버에 실제 TCP 세션을 붙여 부하 테스트를 실행하기 위한 콘솔 앱입니다.

기존 `BenchmarkDotNet` 기반 micro benchmark 코드는 제거했습니다. 이 프로젝트는 앞으로 세션 수, 메시지 크기, ramp-up, duration, send rate를 옵션으로 받아 서버 부하를 만드는 역할을 맡습니다.

## 실행 예시

```bash
dotnet run -c Release --project FastPortLoadRunner -- \
  --sessions 10000 \
  --payload random:4096-16384 \
  --duration 5m \
  --ramp-up 60s
```

```bash
dotnet run -c Release --project FastPortLoadRunner -- \
  --sessions 10000 \
  --payload fixed:8192 \
  --rate 20
```

## 옵션

| 옵션 | 설명 | 기본값 |
|------|------|--------|
| `--host` | 서버 호스트 | `127.0.0.1` |
| `--port` | 서버 포트 | `6628` |
| `--sessions` | 동시 세션 수 | `1` |
| `--payload` | `fixed:<bytes>` 또는 `random:<min>-<max>` | `fixed:8192` |
| `--rate` | 세션당 초당 패킷 수 | `1` |
| `--ramp-up` | 세션 증가 시간 | `10s` |
| `--duration` | 테스트 지속 시간 | `1m` |

## 다음 구현 범위

- 10,000 세션 ramp-up 생성
- 4K~16K 랜덤 payload 및 8K 고정 payload 송신
- TPS, RTT, latency, bytes/sec, packets/sec 수집
- accept/disconnect/socket error telemetry 연동
