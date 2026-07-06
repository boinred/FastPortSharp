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
    // 목적: 기존 listener와 동일한 단일 outstanding accept 동작 보존
    private const int C_DefaultOutstandingAccepts = 1;
    // 목적: 설정 오류로 accept args/callback이 과도하게 늘어나는 상황 방지
    private const int C_MaxOutstandingAccepts = 64;

    // TODO : 파일 설정에서 불러온다.
    private readonly int C_MaxConnections;
    //private readonly int C_MaxBufferSize = 1024 * 8; // 8KB
    private IClientSessionFactory m_ClientSessionFactory;

    protected ILogger m_Logger;

    protected bool m_bIsRunning = false;

    // Listener가 정지되었을 경우 처리하는 CancellationToken 
    private CancellationTokenSource m_CancellationTokenSource = new CancellationTokenSource();

    // 상태: listener accept pump 전용 SocketAsyncEventArgs 목록
    private SocketAsyncEventArgs[] m_AcceptSocketEvents = Array.Empty<SocketAsyncEventArgs>();

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
        return StartAccept(ip, port, C_DefaultListenBacklog, C_DefaultOutstandingAccepts);
    }

    // 흐름: caller가 지정한 backlog로 listener socket을 시작
    public bool StartAccept(string ip, int port, int backlog)
    {
        return StartAccept(ip, port, backlog, C_DefaultOutstandingAccepts);
    }

    // 흐름: caller가 지정한 backlog와 outstanding accept 수로 listener socket을 시작
    public bool StartAccept(string ip, int port, int backlog, int outstandingAccepts)
    {
        if (!AddressConverter.TryToEndPoint(ip, port, out var endPoint))
        {
            // Invalid endpoint: accept 실패 관측, telemetry 타입 비의존
            OnAcceptFailed("start-endpoint", null, null);
            m_Logger.LogError("BaseListener, Start, IP is not valid. {Ip}", ip);
            return false;
        }

        try
        {
            // 설정: 잘못된 backlog 값은 안전한 기본값으로 보정
            int normalizedBacklog = NormalizeListenBacklog(backlog);
            // 설정: 잘못된 outstanding accept 값은 기존 동작과 동일한 1로 보정
            int normalizedOutstandingAccepts = NormalizeOutstandingAccepts(outstandingAccepts);

            m_Socket.Bind(endPoint!);

            m_Socket.Listen(normalizedBacklog);

            // 상태: listener 시작 이후 completion callback이 repost할 수 있도록 running 상태 전환
            m_bIsRunning = true;

            // 상태: accept 전용 args를 outstanding accept 수만큼 생성
            m_AcceptSocketEvents = CreateAcceptSocketEvents(normalizedOutstandingAccepts);
            foreach (SocketAsyncEventArgs acceptArgs in m_AcceptSocketEvents)
            {
                // 흐름: 초기 outstanding accept 등록 실패 시 listener 시작 실패 처리
                if (!Accept(acceptArgs))
                {
                    RequestShutdown();
                    return false;
                }
            }

            return true;
        }
        catch (System.Exception ex)
        {
            // Bind/Listen 예외: accept 실패 및 listener socket error 동시 관측
            OnAcceptFailed("start-bind-listen", null, ex);
            OnListenerSocketError("start-bind-listen", null, ex);
            m_Logger.LogError(ex, "BaseListener, Start, Exception");
        }

        return false;
    }

    // 목적: 설정/환경변수에서 들어온 listen backlog 보정
    private static int NormalizeListenBacklog(int backlog)
    {
        // 목적: appsettings/env override가 0 이하일 때 Listen 호출을 안정화
        return backlog > 0 ? backlog : C_DefaultListenBacklog;
    }

    // 목적: 설정/환경변수에서 들어온 outstanding accept 수 보정
    internal static int NormalizeOutstandingAccepts(int outstandingAccepts)
    {
        if (outstandingAccepts <= 0)
        {
            // 목적: invalid 값은 기존 단일 accept 동작으로 보정
            return C_DefaultOutstandingAccepts;
        }

        // 목적: 과도한 설정값은 listener callback 폭증 방지 상한으로 제한
        return Math.Min(outstandingAccepts, C_MaxOutstandingAccepts);
    }

    // 목적: listener accept pump 전용 SocketAsyncEventArgs 목록 생성
    private SocketAsyncEventArgs[] CreateAcceptSocketEvents(int outstandingAccepts)
    {
        var acceptSocketEvents = new SocketAsyncEventArgs[outstandingAccepts];
        for (int index = 0; index < acceptSocketEvents.Length; index++)
        {
            // 상태: 하나의 args는 하나의 outstanding AcceptAsync에만 사용
            var acceptArgs = new SocketAsyncEventArgs();
            acceptArgs.Completed += OnSocketEventsAcceptCompleted;
            acceptSocketEvents[index] = acceptArgs;
        }

        return acceptSocketEvents;
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
        // 흐름: 동기 완료를 재귀 대신 루프로 처리해 backlog 연쇄 시 스택 증가 방지
        while (Volatile.Read(ref m_bIsRunning))
        {
            // Reset the acceptArgs for reuse
            acceptArgs.AcceptSocket = null;

            try
            {
                if (m_Socket.AcceptAsync(acceptArgs))
                {
                    // 비동기 대기 상태: completion callback이 이어서 처리
                    return true;
                }
            }
            catch (Exception ex)
            {
                // AcceptAsync 시작 실패: subclass hook 기반 외부 관측
                OnAcceptFailed("accept-start", null, ex);
                OnListenerSocketError("accept-start", null, ex);
                m_Logger.LogError(ex, "BaseListener, Accept, Exception");
                return false;
            }

            // 동기 완료: 즉시 처리 후 루프에서 동일 args 재등록
            ProcessAccept(acceptArgs);
        }

        // 상태: shutdown 이후에는 새 accept repost 금지
        return false;
    }

    private void OnSocketEventsAcceptCompleted(object? sender, SocketAsyncEventArgs args)
    {
        ProcessAccept(args);

        // 흐름: completion된 동일 args를 listener running 상태에서만 재등록
        Accept(args);
    }

    private void ProcessAccept(SocketAsyncEventArgs args)
    {
        // 계측 기준점: accept completion 처리 진입 시각
        long acceptCompletedTimestamp = Stopwatch.GetTimestamp();

        try
        {
            if (args.SocketError != SocketError.Success)
            {
                // Accept completion socket error: accept 실패 및 socket error
                OnAcceptFailed("accept-completion", args.SocketError, null);
                OnListenerSocketError("accept-completion", args.SocketError, null);
                m_Logger.LogError("BaseListener, OnSocketEventsAcceptCompleted, SocketError : {SocketError}", args.SocketError);
                return;
            }
            Socket? clientSocket = args.AcceptSocket;
            if (null == clientSocket)
            {
                // Completion without socket: session 생성 불가, accept 실패
                OnAcceptFailed("accept-completion-null-socket", null, null);
                m_Logger.LogError("BaseListener, OnSocketEventsAcceptCompleted, Socket is not valid.");
                return;
            }

            // Accept 성공 hook: session 생성 전 호출, 기존 telemetry 순서 보존
            OnAcceptSucceeded(clientSocket);
            if (m_Logger.IsEnabled(LogLevel.Debug))
            {
                // 관측 로그: RemoteEndPoint 조회(syscall + 할당)는 debug 레벨에서만 수행
                m_Logger.LogDebug("BaseListener, OnSocketEventsAcceptCompleted, End Point : {RemoteEndPoint}", clientSocket.RemoteEndPoint);
            }

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
        }
        catch (Exception ex)
        {
            // Accept 처리 예외: pump 유지를 위해 관측 후 계속 진행
            OnAcceptFailed("accept-process", null, ex);
            m_Logger.LogError(ex, "BaseListener, ProcessAccept, Exception");
        }
    }
}
