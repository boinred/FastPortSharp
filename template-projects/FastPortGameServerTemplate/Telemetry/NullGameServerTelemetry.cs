namespace FastPortGameServerTemplate.Telemetry;

// Design Ref: §4.1 — default no-op implementation registered as the DI default.
// Replace via DI in your own game server when adopting OpenTelemetry / structured logging.
public sealed class NullGameServerTelemetry : IGameServerTelemetry
{
    public static readonly NullGameServerTelemetry Instance = new();

    public void OnSessionAccepted(long sessionId) { }

    public void OnSessionDisconnected(long sessionId) { }

    public void OnPacketReceived(long sessionId, int packetId, int dataSize) { }

    public void OnPacketHandled(long sessionId, int packetId, double elapsedMs) { }

    public void OnHandlerException(long sessionId, int packetId, System.Exception exception) { }
}
