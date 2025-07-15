using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastPortServer;

internal class FastPortServer(ILogger<FastPortServer> logger, IClientSessionFactory clientSessionFactory) : LibNetworks.BaseMessageListener(logger, clientSessionFactory)
{

}
