using Microsoft.Extensions.Logging;

namespace LibNetworks.Sessions;

public abstract class BaseSession
{
    protected ILogger m_Logger;
    private System.Net.Sockets.Socket m_Socket; 

    public BaseSession(ILogger<BaseSession> logger, System.Net.Sockets.Socket socket)
    {
        m_Logger = logger;
        m_Socket = socket; 
    }

    public abstract void OnReceived();

    public abstract void OnSent();

    public abstract void OnDisconnected(); 
}