using System.Net;
using System.Net.Sockets;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;

namespace LibNetworks;

public abstract class BaseListener(ILogger<BaseListener> logger, int maxConnectionsCount)
{
    // TODO : 파일 설정에서 불러온다.
    private readonly int C_MaxConnections = maxConnectionsCount;
    private readonly int C_MaxBufferSize = 1024 * 8; // 8KB

    protected ILogger m_Logger = logger;

    private System.Net.Sockets.Socket m_ListenerSocket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);

    private SocketEventsPool m_SocketEventsPool = new SocketEventsPool(1000);

    private System.Net.Sockets.SocketAsyncEventArgs m_AcceptSocketEvents = new System.Net.Sockets.SocketAsyncEventArgs();

    public bool Start(string ip, int port)
    {
        if (!AddressConverter.TryToEndPoint(ip, port, out var endPoint))
        {
            logger.LogError($"BaseListener, Start, IP is not valid. ${ip}");
            return false;
        }

        try
        {
            m_ListenerSocket.Bind(endPoint!);

            m_ListenerSocket.Listen(100);

            m_AcceptSocketEvents.Completed += OnSocketEventsAcceptCompleted;
            return Accept(m_AcceptSocketEvents);
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
            if (!m_ListenerSocket.AcceptAsync(acceptArgs))
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
            ErrorHandler(args.SocketError);

            return; 
        }

        Socket? clientSocket = args.AcceptSocket;
        if (null == clientSocket)
        {
            ErrorHandler(args.SocketError);
            return;
        }

        m_Logger.LogInformation($"BaseListener, OnSocketEventsAcceptCompleted, End Point : {clientSocket.RemoteEndPoint}");

        //new BaseSessionClient(clientSocket);

        void ErrorHandler(SocketError error)
        {
            m_Logger.LogError($"BaseListener, OnSocketEventsAcceptCompleted, SocketError : {error}");
        }
    }
}
