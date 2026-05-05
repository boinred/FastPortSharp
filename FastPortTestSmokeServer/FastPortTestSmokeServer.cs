using LibNetworks.Sessions;
using LibNetworks.Telemetry;
using Microsoft.Extensions.Logging;

namespace FastPortTestSmokeServer;

public class FastPortTestSmokeServer(
    ILogger<FastPortTestSmokeServer> logger,
    IClientSessionFactory clientSessionFactory,
    IServerTelemetry serverTelemetry)
    : LibNetworks.BaseMessageListener(logger, clientSessionFactory, serverTelemetry)
{
}
