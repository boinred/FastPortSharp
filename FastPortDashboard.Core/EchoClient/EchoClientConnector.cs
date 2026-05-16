// Design Ref: §9 — lifecycle 분리 (Connect/Disconnect/state machine).
// Plan SC: FR-03 reconnect, FR-06 disconnect freeze, FR-07 error label.
// State machine은 socket-free → 단위 테스트 가능. 실제 socket 시작은 StartConnect.
using LibNetworks;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;

namespace FastPortDashboard.Maui.EchoClient;

public sealed class EchoClientConnector
{
    private readonly ILoggerFactory _loggerFactory;

    private EchoClientState _state = EchoClientState.Disconnected;
    private string? _errorMessage;
    private EchoClientSession? _currentSession;
    private BaseMessageConnector? _innerConnector;

    public EchoClientConnector(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public EchoClientState State => _state;
    public string? ErrorMessage => _errorMessage;
    public event Action<EchoClientState>? StateChanged;

    // ── State transitions (pure, socket-free; 단위 테스트 대상) ──────

    public bool TryBeginConnect()
    {
        if (_state != EchoClientState.Disconnected && _state != EchoClientState.Error) return false;
        _errorMessage = null;
        SetState(EchoClientState.Connecting);
        return true;
    }

    public void NotifyConnected() => SetState(EchoClientState.Connected);

    public void NotifyError(string message)
    {
        _errorMessage = message;
        SetState(EchoClientState.Error);
    }

    public void NotifyDisconnected() => SetState(EchoClientState.Disconnected);

    private void SetState(EchoClientState next)
    {
        if (_state == next) return;
        _state = next;
        StateChanged?.Invoke(next);
    }

    // ── Network ops (production wiring) ─────────────────────────────

    public bool StartConnect(
        EchoClientOptions options,
        EchoClientStats stats,
        Action<RttSample> onRttSample)
    {
        if (!TryBeginConnect()) return false;

        try
        {
            var sessionLogger = _loggerFactory.CreateLogger<BaseSessionServer>();
            var connectorLogger = _loggerFactory.CreateLogger<BaseMessageConnector>();

            var factory = new EchoClientSessionFactory(
                sessionLogger,
                options,
                stats,
                onRttSample,
                onError: msg => NotifyError(msg),
                onSessionCreated: s =>
                {
                    _currentSession = s;
                    s.OnEventSessionDisconnected += HandleDisconnect;
                    NotifyConnected();
                });

            _innerConnector = new BaseMessageConnector(connectorLogger, factory);
            return _innerConnector.StartConnect(options.Host, options.Port, 1);
        }
        catch (Exception ex)
        {
            NotifyError($"EC-CONNECT-002: {ex.Message}");
            return false;
        }
    }

    public void RequestDisconnect()
    {
        _currentSession?.StopLoop();
        _currentSession?.RequestDisconnect();
    }

    private void HandleDisconnect()
    {
        _currentSession = null;
        NotifyDisconnected();
    }
}
