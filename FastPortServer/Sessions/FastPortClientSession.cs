using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace FastPortServer.Sessions;

public class FastPortClientSession : LibNetworks.Sessions.BaseSessionClient
{
    public FastPortClientSession(ILogger<LibNetworks.Sessions.BaseSessionClient> logger, Socket socket) : base(logger, socket)
    {
    }

    protected override void OnReceived()
    {
        // Handle received data here
        m_Logger.LogInformation("Data received from client.");
    }

    public override void OnAccepted()
    {
        m_Logger.LogInformation("FastPortClientSession, OnAccepted. ");
    }
}
