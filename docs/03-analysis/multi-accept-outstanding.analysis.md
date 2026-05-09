# Gap Analysis: multi-accept-outstanding

> Date: 2026-05-09 | Design: docs/02-design/features/multi-accept-outstanding.design.md

---

## Match Rate: 96%

## Summary

`multi-accept-outstanding`의 코드 구현과 검증은 설계 목표와 거의 일치한다.

`LibNetworks.BaseListener`는 기존 `StartAccept(string ip, int port)`와 `StartAccept(string ip, int port, int backlog)` 호출을 유지하면서, 새 `StartAccept(string ip, int port, int backlog, int outstandingAccepts)` overload를 추가했다. 기본 outstanding accept count는 `1`로 유지되므로 기존 listener 동작은 보존된다.

listener accept pump는 inherited single `m_SocketEvent` 의존 대신 `BaseListener`가 소유하는 accept 전용 `SocketAsyncEventArgs[]`를 사용한다. 각 args는 completion 이후 같은 args로 repost되며, shutdown 상태에서는 repost하지 않는다. accept completion error 또는 null socket도 running 상태라면 accept loop를 이어갈 수 있게 바뀌었다.

Smoke server는 `FastPortTestSmokeServer:OutstandingAccepts` 설정을 읽고 background service에서 listener로 전달한다. Local verification과 Docker/cloud 비교 검증도 완료했다.

- `dotnet build FastPortCharp.sln -c Release`: passed, warning 0, error 0
- `dotnet test FastPortCharp.sln -c Release --no-build`: passed, 139/139
- Docker/cloud `OutstandingAccepts=1`: 10/10 runners exit 0, connect failures 0
- Docker/cloud `OutstandingAccepts=4`: 10/10 runners exit 0, connect failures 3

설계는 `1,2,4,8` 비교를 기본 matrix로 제안했지만, `4`가 default 변경 근거를 만들지 못했으므로 `2`와 `8`은 이번 feature의 필수 검증에서 제외한다. 이는 설계의 "성능 이득이 명확하지 않으면 default를 올리지 않는다"는 결정과 일치한다.

## Implemented Items

- [x] `BaseListener.StartAccept(string ip, int port)` 기존 API 유지
- [x] `BaseListener.StartAccept(string ip, int port, int backlog)` 기존 API 유지
- [x] `BaseListener.StartAccept(string ip, int port, int backlog, int outstandingAccepts)` 신규 overload 추가
- [x] default outstanding accept count를 `1`로 유지
- [x] invalid/zero/negative outstanding accept count를 `1`로 보정
- [x] 과도한 outstanding accept count를 `64`로 clamp
- [x] `SocketAsyncEventArgs[]`를 accept 전용으로 `BaseListener`가 소유
- [x] accept args별 `Completed` handler 연결
- [x] startup 시 normalized outstanding accept count만큼 `AcceptAsync` 등록
- [x] success completion 후 같은 args를 repost
- [x] completion error 후 running 상태이면 같은 args를 repost
- [x] null accepted socket 후 running 상태이면 같은 args를 repost
- [x] shutdown 이후 `Accept` repost 방지
- [x] smoke server options에 `DefaultOutstandingAccepts`와 `OutstandingAccepts` 추가
- [x] `Program.cs`에서 `FastPortTestSmokeServer:OutstandingAccepts` 설정 binding
- [x] `appsettings.json`에 `OutstandingAccepts: 1` 추가
- [x] background service startup log에 `OutstandingAccepts` 추가
- [x] background service에서 새 `StartAccept` overload 호출
- [x] normalization/config unit test 추가
- [x] `outstandingAccepts=2` smoke integration test 추가
- [x] Release build 통과
- [x] Release test 통과
- [x] Docker/cloud validation에서 `OutstandingAccepts=1` baseline 재확인
- [x] Docker/cloud validation에서 `OutstandingAccepts=4` 비교
- [x] validation 결과를 `docs/load-validation-benchmark-results.md`에 반영

## Validation Result

Common condition:

```text
Topology: Azure smoke server + local Docker Desktop runners
Server: ListenBacklog=10500, SessionIdleCleanup IdleTimeoutSeconds=90
Client load: 10 Docker containers x 1,000 sessions
Payload: random:128-2048
Send rate: 1000
Pacing: fixed-window=1
Ramp-up: 120s
Duration: 3m
```

| Metric | `OutstandingAccepts=1` | `OutstandingAccepts=4` |
|--------|-----------------------:|-----------------------:|
| Docker runner exits | `10/10 exit 0` | `10/10 exit 0` |
| Connect completed | `10,000` | `9,997` |
| Connect failures | `0` | `3` |
| Sum TPS | `6,559.79` | `5,941.41` |
| Average RTT P95 | `3,967.57ms` | `3,725.48ms` |
| Average RTT P99 | `17,579.81ms` | `17,809.50ms` |
| Server accepted sessions | `9,999` | `9,997` |
| Server accept errors | `0` | `0` |
| Server send backpressure events | `0` | `0` |
| Server send rejected requests | `18` | `2` |
| `accept-task-start` avg | `0.616ms` | `0.259ms` |
| `accept-first-socket-receive` avg | `134.037ms` | `126.719ms` |

Artifact roots:

- `artifacts/load-validation/multi-accept-o1-20260509-021411/`
- `artifacts/load-validation/multi-accept-o4-20260509-021411/`

## Missing Items

- [ ] `BaseListener` accept args dispose cleanup
- [ ] `BaseSocket.m_SocketEvent` listener 책임 제거 cleanup
- [ ] Future-only `OutstandingAccepts=2`/`8` cloud comparison if accept pressure reappears

## Changed Items (Deviations from Design)

- [x] `BaseSocket.m_SocketEvent`는 제거하지 않고 그대로 둔다.
  - 판단: design에서도 삭제는 별도 cleanup으로 분리 가능하다고 명시했다.
- [x] smoke server `Program.cs`는 invalid 값 fallback만 수행하고, 최종 max clamp는 `BaseListener.NormalizeOutstandingAccepts`에서 수행한다.
  - 판단: engine-level 최종 방어선이 `BaseListener`에 있으므로 config source가 늘어나도 동일하게 보호된다.
- [x] accept args dispose는 구현하지 않았다.
  - 판단: design에서 optional로 둔 영역이며, 현재 `BaseListener`는 `IDisposable` 책임을 갖지 않는다. shutdown/socket close 정리는 별도 cleanup feature로 다루는 편이 안전하다.
- [x] `OutstandingAccepts=2`와 `8` cloud comparison은 생략했다.
  - 판단: `4`가 default 변경 근거를 만들지 못했고, 설계도 이득이 명확하지 않으면 default를 올리지 않는다고 정의했다.

## Recommendations

1. 코드 구현은 설계와 충분히 맞다.
2. 기본값은 계속 `OutstandingAccepts=1`로 유지해야 한다.
3. `OutstandingAccepts=4`는 accept path 평균을 일부 낮추지만, default로 올릴 정도의 총합 개선은 아니다.
4. 현재 10K 조건의 병목은 accept outstanding보다 session runtime path와 latency tail 쪽으로 보는 것이 타당하다.
5. 다음 개선 후보는 `BaseListener`/`BaseSocket` event args 책임 cleanup 또는 더 큰 변경인 pipeline 실험이다.

## Next Steps

- [x] Code implementation complete
- [x] Local build complete
- [x] Local test complete
- [x] Docker/cloud validation complete
- [x] Validation result documentation complete
- [x] PDCA report
