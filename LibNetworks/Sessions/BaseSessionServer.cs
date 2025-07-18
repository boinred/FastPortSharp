using Microsoft.Extensions.Logging;

namespace LibNetworks.Sessions;

public class BaseSessionServer : BaseSession
{
    public BaseSessionServer(ILogger<BaseSessionServer> logger, System.Net.Sockets.Socket socket)
        : base(logger, socket)
    {
    }

    public virtual void OnConnected()
    {
        RequestSendString("baseSessionServer connected.");
    }
}