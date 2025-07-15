using LibNetworks;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastPortClient;

internal class FastPortClient : LibNetworks.BaseConnector
{
    public FastPortClient(ILogger<BaseMessageListener> logger) : base()
    {
    }
}
