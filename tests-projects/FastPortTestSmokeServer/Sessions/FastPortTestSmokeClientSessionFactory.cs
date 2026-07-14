using LibNetworks.Sessions;
using LibTestTelemetry;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace FastPortTestSmokeServer.Sessions;

public class FastPortTestSmokeClientSessionFactory : IClientSessionFactory
{
    private readonly ILogger<BaseSessionClient> m_Logger;
    private readonly IServerTelemetry m_ServerTelemetry;
    private readonly SessionIdleTracker m_SessionIdleTracker;

    public FastPortTestSmokeClientSessionFactory(
        ILogger<BaseSessionClient> logger,
        IServerTelemetry serverTelemetry,
        SessionIdleTracker sessionIdleTracker)
    {
        m_Logger = logger;
        m_ServerTelemetry = serverTelemetry;
        m_SessionIdleTracker = sessionIdleTracker;
    }

    public BaseSessionClient Create(Socket clientSocket) => new FastPortTestSmokeClientSession(
        m_Logger,
        clientSocket,
        new LibCommons.ArrayPoolCircularBuffers(8 * 1024),
        new LibCommons.ArrayPoolCircularBuffers(8 * 1024),
        m_ServerTelemetry,
        m_SessionIdleTracker);
}
