using Microsoft.Extensions.Logging;

namespace LibNetworks.Sessions;

/// <summary>
/// Base class for client sessions.
/// </summary>
public abstract class BaseSessionClient : BaseSession
{
    public abstract long Id { get; }
    
    public BaseSessionClient(ILogger<BaseSessionClient> logger, System.Net.Sockets.Socket socket, LibCommons.IBuffers receivedBuffers, LibCommons.IBuffers sendBuffers)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {
        
    }

    public virtual void OnAccepted()
    {
        m_Logger.LogInformation($"BaseSessionClient, OnAccepted. Id : {Id}, Remote End Point : {GetSessionAddress()}");

        RequestReceived();
    }

    protected override void OnDisconnected()
    {
        m_Logger.LogInformation($"BaseSessionClient, OnDisconnected. Id : {Id}, Remote End Point : {GetSessionAddress()}");

        base.OnDisconnected();
    }
}