using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace LibNetworks;


public class BaseConnector : BaseSocket
{
    private ILogger m_Logger;

    public BaseConnector(ILogger<BaseConnector> logger)
    {
        m_Logger = logger;
    }

    public bool StartConnect(string ip, int port)
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

        return false;
    }

    private void OnSocketEventsConnectedCompleted(object? sender, SocketAsyncEventArgs args)
    {
        //
        if (args.SocketError != SocketError.Success)
        {
            m_Logger.LogError($"BaseListener, OnSocketEventsAcceptCompleted, SocketError : {args.SocketError}");
            return;
        }


    }
}
