using System.Net.Sockets;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;

namespace LibNetworks;

public class BaseMessageListener : BaseListener
{
    // Message listener: base accept hook 유지, telemetry dependency 없음
    public BaseMessageListener(ILogger<BaseMessageListener> logger, IClientSessionFactory clientSessionFactory)
        : base(logger, clientSessionFactory, 1000)
    {
    }

    //protected override void OnSocketEventsAcceptCompleted(object sender, SocketAsyncEventArgs args)
    //{
    //    m_Logger.LogDebug("BaseMessageListener, OnSocketEventsAcceptCompleted.");
    //}
}
