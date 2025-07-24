using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FastPortClient.Sessions;

public class FastPortServerSessionFactory : LibNetworks.Sessions.IServerSessionFactory
{
    private ILogger<FastPortServerSession> m_Logger;

    public FastPortServerSessionFactory(ILogger<FastPortServerSession> logger)
    {
        m_Logger = logger; 
    }

    public BaseSessionServer Create(Socket connectedSocket) => new FastPortServerSession(m_Logger, connectedSocket, new LibCommons.BaseCircularBuffers(8 * 1024));
}
