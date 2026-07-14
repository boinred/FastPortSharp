// Design Ref: §9 — Echo Client ViewModel: compose Connector + Stats, marshal events to UI thread.
// Plan SC: FR-02 입력, FR-03 Connect, FR-04 RTT 차트, FR-05 KPI 1s 갱신, FR-06 freeze/reset, FR-07 error.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastPortDashboard.Maui.EchoClient;

namespace FastPortDashboard.Maui.ViewModels;

public sealed partial class EchoClientViewModel : ObservableObject, IDisposable
{
    private const int MaxRttSamples = 600; // 1s 간격 가정, 10분 윈도우.

    private readonly EchoClientConnector _connector;
    private readonly EchoClientStats _stats;
    private readonly Action<Action> _postToUi; // MAUI IDispatcher.Dispatch wrapper.
    private readonly System.Threading.Timer _snapshotTimer;

    public ObservableCollection<RttSample> RttSeries { get; } = new();

    [ObservableProperty] private string _host = "127.0.0.1";
    [ObservableProperty] private int _port = 7777;
    [ObservableProperty] private string _message = "hello";
    [ObservableProperty] private int _sendIntervalMs = 100;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private EchoClientState _state = EchoClientState.Disconnected;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private EchoStatsSnapshot _snapshot = Empty;

    private static readonly EchoStatsSnapshot Empty =
        new(0, 0, 0, 0, 0, 0, 0, 0);

    public EchoClientViewModel(
        EchoClientConnector connector,
        EchoClientStats stats,
        Action<Action> postToUi)
    {
        _connector = connector;
        _stats = stats;
        _postToUi = postToUi;

        _connector.StateChanged += OnConnectorStateChanged;
        // Plan SC: FR-05 — KPI 1초 주기 갱신.
        _snapshotTimer = new System.Threading.Timer(_ => RefreshSnapshot(), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private bool CanConnect()
        => State == EchoClientState.Disconnected || State == EchoClientState.Error;

    private bool CanDisconnect()
        => State == EchoClientState.Connecting || State == EchoClientState.Connected;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private void Connect()
    {
        // FR-06: 새 connect 마다 stats reset + chart clear.
        _stats.Reset();
        RttSeries.Clear();
        ErrorMessage = null;
        Snapshot = Empty;

        var opts = new EchoClientOptions(Host, Port, Message, SendIntervalMs);
        _connector.StartConnect(opts, _stats, OnRttSample);
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect()
    {
        _connector.RequestDisconnect();
        // 통계 freeze: Snapshot은 마지막 값 유지.
    }

    private void OnRttSample(RttSample sample)
    {
        _postToUi(() =>
        {
            RttSeries.Add(sample);
            while (RttSeries.Count > MaxRttSamples) RttSeries.RemoveAt(0);
        });
    }

    private void OnConnectorStateChanged(EchoClientState next)
    {
        _postToUi(() =>
        {
            State = next;
            if (next == EchoClientState.Error) ErrorMessage = _connector.ErrorMessage;
        });
    }

    private void RefreshSnapshot()
    {
        // 1s tick: stats에서 최신 snapshot 가져와 UI thread에 push.
        var snap = _stats.Snapshot(DateTime.UtcNow);
        _postToUi(() => Snapshot = snap);
    }

    public void Dispose()
    {
        _snapshotTimer.Dispose();
        _connector.StateChanged -= OnConnectorStateChanged;
    }
}
