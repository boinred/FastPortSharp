using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibNetworks;

public class BaseMessageConnector : BaseConnector
{
    public BaseMessageConnector(ILogger<BaseMessageConnector> logger) : base(logger)
    {
    }
}
