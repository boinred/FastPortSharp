using LibNetworks.Sessions;
using LibNetworks.Telemetry;
using Microsoft.Extensions.Logging;

namespace FastPortSmokeServer;

public class FastPortSmokeServer(
    ILogger<FastPortSmokeServer> logger,
    IClientSessionFactory clientSessionFactory,
    IServerTelemetry serverTelemetry)
    : LibNetworks.BaseMessageListener(logger, clientSessionFactory, serverTelemetry)
{
}
