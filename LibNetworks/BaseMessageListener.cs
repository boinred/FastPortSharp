using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace LibNetworks;

public class BaseMessageListener(ILogger<BaseMessageListener> logger) : BaseListener(logger, 1000)
{
    //protected override void OnSocketEventsAcceptCompleted(object sender, SocketAsyncEventArgs args)
    //{
    //    m_Logger.LogDebug("BaseMessageListener, OnSocketEventsAcceptCompleted.");
    //}
}