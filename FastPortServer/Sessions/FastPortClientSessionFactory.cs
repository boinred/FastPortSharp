using System.Net.Sockets;

namespace FastPortServer.Sessions;

public class FastPortClientSessionFactory : LibNetworks.Sessions.IClientSessionFactory
{
    public LibNetworks.Sessions.BaseSessionClient Create(Socket clientSocket)
    {
        throw new NotImplementedException();
    }
}
