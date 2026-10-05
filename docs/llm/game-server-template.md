# 게임 서버 템플릿 (GameServerTemplate·패킷 핸들러·디스패처·DI·Serilog·샘플 클라이언트·scaffold·golden hash)

`template-projects/`는 엔진(`LibCommons` + `LibNetworks`) 위에 올린 게임 서버 스타터다. `scripts/scaffold-game-server.{sh,ps1}`가 이 템플릿과 엔진을 복사해 새 게임 서버 솔루션을 만들고, `tests/scaffold/`가 그 출력을 golden 파일로 검증한다.

**이 문서를 읽는 경우**: 템플릿 서버 DI 구성, `IPacketHandler`·`PacketDispatcher` 동작, 템플릿에 새 패킷 추가, `GameSession.Send<T>`, Serilog·`GameServer` 설정, 샘플 클라이언트 echo 검증, scaffold로 새 게임 서버 생성, scaffold golden hash 갱신·CI 실패.

**다른 문서로 가는 경우**: 세션 수신·송신·종료 엔진 동작 → [session.md](session.md). `BaseListener`·`BaseConnector`·팩토리 인터페이스 → [listener-connector.md](listener-connector.md). `BasePacket`·버퍼·와이어 포맷 → [packet-buffers.md](packet-buffers.md). `FastPortServer`/`FastPortClient` 샘플 → [sample-apps.md](sample-apps.md). 빌드·CI 전체 → [platform.md](platform.md). 용어 → [glossary.md](glossary.md).

## 핵심 규칙

- **템플릿은 `LibCommons` + `LibNetworks`만 참조한다.** `Protocols`, `FastPortServer`, `FastPortClient`, `LibTestTelemetry`, 테스트 프로젝트를 참조하면 안 된다. scaffold가 엔진 두 개와 템플릿만 복사하므로 다른 참조는 생성된 솔루션에서 깨진다(csproj 주석에도 명시).
- **golden hash 대상 파일을 바꾸면 golden을 갱신한다.** `LibCommons/**`, `LibNetworks/**`, `template-projects/FastPortGameServerTemplate/**`, `template-projects/Protos/**`는 scaffold 출력에 그대로 들어가 `case-01-simple`의 sha256에 포함된다. 주석 한 줄, `README.md`, `QUICKSTART.ko.md` 수정도 해당한다. scaffold 스크립트가 생성하는 `.gitignore`·`.gitattributes`·`README.md` 내용을 바꿔도 같다.
- **sh와 ps1은 byte-identical 출력을 내야 한다.** CI의 `cross-OS byte-identical compare` job이 OS·flavor별 `case-01` sha256을 서로 비교한다. 예: csproj의 Protos 상대 경로는 두 스크립트 모두 `\` 구분자로 쓴다(sh는 `/`를 `\`로 치환). 한쪽 스크립트만 고치지 않는다.
- **핸들러 예외는 세션을 끊지 않는다.** `PacketDispatcher.Dispatch`가 모든 예외를 catch해 로그와 `IGameServerTelemetry.OnHandlerException`만 남긴다. 엔진의 `PacketHandlerError` disconnect까지 예외가 올라가지 않는다.
- 핸들러가 없는 packetId는 경고 로그 후 버린다. payload가 4바이트 미만이면 packetId는 `-1`이다.
- `PacketDispatcher`는 `handlers.ToDictionary(h => h.PacketId)`로 만든다. 같은 `PacketId` 핸들러를 두 개 등록하면 생성 시 예외로 호스트 시작이 실패한다.
- packet ID 대역: `1000-1999`는 템플릿 내장(Echo `1001`/`1002`), 사용자 정의는 `2000` 이상이다(`PacketIds.proto` 주석).
- `PacketIds` enum 값은 `PACKET_IDS_` 접두어로 쓴다. C# 생성기가 접두어를 떼서 `PacketIds.EchoRequest`처럼 노출한다. C#에서는 `(int)` 캐스트가 필요하다.
- `GameSession.Send<T>`는 `RequestSendMessage`를 호출하고 성공 여부(bool)를 버린다. 송신 큐 상한 초과 거부는 호출자에게 보이지 않는다(→ [session.md](session.md)).

## 파일·타입

| 파일 | 타입·심볼 | 내용 |
|---|---|---|
| `template-projects/FastPortGameServerTemplate/Program.cs` | Generic Host | Serilog(`ReadFrom.Configuration`) 연결, `GameServerOptions` 바인딩, DI 등록: `IGameServerTelemetry`→`NullGameServerTelemetry`, `IPacketHandler`→`EchoHandler`, `PacketDispatcher`, `IClientSessionFactory`→`GameSessionFactory`, `GameServer`, `AddHostedService<GameServerHostedService>` |
| `.../FastPortGameServerTemplate/appsettings.json` | `Serilog`, `GameServer` | Console sink, `ListenAddress` `0.0.0.0`, `ListenPort` `7777`, `MaxSessions` `1024` |
| `.../Configuration/GameServerOptions.cs` | `GameServerOptions` | `SectionName = "GameServer"`. `MaxSessions`는 시작 로그에만 쓰이고 연결 수를 제한하지 않는다 |
| `.../Application/GameServer.cs` | `GameServer : BaseMessageListener` | 빈 서브클래스. 생성자에서 `IClientSessionFactory`를 받는다 |
| `.../Application/GameServerHostedService.cs` | `GameServerHostedService : BackgroundService` | `ExecuteAsync` → `StartAccept(ListenAddress, ListenPort)`, `StopAsync` → `RequestShutdown()` |
| `.../Application/PacketDispatcher.cs` | `PacketDispatcher` | payload 선두 int32 LE로 packetId 읽기 → 핸들러 호출, 처리 시간 측정, 예외 catch |
| `.../Handlers/IPacketHandler.cs` | `IPacketHandler` | `int PacketId { get; }`, `void Handle(GameSession, BasePacket)` |
| `.../Handlers/EchoHandler.cs` | `EchoHandler` | `EchoRequest` 파싱 → `EchoResponse{Message, ServerUnixMs}`를 `PacketIds.EchoResponse`로 응답 |
| `.../Sessions/GameSession.cs` | `GameSession : BaseSessionClient` | `IDGenerator`로 `Id` 발급, `Send<T>(int, IMessage<T>)`, `OnReceived` → `PacketDispatcher.Dispatch`, accept/disconnect 텔레메트리 |
| `.../Sessions/GameSessionFactory.cs` | `GameSessionFactory : IClientSessionFactory` | `BufferCapacityBytes = 8 * 1024`. 세션마다 `ArrayPoolCircularBuffers` 두 개 생성 |
| `.../Telemetry/IGameServerTelemetry.cs`, `NullGameServerTelemetry.cs` | `IGameServerTelemetry`, `NullGameServerTelemetry` | 세션 accept/disconnect, packet received/handled, handler exception hook. 기본 구현은 no-op |
| `.../FastPortGameServerTemplate.csproj` | `<Protobuf Include="..\Protos\*.proto">` | `Protos/`의 모든 proto를 자체 어셈블리에 생성. 엔진 `ProjectReference` 두 개 |
| `.../README.md`, `QUICKSTART.ko.md` | 문서 | scaffold 출력에 복사된다(golden 대상) |
| `template-projects/FastPortGameServerTemplate.SampleClient/` | `SampleClientConnector : BaseMessageConnector`, `SampleClientSession : BaseSessionServer`, `SampleClientSessionFactory : IServerSessionFactory`, `SampleClientHostedService`, `EchoSignal`, `SampleClientOptions` | 연결 시 `EchoRequest`(1001) 전송 → `EchoResponse`(1002) 수신 시 RTT 기록 → `EchoSignal` 완료. 10초 타임아웃. `ExitAfterOneEcho`면 종료 |
| `.../SampleClient/appsettings.json` | `Serilog`, `SampleClient` | `Host` `127.0.0.1`, `Port` `7777`, `Message`, `ExitAfterOneEcho` `true` |
| `template-projects/Protos/PacketIds.proto` | `PacketIds` enum | `PACKET_IDS_UNSPECIFIED = 0`, `PACKET_IDS_ECHO_REQUEST = 1001`, `PACKET_IDS_ECHO_RESPONSE = 1002` |
| `template-projects/Protos/Sample.proto` | `EchoRequest`, `EchoResponse` | `package fastport.sample`, `csharp_namespace = "FastPortGameServerTemplate.Protocols"` |
| `scripts/scaffold-game-server.sh`, `.ps1` | 12단계 scaffold | 아래 "scaffold와 golden 테스트" |
| `tests/scaffold/run.sh`, `run.ps1`, `case-*/`, `_shared/` | golden 러너 | `_shared/blocked-tokens.txt`(이름 차단 목록, 스크립트도 읽음), `_shared/name-validation.txt` |
| `.github/workflows/scaffold.yml` | `cases`, `compare` job | OS×flavor 매트릭스 + 교차 비교 |

## 작업별 시작점

| 하려는 작업 | 고칠 곳 | 같이 확인할 것 |
|---|---|---|
| 새 패킷·핸들러 추가 | `template-projects/Protos/*.proto`, `Handlers/`, `Program.cs` | 아래 절차, golden 갱신, `FastPortDashboard.Core` 빌드(같은 Protos 사용) |
| 핸들러 예외·미등록 packetId 정책 변경 | `Application/PacketDispatcher.cs` | `IGameServerTelemetry` hook 호출 순서 |
| 텔레메트리 구현 교체 | `IGameServerTelemetry` 구현 추가 → `Program.cs` 등록 교체 | `GameSession`, `PacketDispatcher`, `GameSessionFactory`가 주입받음 |
| 버퍼 크기 조정 | `Sessions/GameSessionFactory.cs`의 `BufferCapacityBytes` | 수신 상한 동작 → [session.md](session.md) |
| 포트·주소 변경 | `appsettings.json`의 `GameServer` | 샘플 클라이언트 `SampleClient.Port` |
| 세션 수명 hook 추가 | `Sessions/GameSession.cs` (`OnAccepted`, `OnDisconnected`) | 엔진 hook 목록 → [session.md](session.md) |
| scaffold 동작 변경 | `scripts/scaffold-game-server.sh`와 `.ps1` 둘 다 | `tests/scaffold/case-*`, golden, cross-OS compare |
| 이름 차단 토큰 추가 | `tests/scaffold/_shared/blocked-tokens.txt` | `_shared/name-validation.txt` 기대값 |
| 템플릿 폴더·파일 추가 | 템플릿 디렉터리 | `case-01-simple/expected/tree.txt`·`sha256.txt` 갱신, `files-present.txt` |

## 새 패킷 추가 절차

1. 메시지를 정의한다. `template-projects/Protos/`에 새 `.proto`를 추가하거나 `Sample.proto`에 메시지를 더한다. 새 파일은 `package fastport.sample;`와 `option csharp_namespace = "FastPortGameServerTemplate.Protocols";`를 그대로 쓴다(scaffold가 이 토큰을 새 이름으로 치환한다).
2. csproj는 고치지 않는다. 템플릿·SampleClient·`FastPortDashboard.Core`의 `<Protobuf Include>`가 `*.proto` 와일드카드라 새 파일도 빌드 때 자동 생성된다.
3. `template-projects/Protos/PacketIds.proto`의 `PacketIds` enum에 값을 추가한다. 이름은 `PACKET_IDS_MY_REQUEST = 2001;`처럼 `PACKET_IDS_` 접두어를 붙이고 값은 `2000` 이상으로 한다. C#에서는 `PacketIds.MyRequest`가 된다.
4. `template-projects/FastPortGameServerTemplate/Handlers/`에 `IPacketHandler` 구현을 만든다. `EchoHandler`를 본뜬다.
   - `public int PacketId => (int)PacketIds.MyRequest;`
   - `Handle`에서 `packet.ParseMessageFromPacket<MyRequest>(out _, out var request)`(`LibNetworks.Extensions`)로 파싱하고, 실패하면 로그 후 return 한다.
   - 응답은 `session.Send((int)PacketIds.MyResponse, response);`로 보낸다.
5. `Program.cs`에 `builder.Services.AddSingleton<IPacketHandler, MyHandler>();`를 추가한다. `PacketDispatcher`가 `IEnumerable<IPacketHandler>`로 모두 받는다.
6. `dotnet build FastPortSharp.sln -c Release`로 확인한다. 클라이언트 쪽 송수신이 필요하면 SampleClient의 `SampleClientSession`도 고친다.
7. `tests/scaffold/run.sh --update-golden case-01-simple` 후 `tests/scaffold/run.sh` 전체를 통과시킨다. proto 파일을 추가하면 `tree.txt`도 바뀐다.

## scaffold와 golden 테스트

- 사용법: `scripts/scaffold-game-server.sh <NewProjectName> <DestinationPath> [--protos-path PATH] [--force] [--no-git] [--skip-smoke] [--dry-run]`. ps1은 `-ProtosPath`, `-Force`, `-NoGit`, `-SkipSmoke`, `-DryRun`, `-Help`이고 PowerShell 7 이상이 필요하다.
- 이름 규칙: `^[A-Z][A-Za-z0-9]{0,63}$`이고 `blocked-tokens.txt`에 없어야 한다.
- 종료 코드: `0` 성공, `2` 입력 검증 실패, `3` 대상 충돌(`--force` 필요), `4` smoke build 실패, `5` IO·git·dotnet 오류.
- 복사: 템플릿 → `<dest>/<NewName>`, `template-projects/Protos` → `<dest>/Protos`(또는 `--protos-path`), `LibCommons`·`LibNetworks` → `<dest>/` 그대로. `bin`, `obj`, `*.user`는 제외한다. **SampleClient는 복사하지 않는다.**
- 토큰 치환: `FastPortGameServerTemplate` → `<NewName>`을 템플릿 하위 텍스트 파일과 복사된 proto 파일에만 적용한다. 엔진 폴더는 건드리지 않는다. csproj의 `..\..\LibCommons`·`..\..\LibNetworks`는 `..\`로, `..\Protos`는 실제 Protos 상대 경로(`\` 구분자)로 바꾼다.
- 생성: `.gitignore`, `.gitattributes`, `README.md`, `<dest 폴더명>.sln`(`--format sln`, 프로젝트 3개 + `Protos` solution folder). sln 이름은 프로젝트 이름이 아니라 대상 폴더 이름이다.
- smoke build는 dest로 `cd`(ps1은 `Push-Location`)한 뒤 상대 경로 sln으로 `dotnet build -c Release`를 한다. macOS `/var` → `/private/var` symlink 때문에 절대 경로로 빌드하면 같은 프로젝트를 중복 restore해 `obj`가 충돌한다.

| case | 인자 요약 | 검증 |
|---|---|---|
| `case-01-simple` | `MyLobbyServer --no-git --skip-smoke` | exit 0, stdout `[1/12]`·`[12/12]`·`Done.`, 필수 파일, 원래 토큰 폴더 부재, **sha256·tree golden** |
| `case-02-blocked-name` | `Application` | exit 2, stderr `blocked tokens list` |
| `case-03-regex-meta` | `My$Game` | exit 2, stderr `does not match required pattern` |
| `case-04-existing-dest-no-force` | 비어 있지 않은 dest(`input/pre/`) | exit 3, 기존 파일 유지 |
| `case-05-existing-dest-with-force` | 같은 dest + `--force` | exit 0, 기존 파일 삭제, 새 파일 존재 |
| `case-06-dry-run` | `--dry-run` | exit 0, `[DRY-RUN]` 계획 출력, dest 미생성 |
| `case-07-no-git-no-smoke` | `Foo --no-git --skip-smoke` | `.git` 없음, 11·12단계 skip 메시지 |
| `case-08-external-protos` | `--protos-path {DEST}-protos --no-git` | Protos가 dest 밖에 생성, `<dest>/Protos` 없음, **smoke build 실행**(dotnet 필요) |

- golden 비교: `compute_sha256`은 `.git/`, `bin/`, `obj/`, `*.sln`, `*.bak`를 제외한다. `tree.txt`는 `*.sln`을 포함한다. 둘 다 `--update-golden` 시 `case-01-simple`만 다시 쓴다.
- 실패 시 러너가 diff 앞 40줄과 scaffold stdout/stderr 마지막 60줄을 출력하고 tmpdir를 남긴다.
- CI `scaffold.yml`: `main` push와 모든 PR에서 scaffold 스크립트·`tests/scaffold/**`·템플릿·`template-projects/Protos/**`·엔진·`.gitattributes`·워크플로 경로가 바뀔 때 돈다(`workflow_dispatch`도 가능). job 이름은 `<os> / <flavor>`(ubuntu·macos는 sh·ps1, windows는 ps1·sh 총 6개)와 `cross-OS byte-identical compare`다. windows/ps1이 가장 느리다(약 16분). main 필수 체크는 아니다.

## 테스트·실행

```bash
dotnet build FastPortSharp.sln -c Release                                        # 템플릿·SampleClient 포함
dotnet run --project template-projects/FastPortGameServerTemplate -c Release     # 0.0.0.0:7777 대기
dotnet run --project template-projects/FastPortGameServerTemplate.SampleClient -c Release  # echo 1회 후 종료
tests/scaffold/run.sh                                  # 전체 case, sh flavor
tests/scaffold/run.sh --script ps1 case-01-simple      # ps1로 단일 case
tests/scaffold/run.sh --update-golden case-01-simple   # golden 재생성
pwsh -NoProfile -File tests/scaffold/run.ps1 -Script ps1 -UpdateGolden -Cases case-01-simple
scripts/scaffold-game-server.sh MyLobbyServer ../my-lobby --dry-run
```

- 샘플 클라이언트 성공 로그는 `Echo round-trip succeeded`, 실패는 `Echo round-trip timed out (10s).`다.
- 템플릿 전용 단위 테스트 프로젝트는 없다. 검증은 빌드, SampleClient 왕복, scaffold 러너로 한다.

## 주의

- `IPacketHandler.cs` 주석의 "registering them in PacketDispatcher"는 실제와 다르다. 등록은 `Program.cs` DI에서 한다.
- `QUICKSTART.ko.md` 5절 예시의 `PacketId => PacketIds.MyRequest`, `session.Send(PacketIds.MyResponse, ...)`는 `(int)` 캐스트가 빠져 그대로는 컴파일되지 않는다. 위 절차를 따른다.
- `template-projects/Protos`는 템플릿·SampleClient 외에 `FastPortDashboard.Core`도 `<Protobuf Include>`로 쓴다. 기존 메시지·ID를 바꾸면 대시보드 Echo 클라이언트도 깨질 수 있다(→ [dashboard.md](dashboard.md)).
- 엔진 `Protocols/Protos/`(`commons.proto`, `tests.proto`)와 템플릿 `template-projects/Protos/`는 별개다. 템플릿에서 `Protocols` 프로젝트를 참조하지 않는다.
- `run.sh --script ps1`의 인자 변환에는 `--protos-path` → `-ProtosPath`가 없다(`run.ps1`에는 있다). ps1 flavor로 case-08을 돌릴 때는 `run.ps1`을 쓴다. CI도 ps1 flavor는 `run.ps1`로 돈다.
- scaffold 스크립트 주석의 `Design Ref`·`Plan Ref`가 가리키는 `docs/01-plan`, `docs/02-design` 등은 저장소에 없다.
- `GameServer`는 `BaseMessageListener`를 상속해 `BaseListener`에 `1000`을 넘기지만 엔진은 `C_MaxConnections`에 저장만 한다. `GameServer:MaxSessions` 설정도 연결 수를 제한하지 않는다. 동시 접속 제한이 필요하면 직접 구현한다.
