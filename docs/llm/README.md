# LLM 코드 탐색 가이드

이 폴더는 LLM 에이전트가 요청에 필요한 코드만 찾도록 돕는 지도다. 코드 전체를 훑기 전에 이 파일과 도메인 문서 1~2개만 읽는다.

- 용어 정의: [glossary.md](glossary.md)
- 작업 규칙(스타일, 테스트, 커밋): 루트 `AGENTS.md`
- 기준 시점: 2026-10-05 (`main` `c709a26` 기준)

## 사용 순서

1. 아래 "도메인 고르기" 표에서 요청 키워드로 문서를 고른다.
2. 고른 문서의 "작업별 시작점"에서 고칠 파일과 심볼을 찾는다.
3. 심볼은 이름으로 찾는다. 예: `grep -n "void RequestDisconnect" -r LibNetworks`. 이 문서들은 줄 번호를 적지 않는다.
4. 문서와 코드가 다르면 코드를 믿는다. 작업이 끝나면 해당 문서를 고친다.

## 도메인 고르기

| 요청에 나오는 말 | 문서 |
|---|---|
| 패킷 포맷, 헤더, 길이 필드, packetId, protobuf 파싱, 수신 버퍼 구현, 링버퍼, `ArrayPool`, 세션 ID, 지연(RTT) 통계, 타이머 큐 | [packet-buffers.md](packet-buffers.md) |
| 세션, 수신·송신 흐름, 송신 큐 상한, 백프레셔, 연결 종료, disconnect reason, 관측 hook(`OnNetwork*`), 패킷 핸들러 예외, 잘못된 헤더, 수신 버퍼 초과 | [session.md](session.md) |
| 리스너, accept, 서버 시작·종료, 최대 접속 수, backlog, 커넥터, 클라이언트 접속, 세션 팩토리, keep-alive | [listener-connector.md](listener-connector.md) |
| 엔진 샘플 서버·클라이언트(`FastPortServer`, `FastPortClient`), Windows 서비스, 엔진 샘플 proto(`Protocols`) | [sample-apps.md](sample-apps.md) |
| 게임 서버 템플릿, 패킷 핸들러, 디스패처, DI 등록, Serilog, 템플릿 패킷 추가, `PacketIds.proto`, scaffold 스크립트, 새 게임 서버 생성, golden hash | [game-server-template.md](game-server-template.md) |
| 부하 테스트, 스모크 서버, 텔레메트리, JSONL, 부하 생성기(LoadRunner), 단계별 검증(LoadValidation), 임계값, idle 세션 정리, 클라우드 부하 검증, 벤치마크 리포트 | [load-testing.md](load-testing.md) |
| 대시보드, MAUI, 차트, ViewModel, Echo 클라이언트 UI, JSONL 폴링 | [dashboard.md](dashboard.md) |
| 빌드·테스트 명령, CI 워크플로, 필수 체크, 브랜치·릴리스, 개발 환경, 줄바꿈 정책, 코드 스타일 도구 | [platform.md](platform.md) |

## 실행 파일 → 진입 파일

| 실행 파일(프로젝트) | 진입 파일 | 문서 |
|---|---|---|
| `FastPortServer` | `FastPortServer/Program.cs` | sample-apps |
| `FastPortClient` | `FastPortClient/Program.cs` | sample-apps |
| `FastPortGameServerTemplate` | `template-projects/FastPortGameServerTemplate/Program.cs` | game-server-template |
| `FastPortGameServerTemplate.SampleClient` | `template-projects/FastPortGameServerTemplate.SampleClient/Program.cs` | game-server-template |
| `FastPortTestSmokeServer` | `tests-projects/FastPortTestSmokeServer/Program.cs` | load-testing |
| `FastPortTestLoadRunner` | `tests-projects/FastPortTestLoadRunner/Program.cs` | load-testing |
| `FastPortTestLoadValidation` | `tests-projects/FastPortTestLoadValidation/Program.cs` | load-testing |
| `FastPortDashboard.Maui` | `FastPortDashboard.Maui/MauiProgram.cs` | dashboard |
| scaffold | `scripts/scaffold-game-server.sh`, `scripts/scaffold-game-server.ps1` | game-server-template |

## 전체 구조

- .NET 10 / C# 14. `SocketAsyncEventArgs` 기반 TCP 엔진(`LibCommons` + `LibNetworks`) 위에 샘플, 게임 서버 템플릿, 부하·검증 도구, MAUI 대시보드가 있다.
- 솔루션은 두 개다.
  - `FastPortSharp.sln`: 엔진, 샘플, 템플릿, 테스트 도구. Linux·macOS·Windows에서 빌드한다.
  - `FastPortSharp.Dashboard.sln`: `FastPortDashboard.Core`, `FastPortDashboard.Maui`, `FastPortDashboardTests`, `LibTestTelemetry`. MAUI workload가 필요하고 macOS·Windows만 빌드한다.
- 와이어 포맷: `[UInt16 LE 전체 길이(헤더 포함)][int32 LE packetId][protobuf payload]`. 패킷 최대 65,535B.

```
LibCommons  ◀── LibNetworks ◀──┬── FastPortServer
     ▲              ▲          ├── FastPortClient ──────────────▶ Protocols
     │              │          ├── template-projects/FastPortGameServerTemplate (+ .SampleClient)
     │              │          ├── FastPortDashboard.Core ──▶ LibTestTelemetry
     │              │          └── tests-projects/FastPortTestSmokeServer ──▶ LibTestTelemetry, Protocols
     │              │
     └── tests-projects/FastPortTestLoadRunner (LibNetworks 미사용, 자체 소켓) ──▶ LibTestTelemetry, Protocols
tests-projects/FastPortTestLoadValidation ──▶ LibTestTelemetry (LoadRunner를 프로세스로 실행)
tests-projects/FastPortTests (MSTest) ──▶ LibCommons, LibNetworks, LoadRunner, LoadValidation, SmokeServer, LibTestTelemetry
tests-projects/FastPortDashboardTests (MSTest) ──▶ FastPortDashboard.Core
FastPortDashboard.Maui ──▶ FastPortDashboard.Core
```

## 모든 도메인에 공통인 패턴

- **엔진 확장은 상속 + override다.** 서버는 `BaseMessageListener`를, 세션은 `BaseSessionClient`(서버 쪽) 또는 `BaseSessionServer`(클라이언트 쪽)를 상속하고 `OnReceived` 등을 override한다. 세션 생성은 팩토리 인터페이스(`IClientSessionFactory`, `IServerSessionFactory`)가 맡는다.
- **엔진은 관측 구현을 모른다.** `BaseSession`, `BaseListener`의 `protected virtual On*` hook을 앱이 override해서 로그·텔레메트리로 연결한다(예: SmokeServer 세션 → `IServerTelemetry`).
- **세션당 백그라운드 Task 3개**(`DoWorkReceivedBuffers`, `DoWorkReceivedPackets`, `DoWorkSendBuffers`)가 돈다. 종료는 `RequestDisconnect(reason)` 한 곳으로 모이고 한 번만 실행된다.
- **버퍼는 `ArrayPool<byte>.Shared`에서 빌린다.** 누가 반환하는지 한 곳으로 정해져 있다. 수정 시 반환 경로를 함께 확인한다.
- **설정은 `appsettings.json` + Generic Host**다. 앱마다 섹션 이름이 다르다(각 도메인 문서 참고).
- **proto는 두 벌이다.** 엔진 샘플·도구용 `Protocols/Protos/`와 템플릿용 `template-projects/Protos/`. 서로 섞지 않는다.

## 큰 파일

아래 파일은 통째로 읽지 않는다. 먼저 grep으로 위치를 찾고 그 부분만 읽는다. 표에 없는 파일도 패턴만 확인할 때는 grep을 쓴다.

| 파일 | 줄 수 | 찾는 방법 |
|---|---|---|
| `LibNetworks/Sessions/BaseSession.cs` | ~1300 | `grep -n "OnReceived\|RequestDisconnect\|DoWork" LibNetworks/Sessions/BaseSession.cs`처럼 메서드 이름으로 |
| `tests-projects/FastPortTests/FastPortTestLoadRunnerTests.cs` | ~1000 | `grep -n "public .*void\|public async Task"` 후 테스트 이름으로 |
| `tests-projects/FastPortTests/BaseSessionSendPolicyTests.cs` | ~790 | 테스트 메서드 이름 |
| `tests-projects/FastPortTests/FastPortTestLoadValidationTests.cs` | ~760 | 테스트 메서드 이름 |
| `tests-projects/FastPortTestLoadRunner/Metrics.cs` | ~740 | 타입·메서드 이름 |
| `scripts/scaffold-game-server.ps1`, `.sh` | ~690, ~670 | 함수 목록: `grep -n '^function ' *.ps1`, `grep -n '() {' *.sh`. 같은 단계가 짝을 이룬다(`smoke_build` ↔ `Invoke-SmokeBuild`) |
| `tests-projects/FastPortTestLoadRunner/LoadRunnerOptions.cs` | ~610 | 옵션 이름(`--sessions` 등) |
| `tests-projects/LibTestTelemetry/ServerTelemetry.cs` | ~530 | 지표 이름 |

## 읽지 않아도 되는 곳

- `bin/`, `obj/`, `TestResults/`: 빌드 산출물.
- `docs/*.md`(이 폴더 제외): 벤치마크 리포트와 runbook. 성능 수치나 클라우드 절차가 필요할 때만 [load-testing.md](load-testing.md)에서 골라 읽는다.
- 코드 주석의 `Design Ref: §...`: 저장소에 없는 과거 설계 문서 참조다.

## 문서 유지 규칙

- 파일, 타입, public/protected 멤버, 패킷 ID, 설정 키, 명령어, CI job을 추가하거나 옮기면 해당 도메인 문서의 표를 고친다. 코드와 같은 커밋에 넣는다.
- 줄 번호는 적지 않는다. 이름으로 찾을 수 있게 쓴다. 줄 수는 "큰 파일" 표에만 대략 적는다.
- 새 도메인(새 프로젝트·디렉터리)이 생기면 문서를 추가하고 위 "도메인 고르기" 표와 "실행 파일 → 진입 파일" 표에 한 줄씩 넣는다.
- 새 용어나 헷갈리는 이름이 생기면 [glossary.md](glossary.md)에 추가한다.
- 문서 문체: 한국어, "~다"로 끝나는 짧은 문장, 코드 식별자는 백틱으로 원문 유지.
