namespace FastPortGameServerTemplate.Telemetry;

// Design Ref: §4.1 — game-server-level telemetry abstraction.
// Concrete implementations (OpenTelemetry / FastPort.Telemetry) are deferred to a future cycle.
// This interface lives in the template so the engine packages (LibCommons / LibNetworks) stay
// telemetry-free per HANDOFF L278.
public interface IGameServerTelemetry
{
    void OnSessionAccepted(long sessionId);

    void OnSessionDisconnected(long sessionId);

    void OnPacketReceived(long sessionId, int packetId, int dataSize);

    void OnPacketHandled(long sessionId, int packetId, double elapsedMs);

    void OnHandlerException(long sessionId, int packetId, System.Exception exception);
}
