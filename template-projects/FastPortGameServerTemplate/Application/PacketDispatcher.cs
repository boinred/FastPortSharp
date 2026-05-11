using FastPortGameServerTemplate.Handlers;
using FastPortGameServerTemplate.Sessions;
using FastPortGameServerTemplate.Telemetry;
using LibCommons;
using Microsoft.Extensions.Logging;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace FastPortGameServerTemplate.Application;

// Design Ref: §4.1, §9.1-9.4 — routes inbound BasePacket to the registered IPacketHandler by packet id.
// Telemetry hooks fire on receive / handle / exception so a future cycle can plug in metrics.
public sealed class PacketDispatcher
{
    private readonly Dictionary<int, IPacketHandler> m_Handlers;
    private readonly ILogger<PacketDispatcher> m_Logger;
    private readonly IGameServerTelemetry m_Telemetry;

    public PacketDispatcher(
        IEnumerable<IPacketHandler> handlers,
        ILogger<PacketDispatcher> logger,
        IGameServerTelemetry telemetry)
    {
        m_Handlers = handlers.ToDictionary(h => h.PacketId);
        m_Logger = logger;
        m_Telemetry = telemetry;
    }

    public void Dispatch(GameSession session, BasePacket packet)
    {
        // BasePacket framing: PacketSize header + DataSize payload. Packet id is the leading int32
        // of the payload (LibNetworks convention; see BaseSession.RequestSendMessage / ParseMessageFromPacket).
        int packetId = TryReadPacketId(packet);

        m_Telemetry.OnPacketReceived(session.Id, packetId, packet.DataSize);

        if (!m_Handlers.TryGetValue(packetId, out var handler))
        {
            m_Logger.LogWarning(
                "No handler registered. SessionId={SessionId}, PacketId={PacketId}, DataSize={DataSize}",
                session.Id, packetId, packet.DataSize);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            handler.Handle(session, packet);
            stopwatch.Stop();
            m_Telemetry.OnPacketHandled(session.Id, packetId, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (System.Exception ex)
        {
            stopwatch.Stop();
            m_Logger.LogError(ex,
                "Handler threw. SessionId={SessionId}, PacketId={PacketId}",
                session.Id, packetId);
            m_Telemetry.OnHandlerException(session.Id, packetId, ex);
            // Policy: keep session alive; future cycle may add per-handler isolation policies.
        }
    }

    private static int TryReadPacketId(BasePacket packet)
    {
        // Mirrors LibNetworks.Extensions.BasePacketExtensions.ParseMessageFromPacket framing:
        // first 4 bytes of payload = packet id (int32 little-endian).
        if (packet.DataSize < 4)
        {
            return -1;
        }
        return BinaryPrimitives.ReadInt32LittleEndian(packet.Data.Slice(0, 4));
    }
}
