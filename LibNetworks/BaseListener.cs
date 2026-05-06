using System.Net;
using System.Net.Sockets;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;

namespace LibNetworks;

public abstract class BaseListener : BaseSocket
{
    

    // TODO : 파일 설정에서 불러온다.
    private readonly int C_MaxConnections;
    //private readonly int C_MaxBufferSize = 1024 * 8; // 8KB
    private IClientSessionFactory m_ClientSessionFactory;

    protected ILogger m_Logger;

    protected bool m_bIsRunning = false;

    // Listener가 정지되었을 경우 처리하는 CancellationToken 
    private CancellationTokenSource m_CancellationTokenSource = new CancellationTokenSource();

    // TODO: Session Manager 

    // Listener: telemetry 구현 미포함, accept 흐름 전담
    public BaseListener(ILogger<BaseListener> logger, IClientSessionFactory clientSessionFactory, int maxConnectionsCount)
    {
        // Engine dependency: listener logger, session factory
        m_Logger = logger;
        m_ClientSessionFactory = clientSessionFactory;

        // 최대 접속 수: 기존 constructor surface 유지용
        C_MaxConnections = maxConnectionsCount;
    }

    // Accept 성공 hook: subclass 관측 전용
    protected virtual void OnAcceptSucceeded(Socket clientSocket)
    {
    }

    // Accept 실패 hook: phase 포함 accept failure classification 연결점
    protected virtual void OnAcceptFailed(string phase, SocketError? socketError, Exception? exception)
    {
        OnAcceptFailed(socketError, exception);
    }

    // Accept 실패 hook: 기존 subclass 호환용 socket error/exception context
    protected virtual void OnAcceptFailed(SocketError? socketError, Exception? exception)
    {
    }

    // Listener socket error hook: phase 포함 socket error classification 연결점
    protected virtual void OnListenerSocketError(string phase, SocketError? socketError, Exception? exception)
    {
        OnListenerSocketError(socketError, exception);
    }

    // Listener socket error hook: 기존 subclass 호환용 accept 실패와 별도 카운터 분리
    protected virtual void OnListenerSocketError(SocketError? socketError, Exception? exception)
    {
    }

    public bool StartAccept(string ip, int port)
    {
        if (!AddressConverter.TryToEndPoint(ip, port, out var endPoint))
        {
            // Invalid endpoint: accept 실패 관측, telemetry 타입 비의존
            OnAcceptFailed("start-endpoint", null, null);
            m_Logger.LogError($"BaseListener, Start, IP is not valid. ${ip}");
            return false;
        }
        m_bIsRunning = true; 

        try
        {
            m_Socket.Bind(endPoint!);

            m_Socket.Listen(100);


            m_SocketEvent.Completed += OnSocketEventsAcceptCompleted;
            return Accept(m_SocketEvent);
        }
        catch (System.Exception ex)
        {
            // Bind/Listen 예외: accept 실패 및 listener socket error 동시 관측
            OnAcceptFailed("start-bind-listen", null, ex);
            OnListenerSocketError("start-bind-listen", null, ex);
            m_Logger.LogError($"BaseListener, Start, Exception : {ex}");
        }

        return false;
    }

    public void RequestShutdown()
    {
        var result = Interlocked.CompareExchange(ref m_bIsRunning, false, true);
        if(true != result)
        {
            return; 
        }

        // TODO : Shutdown Session Managers

        RequestDisconnect();
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
            // AcceptAsync 시작 실패: subclass hook 기반 외부 관측
            OnAcceptFailed("accept-start", null, ex);
            OnListenerSocketError("accept-start", null, ex);
            m_Logger.LogError($"BaseListener, Accept, Exception : {ex}");
        }

        return false;
    }

    private void OnSocketEventsAcceptCompleted(object? sender, SocketAsyncEventArgs args)
    {
        //
        if (args.SocketError != SocketError.Success)
        {
            // Accept completion socket error: accept 실패 및 socket error
            OnAcceptFailed("accept-completion", args.SocketError, null);
            OnListenerSocketError("accept-completion", args.SocketError, null);
            m_Logger.LogError($"BaseListener, OnSocketEventsAcceptCompleted, SocketError : {args.SocketError}");
            return; 
        }
        Socket? clientSocket = args.AcceptSocket;
        if (null == clientSocket)
        {
            // Completion without socket: session 생성 불가, accept 실패
            OnAcceptFailed("accept-completion-null-socket", null, null);
            m_Logger.LogError($"BaseListener, OnSocketEventsAcceptCompleted, Socket is not valid.");
            return;
        }

        // Accept 성공 hook: session 생성 전 호출, 기존 telemetry 순서 보존
        OnAcceptSucceeded(clientSocket);
        m_Logger.LogInformation($"BaseListener, OnSocketEventsAcceptCompleted, End Point : {clientSocket.RemoteEndPoint}");

        // TODO: 다른 thread 처리 필요

        //new BaseSessionClient(clientSocket);
        BaseSessionClient clientSession = m_ClientSessionFactory.Create(clientSocket);

        // Add Session Managers
        Task.Run(() => clientSession.OnAccepted());

        Accept(m_SocketEvent);
    }
}
