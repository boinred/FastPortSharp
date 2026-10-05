# 리스너·커넥터 (accept, 서버 시작·종료, 접속, 세션 팩토리, backlog, keep-alive, 주소 변환)

`LibNetworks`의 소켓 진입점이다. 서버 쪽은 `BaseListener`가 accept한 소켓을 `IClientSessionFactory`로 세션으로 만들고, 클라이언트 쪽은 `BaseConnector`가 연결한 소켓을 `IServerSessionFactory`로 세션으로 만든다. 세션 이후의 송수신은 이 문서 범위가 아니다.

**이 문서를 읽는 경우**: 리스너 상속, accept pump, `StartAccept`·`RequestShutdown`, listen backlog·outstanding accept 수, accept 실패·소켓 오류 관측 hook, 최대 동시 접속 수, 커넥터 `StartConnect`, 접속 실패 처리, 세션 팩토리 등록, keep-alive 설정, ip/port → `EndPoint` 변환.

**다른 문서로 가는 경우**: 세션 송수신·종료 사유·`OnNetwork*` hook → [session.md](session.md). 버퍼·`BasePacket` → [packet-buffers.md](packet-buffers.md). 리스너를 쓰는 앱 예시 → [sample-apps.md](sample-apps.md), [game-server-template.md](game-server-template.md). accept 텔레메트리·backlog 튜닝 실측 → [load-testing.md](load-testing.md). 용어(Client/Server 세션 명명) → [glossary.md](glossary.md).

## 핵심 규칙

- **명명이 반대다.** `BaseListener`(서버)는 `IClientSessionFactory.Create(Socket)` → `BaseSessionClient`를 만든다. `BaseConnector`(클라이언트)는 `IServerSessionFactory.Create(Socket)` → `BaseSessionServer`를 만든다. "Client 세션 = 서버가 accept한 상대"다.
- **팩토리 파일명이 뒤바뀌어 있다.** `Sessions/IClientSessionFactory.cs`에는 `IServerSessionFactory`가, `Sessions/IServerSessionFactory.cs`에는 `IClientSessionFactory`가 선언돼 있다. 파일명이 아니라 타입명으로 grep한다.
- `StartAccept` 오버로드는 3개다. `(ip, port)`는 backlog `C_DefaultListenBacklog`(4096), outstanding accept `C_DefaultOutstandingAccepts`(1)를 쓴다. `(ip, port, backlog)`, `(ip, port, backlog, outstandingAccepts)`로 바꿀 수 있다.
- 값 보정: backlog ≤ 0이면 4096으로 바꾼다(`NormalizeListenBacklog`, 로컬 함수). outstanding accept ≤ 0이면 1, 64(`C_MaxOutstandingAccepts`) 초과면 64로 자른다(`NormalizeOutstandingAccepts`, `internal static`, 테스트가 직접 호출하므로 시그니처 유지).
- **시작 순서**: `AddressConverter.TryToEndPoint` → `Bind` → `Listen(backlog)` → `m_bIsRunning = true` → outstanding 수만큼 `SocketAsyncEventArgs` 생성 → 각각 `Accept`. 초기 `Accept` 하나라도 실패하면 `RequestShutdown()` 후 `false`를 돌려준다.
- **accept pump**: args 하나는 `AcceptAsync` 하나만 담당한다. 동기 완료는 재귀 없이 `Accept`의 `while` 루프로 처리한다. 비동기 완료는 `OnSocketEventsAcceptCompleted` → `ProcessAccept` → 같은 args로 `Accept` 재등록이다.
- **세션 생성은 pump 밖에서 한다.** `ProcessAccept`는 `OnAcceptSucceeded` 호출 후 `ThreadPool.UnsafeQueueUserWorkItem(new AcceptedSessionWork(...), preferLocal: false)`로 넘긴다. `RunAcceptedSessionWork`가 `m_ClientSessionFactory.Create` → `OnAcceptSessionCreated` → `OnAcceptSessionTaskStarted` → `clientSession.OnAccepted()` 순서로 실행한다. pump 스레드에서 무거운 일을 하지 않는다.
- hook 이름은 `OnAcceptSucceeded`, `OnAcceptSessionCreated`, `OnAcceptSessionTaskStarted`, `OnAcceptFailed`, `OnListenerSocketError`다. 이름에 Task가 있어도 별도 Task는 없다. work item 안에서 `OnAccepted` 직전에 호출된다.
- `OnAcceptFailed`·`OnListenerSocketError`는 `phase` 문자열이 있는 오버로드와 없는 호환용 오버로드가 있다. phase 버전의 기본 구현이 호환용을 부른다. phase 값: `start-endpoint`, `start-bind-listen`, `accept-start`, `accept-completion`, `accept-completion-null-socket`, `accept-process`, `accept-session-work`, `shutdown-close`(`OnListenerSocketError`만).
- 모든 hook은 텔레메트리 타입을 모른다. 엔진에 텔레메트리 의존을 넣지 말고 하위 클래스에서 override한다(예: `FastPortTestSmokeServer`).
- **종료**: `RequestShutdown()`은 `Interlocked.CompareExchange(ref m_bIsRunning, false, true)`로 1회만 실행하고 리스닝 소켓을 `Close()`한다. 두 번 불러도 안전하다. 대기 중인 accept는 `OperationAborted`로 끝나며, running이 아니면 실패로 기록하지 않는다. args는 각 pump가 끝날 때 `Dispose`된다.
- `BaseSocket.RequestDisconnect()`는 `Connected`가 아니면 바로 반환하므로 리스너 종료에 쓰지 않는다. 리스너는 반드시 `RequestShutdown()`이다.
- `RequestShutdown()`은 이미 accept한 세션을 닫지 않는다(세션 관리자 없음, `TODO`). 세션 정리는 앱 책임이다.
- 종료한 리스너 인스턴스는 재시작할 수 없다(소켓을 새로 만들지 않음). 같은 포트로 다시 열려면 새 인스턴스를 만든다.
- `C_MaxConnections`는 생성자 인자를 저장만 하고 접속 수를 제한하지 않는다. `BaseMessageListener`는 이 값으로 1000을 넘긴다.
- **커넥터는 인스턴스당 소켓 1개다.** `StartConnect(ip, port, connectionCount)`의 `connectionCount`는 쓰이지 않는다. `BaseSocket.m_Socket`·`m_SocketEvent` 하나로 `ConnectAsync`를 한 번 건다. 여러 연결이 필요하면 커넥터를 여러 개 만든다(`FastPortClient`는 `AddTransient<FastPortConnector>`).
- `StartConnect`의 `true`는 "연결 시도 시작"이다. 연결 실패는 `OnSocketEventsConnectedCompleted`에서 로그만 남기고, 재시도·hook·반환값이 없다. 성공하면 `IServerSessionFactory.Create(m_Socket)` 후 `Task.Run(() => session.OnConnected())`다. 커넥터와 세션이 같은 `Socket`을 공유한다.
- `AddressConverter.TryToEndPoint`는 `IPAddress.TryParse`만 한다. 호스트명(`localhost` 등)은 실패한다. IP 문자열만 넘긴다.

## 파일·타입

| 파일 | 타입·심볼 | 내용 |
|---|---|---|
| `LibNetworks/BaseSocket.cs` | `BaseSocket`, `m_Socket`, `m_SocketEvent`, `RequestDisconnect` | TCP 소켓 1개와 SAEA 1개를 가진 공통 부모. `m_SocketEvent`는 커넥터만 쓴다 |
| `LibNetworks/BaseListener.cs` | `BaseListener` (abstract), `StartAccept`, `RequestShutdown`, `Accept`, `ProcessAccept`, `AcceptedSessionWork`, `RunAcceptedSessionWork`, `NormalizeOutstandingAccepts` | accept pump, 세션 생성 offload, 관측 hook, 상수 `C_DefaultListenBacklog`·`C_DefaultOutstandingAccepts`·`C_MaxOutstandingAccepts` |
| `LibNetworks/BaseMessageListener.cs` | `BaseMessageListener` | `BaseListener`에 maxConnections 1000을 넘기는 얇은 하위 클래스. 앱 서버들이 상속한다 |
| `LibNetworks/BaseConnector.cs` | `BaseConnector`, `StartConnect`, `OnSocketEventsConnectedCompleted` | 단일 연결, `IServerSessionFactory`로 세션 생성 |
| `LibNetworks/BaseMessageConnector.cs` | `BaseMessageConnector` | 생성자만 있는 하위 클래스. 템플릿 SampleClient·Dashboard가 쓴다 |
| `LibNetworks/Sessions/IServerSessionFactory.cs` | `IClientSessionFactory` | `BaseSessionClient Create(Socket clientSocket)` (파일명 주의) |
| `LibNetworks/Sessions/IClientSessionFactory.cs` | `IServerSessionFactory` | `BaseSessionServer Create(Socket connectedSocket)` (파일명 주의) |
| `LibNetworks/AddressConverter.cs` | `AddressConverter.TryToEndPoint` | ip 문자열 + port → `IPEndPoint` |
| `LibNetworks/Extensions/Socket+Extensions.cs` | `SocketExtensions.SetKeepAlive` | `SIO_KEEPALIVE_VALS` 설정. Windows에서만 적용, 다른 OS는 아무것도 안 한다. 기본 1000ms/1000ms. 엔진·앱 어디에서도 호출하지 않는다 |
| `LibNetworks/SocketEventsPool.cs` | `SocketEventsPool` (internal) | SAEA 스택 풀. 사용처 없음 |
| `LibNetworks/Properties/AssemblyInfo.cs` | `InternalsVisibleTo("FastPortTests")` | `NormalizeOutstandingAccepts` 테스트 접근 |

세션 소켓의 실제 keep-alive는 `BaseSession` 생성자의 `SetSocketOption(..., SocketOptionName.KeepAlive, true)`다 → [session.md](session.md).

## 상속·사용처

| 하위 클래스 | 부모 | 팩토리 |
|---|---|---|
| `FastPortServer.FastPortServer` | `BaseMessageListener` | `FastPortClientSessionFactory` |
| `FastPortTestSmokeServer.FastPortTestSmokeServer` | `BaseMessageListener` (hook 전부 override → `IServerTelemetry`) | `FastPortTestSmokeClientSessionFactory` |
| `GameServer` (템플릿) | `BaseMessageListener` | `GameSessionFactory` |
| `FastPortClient.FastPortConnector` | `BaseConnector` | `FastPortServerSessionFactory` |
| `SampleClientConnector` (템플릿) | `BaseMessageConnector` | `SampleClientSessionFactory` |
| `EchoClientConnector` (Dashboard) | `BaseMessageConnector`를 내부에서 생성 | `EchoClientSessionFactory` |

## 작업별 시작점

| 하려는 작업 | 고칠 곳 | 같이 확인할 것 |
|---|---|---|
| 새 서버 앱에서 리스너 상속하기 | `BaseMessageListener` 상속 + `IClientSessionFactory` 구현 → DI 등록 | 호스팅 서비스 `StartAsync`/`StopAsync`에서 `StartAccept`/`RequestShutdown` (예: `GameServerHostedService`, `FastPortServerBackgroundService`) |
| listen backlog·outstanding accept 조정 | 호출부에서 4-인자 `StartAccept` 사용 | `FastPortTestSmokeServerBackgroundService`의 `ListenBacklog`·`OutstandingAccepts` 옵션, 상한 64 |
| 기본 backlog·상한 변경 | `BaseListener` 상수 | `ServerTelemetryTests`의 `BaseListener_NormalizeOutstandingAccepts_*` 기대값 |
| 최대 동시 접속 수 바꾸기 | 현재 강제 로직 없음. `BaseListener`의 `C_MaxConnections`를 실제로 검사하도록 `ProcessAccept` 또는 `RunAcceptedSessionWork`에 추가해야 한다 | 세션 수 추적 주체(세션 관리자 없음), 초과 소켓 닫기, `OnAcceptFailed` phase 추가 |
| accept 실패 관측 추가 | 하위 클래스에서 `OnAcceptFailed(string phase, ...)` override | 새 phase를 추가하면 `FastPortTestSmokeServer`·`LibTestTelemetry` 분류도 확인 |
| 세션 생성 지연 계측 | `OnAcceptSessionCreated`·`OnAcceptSessionTaskStarted` override | `acceptCompletedTimestamp`는 `Stopwatch.GetTimestamp()` 기준 |
| 종료 시 접속 세션도 정리 | 앱 쪽 세션 목록 → 각 세션 `RequestDisconnect` | `RequestShutdown`은 리스닝 소켓만 닫는다 |
| 커넥터 연결 실패 처리·재시도 | `BaseConnector.OnSocketEventsConnectedCompleted` | 현재 로그만 남김. Dashboard `EchoClientConnector`는 자체 상태 머신으로 감싼다 |
| 다중 연결 클라이언트 | 연결마다 커넥터 인스턴스 생성 | `connectionCount` 미사용. 대량 부하는 `FastPortTestLoadRunner`(자체 소켓) → [load-testing.md](load-testing.md) |
| 호스트명으로 접속 | `AddressConverter.TryToEndPoint`에 DNS 해석 추가 | 리스너·커넥터 둘 다 이 함수를 쓴다 |
| keep-alive 시간 설정 | `SetKeepAlive`를 세션 소켓에 호출 | Windows 전용 구현. Linux/macOS는 `SocketOptionName.TcpKeepAliveTime` 등 별도 필요 |

## 테스트

- `tests-projects/FastPortTests/BaseListenerShutdownTests.cs`: `RequestShutdown` 후 같은 포트 재바인딩, 이중 호출 안전성.
- `tests-projects/FastPortTests/ServerTelemetryTests.cs`: `BaseListener_NormalizeOutstandingAccepts_UsesDefaultForInvalidValues`, `BaseListener_NormalizeOutstandingAccepts_ClampsLargeValues`.
- `tests-projects/FastPortTests/FastPortTestSmokeServerTests.cs`: 실제 accept·echo 왕복과 accept 텔레메트리. `FastPortTestSmokeServer_MultipleOutstandingAccepts_EchoesAndRecordsTelemetry`가 outstanding accept 2개를 검증한다.
- `BaseConnector` 단위 테스트는 없다. `FastPortDashboardTests/EchoClient/EchoClientConnectorTests.cs`는 소켓 없는 상태 머신만 본다.

```bash
dotnet test tests-projects/FastPortTests -c Release --filter "FullyQualifiedName~BaseListener"
dotnet test tests-projects/FastPortTests -c Release --filter "FullyQualifiedName~FastPortTestSmokeServerTests"
```

## 주의

- `LibNetworks/**` 수정은 주석만 바꿔도 scaffold golden hash가 바뀐다. `tests/scaffold/run.sh --update-golden case-01-simple` 후 `tests/scaffold/run.sh` 전체 통과를 확인한다.
- `BaseListener` 생성자 시그니처, hook 시그니처를 바꾸면 `FastPortServer`, `FastPortTestSmokeServer`, 템플릿 `GameServer`가 함께 깨진다. 템플릿은 scaffold 대상이다.
- `StartAccept`의 `Bind`/`Listen` 예외는 `OnAcceptFailed`와 `OnListenerSocketError`를 둘 다 부른다. 카운터가 두 번 오르는 것이 의도다.
- `BaseConnector`의 로그 문자열 일부가 `BaseListener, ...`로 시작한다(복사 흔적). 로그 검색 시 혼동하지 않는다.
- 같은 커넥터에 `StartConnect`를 두 번 부르면 `Completed` 핸들러가 중복 등록되고 이미 연결된 소켓에 `ConnectAsync`를 건다. 재사용하지 않는다.
- `SocketEventsPool`, `C_MaxConnections`, `SetKeepAlive`는 미사용이다. 동작한다고 가정하지 않는다.
