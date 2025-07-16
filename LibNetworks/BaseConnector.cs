using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace LibNetworks;


public class BaseConnector : BaseSocket
{
    private ILogger m_Logger;

    private IServerSessionFactory m_ServerSessionFactory; 

    public BaseConnector(ILogger<BaseConnector> logger, IServerSessionFactory serverSessionFactory)
    {
        m_Logger = logger;
        m_ServerSessionFactory = serverSessionFactory; 
    }

    public bool StartConnect(string ip, int port, int connectionCount)
    {
        if (!AddressConverter.TryToEndPoint(ip, port, out var endPoint))
        {
            m_Logger.LogError($"BaseListener, Start, IP is not valid. ${ip}");
            return false;
        }
        m_SocketEvent.RemoteEndPoint = endPoint;
        m_SocketEvent.Completed += OnSocketEventsConnectedCompleted;
        if (!m_Socket.ConnectAsync(m_SocketEvent))
        {
            OnSocketEventsConnectedCompleted(this, m_SocketEvent);
        }

        return true;
    }

    private void OnSocketEventsConnectedCompleted(object? sender, SocketAsyncEventArgs args)
    {
        //
        if (args.SocketError != SocketError.Success)
        {
            m_Logger.LogError($"BaseListener, OnSocketEventsAcceptCompleted, SocketError : {args.SocketError}");
            return;
        }


        m_Logger.LogInformation($"BaseConnector, OnSocketEventsConnectedCompleted, Connected to {args.RemoteEndPoint}");

        // TODO: 다른 쓰레드에서 처리되어야 한다.
        var session = m_ServerSessionFactory.Create(m_Socket);

        // Add Session Managers 

        Task.Run(() => session.OnConnected());
    }
}
