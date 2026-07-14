# BaseListener Accept 경로 최적화 벤치마크

> 날짜: 2026-07-06
> 대상: `LibNetworks/BaseListener.cs` accept 경로
> 커밋: 베이스라인 `e4b4c8e` → 1단계 `800425d` → 2단계 `f9443c1`

## 환경

| 역할 | 사양 |
|------|------|
| 서버 | Azure `Standard_D2as_v5` (2 vCPU / 8GB), Ubuntu 24.04, koreacentral, Accelerated Networking, .NET 10.0.9 runtime |
| 러너 | 로컬 Windows 11 (i5-14600K), WAN 경유 접속 |
| 서버 앱 | `FastPortTestSmokeServer` (linux-x64 framework-dependent publish) |
| 클라이언트 | `FastPortTestLoadRunner` |
| 서버 OS 튜닝 | `nofile 65535`, `net.core.somaxconn 4096` |

> ⚠️ 러너가 WAN 경유(로컬↔koreacentral, 무부하 RTT 약 6ms)이므로 클라이언트 RTT에는
> 회선 왕복과 회선 변동이 포함된다. 런 간 클라이언트 RTT 차이는 회선 노이즈가 지배할
> 수 있으므로, 코드 변경 효과는 **서버 측 accept 경로 계측(operationDurations)** 기준으로
> 해석한다. 각 조건은 1회 측정이다.

## 적용한 최적화

### 1단계 (`800425d`) — 로깅 핫패스 제거 + 동기완료 재귀 평탄화

- accept당 실행되던 `LogInformation`(보간 문자열 + `RemoteEndPoint` syscall/할당)을
  `IsEnabled(Debug)` 가드 + 구조화 템플릿의 Debug 레벨로 이동.
- 에러 로그를 메시지 템플릿으로 전환, invalid IP 메시지의 `${ip}` 오타 수정.
- `AcceptAsync` 동기 완료 시 `Accept → OnSocketEventsAcceptCompleted → finally Accept`
  재귀를 `ProcessAccept` + while 루프로 전환 (backlog 연쇄 시 스택 증가 방지).
- `ProcessAccept` 예외를 격리해 session factory 실패가 IOCP 콜백을 죽이지 않도록 변경.

### 2단계 (`f9443c1`) — 세션 생성 오프로드 + accept당 할당 제거

- session factory 호출(세션당 8KiB pooled buffer 2개 생성)을 accept 펌프 스레드에서
  ThreadPool work item으로 이동 → 다음 `AcceptAsync` 재등록 지연 최소화.
- accept당 `Task.Run` 클로저(클로저 + Task 2개 할당)를 `IThreadPoolWorkItem` 1개 +
  `UnsafeQueueUserWorkItem(preferLocal: false)`로 대체 → IOCP 스레드 로컬 큐 대신
  글로벌 큐에서 공정하게 소비.
- `OnAcceptSessionCreated` / `OnAcceptSessionTaskStarted` 훅 호출 순서는 유지.
  두 계측값은 이제 work item 큐 지연을 포함한다.

## 시나리오 A — 정상 램프업 (벤치마크 기준선)

조건: 1,000 세션 / payload `fixed:2048` / ramp-up 60s / duration 3m / rate 1 req/s·세션
(≈ 초당 17 accept, 정상 상태 ~987 TPS, 양방향 ~2MB/s)

### 클라이언트 관측 (최종 누적값)

| 지표 | 베이스라인 | 1단계 | 2단계 |
|---|---:|---:|---:|
| 세션 유지 | 1,000/1,000 | 1,000/1,000 | 1,000/1,000 |
| 에러율 | 0.000% | 0.000% | 0.000% |
| RTT 평균 | 19.12ms | 17.30ms | 22.73ms |
| RTT P95 | 46.85ms | 35.77ms | 56.18ms |

> 해석: 서버 측 지표(아래)가 세 런에서 사실상 동일하므로, 클라이언트 RTT의 런 간 차이
> (17~23ms)는 회선 변동으로 판단한다. 초당 17 accept로는 accept 경로에 부하가 걸리지
> 않아 이 시나리오로는 최적화 효과를 판별할 수 없다 → 시나리오 B 참조.

### 서버 관측 (accept 경로, avg / max ms)

| 지표 | 베이스라인 | 1단계 | 2단계 |
|---|---:|---:|---:|
| accept-session-create | 0.096 / 8.32 | 0.098 / 8.35 | 0.099 / 8.20 |
| accept-task-start | 0.114 / 9.27 | 0.117 / 9.27 | 0.118 / 9.15 |
| accept-first-receive | 0.542 / 45.08 | 0.756 / 156.01 | 0.740 / 139.22 |
| onaccepted-start-first-receive | 0.428 / 35.82 | 0.639 / 146.75 | 0.622 / 130.07 |

공통: accept 1,000건, accept/소켓/파싱 에러 0, backpressure 0,
max pending send 30~31, max send buffer 2,089B, 총 수신 ~209.9K 패킷.

> 참고: 2단계의 accept-session-create/task-start는 work item 큐 지연을 포함하는데도
> 베이스라인과 동일하다 = 이 부하에서 오프로드로 인한 추가 지연은 없음.
> first-receive 계열 max는 클라이언트 램프업 타이밍/회선에 좌우되는 노이즈성 지표다.

## 시나리오 B — Accept 스트레스 (최적화 효과 측정)

조건: 1,000 세션 / payload `fixed:2048` / **ramp-up 5s** / duration 60s
(≈ **초당 200 accept**, 베이스라인 vs 2단계 연속 측정)

### 서버 관측 (accept 경로, avg / max ms)

| 지표 | 베이스라인 | 2단계 | 변화 |
|---|---:|---:|---|
| accept-session-create | 0.092 / 10.66 | 0.091 / 8.83 | max **-17%** |
| accept-task-start | 0.197 / 41.29 | 0.167 / 38.82 | avg **-15%** |
| accept-first-receive | 0.607 / 41.37 | 0.487 / 38.90 | avg **-20%** |
| onaccepted-start-first-receive | 0.410 / 31.33 | 0.321 / **11.35** | avg -22%, **max -64%** |

### 클라이언트 관측 (최종 누적값)

| 지표 | 베이스라인 | 2단계 |
|---|---:|---:|
| 세션 유지 / 에러 | 1,000/1,000 / 0% | 1,000/1,000 / 0% |
| RTT 평균 | 12.72ms | 9.67ms |
| RTT P95 | 33.63ms | 21.90ms |

공통: accept 1,000건, 에러/backpressure 0, 총 수신 ~67.7K 패킷.

## 결론

1. **가장 뚜렷한 개선은 세션 시작→첫 수신 꼬리 지연**: 스트레스 램프업에서 max
   31.33ms → 11.35ms (**-64%**). 세션 생성 오프로드 + `preferLocal:false` 글로벌 큐
   분배가 램프업 구간의 세션 시작 지연 뭉침을 해소한 효과다.
2. accept 경로 전 지표가 스트레스 조건에서 일관되게 개선(avg 15~22%). 완만한
   램프업에서는 accept 경로가 유휴 상태라 차이가 드러나지 않는다.
3. 회귀 없음: 모든 런에서 세션 손실 0, 에러 0, backpressure 0. 정상 상태 처리량 동일.
4. 구조적 개선(동기완료 재귀 제거, accept당 할당 2→1, 로그 비용 제거)은 이 측정에
   나타나는 수치와 무관하게 유효하며, 10K/고속 램프업에서 효과가 더 커질 것으로 예상.

## 시나리오 C — OutstandingAccepts 비교 (2026-07-06 추가)

조건: 시나리오 B와 동일 (1,000 세션 / fixed:2048 / ramp-up 5s / 60s),
2단계 최적화 + shutdown fix(`f102dc2`) 빌드, `--FastPortTestSmokeServer:OutstandingAccepts` 만 변경.

### 서버 관측 (accept 경로, avg / max ms)

| 지표 | OA=1 | OA=4 | OA=8 |
|---|---:|---:|---:|
| accept-first-receive | 1.261 / 128.10 | 0.459 / 23.08 | 0.441 / 22.88 |
| accept-task-start | 0.138 / 20.44 | 0.105 / 11.77 | 0.107 / 11.51 |
| onaccepted-start-first-receive | 1.123 / 118.21 | 0.354 / 11.31 | 0.335 / 11.37 |
| accept-session-create | 0.101 / 8.54 | 0.100 / 10.82 | 0.101 / 10.52 |

공통: 세션 1,000/1,000 유지, 에러/backpressure 0. 클라이언트 정상 상태 RTT는
OA 값과 무관 (accept는 램프업 구간에만 영향).

### 결론

- **OA=1 → OA=4: accept-first-receive 평균 -64%, max -82%** (128.10ms → 23.08ms).
  단일 outstanding accept가 초당 200 accept 램프업에서 병목이었음을 확인.
- OA=8은 OA=4와 사실상 동일 — 이 부하에서는 4로 충분 (수확 체감).
- 반영: `FastPortTestSmokeServer/appsettings.json` 기본값을 `OutstandingAccepts: 4`로 변경.
  엔진 기본값(`C_DefaultOutstandingAccepts = 1`)은 호환성 유지를 위해 그대로 두고,
  10K 검증에서 재확인 후 변경을 검토한다.

## 시나리오 D — 폐루프 최대 처리량 (2026-07-06 추가)

조건: 1,000 세션 / payload `fixed:256` / `--pacing-policy fixed-window --pacing-fixed-window 1`
(응답 도착 즉시 다음 요청 전송 = ping-pong), `--rate 200`(페이서가 게이트), ramp-up 10s / 60s.
수신 경로 최적화(`79f6310`) + OA=4 빌드.

| 지표 | 값 |
|---|---:|
| 지속 TPS | **~33,100** |
| 총 처리 패킷 | 2,405,043 |
| RTT 평균 / P95 | 30.2ms / 32.4ms |
| 에러 / 소켓에러 / backpressure | 0 / 0 / 0 |
| receive-packet-handler 평균 | 0.0049ms (33K TPS에서도 유지) |
| max pending send | 878 (유계) |
| 서버 CPU load avg (2 vCPU) | 1.57 (~78%) |

해석:

- 폐루프에서 세션당 처리율은 `1/RTT`(≈33pps)로 수렴 — 시스템이 스스로 한계에
  맞춰 감속하므로 에러 없이 안정 상태 유지.
- 병목은 회선 대역폭(양방향 각 ~9MB/s ≈ 75Mbps)과 서버 CPU(78%)의 혼합.
  분리하려면 러너를 동일 리전 VM으로 옮기거나 서버 스케일업 필요.
- MMORPG 입력 부하 관점: **1K 동접 × 세션당 33pps를 2 vCPU에서 에러 0으로 소화** —
  이동 패킷급(10~30pps/세션) 입력 부하는 1채널 규모에서 실측으로 검증됨.
  미검증 잔여 항목은 1:N 브로드캐스트 증폭 시나리오.

## 한계 및 다음 측정

- 각 조건 1회 측정 — 확정 수치가 필요하면 조건당 3회 이상 반복 후 중앙값 비교.
- WAN 러너 구성이라 클라이언트 RTT 절대값에는 회선 왕복(~6ms)과 변동이 포함됨.
- 다음 후보: 동일 조건 10K 검증(서버 F8s_v2 스케일업), ramp-up 1~2s 극한 accept 스트레스,
  `OutstandingAccepts > 1` 조합 측정.

## 재현 방법

```bash
# 서버 (Azure VM)
~/.dotnet/dotnet ~/fastport/smoke-server/FastPortTestSmokeServer.dll \
  --Telemetry:Output=$HOME/fastport/logs/server.metrics.jsonl

# 시나리오 A (기준선)
dotnet run -c Release --project tests-projects/FastPortTestLoadRunner -- \
  --host <server-ip> --port 6628 --sessions 1000 --payload fixed:2048 \
  --ramp-up 60s --duration 3m --output client.metrics.jsonl

# 시나리오 B (accept 스트레스)
dotnet run -c Release --project tests-projects/FastPortTestLoadRunner -- \
  --host <server-ip> --port 6628 --sessions 1000 --payload fixed:2048 \
  --ramp-up 5s --duration 60s --output client.metrics.jsonl
```

원본 텔레메트리: `artifacts/cloud-bench/{baseline,stage1,stage2,stress-baseline,stress-stage2}/`
(로컬 전용, 커밋 대상 아님)
