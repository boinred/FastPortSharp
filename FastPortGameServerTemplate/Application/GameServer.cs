using LibNetworks;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;

namespace FastPortGameServerTemplate.Application;

// Design Ref: §4.1, §11.1 — listener entry point.
// Mirrors FastPortServer's BaseMessageListener subclass pattern but does not reference FastPortServer code.
// StartAccept / RequestShutdown lifecycle is driven by GameServerHostedService.
public sealed class GameServer : BaseMessageListener
{
    public GameServer(ILogger<BaseMessageListener> logger, IClientSessionFactory clientSessionFactory)
        : base(logger, clientSessionFactory)
    {
    }
}
