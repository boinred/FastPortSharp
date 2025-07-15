using System.Net.Sockets;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;

namespace LibNetworks;

public class BaseMessageListener(ILogger<BaseMessageListener> logger, IClientSessionFactory clientSessionFactory) : BaseListener(logger, clientSessionFactory, 1000)
{
    //protected override void OnSocketEventsAcceptCompleted(object sender, SocketAsyncEventArgs args)
    //{
    //    m_Logger.LogDebug("BaseMessageListener, OnSocketEventsAcceptCompleted.");
    //}
}