# Gap Analysis: listener-backlog-increase

> Date: 2026-05-07 | Design: docs/02-design/features/listener-backlog-increase.design.md

---

## Match Rate: 100%

## Summary

`listener-backlog-increase` 설계의 코드 변경 항목은 모두 반영됐다.

`LibNetworks.BaseListener`는 기존 `StartAccept(string ip, int port)` API를 유지하면서 `StartAccept(string ip, int port, int backlog)` overload를 추가했고, hard-coded `Listen(100)`은 normalized backlog 기반 `Listen(normalizedBacklog)`으로 대체됐다.

`FastPortTestSmokeServer`는 `FastPortTestSmokeServer:ListenBacklog` 설정을 읽고 기본값 `4096`을 적용한다. background service 시작 로그와 listener 호출에도 backlog 값이 전달된다.

Local verification 결과:

- `dotnet build FastPortSharp.sln -c Release`: passed, warning 0, error 0
- `dotnet test FastPortSharp.sln -c Release --no-build`: passed, 134/134

Cloud closed-loop 3분 재검증도 완료했다. 원격 server는 별도 clean worktree에서 실행했으며, `ss -ltnp | grep 6628` 출력에서 `LISTEN 0 4096 ... :6628`로 backlog `4096` 적용을 확인했다.

Cloud validation 결과는 baseline 대비 connect timeout이 `1,057`에서 `7`로 감소했고, client peak session은 `8,943`에서 `9,993`으로 증가했다.

## Implemented Items

- [x] `LibNetworks/BaseListener.cs`에 `C_DefaultListenBacklog = 4096` 추가
- [x] 기존 `StartAccept(string ip, int port)` 유지 및 default backlog overload 위임
- [x] 신규 `StartAccept(string ip, int port, int backlog)` overload 추가
- [x] `NormalizeListenBacklog`로 `0` 이하 backlog 값을 `4096`으로 보정
- [x] `m_Socket.Listen(100)`을 `m_Socket.Listen(normalizedBacklog)`로 변경
- [x] endpoint 변환 실패, bind/listen 예외, accept hook 흐름 유지
- [x] `FastPortTestSmokeServerOptions.ListenBacklog` 추가
- [x] `FastPortTestSmokeServerOptions.DefaultListenBacklog = 4096` 추가
- [x] `Program.cs`에서 `FastPortTestSmokeServer:ListenBacklog` binding 및 invalid value fallback 처리
- [x] `FastPortTestSmokeServer/appsettings.json`에 `ListenBacklog: 4096` 추가
- [x] `FastPortTestSmokeServerBackgroundService`에서 startup log에 `ListenBacklog` 포함
- [x] `FastPortTestSmokeServerBackgroundService`에서 `StartAccept(host, port, listenBacklog)` 호출
- [x] smoke server options/configuration 관련 테스트 보강
- [x] Release build 통과
- [x] Release test 통과

## Missing Items

- [x] Cloud closed-loop 3분 테스트 실행
- [x] Remote `ss -ltnp | grep 6628`로 listen backlog `4096` 관측
- [x] Baseline connect timeout `1,057` 대비 after 값 비교

## Changed Items (Deviations from Design)

- [x] 설계 문서의 `FastPortTestSmokeServerOptions.ListenBacklog` 예시는 literal `4096` default였으나, 구현은 `DefaultListenBacklog` const를 두고 property default에서 참조한다.
  - 판단: 의미상 동일하며, `Program.cs` fallback과 options default가 같은 값을 공유하므로 drift 위험이 낮다.

## Recommendations

1. 현재 code/design gap은 없다.
2. `Listen(100)`은 이번 10K ramp-up의 주요 connection establishment 병목 중 하나였다고 판단한다.
3. 남은 문제는 connect backlog가 아니라 RTT tail 및 일부 server idle-timeout disconnect 해석이다.
4. 다음 실험은 `SessionIdleCleanup`을 테스트 duration보다 길게 조정하거나 비활성화한 상태에서 RTT tail만 분리하는 것이 좋다.

## Next Steps

- [x] Code implementation complete
- [x] Local build/test complete
- [x] Cloud server deploy/start
- [x] Cloud listen backlog observation
- [x] Cloud 3분 closed-loop validation
- [x] PDCA report after cloud result summary
