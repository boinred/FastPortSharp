using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;

namespace FastPortServer;

public class FastPortServer(ILogger<FastPortServer> logger, IClientSessionFactory clientSessionFactory) 
    : LibNetworks.BaseMessageListener(logger, clientSessionFactory)
{

}
