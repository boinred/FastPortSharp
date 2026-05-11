using LibNetworks;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;

namespace FastPortGameServerTemplate.SampleClient;

public sealed class SampleClientConnector : BaseMessageConnector
{
    public SampleClientConnector(ILogger<BaseMessageConnector> logger, IServerSessionFactory factory)
        : base(logger, factory)
    {
    }
}
