using System.Net;
using System.Net.Sockets;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;

namespace LibNetworks;

public abstract class BaseListener : BaseSocket
{

    // TODO : 파일 설정에서 불러온다.
    private readonly int C_MaxConnections;
    private readonly int C_MaxBufferSize = 1024 * 8; // 8KB

    protected ILogger m_Logger;

    private IClientSessionFactory m_ClientSessionFactory;

    // TODO: Session Manager 

    public BaseListener(ILogger<BaseListener> logger, IClientSessionFactory clientSessionFactory , int maxConnectionsCount)
    {
        m_Logger = logger;
        m_ClientSessionFactory = clientSessionFactory;

        C_MaxConnections = maxConnectionsCount;
    }

    public bool StartAccept(string ip, int port)
    {
        if (!AddressConverter.TryToEndPoint(ip, port, out var endPoint))
        {
            m_Logger.LogError($"BaseListener, Start, IP is not valid. ${ip}");
            return false;
        }

        try
        {
            m_Socket.Bind(endPoint!);

            m_Socket.Listen(100);

            m_SocketEvent.Completed += OnSocketEventsAcceptCompleted;
            return Accept(m_SocketEvent);
        }
        catch (System.Exception ex)
        {
            m_Logger.LogError($"BaseListener, Start, Exception : {ex}");
        }

        return true;
    }

    private bool Accept(System.Net.Sockets.SocketAsyncEventArgs acceptArgs)
    {
        // Reset the acceptArgs for reuse
        acceptArgs.AcceptSocket = null;

        try
        {
            if (!m_Socket.AcceptAsync(acceptArgs))
            {
                // If AcceptAsync returns false, we handle the accept operation immediately
                OnSocketEventsAcceptCompleted(this, acceptArgs);
            }

            return true;
        }
        catch (Exception ex)
        {
            m_Logger.LogError($"BaseListener, Accept, Exception : {ex}");
        }

        return false;
    }

    private void OnSocketEventsAcceptCompleted(object? sender, SocketAsyncEventArgs args)
    {
        //
        if (args.SocketError != SocketError.Success)
        {
            m_Logger.LogError($"BaseListener, OnSocketEventsAcceptCompleted, SocketError : {args.SocketError}");
            return; 
        }
        Socket? clientSocket = args.AcceptSocket;
        if (null == clientSocket)
        {
            m_Logger.LogError($"BaseListener, OnSocketEventsAcceptCompleted, Socket is not valid.");
            return;
        }

        m_Logger.LogInformation($"BaseListener, OnSocketEventsAcceptCompleted, End Point : {clientSocket.RemoteEndPoint}");

        // TODO: 다른 쓰레드에서 처리되어야 한다.

        //new BaseSessionClient(clientSocket);
        BaseSessionClient clientSession = m_ClientSessionFactory.Create(clientSocket);

        // Add Session Managers


        Accept(m_SocketEvent);
    }
}
