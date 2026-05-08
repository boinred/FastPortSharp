using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;

namespace LibNetworks;

public abstract class BaseListener : BaseSocket
{
    // 목적: 10K ramp-up 중 TCP connect queue 포화를 줄이기 위한 기본 listen backlog
    private const int C_DefaultListenBacklog = 4096;

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

    // Accept 이후 hook: session factory 생성 비용 및 accept 기준 시각 계측 연결점
    protected virtual void OnAcceptSessionCreated(Socket clientSocket, BaseSessionClient clientSession, TimeSpan duration, long acceptCompletedTimestamp)
    {
    }

    // Accept 이후 hook: OnAccepted task scheduling 지연 및 task 시작 시각 계측 연결점
    protected virtual void OnAcceptSessionTaskStarted(Socket clientSocket, BaseSessionClient clientSession, TimeSpan duration, long onAcceptedStartedTimestamp)
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

    // 흐름: 기존 호출자는 기본 backlog를 사용하도록 유지
    public bool StartAccept(string ip, int port)
    {
        return StartAccept(ip, port, C_DefaultListenBacklog);
    }

    // 흐름: caller가 지정한 backlog로 listener socket을 시작
    public bool StartAccept(string ip, int port, int backlog)
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
            // 설정: 잘못된 backlog 값은 안전한 기본값으로 보정
            int normalizedBacklog = NormalizeListenBacklog(backlog);

            m_Socket.Bind(endPoint!);

            m_Socket.Listen(normalizedBacklog);


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

    // 목적: 설정/환경변수에서 들어온 listen backlog 보정
    private static int NormalizeListenBacklog(int backlog)
    {
        // 목적: appsettings/env override가 0 이하일 때 Listen 호출을 안정화
        return backlog > 0 ? backlog : C_DefaultListenBacklog;
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
        // 계측 기준점: accept completion callback 진입 시각
        long acceptCompletedTimestamp = Stopwatch.GetTimestamp();

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
        // 계측: accept callback 진입부터 session factory 완료까지의 비용
        OnAcceptSessionCreated(
            clientSocket,
            clientSession,
            Stopwatch.GetElapsedTime(acceptCompletedTimestamp),
            acceptCompletedTimestamp);

        // Add Session Managers
        Task.Run(() =>
        {
            // 계측 기준점: ThreadPool task가 실제 실행을 시작한 시각
            long onAcceptedStartedTimestamp = Stopwatch.GetTimestamp();

            // 계측: accept callback 진입부터 OnAccepted task 시작까지의 scheduling 지연
            OnAcceptSessionTaskStarted(
                clientSocket,
                clientSession,
                Stopwatch.GetElapsedTime(acceptCompletedTimestamp),
                onAcceptedStartedTimestamp);

            clientSession.OnAccepted();
        });

        Accept(m_SocketEvent);
    }
}
