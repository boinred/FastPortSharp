using LibCommons;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace FastPortServer.Sessions;

public class FastPortClientSession : LibNetworks.Sessions.BaseSessionClient
{
    public FastPortClientSession(ILogger<LibNetworks.Sessions.BaseSessionClient> logger, Socket socket, LibCommons.IBuffers receivedBuffers) : base(logger, socket, receivedBuffers)
    {
    }

    protected override void OnReceived(BasePacket packet)
    {
        // Handle received data here
        m_Logger.LogInformation("Data received from client.");
    }

    public override void OnAccepted()
    {
        m_Logger.LogInformation("FastPortClientSession, OnAccepted. ");
    }
}
