# FastPortSharp Code Map

> LLM/에이전트용 저장소 지도. 코드를 열기 전에 이 문서로 위치를 찾고, 필요한 파일만 읽는다.
> 구조가 바뀌면 같은 커밋에서 이 문서도 갱신한다 (`AGENTS.md` Code Map Rule).
> 최종 갱신: 2026-10-05 (`main` 1aa6e63 + job 이름 변경 기준)

## 1. 한눈에 보기

- .NET 10 / C# 14. `SocketAsyncEventArgs` 기반 TCP 엔진(`LibCommons` + `LibNetworks`) + 게임 서버 템플릿 + 부하/검증 도구 + MAUI 대시보드.
- 솔루션 2개
  - `FastPortSharp.sln`: 엔진, 샘플 서버/클라이언트, 템플릿, 테스트 도구 (Linux/macOS/Windows 빌드 가능)
  - `FastPortSharp.Dashboard.sln`: `FastPortDashboard.Core` / `.Maui` / `FastPortDashboardTests` / `LibTestTelemetry` (MAUI workload 필요, macOS·Windows만)
- 와이어 포맷: `[UInt16 LE 전체 길이(헤더 포함)][int32 LE packetId][protobuf payload]`. 패킷 최대 65,535B.

## 2. 프로젝트 의존 그래프

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

템플릿은 `LibCommons` + `LibNetworks`만 참조한다 (`Protocols`/`FastPortServer`/테스트 프로젝트 참조 금지 — scaffold가 엔진만 복사).

## 3. 디렉터리 맵

| 경로 | 역할 |
|---|---|
| `LibCommons/` | 버퍼, `BasePacket`, ID 생성, 지연 통계, 타이머 큐 |
| `LibNetworks/` | Listener / Connector / Session 엔진 |
| `Protocols/Protos/` | 엔진 샘플용 proto (`commons.proto`: `ProtocolId`, `ResultCode`, `Header` / `tests.proto`: Ping·Echo·Error) |
| `FastPortServer/` | 엔진 샘플 서버 (Generic Host, Windows Service 지원) |
| `FastPortClient/` | 엔진 샘플 클라이언트 (`LatencyStats` 사용) |
| `template-projects/FastPortGameServerTemplate/` | 게임 서버 스타터 (Serilog, DI, dispatcher/handler) |
| `template-projects/FastPortGameServerTemplate.SampleClient/` | 템플릿 echo round-trip 검증 클라이언트 |
| `template-projects/Protos/` | 템플릿 공유 proto (`PacketIds.proto` enum, `Sample.proto` Echo 메시지). 각 소비 프로젝트가 `<Protobuf Include>`로 자체 생성 |
| `FastPortDashboard.Core/` | 대시보드 로직 (JSONL 폴링, 차트 수학, Echo 클라이언트, ViewModel) |
| `FastPortDashboard.Maui/` | MAUI UI (macOS Catalyst / Windows) |
| `tests-projects/FastPortTests/` | 엔진·도구 단위/통합 테스트 (MSTest) |
| `tests-projects/FastPortDashboardTests/` | 대시보드 Core 테스트 (MSTest) |
| `tests-projects/FastPortTestSmokeServer/` | 계측 포함 echo 서버 (텔레메트리 JSONL export, idle 정리) |
| `tests-projects/FastPortTestLoadRunner/` | 10K 세션 부하 생성기 (CLI) |
| `tests-projects/FastPortTestLoadValidation/` | 단계별 부하 검증 하네스 (LoadRunner 실행 + 서버 메트릭 병합·판정) |
| `tests-projects/LibTestTelemetry/` | 서버 텔레메트리 수집/스냅샷/JSONL 계약 |
| `scripts/scaffold-game-server.{sh,ps1}` | 템플릿 → 새 게임 서버 솔루션 생성 |
| `scripts/cloud/`, `scripts/load-validation/` | 클라우드(Azure/OCI) 부하 검증 보조 스크립트 |
| `tests/scaffold/` | scaffold golden 테스트 (`run.sh` / `run.ps1`, case-01~08) |
| `docs/` | 벤치마크 리포트, 부하 검증 runbook/가이드, 이 코드맵 |
| `.github/workflows/` | `build.yml`, `dashboard.yml`, `scaffold.yml` |

## 4. 엔진 핵심

### LibCommons

| 파일 | 타입 | 요점 |
|---|---|---|
| `IBuffers.cs` | `IBuffers` | `Write`, `Peek(ref byte[])`, `Drain`, `TryGetBasePackets`, `CanReadSize` |
| `ArrayPoolCircularBuffers.cs` | `ArrayPoolCircularBuffers` | **실사용 기본 구현**. `ArrayPool` 대여, 부족 시 확장(상한 없음 → 상한은 `BaseSession`이 강제), `Lock` 보호 |
| `BaseCircularBuffers.cs` / `BaseQueueBuffers.cs` | 레거시 구현 | 테스트·비교용 |
| `BasePacket.cs` | `BasePacket` | `HeaderSize = 2`, payload 복사본 보유, `Data`(ReadOnlySpan) |
| `IDGenerator.cs` | `IDGenerator` | 세션 ID 발급 |
| `LatencyStats.cs` | `LatencyStats` 외 | RTT/서버 처리/네트워크 지연 통계 (FastPortClient) |
| `Timers/` | `ITimerQueue`, `TimerQueue`, `IMonotonicTimeSource` | 단조 시계 기반 타이머 큐 (SmokeServer `SessionIdleTracker`가 사용) |

### LibNetworks

| 파일 | 타입 | 요점 |
|---|---|---|
| `BaseSocket.cs` | `BaseSocket` | listener/connector 공통 소켓 보유 |
| `BaseListener.cs` | `BaseListener` (abstract) | `StartAccept(ip, port[, backlog, outstandingAccepts])`, `RequestShutdown()`. accept → `IClientSessionFactory.Create` → `OnAccepted` (세션 생성은 accept pump 밖으로 offload). 관측 hook: `OnAcceptSucceeded/SessionCreated/SessionTaskStarted/AcceptFailed/ListenerSocketError` |
| `BaseMessageListener.cs` | `BaseMessageListener` | `BaseListener` + maxConnections 1000 (서버들이 상속) |
| `BaseConnector.cs` / `BaseMessageConnector.cs` | `BaseConnector` | `StartConnect(ip, port, connectionCount)` → `IServerSessionFactory.Create` |
| `Sessions/BaseSession.cs` | `BaseSession` (abstract, ~1.3K줄) | 세션 엔진 본체. 아래 5절 참고 |
| `Sessions/BaseSessionClient.cs` | `BaseSessionClient` | **서버 측에서 accept된 클라이언트 세션**. `Id`, `OnAccepted()` |
| `Sessions/BaseSessionServer.cs` | `BaseSessionServer` | **클라이언트 측에서 서버에 연결된 세션**. `OnConnected()` |
| `Sessions/IServerSessionFactory.cs` | `IClientSessionFactory` ⚠️ | 파일명과 타입명이 서로 뒤바뀜 (아래 9절) |
| `Sessions/IClientSessionFactory.cs` | `IServerSessionFactory` ⚠️ | 〃 |
| `Sessions/SessionSendOptions.cs` | `SessionSendOptions` | 송신 큐 상한(기본 1MB), chunk 64KB, drain 예산 |
| `Sessions/NetworkDisconnectReason.cs` | enum | `RemoteClosed`…`LocalShutdown`, `InvalidPacketHeader`, `ReceiveBufferOverflow`, `PacketHandlerError` |
| `Sessions/SendCompletionTracker.cs` | internal | 송신 완료 바이트 추적. 현재 엔진은 미사용, `FastPortTests`만 참조 (`InternalsVisibleTo`) |
| `Extensions/BasePacket+Extensions.cs` | `ParseMessageFromPacket<T>` | payload 선두 int32 packetId + protobuf 파싱 |
| `AddressConverter.cs` | `AddressConverter` | ip/port → `EndPoint` 변환 |
| `Extensions/Socket+Extensions.cs` | `SetKeepAlive` | TCP keep-alive 설정 확장 |
| `SocketEventsPool.cs` | internal | SAEA 풀. 현재 어디에서도 사용하지 않음 |

## 5. 데이터 흐름 (BaseSession)

```
[수신] SAEA ReceiveAsync (세션당 8KB 고정 배열, 동기 완료는 루프 처리)
  → ProcessReceiveCompleted: 미파싱 바이트 > MaxReceiveBufferedBytes(기본 1MB, virtual) 이면 disconnect(ReceiveBufferOverflow)
  → m_ReceivedBuffers.Write → SemaphoreSlim signal
  → Task DoWorkReceivedBuffers: TryGetBasePackets
       실패 시 header size < 2 → disconnect(InvalidPacketHeader), 아니면 partial로 대기
  → bounded Channel(1000) → Task DoWorkReceivedPackets → OnReceived(packet)
       handler 예외 → disconnect(PacketHandlerError)

[송신] TryRequestSendMessage(packetId, IMessage) / TryRequestSendBuffers(span)
  → 큐 바이트 예약(SessionSendOptions.MaxQueuedBytes 초과 시 거부) → ArrayPool 대여 + 헤더 기록
  → unbounded Channel → Task DoWorkSendBuffers: 최대 16 segment batch, drain 예산 후 Yield, transient 오류 backoff

[종료] RequestDisconnect(reason): 1회만 실행 → OnNetworkSessionDisconnected(reason) → CTS cancel → socket close → 채널 complete → OnDisconnected
```

- 세션당 백그라운드 Task 3개 (`DoWorkReceivedBuffers`, `DoWorkReceivedPackets`, `DoWorkSendBuffers`). `WaitSession()`으로 종료 대기.
- 관측 hook (`protected virtual OnNetwork*`): SocketError, PacketReceived, ReceiveCompleted, OperationDuration, BytesSent, SendRequested/Completed/Abandoned/Backpressure/Rejected/DrainYield/BufferSample, SessionDisconnected(reason). 엔진은 텔레메트리 구현을 모름 → SmokeServer 세션이 override해 `IServerTelemetry`로 연결.

## 6. 앱·템플릿

| 프로젝트 | 진입점 / 구성 | 핵심 타입 |
|---|---|---|
| FastPortServer | `Program.cs` (Generic Host), `appsettings.json`: `Logging`, `LatencyStats` | `FastPortServer : BaseMessageListener`, `FastPortClientSession : BaseSessionClient`, `FastPortClientSessionManager`(빈 stub) |
| FastPortClient | `Program.cs`, `appsettings.json`: `Logging`, `LatencyStats` | `FastPortConnector : BaseConnector`, `FastPortServerSession : BaseSessionServer` |
| GameServerTemplate | `Program.cs` DI: `IGameServerTelemetry`→`NullGameServerTelemetry`, `IPacketHandler`→`EchoHandler`, `PacketDispatcher`, `IClientSessionFactory`→`GameSessionFactory`, `GameServer`, `GameServerHostedService`. `appsettings.json`: `Serilog`, `GameServer` | `GameServer : BaseMessageListener`, `GameSession : BaseSessionClient` (`Send<T>`), `PacketDispatcher` (packetId → handler, 예외 catch), `GameSessionFactory` (`BufferCapacityBytes` 8KB) |
| Template.SampleClient | `appsettings.json`: `Serilog`, `SampleClient` | `SampleClientConnector : BaseMessageConnector`, `SampleClientSession : BaseSessionServer`, `SampleClientHostedService` (1001 전송 → 1002 대기) |
| SmokeServer | `appsettings.json`: `FastPortTestSmokeServer`, `SessionIdleCleanup` | `FastPortTestSmokeServer : BaseMessageListener`, `FastPortTestSmokeClientSession` (hook → 텔레메트리), `SessionIdleTracker`, `ServerTelemetryExportBackgroundService` (JSONL) |
| Dashboard.Core | — | `JsonlPollingAdapter`(SmokeServer JSONL 읽기), `LineChartMath`, `EchoClient*`(LibNetworks 연결), `DashboardViewModel`, `EchoClientViewModel` (CommunityToolkit.Mvvm) |

**템플릿에 패킷 추가**: `template-projects/Protos/`에 메시지 추가 → `PacketIds.proto` enum에 ID 추가(사용자 정의 ≥ 2000, C#에서는 `PACKET_IDS_` 접두어 제거됨) → `IPacketHandler` 구현(`PacketId => (int)PacketIds.X`) → `Program.cs`에 `AddSingleton<IPacketHandler, XHandler>()`.

## 7. 테스트·검증 도구

| 대상 | 위치 | 비고 |
|---|---|---|
| 엔진/도구 테스트 | `tests-projects/FastPortTests/*Tests.cs` | 파일명이 대상 타입을 따름 (예: `BaseSessionReceivePolicyTests`, `BaseSessionSendPolicyTests`, `ArrayPoolCircularBufferTest`, `TimerQueueTests`, `BaseListenerShutdownTests`). 소켓 테스트는 loopback `SocketPair` 헬퍼 사용 |
| 대시보드 테스트 | `tests-projects/FastPortDashboardTests/` | Adapters, Charts, EchoClient, ViewModels, E2E(Mock) |
| 부하 생성 | `FastPortTestLoadRunner` | 주요 옵션: `--host --port --sessions --payload --duration --ramp-up --rate --pacing-policy --output` |
| 부하 검증 | `FastPortTestLoadValidation` | `--profile --stage --server-metrics --runner-project --dry-run` 등. 서버 JSONL과 러너 결과 병합·임계값 판정 |
| scaffold | `tests/scaffold/run.sh` / `run.ps1` | case-01 sha256/tree golden. 실패 시 scaffold stdout/stderr 마지막 60줄 출력 |

## 8. 빌드·테스트·CI

```bash
dotnet build FastPortSharp.sln -c Release
dotnet test  FastPortSharp.sln -c Release                      # FastPortTests
dotnet test  tests-projects/FastPortDashboardTests -c Release  # Dashboard Core 테스트 (MAUI 불필요)
tests/scaffold/run.sh [--script ps1] [--update-golden case-01-simple] [case...]
```

| 워크플로 | 트리거 | job 이름 |
|---|---|---|
| `build.yml` | `main`, `builds/release` push/PR | `build (ubuntu-latest / macos-latest / windows-latest)` — main 필수 체크 |
| `dashboard.yml` | 위 브랜치 + Dashboard·엔진 경로 변경 시 | `dashboard (macos-latest / windows-latest)` |
| `scaffold.yml` | `main` push / 모든 PR + scaffold·템플릿·엔진·Protos 경로 변경 시 | `<os> / <sh|ps1>`, `cross-OS byte-identical compare` (windows/ps1 ≈ 16분) |

## 9. 주의사항 (자주 밟는 함정)

- **scaffold golden**: scaffold가 복사하는 `LibCommons/`, `LibNetworks/`, `template-projects/FastPortGameServerTemplate/`, `template-projects/Protos/` 파일 내용이 golden sha256에 포함됨 → 수정 시 `tests/scaffold/run.sh --update-golden case-01-simple` 필수 (주석만 바꿔도 해당).
- **팩토리 파일명 뒤바뀜**: `IClientSessionFactory.cs`에 `IServerSessionFactory`가, `IServerSessionFactory.cs`에 `IClientSessionFactory`가 선언됨. 타입 검색은 파일명이 아니라 타입명으로.
- **Client/Server 명명**: `BaseSessionClient` = 서버가 accept한 세션, `BaseSessionServer` = 클라이언트가 연결한 세션.
- **미사용 코드**: `BaseSession` 생성자의 `sendbuffers`(IBuffers), `BaseListener.C_MaxConnections`(저장만 함), `SocketEventsPool`, `FastPortClientSessionManager`(빈 stub).
- **Dashboard.sln**에는 `LibCommons`/`LibNetworks`가 포함되지 않아 Release 빌드에서도 ProjectReference가 Debug 구성으로 빌드됨.
- **MAUI CI**: `--no-restore` Release 빌드 전 restore에도 `-p:Configuration=Release` 필요 (NETSDK1047/1112).
- **macOS symlink**: `/var` → `/private/var`. 절대 경로 sln 빌드 시 ProjectReference 중복 restore 경합 → scaffold smoke build는 dest로 이동 후 상대 경로로 빌드.
- **릴리스 브랜치 이름**: `builds/release` (`builds.release` 아님).
- **코드 주석의 `Design Ref: §...`**: 과거 설계 문서 참조이며 해당 문서는 저장소에 없음.
