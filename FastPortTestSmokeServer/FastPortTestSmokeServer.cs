using FastPortTestSmokeServer.Sessions;
using LibNetworks.Sessions;
using LibTestTelemetry;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace FastPortTestSmokeServer;

public class FastPortTestSmokeServer(
    ILogger<FastPortTestSmokeServer> logger,
    IClientSessionFactory clientSessionFactory,
    IServerTelemetry serverTelemetry)
    : LibNetworks.BaseMessageListener(logger, clientSessionFactory)
{
    protected override void OnAcceptSucceeded(Socket clientSocket)
    {
        serverTelemetry.RecordAccept();
    }

    // 계측: accept callback 이후 session 객체 생성 완료까지의 비용
    protected override void OnAcceptSessionCreated(Socket clientSocket, BaseSessionClient clientSession, TimeSpan duration, long acceptCompletedTimestamp)
    {
        serverTelemetry.RecordOperationDuration("accept-session-create", duration);
        // 계측 전달: smoke session에만 accept completion 기준 timestamp 저장
        if (clientSession is FastPortTestSmokeClientSession smokeSession)
        {
            smokeSession.MarkAcceptedByListener(acceptCompletedTimestamp);
        }
    }

    // 계측: accept callback 이후 OnAccepted task 실행 시작까지의 scheduling 지연
    protected override void OnAcceptSessionTaskStarted(Socket clientSocket, BaseSessionClient clientSession, TimeSpan duration, long onAcceptedStartedTimestamp)
    {
        serverTelemetry.RecordOperationDuration("accept-task-start", duration);
        // 계측 전달: smoke session에만 OnAccepted task 시작 timestamp 저장
        if (clientSession is FastPortTestSmokeClientSession smokeSession)
        {
            smokeSession.MarkOnAcceptedStarted(onAcceptedStartedTimestamp);
        }
    }

    protected override void OnAcceptFailed(string phase, SocketError? socketError, Exception? exception)
    {
        serverTelemetry.RecordAcceptError();
    }

    protected override void OnListenerSocketError(string phase, SocketError? socketError, Exception? exception)
    {
        serverTelemetry.RecordSocketError($"listener-{phase}", socketError, exception);
    }
}
