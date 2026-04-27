using LibNetworks.Sessions;
using LibNetworks.Telemetry;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace FastPortSmokeServer.Sessions;

public class FastPortSmokeClientSessionFactory : IClientSessionFactory
{
    private readonly ILogger<BaseSessionClient> m_Logger;
    private readonly IServerTelemetry m_ServerTelemetry;

    public FastPortSmokeClientSessionFactory(ILogger<BaseSessionClient> logger, IServerTelemetry serverTelemetry)
    {
        m_Logger = logger;
        m_ServerTelemetry = serverTelemetry;
    }

    public BaseSessionClient Create(Socket clientSocket) => new FastPortSmokeClientSession(
        m_Logger,
        clientSocket,
        new LibCommons.ArrayPoolCircularBuffers(8 * 1024),
        new LibCommons.ArrayPoolCircularBuffers(8 * 1024),
        m_ServerTelemetry);
}
