using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;

namespace FastPortClient;

 
public class FastPortConnector : LibNetworks.BaseConnector
{
    public FastPortConnector(ILogger<FastPortConnector> logger, IServerSessionFactory serverSessionFactory) : base(logger, serverSessionFactory)
    {
    }
}
