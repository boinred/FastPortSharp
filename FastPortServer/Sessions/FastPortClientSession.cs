using LibCommons;
using LibNetworks;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace FastPortServer.Sessions;

public class FastPortClientSession : LibNetworks.Sessions.BaseSessionClient
{
    public FastPortClientSession(ILogger<LibNetworks.Sessions.BaseSessionClient> logger, Socket socket, LibCommons.IBuffers receivedBuffers, LibCommons.IBuffers sendBuffers)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {
    }

    protected override void OnReceived(BasePacket packet)
    {
        base.OnReceived(packet);
        // Handle received data here
        m_Logger.LogInformation($"FastPortClientSession, OnReceived, Packet Size : {packet.PacketSize}, Date Size : {packet.DataSize}");

        RequestSendBuffers(packet.Data.ToArray());
    }

    public override void OnAccepted()
    {
        base.OnAccepted();

        m_Logger.LogInformation("FastPortClientSession, OnAccepted. ");
    }

    protected override void OnDisconnected()
    {
        base.OnDisconnected();

        m_Logger.LogInformation($"FastPortClientSession, OnDisconnected. Remote End Point : {GetSessionAddress()}");
    }
}
