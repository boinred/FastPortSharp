# fastport-smoke-server - Design Document

> Version: 1.0.0 | Date: 2026-04-27 | Status: Completed
> Level: Starter | Plan: docs/01-plan/features/fastport-smoke-server.plan.md

---

## 1. Overview

`fastport-smoke-server` adds a dedicated smoke/test server around the FastPort networking path and verifies the real server/client echo path with automated integration smoke tests.

The current `FastPortLoadRunner` provides client-observed metrics. This design adds the missing server side:

- listener accept success/failure
- connected/disconnected sessions
- server-observed received/sent packets and bytes
- socket errors
- packet parse/protocol errors
- deterministic server startup/shutdown for smoke tests

This is intentionally not a dashboard feature. It defines the measurement contract and proves it with tests so the later MAUI dashboard can consume stable data.

The architectural boundary is important: `FastPortServer` remains a basic, ready-to-use network engine host. Echo protocol handling, server telemetry DI, and smoke assertions live in `FastPortSmokeServer` and tests.

## 2. Design Goals

- Keep telemetry generic in `LibNetworks`; avoid coupling engine code to `FastPortServer` game protocol.
- Make counters thread-safe and low overhead for hot socket paths.
- Distinguish server-observed telemetry from LoadRunner client-observed metrics.
- Let tests start `FastPortSmokeServer` on a dynamic port and stop it without a manual process.
- Verify both small fixed payload and large random payload echo paths.
- Keep metric names camelCase-compatible for JSON/dashboard output later.
- Keep `FastPortServer` free of smoke/test protocol responsibilities.

## 3. Architecture

### 3.1 Project Boundaries

| Project | Responsibility |
|---------|----------------|
| `LibNetworks` | Generic server telemetry collector/snapshot, listener/session instrumentation |
| `FastPortServer` | Basic network engine host/sample without smoke protocol behavior |
| `FastPortSmokeServer` | Smoke server DI wiring, server options, echo protocol handling, protocol-specific parse/protocol error recording |
| `FastPortLoadRunner` | Client-observed load generation and metrics |
| `LibCommonTest` | Integration smoke tests and telemetry assertions |
| `Protocols` | Echo request/response messages used by smoke tests |

### 3.2 Runtime Flow

```text
FastPortSmokeServer host
  -> FastPortSmokeServerOptions
  -> FastPortSmokeServerBackgroundService
  -> FastPortSmokeServer / BaseMessageListener
  -> BaseListener accept path
  -> BaseSession receive/send/disconnect paths
  -> FastPortSmokeClientSession echo handling
  -> ServerTelemetryCollector
  -> ServerTelemetrySnapshot
  -> integration smoke assertions
```

### 3.3 Main Design Decision

Add a small telemetry abstraction in `LibNetworks`:

```csharp
public interface IServerTelemetry
{
    void RecordAccept();
    void RecordAcceptError();
    void RecordSessionDisconnected();
    void RecordReceived(int bytes);
    void RecordSent(int bytes);
    void RecordSocketError();
    void RecordParseError();
    void RecordProtocolError();
    ServerTelemetrySnapshot CreateSnapshot();
    void Reset();
}
```

The implementation uses `Interlocked` counters only. No per-packet logging, allocation, or histogram is introduced in this phase.

## 4. Data Model

### 4.1 ServerTelemetrySnapshot

```csharp
public sealed record ServerTelemetrySnapshot(
    DateTimeOffset Timestamp,
    long AcceptedSessions,
    long DisconnectedSessions,
    long ConnectedSessions,
    long ReceivedPackets,
    long SentPackets,
    long ReceivedBytes,
    long SentBytes,
    long AcceptErrors,
    long SocketErrors,
    long ParseErrors,
    long ProtocolErrors,
    double SocketErrorRate);
```

Rules:

- `ConnectedSessions = max(0, AcceptedSessions - DisconnectedSessions)`.
- `ReceivedPackets` is server-observed parsed FastPort packet count, not raw socket read count.
- `SentPackets` is server-observed FastPort packet write completion count.
- `ReceivedBytes` uses socket bytes transferred or parsed packet bytes; implementation should choose one and name it clearly.
- `SentBytes` uses socket send completion bytes.
- `SocketErrorRate = SocketErrors / max(1, ReceivedPackets + SentPackets + SocketErrors)`.

### 4.2 Counter Semantics

| Counter | Source | Meaning |
|---------|--------|---------|
| `acceptedSessions` | `BaseListener.OnSocketEventsAcceptCompleted` success | Accepted TCP clients |
| `acceptErrors` | `BaseListener.OnSocketEventsAcceptCompleted` error/null socket | Failed accept attempts |
| `disconnectedSessions` | `BaseSession.RequestDisconnect` first transition | Closed sessions |
| `connectedSessions` | derived | Current server CCU |
| `receivedPackets` | `BaseSession.DoWorkReceivedBuffers` after packet parse | Server-side packet count |
| `receivedBytes` | receive/packet path | Server-side incoming bytes |
| `sentPackets` | `BaseSession.OnSocketEventsSentCompleted` success | Server-side send completions |
| `sentBytes` | `BaseSession.OnSocketEventsSentCompleted` success | Server-side outgoing bytes |
| `socketErrors` | listener/session socket error paths | Socket-level failures |
| `parseErrors` | `FastPortSmokeClientSession.OnReceived` parse failure | Protocol payload parse failures |
| `protocolErrors` | `FastPortSmokeClientSession.OnReceived` wrong packet id/result | Protocol mismatch failures |

## 5. Instrumentation Design

### 5.1 BaseListener

Change constructor shape so a telemetry instance can be injected:

```csharp
public BaseListener(
    ILogger<BaseListener> logger,
    IClientSessionFactory clientSessionFactory,
    IServerTelemetry serverTelemetry,
    int maxConnectionsCount)
```

Instrumentation points:

- `StartAccept` bind/listen exception: `RecordAcceptError()` and optionally `RecordSocketError()`
- `Accept` exception: `RecordAcceptError()`
- `OnSocketEventsAcceptCompleted` socket error: `RecordAcceptError()`, `RecordSocketError()`
- `OnSocketEventsAcceptCompleted` null socket: `RecordAcceptError()`
- successful client socket: `RecordAccept()`

The listener continues accepting after success as it does today.

### 5.2 BaseSession

Change constructor shape so each session receives the same telemetry instance:

```csharp
public BaseSession(
    ILogger<BaseSession> logger,
    Socket socket,
    IBuffers receivedBuffers,
    IBuffers sendbuffers,
    IServerTelemetry serverTelemetry)
```

Instrumentation points:

- receive completion with `SocketError != Success`: `RecordSocketError()`
- receive completion with successful bytes: `RecordReceivedBytes(e.BytesTransferred)` or `RecordReceivedRawBytes`
- parsed base packet in `DoWorkReceivedBuffers`: `RecordReceivedPacket(basePacket.PacketSize)`
- send completion with `SocketError != Success`: `RecordSocketError()`
- send completion success: `RecordSent(e.BytesTransferred)`
- first `RequestDisconnect` transition: `RecordSessionDisconnected()`
- `RequestReceived` socket exception: `RecordSocketError()`
- `DoWorkSendBuffers` socket exception: `RecordSocketError()`

Implementation note: if both raw receive bytes and parsed packet bytes are wanted later, add separate names. For this scope, one `receivedBytes` counter is enough, but the implementation must avoid double counting.

### 5.3 FastPortSmokeClientSession

Add protocol-level instrumentation:

- parse failure: `RecordParseError()`
- packet id mismatch: `RecordProtocolError()`

The current `ParseMessageFromPacket` returns packet id and typed request together. If it fails, record parse error. If a future branch handles different packet ids, record protocol error on unexpected ids.

### 5.4 FastPortSmokeClientSessionFactory

Inject telemetry and pass it into sessions:

```csharp
public FastPortSmokeClientSessionFactory(
    ILogger<BaseSessionClient> logger,
    IServerTelemetry serverTelemetry)
```

Then:

```csharp
new FastPortSmokeClientSession(
    logger,
    clientSocket,
    new ArrayPoolCircularBuffers(8 * 1024),
    new ArrayPoolCircularBuffers(8 * 1024),
    serverTelemetry);
```

### 5.5 FastPortSmokeServer Options

Add an options record/class in `FastPortSmokeServer`:

```csharp
public sealed class FastPortSmokeServerOptions
{
    public string Host { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 6628;
}
```

`FastPortSmokeServerBackgroundService` uses options instead of hard-coded `0.0.0.0:6628`.

Tests can inject `127.0.0.1:<freePort>`.

`FastPortServer` can keep its own basic host/port options, but it does not register telemetry or protocol-specific echo handling.

## 6. API and DI Design

### 6.1 Production DI

`FastPortSmokeServer/Program.cs` registers:

```csharp
s.AddSingleton<IServerTelemetry, ServerTelemetryCollector>();
s.AddSingleton(new FastPortSmokeServerOptions());
s.AddSingleton<IClientSessionFactory, FastPortSmokeClientSessionFactory>();
s.AddSingleton<FastPortSmokeServer>();
```

### 6.2 Test DI

Smoke tests build a host with:

- dynamic free port
- `FastPortSmokeServerOptions { Host = "127.0.0.1", Port = freePort }`
- singleton `IServerTelemetry`
- same hosted service and session factory as production

The test gets `IServerTelemetry` from the host service provider and asserts snapshots.

### 6.3 Ready Check

The test harness should wait until the server is accepting connections. Preferred ready check:

```text
start host
repeat until timeout:
  try TcpClient.ConnectAsync(127.0.0.1, port)
  if success: close probe client and continue
```

Because the ready probe itself increments telemetry, tests should call `serverTelemetry.Reset()` after the ready check and before the actual smoke run.

## 7. Integration Smoke Design

### 7.1 Test Harness

Add helper in `LibCommonTest`, for example:

```csharp
internal sealed class FastPortSmokeServerTestHost : IAsyncDisposable
{
    public int Port { get; }
    public IServerTelemetry Telemetry { get; }

    public static Task<FastPortSmokeServerTestHost> StartAsync();
    public Task WaitUntilReadyAsync();
    public ValueTask DisposeAsync();
}
```

Responsibilities:

- find a free local TCP port
- build and start `IHost`
- wait for readiness
- expose telemetry
- stop/dispose host cleanly

### 7.2 Smoke Client Strategy

Use `FastPortLoadRunner` for the actual smoke path when possible, because it exercises the same code used for load validation.

Current limitation: `LoadRunner`, `MetricsCollector`, and related types are internal and are already exposed to `LibCommonTest` via `InternalsVisibleTo`. The tests can directly construct:

```csharp
var scenario = new LoadScenario(
    Host: "127.0.0.1",
    Port: testHost.Port,
    Sessions: 10,
    Payload: PayloadProfile.Fixed(1024),
    SendRatePerSession: 1,
    RampUp: TimeSpan.FromSeconds(1),
    Duration: TimeSpan.FromSeconds(2),
    MetricsInterval: TimeSpan.FromSeconds(1),
    OutputPath: null);
```

Then run `LoadRunner` with a no-op reporter or a test reporter.

### 7.3 Fixed Payload Smoke

Scenario:

- sessions: 10
- payload: `fixed:1024`
- rate: 1 packet/sec/session
- ramp-up: 1s
- duration: 2s

Assertions:

- client sent packets > 0
- client received packets > 0
- server accepted sessions >= 10
- server received packets > 0
- server sent packets > 0
- server parse errors == 0
- server protocol errors == 0
- server socket error rate is below threshold
- connected sessions eventually returns to 0 after stop/disconnect

### 7.4 Large Random Payload Smoke

Scenario:

- sessions: 2 to 10
- payload: `random:4096-16384`
- rate: 1 packet/sec/session
- ramp-up: 1s
- duration: 2s

This case specifically validates the large payload path against the current 8KB socket receive buffer and packet reassembly behavior.

Assertions are the same as fixed payload smoke. If this fails, telemetry must make the failure visible as parse/protocol/socket errors.

## 8. Test Plan

### 8.1 Unit Tests

- `ServerTelemetryCollector_CreateSnapshot_ReturnsDerivedConnectedSessions`
- `ServerTelemetryCollector_Reset_ClearsCounters`
- `ServerTelemetryCollector_SocketErrorRate_UsesPacketsAndErrors`
- `FastPortSmokeServerOptions_Defaults_ToProductionAddress`

### 8.2 Integration Tests

- `FastPortSmokeServer_FixedPayload_EchoesAndRecordsTelemetry`
- `FastPortSmokeServer_RandomLargePayload_EchoesAndRecordsTelemetry`

### 8.3 Verification Commands

```bash
dotnet build FastPortCharp.sln
dotnet test FastPortCharp.sln --no-build
```

## 9. Implementation Order

1. Add telemetry model and collector in `LibNetworks`.
2. Add `FastPortSmokeServer` project with telemetry DI registration.
3. Thread telemetry through `BaseMessageListener`, `BaseListener`, session factory, and `BaseSession`.
4. Instrument accept, disconnect, receive, send, and socket error paths.
5. Add protocol parse/protocol error instrumentation in `FastPortSmokeClientSession`.
6. Add `FastPortSmokeServerOptions` and dynamic host/port wiring in the smoke server background service.
7. Add test host helper with dynamic port and ready check.
8. Add telemetry collector unit tests.
9. Add fixed payload integration smoke test.
10. Add random large payload integration smoke test.
11. Run build/test and document any remaining limits in analysis.

## 10. Risks

| Risk | Design Response |
|------|-----------------|
| Telemetry adds overhead to hot path | Use only `Interlocked` counters and no per-packet allocations |
| Ready probe pollutes telemetry | Call `Reset()` after ready check |
| Dynamic port test differs from production | Production defaults remain `0.0.0.0:6628`; tests override options |
| Smoke protocol leaks into base server | Keep echo/protocol implementation in `FastPortSmokeServer`, not `FastPortServer` |
| `receivedBytes` double counts socket bytes and packet bytes | Pick one implementation meaning and keep it consistent |
| Existing 8KB receive buffer exposes large payload bugs | Keep random large payload smoke as a separate validation case |
| Session shutdown timing is asynchronous | Assert disconnect/connected zero with timeout polling, not immediate equality |

## 11. Future Extensions

- Add JSON export for server telemetry snapshots.
- Add MAUI dashboard streaming endpoint or file tailing mode.
- Add staged 1k/3k/5k/10k load validation report.
- Split telemetry into engine metrics and game protocol metrics if game template work needs it.

## 12. Next Phase

Do phase should implement telemetry first, then server options/test harness, then integration smoke tests. This order keeps smoke failures diagnosable before large-payload validation begins.
