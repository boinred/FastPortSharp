# Staged Load Validation Test Guide

> Date: 2026-04-28  
> Tool: `FastPortLoadValidation`  
> Scope: smoke validation, staged validation, result review

---

## 1. 목적

이 문서는 `FastPortSmokeServer`와 `FastPortLoadValidation`을 사용해 FastPortSharp의 실제 TCP 경로를 검증하는 방법을 정리한다.

검증 목표는 다음과 같다.

- LoadRunner가 `ObservedMetricsSnapshot` JSONL을 정상 생성하는지 확인
- session / packet / buffer 흐름이 단계별 부하에서 유지되는지 확인
- 1,000 / 3,000 / 5,000 / 10,000 session 검증을 재현 가능한 명령으로 실행
- 실패 시 어떤 stage와 metric에서 실패했는지 `summary.md` / `summary.json`으로 확인

## 2. 검증 레벨

| Level | 목적 | 서버 필요 | 실행 시간 | 기본 CI 적합성 |
|-------|------|-----------|-----------|----------------|
| Tool verification | 빌드, 단위 테스트, command 생성 확인 | No | 짧음 | Yes |
| Smoke validation | 실제 서버 연결 + 작은 부하 확인 | Yes | 짧음 | 조건부 |
| Staged validation | 1k-10k session 부하 검증 | Yes | 긴 편 | No |

Full staged validation은 기본 `dotnet test`에 넣지 않는다. 10,000 session 검증은 OS/socket/file descriptor limit 영향을 크게 받기 때문에 전용 성능 환경에서 opt-in으로 실행한다.

## 3. 사전 준비

### 3.1 필수 조건

- .NET SDK 10
- repository root에서 실행
- `FastPortSmokeServer`가 사용할 port가 비어 있어야 함
- high-load 실행 전에는 OS file descriptor limit 확인 필요

기본 서버 port는 `6628`이다.

### 3.2 권장 build

```bash
dotnet build FastPortCharp.sln -c Release
```

Debug build도 동작하지만 부하 검증은 Release build를 기준으로 본다.

## 4. Tool Verification

서버 없이 검증 도구 자체를 확인하는 단계다.

### 4.1 Build

```bash
dotnet build FastPortCharp.sln
```

기대 결과:

- warning 0
- error 0

### 4.2 Unit Test

```bash
dotnet test FastPortCharp.sln --no-build
```

기대 결과:

- `FastPortLoadValidationTests` 포함
- 현재 기준: 71 passed, 0 failed

### 4.3 Dry-run

Dry-run은 실제 부하를 실행하지 않고 `FastPortLoadRunner` 명령만 출력한다.

```bash
./FastPortLoadValidation/bin/Debug/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/dry-run \
  --dry-run
```

기대 출력:

```bash
dotnet run -c Release --project FastPortLoadRunner -- --host 127.0.0.1 --port 6628 --sessions 10000 --payload random:4096-16384 --rate 1 --ramp-up 120s --duration 5m --metrics-interval 1s --output artifacts/load-validation/dry-run/s5-random-10k.metrics.jsonl
```

Release build binary로 확인하려면:

```bash
./FastPortLoadValidation/bin/Release/net10.0/FastPortLoadValidation \
  --profile staged \
  --stage s5-random-10k \
  --dry-run
```

## 5. Smoke Validation

작은 session 수로 실제 server/client TCP 경로를 검증한다.

### 5.1 서버 실행

터미널 1:

```bash
dotnet run -c Release --project FastPortSmokeServer
```

서버가 `6628`이 아닌 다른 port를 사용해야 하면 `FastPortSmokeServer/appsettings.json`의 `FastPortSmokeServer:Port`를 조정하고, validation 실행 시 같은 port를 `--port`로 넘긴다.

### 5.2 Smoke profile 실행

터미널 2:

```bash
dotnet run -c Release --project FastPortLoadValidation -- \
  --profile smoke \
  --output artifacts/load-validation/smoke-local
```

Smoke profile stage:

| Stage | Sessions | Payload | Duration | Ramp-up |
|-------|----------|---------|----------|---------|
| `smoke-fixed-10` | 10 | `fixed:1024` | 10s | 2s |
| `smoke-random-25` | 25 | `random:4096-16384` | 15s | 5s |

### 5.3 결과 확인

```bash
sed -n '1,160p' artifacts/load-validation/smoke-local/summary.md
```

기대 결과:

- `Status: Passed`
- 각 stage가 `Passed`
- `summary.json`의 root `passed`가 `true`

생성 파일:

```text
artifacts/load-validation/smoke-local/
  manifest.json
  summary.json
  summary.md
  smoke-fixed-10.metrics.jsonl
  smoke-fixed-10.stdout.log
  smoke-fixed-10.stderr.log
  smoke-random-25.metrics.jsonl
  smoke-random-25.stdout.log
  smoke-random-25.stderr.log
```

## 6. Staged Validation

큰 session 수로 단계별 부하를 검증한다.

### 6.1 전체 staged profile

터미널 1:

```bash
dotnet run -c Release --project FastPortSmokeServer
```

터미널 2:

```bash
dotnet run -c Release --project FastPortLoadValidation -- \
  --profile staged \
  --output artifacts/load-validation/staged-local \
  --continue-on-failure
```

Stage matrix:

| Stage | Sessions | Payload | Duration | Ramp-up |
|-------|----------|---------|----------|---------|
| `s1-fixed-1k` | 1,000 | `fixed:8192` | 2m | 30s |
| `s2-random-1k` | 1,000 | `random:4096-16384` | 2m | 30s |
| `s3-random-3k` | 3,000 | `random:4096-16384` | 3m | 60s |
| `s4-random-5k` | 5,000 | `random:4096-16384` | 5m | 90s |
| `s5-random-10k` | 10,000 | `random:4096-16384` | 5m | 120s |

### 6.2 특정 stage만 실행

먼저 낮은 단계부터 실행한다.

```bash
dotnet run -c Release --project FastPortLoadValidation -- \
  --profile staged \
  --stage s1-fixed-1k \
  --output artifacts/load-validation/s1-local
```

10,000 session 단일 stage:

```bash
dotnet run -c Release --project FastPortLoadValidation -- \
  --profile staged \
  --stage s5-random-10k \
  --output artifacts/load-validation/s5-local
```

## 7. Pass / Fail 기준

각 stage는 아래 조건을 모두 만족해야 pass다.

| Rule | 기본 기준 |
|------|-----------|
| JSON sample count | 3개 이상 |
| Peak session ratio | target의 95% 이상 |
| Socket error rate | 1% 이하 |
| Disconnect ratio | 5% 이하 |
| Total received packets | 0보다 큼 |
| Max TPS | 0보다 큼 |

RTT 값은 `rttP95Ms`, `rttP99Ms`로 기록하지만 현재 hard fail 기준에는 포함하지 않는다. RTT threshold는 baseline data를 확보한 뒤 별도 기준으로 추가한다.

## 8. 결과 읽는 방법

### 8.1 `summary.md`

사람이 읽는 요약이다.

```bash
sed -n '1,200p' artifacts/load-validation/staged-local/summary.md
```

확인할 항목:

- 전체 `Status`
- stage별 `Passed` / `Failed`
- `Peak Ratio`
- `Max TPS`
- `Socket Errors`
- 실패 사유 목록

### 8.2 `summary.json`

자동 분석용 요약이다. 주요 필드:

```json
{
  "passed": true,
  "stages": [
    {
      "stageId": "s1-fixed-1k",
      "passed": true,
      "targetSessions": 1000,
      "peakCurrentSessions": 1000,
      "peakSessionRatio": 1.0,
      "maxSocketErrorRate": 0,
      "maxTps": 1000,
      "failures": []
    }
  ]
}
```

### 8.3 `.metrics.jsonl`

원본 observed metrics stream이다. 각 줄은 `ObservedMetricsSnapshot` JSON이다.

확인할 필드:

- `clientObserved.targetSessions`
- `clientObserved.currentSessions`
- `clientObserved.totalSentPackets`
- `clientObserved.totalReceivedPackets`
- `clientObserved.sentBytesPerSecond`
- `clientObserved.receivedBytesPerSecond`
- `clientObserved.tps`
- `clientObserved.rttP95Ms`
- `clientObserved.rttP99Ms`
- `clientObserved.socketErrorRate`

## 9. 실패 시 확인 순서

### 9.1 `Metrics file not found`

가능 원인:

- `FastPortSmokeServer`가 실행 중이 아님
- port가 맞지 않음
- LoadRunner process가 시작 직후 실패함

확인:

```bash
sed -n '1,160p' artifacts/load-validation/<run-id>/<stage-id>.stderr.log
sed -n '1,160p' artifacts/load-validation/<run-id>/<stage-id>.stdout.log
```

### 9.2 `Peak session ratio ... below`

가능 원인:

- OS file descriptor limit 부족
- 서버 backlog 또는 socket resource 부족
- ramp-up이 너무 짧음
- target session 수가 현재 머신 한계를 넘음

대응:

- 낮은 stage부터 재실행
- `ulimit -n` 확인
- 필요 시 현재 shell에서 file descriptor limit 상향
- `--stage s1-fixed-1k`부터 다시 시작

### 9.3 `Socket error rate ... exceeds`

가능 원인:

- 서버 disconnect / accept 실패
- port 충돌
- local resource exhaustion
- client와 server port 불일치

대응:

- stderr/stdout log 확인
- Smoke profile로 축소해 재검증
- server restart 후 재실행

### 9.4 `Total received packets must be greater than zero`

가능 원인:

- 서버가 echo response를 보내지 못함
- protocol mismatch
- 연결은 됐지만 packet round-trip이 실패함

대응:

- Smoke profile 실행
- `FastPortSmokeServer`가 최신 코드인지 확인
- `FastPortLoadRunner` output log 확인

## 10. High-load 실행 전 체크리스트

- [ ] Release build 완료
- [ ] `dotnet test FastPortCharp.sln --no-build` 통과
- [ ] `--dry-run`으로 command 확인
- [ ] `FastPortSmokeServer` 단독 실행 확인
- [ ] Smoke profile pass
- [ ] `ulimit -n` 확인
- [ ] 1k stage pass 후 3k, 5k, 10k 순서로 진행
- [ ] generated artifacts가 git에 들어가지 않는지 `git status --short` 확인

macOS/Linux에서 현재 shell의 file descriptor limit 확인:

```bash
ulimit -n
```

일시 상향 예시:

```bash
ulimit -n 65536
```

환경에 따라 이 값은 OS 정책 때문에 실패할 수 있다. 실패하면 OS별 limit 설정을 먼저 조정해야 한다.

## 11. 공유해야 할 결과

부하 검증 결과를 공유할 때는 아래 파일을 기준으로 한다.

- `summary.md`
- `summary.json`
- 실패 stage의 `*.stderr.log`
- 실패 stage의 `*.stdout.log`
- 필요 시 실패 stage의 `*.metrics.jsonl`

`artifacts/load-validation/`은 기본적으로 git ignored다. baseline으로 남길 결과만 선별해 별도 문서에 요약한다.

## 12. 권장 실행 순서

1. Tool verification
2. Smoke validation
3. `s1-fixed-1k`
4. `s2-random-1k`
5. `s3-random-3k`
6. `s4-random-5k`
7. `s5-random-10k`

10,000 session 검증이 실패하면 바로 엔진 버그로 판단하지 않는다. 먼저 OS/socket limit, server log, stage별 peak session ratio, socket error rate를 같이 확인한다.
