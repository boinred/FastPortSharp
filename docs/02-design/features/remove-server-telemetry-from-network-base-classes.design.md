# remove-server-telemetry-from-network-base-classes - Design Document

> Version: 1.0.0 | Date: 2026-05-05 | Status: Draft
> Level: Starter | Plan: docs/01-plan/features/remove-server-telemetry-from-network-base-classes.plan.md

---

## 1. Overview

`remove-server-telemetry-from-network-base-classes`는 `LibNetworks`의 base networking classes에서 server telemetry contract를 제거하고, 동일한 관측 지점을 telemetry-neutral protected hook으로 노출하는 작업이다.

목표는 network engine의 send/receive/accept 동작을 바꾸지 않으면서 다음 경계를 명확히 하는 것이다.

- `LibNetworks`: socket/session/listener lifecycle과 send queue engine만 소유한다.
- `LibTestTelemetry`: smoke/load validation telemetry collector, snapshot, observed JSONL exporter contract를 소유한다.
- `FastPortTestSmokeServer`: `LibNetworks` hook을 override해서 `LibTestTelemetry.ServerTelemetryCollector`에 기록한다.

이번 설계는 public JSONL field contract, send policy, pacing/backpressure threshold를 변경하지 않는다.

## 2. Current Coupling

현재 `LibNetworks`에 다음 telemetry 타입과 engine dependency가 남아 있다.

| Area | Current location | Current consumers | Issue |
|------|------------------|-------------------|-------|
| `IServerTelemetry` | `LibNetworks/Telemetry/ServerTelemetry.cs` | `BaseSession`, `BaseListener`, smoke server, tests, exporter | Engine constructor surface가 test telemetry contract를 요구한다. |
| `ServerTelemetryCollector` | `LibNetworks/Telemetry/ServerTelemetry.cs` | smoke server, tests | Test/smoke collector가 core project에 있다. |
| `ServerTelemetrySnapshot` | `LibNetworks/Telemetry/ServerTelemetry.cs` | `LibTestTelemetry`, tests | Observed metrics export read model이 engine project에 있다. |
| `NullServerTelemetry` | `LibNetworks/Telemetry/ServerTelemetry.cs` | base constructors | Default no-op dependency를 위해 engine이 telemetry abstraction을 보유한다. |
| `BaseSession.ServerTelemetry` | `LibNetworks/Sessions/BaseSession.cs` | derived smoke session | protected property로 telemetry가 inheritance API가 되었다. |

현재 engine call site는 다음 범주다.

| Class | Existing calls |
|-------|----------------|
| `BaseListener` | `RecordAccept`, `RecordAcceptError`, `RecordSocketError` |
| `BaseSession` | `RecordSessionDisconnected`, `RecordReceived`, `RecordSent`, `RecordSendRequested`, `RecordSendCompleted`, `RecordSendBackpressure`, `RecordSendRejected`, `RecordSendDrainYield`, `RecordSendBufferSample`, `RecordSocketError` |
| `FastPortTestSmokeClientSession` | `RecordParseError`, `RecordProtocolError` |

`FastPortTestSmokeClientSession`의 parse/protocol errors는 protocol-specific smoke behavior이므로 engine hook이 아니라 smoke session layer에서 계속 기록한다.

## 3. Target Architecture

### 3.1 Project Boundary

Target dependency direction:

```text
LibCommons
  ^
  |
LibNetworks

LibTestTelemetry

FastPortTestSmokeServer -> LibNetworks + LibTestTelemetry
FastPortTests           -> LibNetworks + LibTestTelemetry
FastPortTestLoadRunner  -> LibTestTelemetry
FastPortTestLoadValidation -> LibTestTelemetry
```

Rules:

- `LibNetworks` must not reference `LibTestTelemetry`.
- `LibNetworks` source must not mention `IServerTelemetry`, `ServerTelemetryCollector`, `ServerTelemetrySnapshot`, or `NullServerTelemetry`.
- `LibTestTelemetry` owns server telemetry collector/snapshot/exporter contracts.
- Runtime network behavior remains in `LibNetworks`.
- Smoke server and tests bridge the two projects through subclass overrides or local test subclasses.

### 3.2 Type Movement

Move from `LibNetworks/Telemetry/ServerTelemetry.cs` to `LibTestTelemetry`:

| Type | Target | Decision |
|------|--------|----------|
| `IServerTelemetry` | `LibTestTelemetry` | Keep as test/smoke telemetry interface. |
| `ServerTelemetryCollector` | `LibTestTelemetry` | Keep counter semantics unchanged. |
| `ServerTelemetrySnapshot` | `LibTestTelemetry` | Keep observed metrics input contract unchanged. |
| `NullServerTelemetry` | Delete | No longer needed after engine constructors remove telemetry dependency. |

After this move, `LibNetworks/Telemetry/ServerTelemetry.cs` should be removed if it becomes empty.

`LibTestTelemetry/ObservedMetrics.cs` should remove `using LibNetworks.Telemetry;` after the server telemetry types move into the same namespace.

## 4. Hook Design

### 4.1 Design Principles

- Hooks are `protected virtual void` methods with no-op base implementation.
- Hooks use engine-native values only: `Socket`, `SocketError`, `Exception`, `BasePacket`, byte counts, and queued byte counts.
- Hook call locations must replace existing `Record*` call locations 1:1 where possible.
- Hook names should describe network events, not telemetry implementation.
- Hooks must not allocate event objects on hot send/receive paths.
- Hooks must not perform logging by default.

### 4.2 `BaseListener` Hooks

Add these hooks to `LibNetworks/BaseListener.cs`:

```csharp
protected virtual void OnAcceptSucceeded(Socket clientSocket)
{
}

protected virtual void OnAcceptFailed(SocketError? socketError, Exception? exception)
{
}

protected virtual void OnListenerSocketError(SocketError? socketError, Exception? exception)
{
}
```

Mapping:

| Existing call site | New hook |
|--------------------|----------|
| invalid endpoint `RecordAcceptError()` | `OnAcceptFailed(null, null)` |
| `StartAccept` exception `RecordAcceptError()` | `OnAcceptFailed(null, ex)` |
| `StartAccept` exception `RecordSocketError()` | `OnListenerSocketError(null, ex)` |
| `Accept` exception `RecordAcceptError()` | `OnAcceptFailed(null, ex)` |
| `Accept` exception `RecordSocketError()` | `OnListenerSocketError(null, ex)` |
| accept completed socket error | `OnAcceptFailed(args.SocketError, null)` and `OnListenerSocketError(args.SocketError, null)` |
| accept completed null socket | `OnAcceptFailed(null, null)` |
| accept success `RecordAccept()` | `OnAcceptSucceeded(clientSocket)` |

`OnSocketEventsAcceptCompleted` can remain private. The hook allows subclasses to observe accepted/error outcomes without taking over accept loop control.

### 4.3 `BaseSession` Hooks

Add these hooks to `LibNetworks/Sessions/BaseSession.cs`:

```csharp
protected virtual void OnNetworkSessionDisconnected()
{
}

protected virtual void OnNetworkSocketError(SocketError? socketError, Exception? exception)
{
}

protected virtual void OnNetworkPacketReceived(BasePacket packet)
{
}

protected virtual void OnNetworkBytesSent(int bytes)
{
}

protected virtual void OnNetworkSendRequested(int bytes, int queuedBytes)
{
}

protected virtual void OnNetworkSendCompleted()
{
}

protected virtual void OnNetworkSendBackpressure()
{
}

protected virtual void OnNetworkSendRejected(int bytes, int queuedBytes)
{
}

protected virtual void OnNetworkSendDrainYield(int queuedBytes)
{
}

protected virtual void OnNetworkSendBufferSample(int queuedBytes)
{
}
```

The `OnNetwork` prefix separates engine observation hooks from existing application lifecycle hooks such as `OnReceived`, `OnSent`, and `OnDisconnected`.

Mapping:

| Existing call site | New hook |
|--------------------|----------|
| receive completion socket error | `OnNetworkSocketError(e.SocketError, null)` |
| `RequestDisconnect` `RecordSessionDisconnected()` | `OnNetworkSessionDisconnected()` |
| `RequestReceived` socket exception | `OnNetworkSocketError(ex.SocketErrorCode, ex)` |
| queue limit rejection `RecordSendBackpressure()` | `OnNetworkSendBackpressure()` |
| queue limit rejection `RecordSendRejected(bytes, queuedBefore)` | `OnNetworkSendRejected(bytes, queuedBefore)` |
| closed queue rejection `RecordSendRejected(bytes, queuedAfterRollback)` | `OnNetworkSendRejected(bytes, queuedAfterRollback)` |
| successful queue `RecordSendRequested(bytes, queuedAfter)` | `OnNetworkSendRequested(bytes, queuedAfter)` |
| high queued bytes `RecordSendBackpressure()` | `OnNetworkSendBackpressure()` |
| parsed packet `RecordReceived(packet.PacketSize)` | `OnNetworkPacketReceived(basePacket)` |
| send drain yield `RecordSendDrainYield(queuedBytes)` | `OnNetworkSendDrainYield(queuedBytes)` |
| send loop buffer sample | `OnNetworkSendBufferSample(queuedBytes)` |
| transient send socket exception | `OnNetworkSocketError(ex.SocketErrorCode, ex)` and `OnNetworkSendBackpressure()` |
| non-transient send socket exception | `OnNetworkSocketError(ex.SocketErrorCode, ex)` |
| sent bytes `RecordSent(advancedSize)` | `OnNetworkBytesSent(advancedSize)` |
| post-send buffer sample | `OnNetworkSendBufferSample(queuedBytesAfterSend)` |
| completed send request | `OnNetworkSendCompleted()` |
| outer send loop socket exception | `OnNetworkSocketError(ex.SocketErrorCode, ex)` |

Do not change `TryRequestSendBuffers`, `TryRequestSendMessage`, `SessionSendOptions`, or send queue accounting semantics in this feature.

## 5. Smoke Server Bridge

### 5.1 Listener

`FastPortTestSmokeServer.FastPortTestSmokeServer` keeps constructor injection of `IServerTelemetry`, but no longer passes telemetry into `BaseMessageListener`.

Target shape:

```csharp
public class FastPortTestSmokeServer(
    ILogger<FastPortTestSmokeServer> logger,
    IClientSessionFactory clientSessionFactory,
    IServerTelemetry serverTelemetry)
    : BaseMessageListener(logger, clientSessionFactory)
{
    protected override void OnAcceptSucceeded(Socket clientSocket)
    {
        serverTelemetry.RecordAccept();
    }

    protected override void OnAcceptFailed(SocketError? socketError, Exception? exception)
    {
        serverTelemetry.RecordAcceptError();
    }

    protected override void OnListenerSocketError(SocketError? socketError, Exception? exception)
    {
        serverTelemetry.RecordSocketError();
    }
}
```

If `socketError`/`exception` are unused by the current collector, keep them as hook context for future diagnostic tests.

### 5.2 Session

`FastPortTestSmokeClientSession` keeps constructor injection of `IServerTelemetry`, but calls the base constructor without telemetry.

It overrides the `BaseSession` network hooks and maps them to current collector methods:

```csharp
protected override void OnNetworkSessionDisconnected()
    => m_ServerTelemetry.RecordSessionDisconnected();

protected override void OnNetworkSocketError(SocketError? socketError, Exception? exception)
    => m_ServerTelemetry.RecordSocketError();

protected override void OnNetworkPacketReceived(BasePacket packet)
    => m_ServerTelemetry.RecordReceived(packet.PacketSize);

protected override void OnNetworkBytesSent(int bytes)
    => m_ServerTelemetry.RecordSent(bytes);

protected override void OnNetworkSendRequested(int bytes, int queuedBytes)
    => m_ServerTelemetry.RecordSendRequested(bytes, queuedBytes);

protected override void OnNetworkSendCompleted()
    => m_ServerTelemetry.RecordSendCompleted();

protected override void OnNetworkSendBackpressure()
    => m_ServerTelemetry.RecordSendBackpressure();

protected override void OnNetworkSendRejected(int bytes, int queuedBytes)
    => m_ServerTelemetry.RecordSendRejected(bytes, queuedBytes);

protected override void OnNetworkSendDrainYield(int queuedBytes)
    => m_ServerTelemetry.RecordSendDrainYield(queuedBytes);

protected override void OnNetworkSendBufferSample(int queuedBytes)
    => m_ServerTelemetry.RecordSendBufferSample(queuedBytes);
```

Parse/protocol errors remain in existing protocol-specific code:

```csharp
m_ServerTelemetry.RecordParseError();
m_ServerTelemetry.RecordProtocolError();
```

Because `BaseSession.ServerTelemetry` is removed, smoke session stores telemetry in a private readonly field.

## 6. Constructor/API Changes

Remove telemetry overloads from engine base classes:

| File | Change |
|------|--------|
| `LibNetworks/BaseListener.cs` | Remove `IServerTelemetry` field and constructor overload. |
| `LibNetworks/BaseMessageListener.cs` | Remove telemetry constructor overload. |
| `LibNetworks/Sessions/BaseSession.cs` | Remove `ServerTelemetry` property and telemetry constructor overloads. Keep `SessionSendOptions` overload without telemetry. |
| `LibNetworks/Sessions/BaseSessionClient.cs` | Remove telemetry constructor overloads. Keep overload with `SessionSendOptions?`. |
| `LibNetworks/Sessions/BaseSessionServer.cs` | Remove telemetry constructor overloads. Keep overload with `SessionSendOptions?`. |

Target constructor set:

```csharp
BaseSession(
    ILogger<BaseSession> logger,
    Socket socket,
    IBuffers receivedBuffers,
    IBuffers sendbuffers)

BaseSession(
    ILogger<BaseSession> logger,
    Socket socket,
    IBuffers receivedBuffers,
    IBuffers sendbuffers,
    SessionSendOptions? sendOptions)
```

`BaseSessionClient` and `BaseSessionServer` mirror this shape.

This is an intentional repo-local API break. Current repo consumers should be updated in the same implementation.

## 7. File Changes

Expected implementation files:

| File | Change |
|------|--------|
| `LibTestTelemetry/ServerTelemetry.cs` | Add moved `IServerTelemetry`, `ServerTelemetryCollector`, `ServerTelemetrySnapshot`; do not add `NullServerTelemetry`. |
| `LibTestTelemetry/ObservedMetrics.cs` | Use local server telemetry types, remove `LibNetworks.Telemetry` dependency. |
| `LibTestTelemetry/LibTestTelemetry.csproj` | Remove `LibNetworks` project reference if no longer used after move. |
| `LibNetworks/Telemetry/ServerTelemetry.cs` | Delete after move. |
| `LibNetworks/BaseListener.cs` | Remove telemetry dependency, add accept/socket hooks. |
| `LibNetworks/BaseMessageListener.cs` | Remove telemetry overload and `using LibNetworks.Telemetry`. |
| `LibNetworks/Sessions/BaseSession.cs` | Remove telemetry dependency, add network hooks, replace `Record*` calls. |
| `LibNetworks/Sessions/BaseSessionClient.cs` | Remove telemetry overloads and telemetry using. |
| `LibNetworks/Sessions/BaseSessionServer.cs` | Remove telemetry overloads and telemetry using. |
| `FastPortTestSmokeServer/FastPortTestSmokeServer.cs` | Override listener hooks and record collector events. |
| `FastPortTestSmokeServer/Sessions/FastPortTestSmokeClientSession.cs` | Store telemetry field, override session hooks, keep parse/protocol records. |
| `FastPortTestSmokeServer/Sessions/FastPortTestSmokeClientSessionFactory.cs` | Update namespace and constructor call. |
| `FastPortTestSmokeServer/Program.cs` | Register `LibTestTelemetry.IServerTelemetry`. |
| `FastPortTests/*.cs` | Update telemetry namespace and test subclasses. |

## 8. Test Design

### 8.1 Existing Test Updates

`ServerTelemetryTests`:

- Should import `LibTestTelemetry` only for collector/snapshot/exporter.
- Counter behavior assertions must remain unchanged.

`FastPortTestSmokeServerTests`:

- Should validate that listener/session hook overrides preserve accepted/disconnected/received/sent/send queue counters.
- DI registration should continue to expose `IServerTelemetry` and `IServerTelemetryExporter`.

`BaseSessionSendPolicyTests`:

- Test-only `TestSession` should accept an `IServerTelemetry` field and override `BaseSession` hooks to record collector counters.
- This keeps tests focused on engine send policy while avoiding telemetry constructor dependency in `BaseSession`.

Suggested test helper shape:

```csharp
private sealed class TestSession : BaseSession
{
    private readonly IServerTelemetry _telemetry;

    public TestSession(Socket socket, IServerTelemetry telemetry, SessionSendOptions sendOptions, ...)
        : base(..., sendOptions)
    {
        _telemetry = telemetry;
    }

    protected override void OnNetworkSendRejected(int bytes, int queuedBytes)
        => _telemetry.RecordSendRejected(bytes, queuedBytes);

    // Override only the hooks needed by send policy assertions.
}
```

### 8.2 Required Verification

```text
dotnet build FastPortSharp.sln -c Release
dotnet test FastPortSharp.sln -c Release --no-build
```

Focused tests:

- `ServerTelemetryTests`
- `BaseSessionSendPolicyTests`
- `FastPortTestSmokeServerTests`
- `ObservedMetricsTests`

Static boundary check after implementation:

```text
rg -n "IServerTelemetry|ServerTelemetryCollector|ServerTelemetrySnapshot|NullServerTelemetry" LibNetworks
```

Expected result:

```text
no matches
```

Also verify:

```text
rg -n "LibTestTelemetry" LibNetworks
```

Expected result:

```text
no matches
```

## 9. Compatibility

### 9.1 Preserved

- Observed JSONL field names and camelCase serialization.
- `ServerTelemetryCollector` counter semantics.
- `ServerTelemetrySnapshot` property names and derived `ConnectedSessions`/`SocketErrorRate` behavior.
- Send queue accounting and backpressure semantics.
- Smoke server export service behavior.

### 9.2 Intentionally Changed

- Engine base constructors no longer accept `IServerTelemetry`.
- `BaseSession.ServerTelemetry` protected property is removed.
- `NullServerTelemetry` is removed.
- Telemetry namespace for server collector/snapshot becomes `LibTestTelemetry`.

## 10. Risks And Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Hook placement drifts from old `Record*` placement | High | Replace each call site with the matching hook in the same branch. |
| Hook surface becomes too broad | Medium | Add only hooks needed to preserve current collector counters. |
| Derived smoke session loses parse/protocol counters | Medium | Keep parse/protocol recording in `FastPortTestSmokeClientSession`. |
| `BaseSession` existing lifecycle hooks become ambiguous | Medium | Use `OnNetwork*` prefix for observation hooks and leave `OnReceived`/`OnDisconnected` behavior unchanged. |
| Project reference cycle | High | Keep `LibNetworks` independent; move server telemetry types into `LibTestTelemetry`. |
| Tests hide missing production hook mapping | Medium | Smoke server tests must assert accepted, disconnected, received, sent, send request, and buffer sample counters. |

## 11. Implementation Order

1. Move server telemetry types into `LibTestTelemetry` and update namespaces.
2. Remove `NullServerTelemetry` and `LibNetworks.Telemetry` dependency from `LibNetworks`.
3. Add listener/session network hooks in `LibNetworks`.
4. Replace existing `ServerTelemetry.Record*` call sites with hook calls.
5. Remove telemetry constructor overloads from base listener/session classes.
6. Update smoke server listener/session to override hooks and record collector events.
7. Update tests and test helper sessions to record telemetry through hook overrides.
8. Run build and focused tests.
9. Run static boundary checks.
10. Run full release test suite.

## 12. Acceptance Criteria

- `LibNetworks` has no references to `IServerTelemetry`, `ServerTelemetryCollector`, `ServerTelemetrySnapshot`, `NullServerTelemetry`, or `LibTestTelemetry`.
- `FastPortTestSmokeServer` still exports server observed JSONL through `IServerTelemetryExporter`.
- `ServerTelemetryCollector` tests pass with unchanged counter assertions.
- `BaseSessionSendPolicyTests` pass using hook-based test telemetry recording.
- `FastPortTestSmokeServerTests` pass and validate telemetry fidelity.
- `ObservedMetricsTests` pass and preserve JSON contract.
- Required build/test commands pass.

## 13. Next Phase

Recommended next command:

```text
$pdca do remove-server-telemetry-from-network-base-classes
```
