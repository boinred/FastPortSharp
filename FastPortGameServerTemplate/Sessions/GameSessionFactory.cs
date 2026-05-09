using FastPortGameServerTemplate.Application;
using FastPortGameServerTemplate.Telemetry;
using LibCommons;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace FastPortGameServerTemplate.Sessions;

// Design Ref: §4.1, §9.4 — bridges LibNetworks's IClientSessionFactory to GameSession.
// Each accepted socket gets a fresh GameSession with its own buffer pair.
public sealed class GameSessionFactory : IClientSessionFactory
{
    // Receive/send buffer size aligned with FastPortServer convention (8 KiB).
    private const int BufferCapacityBytes = 8 * 1024;

    private readonly ILogger<BaseSessionClient> m_Logger;
    private readonly PacketDispatcher m_Dispatcher;
    private readonly IGameServerTelemetry m_Telemetry;

    public GameSessionFactory(
        ILogger<BaseSessionClient> logger,
        PacketDispatcher dispatcher,
        IGameServerTelemetry telemetry)
    {
        m_Logger = logger;
        m_Dispatcher = dispatcher;
        m_Telemetry = telemetry;
    }

    public BaseSessionClient Create(Socket clientSocket) => new GameSession(
        m_Logger,
        clientSocket,
        new ArrayPoolCircularBuffers(BufferCapacityBytes),
        new ArrayPoolCircularBuffers(BufferCapacityBytes),
        m_Dispatcher,
        m_Telemetry);
}
