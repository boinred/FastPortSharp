# 세션 (`BaseSession` 수신·송신·종료, 송신 큐, 백프레셔, disconnect reason, 관측 hook)

`LibNetworks/Sessions/`는 연결 하나를 맡는 세션 엔진이다. 본체는 `BaseSession.cs`(약 1.3K줄)이고, 수신 파싱, 패킷 핸들러 호출, 송신 큐, 연결 종료, 관측 hook이 모두 이 파일에 있다.

**이 문서를 읽는 경우**: 세션 수명, 수신 흐름, 패킷 핸들러(`OnReceived`) 예외, 수신 버퍼 상한, 잘못된 패킷 헤더, 송신 요청·거부, 송신 큐 상한, 백프레셔, drain 예산, 연결 종료 순서, disconnect reason 추가, 관측 hook(`OnNetwork*`) 추가, 세션 팩토리, `BaseSessionClient`/`BaseSessionServer` 차이.

**다른 문서로 가는 경우**: 와이어 포맷, `BasePacket`, 링버퍼 내부 → [packet-buffers.md](packet-buffers.md). accept·connect와 세션 생성 시점 → [listener-connector.md](listener-connector.md). hook을 텔레메트리로 잇는 SmokeServer 세션, idle 정리 → [load-testing.md](load-testing.md). 템플릿 `GameSession` → [game-server-template.md](game-server-template.md). 용어 → [glossary.md](glossary.md).

## 핵심 규칙

- **이름이 반대다.** `BaseSessionClient`는 서버가 accept한 세션이다(`Id`, `OnAccepted()`). `BaseSessionServer`는 클라이언트가 서버에 연결한 세션이다(`OnConnected()`).
- **팩토리 파일명도 반대다.** `IClientSessionFactory.cs`에 `IServerSessionFactory`(`BaseSessionServer Create(Socket)`)가, `IServerSessionFactory.cs`에 `IClientSessionFactory`(`BaseSessionClient Create(Socket)`)가 있다. 타입명으로 찾는다.
- 생성자가 소켓 옵션(KeepAlive, Linger 1초, NoDelay)을 걸고 백그라운드 Task 3개(`DoWorkReceivedBuffers`, `DoWorkReceivedPackets`, `DoWorkSendBuffers`)를 바로 시작한다. **수신은 `RequestReceived()`를 불러야 시작한다.** `OnAccepted()`와 `OnConnected()`가 이를 부른다. override하면 `base`를 부르거나 직접 `RequestReceived()`를 부른다.
- **`RequestDisconnect(reason)`는 한 번만 실행된다**(`m_DisconnectRequested`에 `Interlocked.CompareExchange`). 두 번째 호출은 `false`다. 순서: `OnNetworkSessionDisconnected(reason)` → `AbandonPendingSendRequests` → `ClearQueuedSendBytesForDisconnect` → CTS cancel → socket `Shutdown`/`Close` → 송신 큐·수신 패킷 채널 `TryComplete` → `OnEventSessionDisconnected`(여기에 `OnDisconnected`가 구독돼 있다).
- 인자 없는 `RequestDisconnect()`는 `NetworkDisconnectReason.Unknown`이다. 원인을 아는 곳에서는 reason을 넘긴다.
- **`OnReceived`는 세션당 한 task에서 순서대로 불린다.** 수신 패킷 채널은 `Channel.CreateBounded(1000)`, `FullMode = Wait`, single reader다. 핸들러가 느리면 parser가 막히고, 그동안 수신 바이트가 쌓여 결국 `ReceiveBufferOverflow`로 끊긴다. 이것이 수신 쪽 백프레셔다.
- **`OnReceived`에서 처리하지 않은 예외는 세션을 `PacketHandlerError`로 끊는다.** worker가 조용히 죽지 않게 하려는 것이다. 계속 살려야 하는 오류는 핸들러 안에서 잡는다(템플릿 `PacketDispatcher`가 그렇게 한다).
- 수신 상한은 `protected virtual int MaxReceiveBufferedBytes`(기본 `DefaultMaxReceiveBufferedBytes` = 1MB)다. `ProcessReceiveCompleted`가 "버퍼에 남은 바이트 + 이번 수신 바이트"가 상한을 넘으면 쓰기 전에 끊는다. **`ushort.MaxValue`보다 낮추면 정상 대형 패킷도 끊긴다.**
- 길이 필드가 `BasePacket.HeaderSize`(2)보다 작으면 패킷이 영원히 완성되지 않는다. `TryGetBasePackets` 실패 뒤 `HasInvalidPacketHeader()`가 이를 판정해 `InvalidPacketHeader`로 끊는다. 앞의 정상 패킷은 먼저 전달된다.
- 수신 콜백은 동기 완료를 재귀가 아니라 `RequestReceived`의 `while` 루프로 처리한다. SAEA 수신 버퍼는 세션당 8KB 고정 배열(`m_ReceivedSocketBuffers`)이다.
- **송신은 여러 스레드에서 불러도 된다.** 송신 큐는 unbounded `Channel`(single reader, multi writer)이다. 크기 제한은 채널이 아니라 바이트 예약(`TryReserveQueuedSendBytes`, CAS 루프)으로 건다.
- `TryRequestSendBuffers` 거부 조건: 빈 버퍼, 헤더 포함 `ushort.MaxValue` 초과(로그만, hook 없음), 이미 종료 중(`OnNetworkSendRejected`), 큐 바이트 상한 초과(`OnNetworkSendBackpressure` + `OnNetworkSendRejected`), 채널이 닫힘(예약 롤백 후 `OnNetworkSendRejected`). `RequestSendMessage`/`RequestSendBuffers`는 결과를 버린다. 거부를 알아야 하면 `TryRequestSendMessage`/`TryRequestSendBuffers`를 쓴다.
- **송신 버퍼 소유권**: 패킷 배열은 `ArrayPool<byte>.Shared`에서 빌린다. 큐에 넣기 전 실패하면 즉시 반환하고 예약을 롤백한다. 전송 완료(`AdvanceSendItems`), worker 종료(`ReturnPendingSendBuffers`, `DrainQueuedSendBuffers`)에서 반환한다. 새 경로를 만들면 반환 지점을 반드시 넣는다.
- `DoWorkSendBuffers`는 최대 `MaxSendBatchSegments`(16)개 segment를 `SendChunkBytes`까지 묶어 보낸다. 한 wake에서 `MaxDrainBytesPerSignal` 바이트 또는 `MaxDrainOperationsPerSignal` 회를 넘기면 `Task.Yield`한다(`OnNetworkSendDrainYield`). `NoBufferSpaceAvailable`/`WouldBlock`은 transient로 보고 `TransientSendBackoffMs` 후 재시도한다. 그 밖의 `SocketException`은 `SendSocketError`, 0바이트 전송은 `SendZeroBytes`로 끊는다.
- `SessionSendOptions` 기본값: `MaxQueuedBytes` 1MB, `SendChunkBytes` 64KB, `MaxDrainBytesPerSignal` 256KB, `MaxDrainOperationsPerSignal` 4, `TransientSendBackoffMs` 1. 엔진은 `Normalized*` 속성만 읽는다. 현재 앱·템플릿은 모두 `SessionSendOptions.Default`를 쓴다(옵션 생성자 오버로드는 테스트만 쓴다).
- 관측 hook은 `protected virtual`이고 기본 구현은 비어 있다. 엔진은 텔레메트리를 모른다. hook 안에서 예외를 던지거나 오래 막지 않는다. hook은 수신 콜백·worker task 위에서 불린다.
- `OnNetworkSessionDisconnected(reason)` 기본 구현은 인자 없는 오버로드를 부른다. `OnNetworkSocketError(phase, ...)`도 2인자 오버로드를 부른다. 옛 오버로드만 override한 하위 클래스를 깨지 않으려는 구조다.

## 파일·타입

| 파일 | 타입·심볼 | 내용 |
|---|---|---|
| `LibNetworks/Sessions/BaseSession.cs` | `BaseSession` | 수신: `RequestReceived`, `ProcessReceiveCompleted`, `DoWorkReceivedBuffers`, `HasInvalidPacketHeader`, `DoWorkReceivedPackets`. 송신: `TryRequestSendMessage<T>`, `TryRequestSendBuffers`, `RequestSendString`, `DoWorkSendBuffers`, `BuildSendSegmentsAsync`, `SendSocketAsync`(virtual). 종료: `RequestDisconnect`, `IsDisconnected`, `WaitSession`, `OnEventSessionDisconnected` |
| 〃 | 상수 | `DefaultMaxReceiveBufferedBytes`(1MB), `MaxSendBatchSegments`(16), `SendBufferBackpressureThresholdBytes`(1MB) |
| 〃 | 내부 타입 | `SendQueueItem`(pooled 배열, `Offset`, `MarkTelemetryRegistered`), `ReceivedPacketItem` |
| 〃 | hook | `OnNetworkSessionDisconnected`, `OnNetworkSocketError`, `OnNetworkPacketReceived`, `OnNetworkReceiveCompleted`, `OnNetworkOperationDuration`, `OnNetworkBytesSent`, `OnNetworkSendRequested`, `OnNetworkSendCompleted`, `OnNetworkSendAbandoned`, `OnNetworkSendBackpressure`, `OnNetworkSendRejected`, `OnNetworkSendDrainYield`, `OnNetworkSendBufferSample`, `GetNetworkTimestamp`, `MarkNetworkActivity`, `LastReceivedTimestamp` |
| `LibNetworks/Sessions/BaseSessionClient.cs` | `BaseSessionClient` | 서버 측 accept 세션. abstract `Id`, `OnAccepted()`. `BaseListener`가 팩토리 생성 직후 `OnAccepted()`를 부른다 |
| `LibNetworks/Sessions/BaseSessionServer.cs` | `BaseSessionServer` | 클라이언트 측 연결 세션. `OnConnected()`. `BaseConnector`가 `Task.Run`으로 부른다 |
| `LibNetworks/Sessions/IClientSessionFactory.cs` | `IServerSessionFactory` | 파일명과 반대. `BaseConnector`가 쓴다 |
| `LibNetworks/Sessions/IServerSessionFactory.cs` | `IClientSessionFactory` | 파일명과 반대. `BaseListener`가 쓴다 |
| `LibNetworks/Sessions/SessionSendOptions.cs` | `SessionSendOptions` (record) | 송신 큐·chunk·drain 예산·backoff 설정, `Default` |
| `LibNetworks/Sessions/NetworkDisconnectReason.cs` | `NetworkDisconnectReason` | `Unknown`(0) ~ `PacketHandlerError`(10) |
| `LibNetworks/Sessions/SendCompletionTracker.cs` | `SendCompletionTracker` (internal) | 바이트 drain을 요청 단위 완료로 바꾸는 추적기. 엔진 미사용, `BaseSessionSendPolicyTests`만 쓴다(`InternalsVisibleTo("FastPortTests")`) |

disconnect reason이 나오는 곳: `RemoteClosed`(수신 0바이트), `ReceiveSocketError`(수신 완료 오류), `ReceiveRequestError`(`ReceiveAsync` 예외), `SendSocketError`, `SendZeroBytes`, `InvalidPacketHeader`, `ReceiveBufferOverflow`, `PacketHandlerError`는 `BaseSession` 안이다. `IdleTimeout`은 SmokeServer `SessionIdleTracker`가 넘긴다. `LocalShutdown`은 정의만 있고 엔진·앱에서 넘기는 곳이 없다.

`OnNetworkOperationDuration`의 operation 이름: `receive-buffer-write`, `receive-signal-to-parse`, `receive-packet-extract`, `receive-packet-channel-write`, `receive-packet-queue-delay`, `receive-packet-handler`, `send-enqueue`. `OnNetworkSocketError`의 phase: `receive-completion`, `receive-request`, `send-transient`, `send`, `send-worker`.

## 작업별 시작점

| 하려는 작업 | 고칠 곳 | 같이 확인할 것 |
|---|---|---|
| 수신 버퍼 상한 바꾸기 | 세션 하위 클래스에서 `MaxReceiveBufferedBytes` override (엔진 기본값은 `DefaultMaxReceiveBufferedBytes`) | 65,535 이상 유지, `BaseSessionReceivePolicyTests`의 overflow 케이스 |
| 송신 큐 상한·drain 예산 조정 | `SessionSendOptions` 기본값, 또는 팩토리에서 5인자 생성자로 옵션 전달 | `SendBufferBackpressureThresholdBytes`(1MB 고정)는 `MaxQueuedBytes`가 1MB를 넘어야 의미가 있다. `BaseSessionSendPolicyTests` |
| 새 disconnect reason 추가 | `NetworkDisconnectReason`에 값 추가(번호 재사용 금지) → `BaseSession`에서 `RequestDisconnect(새값)` | SmokeServer `FastPortTestSmokeClientSession.ToTelemetryReason` 문자열 매핑, `BaseSessionSendPolicyTests` 내부 `ToTelemetryReason` 복사본, 대시보드·부하 검증이 reason 문자열을 읽는지 |
| 관측 hook 추가 | `BaseSession`에 빈 `protected virtual OnNetwork*` 추가 후 호출 지점 삽입 | SmokeServer 세션 override → `IServerTelemetry`([load-testing.md](load-testing.md)), 테스트 세션 override |
| 패킷 핸들러 예외 정책 변경 | `DoWorkReceivedPackets`의 `catch` 블록 | `BaseSession_PacketHandlerThrows_DisconnectsAndWorkersComplete`, 템플릿 `PacketDispatcher`의 자체 catch |
| 잘못된 헤더 판정 변경 | `HasInvalidPacketHeader`, `DoWorkReceivedBuffers` | `ArrayPoolCircularBuffers.TryGetBasePackets`의 멈춤 조건([packet-buffers.md](packet-buffers.md)) |
| 송신 실패를 호출자가 알게 하기 | 호출부를 `TryRequestSendMessage`로 교체 | 템플릿 `GameSession.Send<T>`, `FastPortServerSession.SendMessage`는 결과를 버린다 |
| 테스트에서 송신 실패 흉내 | `SendSocketAsync` 두 오버로드 override | `BaseSessionSendPolicyTests`의 `_sendOverride` 패턴 |
| 세션 종료 후 정리 추가 | `OnDisconnected` override(마지막에 `base.OnDisconnected()`) 또는 `OnEventSessionDisconnected` 구독 | 이벤트는 `RequestDisconnect` 끝에서 한 번 호출된다. `EchoClientConnector`가 구독 예시다 |

## 테스트

- `tests-projects/FastPortTests/BaseSessionReceivePolicyTests.cs`: 잘못된 헤더(정상 패킷 뒤·단독), partial 패킷, 1바이트 단위 분할 수신, 수신 상한 초과, 핸들러 예외. 테스트 세션 `ReceiveTestSession`이 `MaxReceiveBufferedBytes`와 `OnNetworkSessionDisconnected`를 override한다.
- `BaseSessionSendPolicyTests.cs`: 큐 바이트 상한 거부, `SendCompletionTracker`, `SessionSendOptions` 정규화, drain yield, transient backpressure, partial send 완료, FIFO 완료, chunk 제한, 닫힌 큐 거부, disconnect reason 기록, `LastReceivedTimestamp` 갱신, pending abandon.
- `SessionIdleTrackerTests.cs`: `IdleTimeout` 종료. `BaseListenerShutdownTests.cs`: 팩토리 예외([listener-connector.md](listener-connector.md)).
- 소켓 테스트는 각 파일 안의 private `SocketPair`(loopback `TcpListener` port 0 + `TcpClient`)로 실제 연결을 만든다. 공용 헬퍼 파일은 없다. 새 테스트 파일도 같은 패턴을 복사한다.

```bash
dotnet test tests-projects/FastPortTests -c Release --filter "FullyQualifiedName~BaseSessionReceivePolicyTests"
dotnet test tests-projects/FastPortTests -c Release --filter "FullyQualifiedName~BaseSessionSendPolicyTests"
```

## 주의

- **`BaseSession.cs`는 통째로 읽지 않는다.** `grep -n "private async Task DoWork\|RequestDisconnect\|protected virtual" LibNetworks/Sessions/BaseSession.cs`처럼 메서드명으로 찾고 그 부분만 연다.
- **scaffold golden**: `LibNetworks/**`를 바꾸면 주석만 바꿔도 `tests/scaffold/run.sh --update-golden case-01-simple` 후 `tests/scaffold/run.sh` 전체 통과가 필요하다.
- 생성자의 `sendbuffers`(`IBuffers`) 인자는 받기만 하고 쓰지 않는다. 송신은 `ArrayPool` + 채널이다. `OnSent()`도 선언만 있고 호출되지 않는다.
- `SendQueueItem.MarkTelemetryRegistered` 전에는 send worker가 그 항목을 drain하지 않는다(`WaitForSendTelemetryRegistrationAsync`). `OnNetworkSendRequested`가 `OnNetworkSendCompleted`보다 먼저 찍히게 하려는 순서다. 이 순서를 바꾸면 외부 텔레메트리의 pending 수가 어긋난다.
- disconnect 직후 enqueue가 성공하는 race는 `RecordPendingSendRequested`가 abandon과 예약 롤백으로 보정한다.
- `OnDisconnected`는 `OnEventSessionDisconnected`에서 자기 구독을 해제한다. override에서 `base.OnDisconnected()`를 빼면 구독이 남는다.
- `IsDisconnected`는 "종료 요청됨"이다. 소켓이 실제로 닫혔는지와 worker 종료는 `WaitSession()`으로 기다린다.
