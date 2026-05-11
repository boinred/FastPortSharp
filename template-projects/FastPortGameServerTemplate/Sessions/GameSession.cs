using FastPortGameServerTemplate.Application;
using FastPortGameServerTemplate.Telemetry;
using Google.Protobuf;
using LibCommons;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace FastPortGameServerTemplate.Sessions;

// Design Ref: §4.1, §9.4 — server-side per-client session.
// Inherits BaseSessionClient (LibNetworks's accepted-session base) and routes incoming packets
// through PacketDispatcher. The send wrapper exposes BaseSession.RequestSendMessage to handlers
// without leaking BaseSession internals.
public class GameSession : BaseSessionClient
{
    private static readonly IDGenerator s_IdGenerator = new();

    private readonly long m_Id = s_IdGenerator.GetNextGeneratedId();
    private readonly PacketDispatcher m_Dispatcher;
    private readonly IGameServerTelemetry m_Telemetry;

    public override long Id => m_Id;

    public GameSession(
        ILogger<BaseSessionClient> logger,
        Socket socket,
        IBuffers receivedBuffers,
        IBuffers sendBuffers,
        PacketDispatcher dispatcher,
        IGameServerTelemetry telemetry)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {
        m_Dispatcher = dispatcher;
        m_Telemetry = telemetry;
    }

    // Public send wrapper so handlers can reply without referencing BaseSession's protected API.
    public void Send<T>(int packetId, IMessage<T> message)
        where T : IMessage<T>
    {
        RequestSendMessage(packetId, message);
    }

    public override void OnAccepted()
    {
        base.OnAccepted();
        m_Logger.LogInformation("GameSession accepted. Id={Id}", m_Id);
        m_Telemetry.OnSessionAccepted(m_Id);
    }

    protected override void OnReceived(BasePacket packet)
    {
        base.OnReceived(packet);
        m_Dispatcher.Dispatch(this, packet);
    }

    protected override void OnDisconnected()
    {
        base.OnDisconnected();
        m_Logger.LogInformation(
            "GameSession disconnected. Id={Id}, RemoteEndPoint={Address}",
            m_Id,
            GetSessionAddress());
        m_Telemetry.OnSessionDisconnected(m_Id);
    }
}
