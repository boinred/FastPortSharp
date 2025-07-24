using Microsoft.Extensions.Logging;

namespace LibNetworks.Sessions;

public class BaseSessionServer : BaseSession
{
    public BaseSessionServer(ILogger<BaseSessionServer> logger, System.Net.Sockets.Socket socket, LibCommons.IBuffers receivedBuffers)
        : base(logger, socket, receivedBuffers)
    {
    }

    public virtual void OnConnected()
    {
        RequestSendString("baseSessionServer connected.");
    }
}