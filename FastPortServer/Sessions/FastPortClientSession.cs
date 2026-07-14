using LibCommons;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace FastPortServer.Sessions;

public class FastPortClientSession : BaseSessionClient
{
    private static readonly IDGenerator m_IdGenerator = new IDGenerator();

    private readonly long m_Id = m_IdGenerator.GetNextGeneratedId();

    public override long Id => m_Id;

    public FastPortClientSession(
        ILogger<BaseSessionClient> logger,
        Socket socket,
        IBuffers receivedBuffers,
        IBuffers sendBuffers)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {
    }

    protected override void OnReceived(BasePacket packet)
    {
        base.OnReceived(packet);

        m_Logger.LogDebug(
            "FastPortClientSession, OnReceived. PacketSize:{PacketSize}, DataSize:{DataSize}",
            packet.PacketSize,
            packet.DataSize);
    }

    public override void OnAccepted()
    {
        base.OnAccepted();
        m_Logger.LogInformation("FastPortClientSession, OnAccepted. Id:{Id}", Id);
    }

    protected override void OnDisconnected()
    {
        base.OnDisconnected();
        m_Logger.LogInformation("FastPortClientSession, OnDisconnected. Id:{Id}, RemoteEndPoint:{Address}", Id, GetSessionAddress());
    }
}
