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

    protected override void OnAcceptFailed(SocketError? socketError, Exception? exception)
    {
        serverTelemetry.RecordAcceptError();
    }

    protected override void OnListenerSocketError(SocketError? socketError, Exception? exception)
    {
        serverTelemetry.RecordSocketError();
    }
}
