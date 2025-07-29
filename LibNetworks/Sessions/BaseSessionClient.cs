using Microsoft.Extensions.Logging;

namespace LibNetworks.Sessions;

/// <summary>
/// Base class for client sessions.
/// </summary>
public class BaseSessionClient : BaseSession
{
    public BaseSessionClient(ILogger<BaseSessionClient> logger, System.Net.Sockets.Socket socket, LibCommons.IBuffers receivedBuffers, LibCommons.IBuffers sendBuffers)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {
        
    }

    public virtual void OnAccepted()
    {
        m_Logger.LogInformation($"BaseSessionClient, OnAccepted. Remote End Point : {GetSessionAddress()}");

        RequestReceived();
    }
}