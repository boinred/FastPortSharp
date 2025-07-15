using Microsoft.Extensions.Logging;

namespace LibNetworks.Sessions;

public abstract class BaseSessionClient : BaseSession
{
    public BaseSessionClient(ILogger<BaseSessionClient> logger, System.Net.Sockets.Socket socket) : base(logger, socket)
    {

    }

    protected abstract void OnAccepted();
    

}