using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace FastPortServer.Sessions;

public class FastPortClientSessionFactory : LibNetworks.Sessions.IClientSessionFactory
{
    private ILogger<BaseSessionClient> m_Logger; 
    public FastPortClientSessionFactory(ILogger<BaseSessionClient> logger)
    {
        m_Logger = logger; 
    }

    public LibNetworks.Sessions.BaseSessionClient Create(Socket clientSocket)
    {
        return new FastPortClientSession(m_Logger, clientSocket);
    }
}
