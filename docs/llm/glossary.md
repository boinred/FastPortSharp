# 용어집

코드와 문서에서 쓰는 이름을 정리했다. 이름과 역할이 엇갈리는 항목은 **주의**로 표시했다.

## 엔진

| 용어 | 뜻 | 코드 |
|---|---|---|
| 엔진 | `LibCommons` + `LibNetworks`. 템플릿과 scaffold가 복사해 가는 범위 | `LibCommons/`, `LibNetworks/` |
| 패킷 | `[UInt16 LE 전체 길이][int32 LE packetId][protobuf payload]` 한 덩어리. 길이는 헤더 2바이트를 포함한다 | `BasePacket`, `BasePacket.HeaderSize` |
| 헤더 | 패킷 앞 2바이트 길이 필드. 값이 `HeaderSize`(2)보다 작으면 잘못된 헤더다 | `NetworkDisconnectReason.InvalidPacketHeader` |
| packetId | payload 앞 4바이트 int32. 메시지 종류를 나타낸다 | `ParseMessageFromPacket<T>` |
| 세션 | TCP 연결 하나와 그 송수신 상태. 세션당 worker Task 3개가 돈다 | `BaseSession` |
| **`BaseSessionClient`** (주의) | **서버가 accept한 클라이언트 연결**을 표현하는 서버 쪽 세션이다. 이름의 Client는 "상대가 클라이언트"라는 뜻이다 | `LibNetworks/Sessions/BaseSessionClient.cs` |
| **`BaseSessionServer`** (주의) | **클라이언트가 서버에 연결한** 클라이언트 쪽 세션이다. 이름의 Server는 "상대가 서버"라는 뜻이다 | `LibNetworks/Sessions/BaseSessionServer.cs` |
| **세션 팩토리** (주의) | 리스너는 `IClientSessionFactory`, 커넥터는 `IServerSessionFactory`로 세션을 만든다. 두 인터페이스는 **파일 이름과 타입 이름이 서로 뒤바뀌어 있다**(`IClientSessionFactory.cs`에 `IServerSessionFactory`가 있다). 타입 이름으로 찾는다 | `LibNetworks/Sessions/` |
| 리스너 | 포트를 열고 accept 루프를 돌리는 서버 객체 | `BaseListener`, `BaseMessageListener` |
| 커넥터 | 서버에 연결을 여러 개 맺는 클라이언트 객체 | `BaseConnector`, `BaseMessageConnector` |
| accept pump | 리스너의 accept 반복. 세션 생성은 pump 밖으로 넘겨 accept 지연을 줄인다 | `BaseListener` |
| outstanding accepts | 동시에 걸어 두는 accept 요청 수 | `BaseListener.StartAccept` 인자 |
| 관측 hook | 엔진이 이벤트마다 부르는 `protected virtual` 메서드. 기본 구현은 비어 있고 앱이 override한다 | `OnNetwork*`, `OnAccept*` |
| disconnect reason | 세션이 끊긴 이유 enum. 종료 경로마다 하나를 넘긴다 | `NetworkDisconnectReason` |
| 송신 큐 상한 | 아직 보내지 못한 바이트 예약 한도. 넘으면 송신 요청을 거부한다 | `SessionSendOptions` |
| 백프레셔 | 송신 큐가 차서 보내기를 늦추거나 거부하는 상태 | `OnNetworkSendBackpressure` 등 |
| drain 예산 | 송신 worker가 한 번에 내보내고 양보(`Yield`)하기 전까지 처리하는 양 | `SessionSendOptions` |

## 템플릿·scaffold

| 용어 | 뜻 | 코드 |
|---|---|---|
| 템플릿 | 새 게임 서버의 출발점 프로젝트. 엔진만 참조한다 | `template-projects/FastPortGameServerTemplate/` |
| 디스패처 | packetId로 `IPacketHandler`를 골라 호출하는 객체 | `PacketDispatcher` |
| 핸들러 | packetId 하나를 처리하는 클래스 | `IPacketHandler`, `EchoHandler` |
| scaffold | 템플릿과 엔진을 복사해 새 게임 서버 솔루션을 만드는 스크립트 | `scripts/scaffold-game-server.sh`, `.ps1` |
| golden | scaffold 결과의 기대 sha256·파일 트리. 엔진·템플릿 파일 내용이 들어가므로 주석만 바꿔도 갱신해야 한다 | `tests/scaffold/case-01-simple/expected/` |
| 사용자 정의 패킷 ID | 템플릿에서 새 패킷에 쓰는 ID 범위(2000 이상) | `template-projects/Protos/PacketIds.proto` |

## 부하·관측 도구

| 용어 | 뜻 | 코드 |
|---|---|---|
| 스모크 서버 | 계측이 들어간 echo 서버. 텔레메트리를 JSONL로 내보낸다 | `tests-projects/FastPortTestSmokeServer/` |
| 텔레메트리 | 서버 지표 수집·스냅샷과 JSONL 계약 | `tests-projects/LibTestTelemetry/` |
| JSONL | 한 줄에 스냅샷 JSON 하나를 쓰는 지표 파일. 대시보드와 LoadValidation이 읽는다 | `ServerTelemetryExportBackgroundService` |
| LoadRunner | 최대 10K 세션 부하 생성 CLI. 엔진(`LibNetworks`)을 쓰지 않고 자체 소켓을 쓴다 | `tests-projects/FastPortTestLoadRunner/` |
| LoadValidation | LoadRunner를 프로세스로 실행하고 서버 지표와 합쳐 단계별 합격·불합격을 판정하는 하네스 | `tests-projects/FastPortTestLoadValidation/` |
| idle 정리 | 일정 시간 수신이 없는 세션을 끊는 기능 | `SessionIdleTracker` |

## 저장소·운영

| 용어 | 뜻 |
|---|---|
| 필수 체크 | `main` ruleset이 머지 전에 요구하는 CI job: `build (ubuntu-latest)`, `build (macos-latest)`, `build (windows-latest)` |
| 릴리스 브랜치 | `builds/release`. `main`에서 PR로 승격한다 |
| `Design Ref: §...` | 코드 주석에 남은 과거 설계 문서 참조. 문서는 저장소에 없다 |
