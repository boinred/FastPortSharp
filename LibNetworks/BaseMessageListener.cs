using System.Net.Sockets;
using LibNetworks.Sessions;
using LibNetworks.Telemetry;
using Microsoft.Extensions.Logging;

namespace LibNetworks;

public class BaseMessageListener : BaseListener
{
    public BaseMessageListener(ILogger<BaseMessageListener> logger, IClientSessionFactory clientSessionFactory)
        : base(logger, clientSessionFactory, 1000)
    {
    }

    public BaseMessageListener(ILogger<BaseMessageListener> logger, IClientSessionFactory clientSessionFactory, IServerTelemetry serverTelemetry)
        : base(logger, clientSessionFactory, serverTelemetry, 1000)
    {
    }

    //protected override void OnSocketEventsAcceptCompleted(object sender, SocketAsyncEventArgs args)
    //{
    //    m_Logger.LogDebug("BaseMessageListener, OnSocketEventsAcceptCompleted.");
    //}
}
