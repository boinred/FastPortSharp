# 엔진 샘플 앱 (FastPortServer, FastPortClient, Protocols, appsettings, Windows 서비스)

엔진(`LibCommons` + `LibNetworks`)을 최소한으로 쓰는 예시 서버·클라이언트와, 엔진 샘플·테스트 도구가 공유하는 proto 프로젝트다. 실제 게임 서버 출발점은 템플릿이고, 이 앱들은 엔진 사용 예와 지연 측정 실험용이다.

**이 문서를 읽는 경우**: 샘플 서버·샘플 클라이언트 실행, 엔진 사용 예, 리스너·커넥터를 Generic Host에 붙이는 방법, `FastPortServer` 포트·호스트 설정, Windows 서비스 등록, `appsettings.json`의 `Logging`·`LatencyStats`, 클라이언트 RTT 측정, `Protocols/Protos/` 프로토콜 정의(`ProtocolId`, `ResultCode`, `Header`, Ping·Echo·Error).

**다른 문서로 가는 경우**: 새 게임 서버를 만들거나 패킷 핸들러 구조가 필요함 → [game-server-template.md](game-server-template.md)(템플릿은 `Protocols`가 아니라 `template-projects/Protos/`를 쓴다). 리스너·커넥터 내부 → [listener-connector.md](listener-connector.md). 세션 송수신 → [session.md](session.md). `LatencyStats`·`BasePacket` → [packet-buffers.md](packet-buffers.md). 계측 echo 서버·부하 → [load-testing.md](load-testing.md).

## 핵심 규칙

- **`FastPortServer`는 echo하지 않는다.** `FastPortClientSession.OnReceived`는 로그만 남긴다. `FastPortServer.csproj`는 `Protocols`를 참조하지 않아 proto를 해석하지도 않는다.
- **`FastPortClient`의 echo 왕복 상대는 `FastPortTestSmokeServer`다.** 둘 다 기본 포트 6628이다. `FastPortClient`를 `FastPortServer`에 붙이면 첫 `EchoRequest`를 보낸 뒤 응답이 오지 않아 멈춘다.
- `FastPortClient` 접속 대상은 `FastPortClientBackgroundService.ExecuteAsync`의 `StartConnect("127.0.0.1", 6628, 1)`로 하드코딩돼 있다. 설정으로 바꿀 수 없다.
- `FastPortServer` 주소는 `Program.cs`가 `FastPortServer` 설정 섹션의 `Host`·`Port`를 읽어 `FastPortServerOptions`로 등록한다. 기본값은 `0.0.0.0`, `6628`이다. 배포된 `appsettings.json`에는 이 섹션이 없어 기본값이 쓰인다. `Host`는 IP 문자열이어야 한다(`AddressConverter`가 DNS를 안 함).
- 서버 수명: `FastPortServerBackgroundService.ExecuteAsync` → `StartAccept(Host, Port)`(backlog 기본값) → 1초 `Task.Delay` 루프. `StopAsync` → `RequestShutdown()`. accept된 세션은 따로 닫지 않는다.
- **Windows 서비스는 패키지 참조만 있다.** `FastPortServer.csproj`가 `Microsoft.Extensions.Hosting.WindowsServices`를 참조하지만 `Program.cs`에 `UseWindowsService()` 호출이 없다. 서비스로 돌리려면 `Host.CreateDefaultBuilder(args)` 다음에 추가해야 한다.
- **서버 `appsettings.json`의 `LatencyStats` 섹션은 읽히지 않는다.** 서버 코드에 `LatencyStats` 사용처가 없다. 클라이언트만 `LatencyStats` 섹션을 `LatencyStatsOptions`로 바인딩한다.
- 클라이언트 지연 측정: `Program.cs`가 `LatencyStats` 싱글톤을 만들고 `FastPortServerSession.ConfigureLatencyStats`로 static 필드에 넣는다. Ctrl+C(`Console.CancelKeyPress`)·`ProcessExit`에서 `PrintLatencyStats`·`SaveLatencyStatsAsync`를 호출한다.
- echo 루프: `OnConnected` → `SendEchoRequest(1)`. 응답마다 `OnReceived`가 `ParseMessageFromPacket<EchoResponse>` → `RecordSample(T1~T4)` → 다음 `requestId`로 재전송한다. 동시에 1개 요청만 날아간다.
- 패킷 ID는 `(int)ProtocolId.Tests`(=1) 하나다. Echo·Ping 구분은 packetId가 아니라 메시지 타입으로 한다. 서버 쪽은 `FastPortTestSmokeClientSession`이 `ProtocolId.Tests`가 아니면 프로토콜 오류로 기록한다.
- 세션 버퍼: 두 팩토리 모두 `new ArrayPoolCircularBuffers(8 * 1024)`를 수신·송신용으로 하나씩 넘긴다. 송신용 `IBuffers`는 엔진이 쓰지 않는다 → [session.md](session.md).
- 세션 ID: `FastPortClientSession`은 static `IDGenerator`로 `Id`를 만든다.

## 파일·타입

| 파일 | 타입·심볼 | 내용 |
|---|---|---|
| `FastPortServer/Program.cs` | top-level | `Host.CreateDefaultBuilder(args)`, `FastPortServer` 섹션 → `FastPortServerOptions`, DI: `FastPortServerBackgroundService`, `IClientSessionFactory`→`FastPortClientSessionFactory`, `FastPortServer` |
| `FastPortServer/FastPortServer.cs` | `FastPortServer : BaseMessageListener` | 본문 없는 리스너 |
| `FastPortServer/FastPortServerOptions.cs` | `FastPortServerOptions` | `Host`(`0.0.0.0`), `Port`(6628) |
| `FastPortServer/FastPortServerBackgroundService.cs` | `FastPortServerBackgroundService : BackgroundService` | `StartAccept` / `RequestShutdown` 호출 |
| `FastPortServer/Sessions/FastPortClientSession.cs` | `FastPortClientSession : BaseSessionClient` | `OnReceived`·`OnAccepted`·`OnDisconnected` 로그만 |
| `FastPortServer/Sessions/FastPortClientSessionFactory.cs` | `FastPortClientSessionFactory : IClientSessionFactory` | 세션 + 8KB 버퍼 생성 |
| `FastPortServer/Sessions/FastPortClientSessionManager.cs` | `FastPortClientSessionManager` | 빈 stub. 어디에서도 쓰지 않는다 |
| `FastPortServer/appsettings.json` | `Logging`, `LatencyStats` | `LatencyStats`는 미사용 |
| `FastPortClient/Program.cs` | top-level | `LatencyStats` 바인딩·싱글톤, DI: `FastPortClientBackgroundService`, `IServerSessionFactory`→`FastPortServerSessionFactory`, `FastPortConnector`(Transient), 종료 시 통계 출력 |
| `FastPortClient/FastPortConnector.cs` | `FastPortConnector : BaseConnector` | 생성자만 있음 |
| `FastPortClient/FastPortClientBackgroundService.cs` | `FastPortClientBackgroundService` | `StartConnect("127.0.0.1", 6628, 1)`, `StopAsync`에서 `RequestDisconnect()` |
| `FastPortClient/Sessions/FastPortServerSession.cs` | `FastPortServerSession : BaseSessionServer` | `SendMessage<T>`, `SendEchoRequest`, `ConfigureLatencyStats`, `PrintLatencyStats`, `SaveLatencyStatsAsync` |
| `FastPortClient/Sessions/FastPortServerSessionFactory.cs` | `FastPortServerSessionFactory : IServerSessionFactory` | 세션 + 8KB 버퍼 생성 |
| `FastPortClient/appsettings.json` | `Logging`, `LatencyStats` | `OutputDirectory: "Stats"`, `OutputFilePrefix: "latency_stats"` |
| `Protocols/Protocols.csproj` | `<Protobuf Include="Protos\**\*.proto" GrpcServices="Both">` | 빌드 시 C# 생성. `Google.Protobuf`, `Grpc.AspNetCore` 참조 |
| `Protocols/Protos/commons.proto` | `ProtocolId`(`Tests`=1), `ResultCode`(`Ok`, `Error`), `Empty`, `Header` | C# 네임스페이스 `FastPort.Protocols.Commons`. `Header`는 `request_id`와 T1~T3 타임스탬프(Stopwatch ticks) |
| `Protocols/Protos/tests.proto` | `PingRequest/Response`, `EchoRequest/Response`, `ErrorResponse`, `service TestService` | C# 네임스페이스 `FastPort.Protocols.Tests`. Ping·Error·`TestService`는 코드에서 쓰지 않는다 |

`Protocols` 참조 프로젝트: `FastPortClient`, `FastPortTestSmokeServer`, `FastPortTestLoadRunner`. proto 필드를 바꾸면 세 곳 모두 확인한다.

## 작업별 시작점

| 하려는 작업 | 고칠 곳 | 같이 확인할 것 |
|---|---|---|
| 샘플 서버에 패킷 처리 추가 | `FastPortServer.csproj`에 `Protocols` 참조 추가 → `FastPortClientSession.OnReceived`에서 `ParseMessageFromPacket` → `RequestSendMessage` | 참고 구현 `FastPortTestSmokeClientSession.OnReceived`. 구조화된 dispatcher는 템플릿 쪽 |
| 서버 포트·바인드 주소 변경 | `appsettings.json`에 `"FastPortServer": { "Host", "Port" }` 추가, 또는 환경 변수 `FastPortServer__Port`, 또는 `--FastPortServer:Port=...` 인자 | `FastPortServerOptions` 기본값 |
| 클라이언트 접속 대상 변경 | `FastPortClientBackgroundService.ExecuteAsync`의 `StartConnect` 인자 | 설정화하려면 옵션 클래스 추가 |
| Windows 서비스로 실행 | `FastPortServer/Program.cs`에 `builder.UseWindowsService()` | 서비스 계정의 작업 디렉터리, 로그 대상 |
| 지연 통계 출력 경로·주기 변경 | `FastPortClient/appsettings.json`의 `LatencyStats` | `LatencyStatsOptions`(`LibCommons/LatencyStats.cs`). 경로는 프로세스 현재 디렉터리 기준 |
| 클라이언트 동시 연결 늘리기 | `FastPortConnector`를 여러 개 resolve해 각각 `StartConnect` | `connectionCount` 인자는 무시된다 → [listener-connector.md](listener-connector.md) |
| 새 proto 메시지 추가 | `Protocols/Protos/tests.proto` 또는 새 `.proto`(자동 포함) | packetId 체계가 `ProtocolId` 하나뿐이므로 필요하면 enum 값 추가 |
| 서버에 세션 목록 관리 추가 | `FastPortClientSessionManager` 구현 + 팩토리·`OnDisconnected`에서 등록·해제 | `RequestShutdown`이 세션을 닫지 않는 점 |

## 실행

```bash
dotnet build FastPortSharp.sln -c Release
dotnet run --project FastPortServer -c Release                          # 0.0.0.0:6628 listen, echo 없음
dotnet run --project tests-projects/FastPortTestSmokeServer -c Release  # echo 상대가 필요할 때 (6628)
dotnet run --project FastPortClient -c Release                          # 127.0.0.1:6628 접속, Ctrl+C 시 통계 출력·저장
```

- `FastPortServer`와 `FastPortTestSmokeServer`는 둘 다 6628을 쓰므로 동시에 띄우지 않는다.
- `FastPortClient` 통계 파일은 `Stats/latency_stats_yyyy-MM-dd_HH-mm-ss.log`에 생긴다.
- 로그 레벨: 두 `appsettings.json` 모두 `FastPortClient`·`FastPortServer` 카테고리를 `Debug`로 둔다. 수신마다 Debug 로그가 찍힌다.

## 테스트

- 샘플 앱 전용 테스트는 없다. CI(`build.yml`)는 `FastPortSharp.sln` 빌드와 `dotnet test`만 하므로 샘플 앱은 컴파일만 검증된다.
- proto 메시지의 실제 송수신은 `tests-projects/FastPortTests/FastPortTestSmokeServerTests.cs`, `FastPortTestLoadRunnerTests.cs`가 간접 검증한다.

```bash
dotnet test tests-projects/FastPortTests -c Release --filter "FullyQualifiedName~FastPortTestSmokeServerTests"
```

## 주의

- 샘플 앱은 scaffold 복사 대상이 아니다. 단, 샘플 수정 중 `LibCommons/**`·`LibNetworks/**`를 건드리면 주석만 바꿔도 `tests/scaffold/run.sh --update-golden case-01-simple`과 `tests/scaffold/run.sh` 전체 통과가 필요하다.
- 이름이 반대다. 서버 앱의 세션은 `FastPortClientSession`(`BaseSessionClient`), 클라이언트 앱의 세션은 `FastPortServerSession`(`BaseSessionServer`)이다.
- 팩토리 인터페이스는 파일명이 뒤바뀌어 있다(`Sessions/IServerSessionFactory.cs`에 `IClientSessionFactory`). 타입명으로 찾는다.
- `Protocols`는 엔진 샘플용이다. 템플릿·scaffold 결과물은 `Protocols`를 참조하면 안 된다(`template-projects/Protos/` 사용).
- `FastPortServerSession`의 `m_LatencyStats`는 static이다. 한 프로세스에서 여러 세션이 같은 통계에 기록한다.
- `FastPortClient/Program.cs`는 `Console.CancelKeyPress`(`Environment.Exit(0)` 호출)와 `ProcessExit` 양쪽에서 통계를 출력·저장한다. 종료 처리를 바꿀 때 두 핸들러를 함께 본다.
