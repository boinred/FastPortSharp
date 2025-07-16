using Microsoft.Extensions.Logging;

namespace LibNetworks.Sessions;

public abstract class BaseSessionServer : BaseSession
{
    public BaseSessionServer(ILogger<BaseSessionServer> logger, System.Net.Sockets.Socket socket)
        : base(logger, socket)
    {
    }

    public abstract void OnConnected();
}