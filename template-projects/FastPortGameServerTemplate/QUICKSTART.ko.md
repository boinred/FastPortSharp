# FastPortGameServerTemplate — 5분 부트스트랩

이 템플릿은 FastPort 네트워크 엔진(`FastPort.Common` + `FastPort.Networks`,
즉 monorepo 안의 `LibCommons` + `LibNetworks`) 위에서 게임 서버를 빠르게
시작하기 위한 출발점입니다. Generic Host + Serilog + Protobuf + 샘플
session/handler/dispatcher가 이미 wired-up 되어 있고, 외부 의존은 엔진 둘뿐입니다.

## 1. 빌드

```bash
# 레포 루트에서
dotnet build FastPortSharp.sln -c Release
```

## 2. 실행

```bash
dotnet run --project template-projects/FastPortGameServerTemplate -c Release
```

콘솔에 다음 로그가 보이면 정상:

```
[INF] GameServer starting. ListenAddress=0.0.0.0, ListenPort=7777, MaxSessions=1024
[INF] GameServer listening. Press Ctrl+C to stop.
```

## 3. TCP 연결 확인 (1초)

```bash
nc -zv 127.0.0.1 7777
# Connection to 127.0.0.1 port 7777 succeeded!
```

연결을 맺으면 서버 로그에 세션 라이프사이클이 찍힙니다:

```
[INF] GameSession accepted. Id=1
[INF] GameSession disconnected. Id=1
```

## 4. 설정 변경

`appsettings.json`에서 listen 주소/포트, 로그 레벨을 바꿀 수 있습니다.

```json
{
  "Serilog": { "MinimumLevel": "Debug" },
  "GameServer": {
    "ListenAddress": "0.0.0.0",
    "ListenPort": 7777,
    "MaxSessions": 1024
  }
}
```

## 5. 새 패킷 핸들러 추가

1. `../FastPortGameServerTemplate.Contracts/Protocols/MyGame.proto` 추가
   (`csharp_namespace`은 기존 `Sample.proto`와 동일 패턴). Contracts lib 빌드
   시 Grpc.Tools가 C# 클래스를 자동 생성하며 Template/SampleClient/Dashboard
   가 공유합니다.
2. 새 packet id를
   `../FastPortGameServerTemplate.Contracts/Handlers/PacketIds.cs`에 추가
   (사용자 정의는 `2000+` 범위 권장).
3. `IPacketHandler`를 구현:

   ```csharp
   public sealed class MyHandler : IPacketHandler
   {
       public int PacketId => PacketIds.MyRequest;
       public void Handle(GameSession session, BasePacket packet)
       {
           // 1) ParseMessageFromPacket<MyRequest> 로 디코딩
           // 2) 게임 로직
           // 3) session.Send(PacketIds.MyResponse, response)
       }
   }
   ```

4. `Program.cs`에 등록:

   ```csharp
   builder.Services.AddSingleton<IPacketHandler, MyHandler>();
   ```

## 6. 텔레메트리 교체

기본 `NullGameServerTelemetry`는 무동작입니다. OpenTelemetry나 자체 metrics
adapter로 바꾸려면 `IGameServerTelemetry`를 구현한 뒤 `Program.cs`에서
교체하면 됩니다.

```csharp
builder.Services.AddSingleton<IGameServerTelemetry, MyOtelTelemetry>();
```

## 전체 echo 왕복 검증

레포에 이미 매칭 샘플 클라이언트가 포함되어 있습니다 (`FastPortGameServerTemplate.SampleClient/`).
서버를 한 터미널에서 실행하고, 다른 터미널에서 클라이언트를 실행:

```bash
# 터미널 1
dotnet run --project template-projects/FastPortGameServerTemplate -c Release

# 터미널 2
dotnet run --project template-projects/FastPortGameServerTemplate.SampleClient -c Release
```

예상 클라이언트 출력:

```
[INF] SampleClient connecting. Host=127.0.0.1, Port=7777
[INF] BaseConnector, OnSocketEventsConnectedCompleted, Connected to 127.0.0.1:7777
[INF] Connected. Sending EchoRequest. Message="Hello, FastPort!"
[INF] EchoResponse received. Message="Hello, FastPort!", ServerUnixMs=..., RTT=...ms
[INF] Echo round-trip succeeded. Echoed="Hello, FastPort!", RTT=...ms
```

샘플 클라이언트는 의도적으로 작게(~150줄) 유지되며, 자체 smoke probe / load
runner의 출발점으로도 사용할 수 있습니다.

## 다음 단계

- 본 cycle 자체에 대한 배경: `docs/00-pm/...prd.md`,
  `docs/01-plan/features/...plan.md`,
  `docs/02-design/features/...design.md`.
- 엔진 패키지 NuGet publish 정책: `HANDOFF.md` "Important Architecture
  Decisions" 섹션의 game server template 항목 + 차기 cycle.
