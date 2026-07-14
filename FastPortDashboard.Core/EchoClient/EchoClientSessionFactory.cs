// Design Ref: §9 — Session factory. Single-connection scope (다중 connection은 OOS).
using LibCommons;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace FastPortDashboard.Maui.EchoClient;

public sealed class EchoClientSessionFactory : IServerSessionFactory
{
    private const int BufferCapacityBytes = 8 * 1024;

    private readonly ILogger<BaseSessionServer> _sessionLogger;
    private readonly EchoClientOptions _options;
    private readonly EchoClientStats _stats;
    private readonly Action<RttSample> _onRttSample;
    private readonly Action<string> _onError;
    private readonly Action<EchoClientSession> _onSessionCreated;

    public EchoClientSessionFactory(
        ILogger<BaseSessionServer> sessionLogger,
        EchoClientOptions options,
        EchoClientStats stats,
        Action<RttSample> onRttSample,
        Action<string> onError,
        Action<EchoClientSession> onSessionCreated)
    {
        _sessionLogger = sessionLogger;
        _options = options;
        _stats = stats;
        _onRttSample = onRttSample;
        _onError = onError;
        _onSessionCreated = onSessionCreated;
    }

    public BaseSessionServer Create(Socket connectedSocket)
    {
        var session = new EchoClientSession(
            _sessionLogger,
            connectedSocket,
            new ArrayPoolCircularBuffers(BufferCapacityBytes),
            new ArrayPoolCircularBuffers(BufferCapacityBytes),
            _options,
            _stats,
            _onRttSample,
            _onError);
        _onSessionCreated(session);
        return session;
    }
}
