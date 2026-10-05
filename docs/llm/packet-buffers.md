# 패킷·버퍼 (와이어 포맷, `BasePacket`, `IBuffers`, 링버퍼, packetId 파싱, 지연 통계, 타이머)

`LibCommons`는 엔진이 쓰는 바이트 단위 기반 코드다. 수신 바이트를 모아 패킷으로 자르는 링버퍼, 패킷 객체, ID 생성, 지연 통계, 타이머 큐가 있다. `LibNetworks/Extensions/BasePacket+Extensions.cs`는 패킷 payload에서 packetId와 protobuf 메시지를 꺼낸다.

**이 문서를 읽는 경우**: 패킷 포맷, 길이 헤더, packetId, protobuf 파싱 실패, 패킷 최대 크기, 수신 버퍼(링버퍼) 동작·확장, ArrayPool 대여·반환, `IBuffers` 새 구현, 세션 ID 발급, RTT·서버 처리 시간 통계, 타이머·주기 작업, 단조 시계, 엔진 샘플 proto(`commons.proto`, `tests.proto`).

**다른 문서로 가는 경우**: 버퍼를 언제 쓰고 언제 끊는지(수신 상한, 잘못된 헤더 disconnect, 송신 큐) → [session.md](session.md). 템플릿 proto(`PacketIds.proto`, `Sample.proto`)와 패킷 추가 절차 → [game-server-template.md](game-server-template.md). `LatencyStats`를 쓰는 샘플 클라이언트·서버 → [sample-apps.md](sample-apps.md). `TimerQueue`를 쓰는 idle 정리 → [load-testing.md](load-testing.md). 용어 → [glossary.md](glossary.md).

## 핵심 규칙

- **와이어 포맷은 `[UInt16 LE 전체 길이][int32 LE packetId][protobuf payload]`다.** 길이 필드는 헤더 2바이트를 포함한 전체 길이다. 송신 쪽은 `BaseSession.TryRequestSendBuffers`가, 수신 쪽은 `ArrayPoolCircularBuffers.GetPacketSizeInBuffersCore`가 같은 규약을 쓴다. 한쪽만 바꾸면 안 된다.
- **헤더 크기는 `BasePacket.HeaderSize` = 2 한 곳에서 정한다.** 하드코딩된 `2`를 새로 만들지 않는다.
- 패킷 최대 크기는 `ushort.MaxValue`(65,535B)다. packetId 4바이트를 빼면 protobuf payload는 최대 65,529B다. 초과하면 `TryRequestSendBuffers`가 `false`를 돌려준다.
- `BasePacket`은 헤더를 뺀 나머지(packetId + protobuf)를 **새 `byte[]`로 복사**해 가진다. 수신 버퍼가 반환·재사용돼도 `BasePacket.Data`는 안전하다.
- `BasePacket.Data`는 packetId 4바이트로 시작한다. protobuf만 원하면 `ParseMessageFromPacket<T>`를 쓴다. `DataSize < 4`면 `false`를 돌려준다.
- **`ParseMessageFromPacket<T>`는 protobuf 예외를 잡지 않는다.** `MergeFrom`이 던진 예외는 `OnReceived`로 올라가고, 세션이 `PacketHandlerError`로 끊긴다([session.md](session.md)).
- **실사용 `IBuffers` 구현은 `ArrayPoolCircularBuffers` 하나다.** 모든 세션 팩토리가 `new ArrayPoolCircularBuffers(8 * 1024)` 또는 `BufferCapacityBytes`로 만든다. `BaseCircularBuffers`, `BaseQueueBuffers`는 테스트·비교용 레거시다.
- `ArrayPoolCircularBuffers`는 모든 public 메서드를 `System.Threading.Lock`으로 보호한다. 수신 콜백(쓰기)과 parser task(읽기)가 동시에 불러도 된다.
- `ArrayPoolCircularBuffers`는 공간이 모자라면 2배씩 커진다(`ExpandBuffer`, `GrowCapacity`). **버퍼 자체에는 상한이 없다.** 상한은 `BaseSession.MaxReceiveBufferedBytes`가 쓰기 전에 검사한다.
- **ArrayPool 소유권**: `GetPacketBuffers`가 돌려준 배열은 호출자가 `ArrayPoolCircularBuffers.ReturnBuffer`로 반환한다. `TryGetBasePackets`는 내부에서 빌린 배열을 `finally`에서 반환한다. 대여 배열은 요청 크기보다 클 수 있으므로 반환값(실제 길이)만 읽는다. `Dispose`는 내부 배열을 풀에 돌려준다. 이후 호출은 `ObjectDisposedException`이다.
- `TryGetBasePackets`는 완성된 패킷만 꺼낸다. 길이 필드가 `HeaderSize`보다 작으면 거기서 멈추고 바이트를 남긴다. 잘못된 헤더 판정과 disconnect는 세션(`HasInvalidPacketHeader`)이 한다.
- `IBuffers.Peek(ref byte[])`는 데이터를 지우지 않는다. 구현이 `ref` 배열을 바꿀 수 있다고 가정하고 호출한다.
- `IDGenerator.GetNextGeneratedId()`는 `Interlocked.Increment` 기반이다. 인스턴스마다 카운터가 따로라서 세션 클래스가 `static readonly`로 하나를 공유한다(`FastPortClientSession`, `GameSession`, `FastPortTestSmokeClientSession`).
- `TimerQueue`는 worker task 하나에서 콜백을 순서대로 실행한다. 콜백은 짧아야 한다. 콜백 예외는 삼키고 `FailedCallbackCount`만 올린다. 주기 타이머는 콜백이 끝난 시각 기준으로 다음 due를 잡는다.
- 타이머 시간은 `IMonotonicTimeSource`(기본 `StopwatchMonotonicTimeSource.Instance`)로 계산한다. 테스트는 가짜 time source를 생성자에 넣는다.

## 파일·타입

| 파일 | 타입·심볼 | 내용 |
|---|---|---|
| `LibCommons/BasePacket.cs` | `BasePacket`, `HeaderSize`, `PacketSize`, `DataSize`, `Data` | 길이 헤더를 뺀 payload 복사본 |
| `LibCommons/IBuffers.cs` | `IBuffers` | `CanReadSize`, `CanWriteSize`, `Write`, `Peek`, `Drain`, `TryGetBasePackets` |
| `LibCommons/ArrayPoolCircularBuffers.cs` | `ArrayPoolCircularBuffers` | 기본 구현. span `Write`/`Peek` 오버로드, `GetPacketBuffers`, `ReturnBuffer`, `GetPacketSizeInBuffers` |
| `LibCommons/BaseCircularBuffers.cs` | `BaseCircularBuffers` | 레거시 링버퍼. 헤더만 있는 패킷(길이 2)을 꺼내지 못한다 |
| `LibCommons/BaseQueueBuffers.cs` | `BaseQueueBuffers` | 레거시 `Queue<byte>` 구현. `TryGetBasePackets`가 패킷을 채우지 않는다 |
| `LibCommons/IDGenerator.cs` | `IDGenerator` | `GetNextGeneratedId`, `GetNextGeneratedGuid` |
| `LibCommons/LatencyStats.cs` | `LatencyStats`, `LatencyStatsOptions`, `LatencySample`, `LatencyCalculation`, `LatencyStatsSummary` | T1~T4 기반 RTT·서버 처리·네트워크 지연, p50/p95/p99, 파일 출력 |
| `LibCommons/Timers/ITimerQueue.cs` | `ITimerQueue`, `ITimerQueueHandle` | `Schedule`, `SchedulePeriodic`, `Cancel` |
| `LibCommons/Timers/TimerQueue.cs` | `TimerQueue` | `PriorityQueue` min-heap + `SemaphoreSlim` worker. `ExecutedCallbackCount`, `FailedCallbackCount` |
| `LibCommons/Timers/TimerQueueOptions.cs` | `TimerQueueOptions` | `MaxCallbacksPerWake`(기본 1024) 후 `Task.Yield` |
| `LibCommons/Timers/IMonotonicTimeSource.cs` | `IMonotonicTimeSource`, `StopwatchMonotonicTimeSource` | Stopwatch 기반 단조 시계 |
| `LibNetworks/Extensions/BasePacket+Extensions.cs` | `BasePacketExtensions.ParseMessageFromPacket<T>` | payload 앞 int32 LE packetId + 나머지 protobuf `MergeFrom` |
| `Protocols/Protos/commons.proto` | `ProtocolId`, `ResultCode`, `Empty`, `Header` | C# 네임스페이스 `FastPort.Protocols.Commons`. `Header`에 T1~T3 Stopwatch tick |
| `Protocols/Protos/tests.proto` | `PingRequest/Response`, `EchoRequest/Response`, `ErrorResponse`, `TestService` | C# 네임스페이스 `FastPort.Protocols.Tests` |

엔진 샘플(`FastPortClient`, SmokeServer)은 `ProtocolId` 값을 packetId로 보낸다(`(int)protocolId`). 템플릿은 `Protocols`를 쓰지 않는다.

## 작업별 시작점

| 하려는 작업 | 고칠 곳 | 같이 확인할 것 |
|---|---|---|
| 길이 헤더를 4바이트로 바꾸기 | `BasePacket.HeaderSize`, `ArrayPoolCircularBuffers.GetPacketSizeInBuffersCore`, `BaseSession.TryRequestSendBuffers`(`WriteUInt16LittleEndian`, `ushort.MaxValue`), `BaseSession.HasInvalidPacketHeader` | `FastPortTestLoadRunner/LoadSession.cs`의 자체 인코딩·디코딩, 템플릿 `PacketDispatcher.TryReadPacketId`, `BaseSessionReceivePolicyTests.BuildPacket`, scaffold golden |
| packetId 파싱 실패 처리 | `BasePacketExtensions.ParseMessageFromPacket<T>` | 호출부 `FastPortServerSession.OnReceived`(FastPortClient), `FastPortTestSmokeClientSession.OnReceived`. 템플릿은 `PacketDispatcher.TryReadPacketId`로 따로 읽는다 |
| 수신 버퍼 초기 용량 바꾸기 | 각 세션 팩토리의 `new ArrayPoolCircularBuffers(...)` / `BufferCapacityBytes` | 상한은 따로다 → `BaseSession.MaxReceiveBufferedBytes` |
| 새 `IBuffers` 구현 추가 | `IBuffers` 구현 클래스 | `IBuffersInterfaceTest`의 `DataRow`에 타입 추가, `TryGetBasePackets`가 잘못된 헤더에서 멈추는지 |
| 링버퍼 버그 수정 | `ArrayPoolCircularBuffers.WriteInternal`/`ReadInternal`/`ExpandBuffer`/`DrainCore` | `ArrayPoolCircularBufferTest`의 wrap-around·확장 케이스 |
| 지연 통계 항목 추가 | `LatencySample.Calculate`, `LatencyStatsSummary`, `LatencyStats.GetSummary` | `GetSummaryString`, `GetSummaryJson`, `FastPortClient/appsettings.json`의 `LatencyStats` 섹션 |
| 주기 작업 추가 | `ITimerQueue.SchedulePeriodic` 호출, 반환 handle 보관 후 `Dispose` | DI 등록 예: `FastPortTestSmokeServer/Program.cs`(`TimerQueue` singleton) |
| 엔진 샘플 메시지 추가 | `Protocols/Protos/tests.proto` (`ProtocolId`가 필요하면 `commons.proto`) | `Protocols.csproj`의 `<Protobuf Include>`로 자동 생성된다. 사용처 SmokeServer·LoadRunner·FastPortClient |

## 테스트

- `tests-projects/FastPortTests/BasePacketTest.cs`: `HeaderSize`, payload 복사, 헤더만 있는 패킷.
- `ArrayPoolCircularBufferTest.cs`: 쓰기·Peek·Drain·확장·wrap-around·`TryGetBasePackets`·Dispose.
- `IBuffersInterfaceTest.cs`: 세 구현 공통 계약. `TryGetBasePackets` 케이스는 `BaseQueueBuffers`를 뺀다.
- `CircularBufferTest.cs`, `QueueBufferTest.cs`: 레거시 구현.
- `IDGeneratorTest.cs`: 증가·동시성·인스턴스 독립.
- `TimerQueueTests.cs`: due 순서, 취소, 주기, 콜백 예외, Dispose.
- `ParseMessageFromPacket`과 `LatencyStats` 전용 테스트는 없다. 수신 경로 통합 테스트(`BaseSessionReceivePolicyTests`)가 와이어 포맷을 간접 확인한다.

```bash
dotnet test tests-projects/FastPortTests -c Release --filter "FullyQualifiedName~ArrayPoolCircularBufferTest"
dotnet test tests-projects/FastPortTests -c Release --filter "FullyQualifiedName~TimerQueueTests"
```

## 주의

- **scaffold golden**: `LibCommons/**`와 `LibNetworks/**`는 scaffold가 그대로 복사한다. 주석 한 줄만 바꿔도 `tests/scaffold/run.sh --update-golden case-01-simple` 후 `tests/scaffold/run.sh` 전체 통과가 필요하다. `Protocols/Protos/`는 scaffold 대상이 아니다.
- `LibCommons/LatencyStats.cs`는 UTF-8이 아니다(CP949 계열). 한글 주석이 깨져 보인다. 편집기가 인코딩을 바꿔 저장하면 diff 전체가 바뀌고 golden hash도 바뀐다. 테스트의 `IBuffersInterfaceTest.cs`, `BasePacketTest.cs`, `IDGeneratorTest.cs`도 같은 상태다.
- `LatencyStatsOptions.EnableConsoleOutput` 기본값은 `true`다. 샘플마다 Information 로그를 남기므로 부하 측정에서는 끈다.
- `Header`의 T1~T3은 각 머신의 `Stopwatch` tick이다. `ServerProcessingMs`(T3−T2)와 RTT(T4−T1)만 같은 시계끼리 뺀다. `NetworkLatencyMs`는 RTT − 서버 처리이며 0 미만은 0으로 자른다.
- `BaseQueueBuffers.TryGetBasePackets`는 바이트를 소비하지만 `basePackets`에 넣지 않는다. 세션에 넣으면 패킷이 사라진다.
- `ArrayPoolCircularBuffers.CanWriteSize`는 다음 확장 전까지 남은 논리 용량이다. 쓰기 가능 상한이 아니다.
