using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastPortDashboard.Maui.Adapters;
using LibTestTelemetry;

namespace FastPortDashboard.Maui.ViewModels;

// Design Ref: §3.2 — CommunityToolkit.Mvvm 소스 제너레이터 적용.
// View와 Adapter 사이의 application 계층. UI 의존 0이라 unit test 용이.
public sealed partial class DashboardViewModel : ObservableObject
{
    private const int MaxChartPoints = 600; // 10분치 (1초 간격 가정)

    private IPollingAdapter? _adapter;
    private CancellationTokenSource? _cts;

    public ObservableCollection<TimedDoublePoint> ThroughputSeries { get; } = new();

    // Design Ref: §3.2 (dashboard-multi-rtt-overlay) — Client RTT P50/P95/P99 시계열.
    // 직전 cycle (dashboard-rtt-chart)에서 P95 단일이었으나 본 cycle에서 multi-percentile로 확장.
    public ObservableCollection<TimedRttPoint> ClientRttSeries { get; } = new();

    // ─── KPI (Plan SC: ~60% LOC 축소) ────────────────────────────
    [ObservableProperty] private long _currentSessions;
    [ObservableProperty] private long _totalAcceptedSessions;
    [ObservableProperty] private long _totalSentBytes;
    [ObservableProperty] private long _pendingSendRequests;
    [ObservableProperty] private long _sendBufferBytes;
    [ObservableProperty] private DateTimeOffset _lastUpdate = DateTimeOffset.MinValue;

    // ─── UI Input ────────────────────────────────────────────────
    [ObservableProperty] private bool _useMock;
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    // ─── State (CanExecute trigger; Design Ref: §3.3) ────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private PollingState _state = PollingState.Idle;

    private bool CanConnect()
        => State == PollingState.Idle || State == PollingState.Disconnected || State == PollingState.Error;

    private bool CanDisconnect() => State == PollingState.Polling;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync() => await StartAsync();

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect() => Stop();

    // Test entry point: adapter를 직접 주입해 데이터 흐름 검증.
    public async Task PumpAsync(IPollingAdapter adapter, CancellationToken ct)
    {
        await foreach (var snap in adapter.StreamAsync(ct))
        {
            ApplySnapshot(snap);
        }
    }

    public void ApplySnapshot(ObservedMetricsSnapshot snap)
    {
        // Design Ref: §3.3 (dashboard-rtt-chart) — Server/Client 둘 다 null-safe 분기.
        var server = snap.ServerObserved;
        if (server is not null)
        {
            CurrentSessions = server.CurrentSessions;
            TotalAcceptedSessions = server.TotalAcceptedSessions;
            TotalSentBytes = server.TotalSentBytes;
            PendingSendRequests = server.PendingSendRequests;
            SendBufferBytes = server.SendBufferBytes;
            LastUpdate = server.Timestamp;

            double tsMs = server.Timestamp.ToUnixTimeMilliseconds();
            ThroughputSeries.Add(new TimedDoublePoint(tsMs, server.SentBytesPerSecond));
            while (ThroughputSeries.Count > MaxChartPoints)
            {
                ThroughputSeries.RemoveAt(0);
            }
        }

        var client = snap.ClientObserved;
        if (client is not null)
        {
            ApplyClientSnapshot(client);
        }
    }

    // Design Ref: §3.2 (dashboard-multi-rtt-overlay) — ClientObserved → ClientRttSeries (P50/P95/P99).
    private void ApplyClientSnapshot(ClientObservedMetricsSnapshot client)
    {
        double tsMs = client.Timestamp.ToUnixTimeMilliseconds();
        ClientRttSeries.Add(new TimedRttPoint(tsMs, client.RttP50Ms, client.RttP95Ms, client.RttP99Ms));
        while (ClientRttSeries.Count > MaxChartPoints)
        {
            ClientRttSeries.RemoveAt(0);
        }
    }

    private async Task StartAsync()
    {
        ErrorMessage = null;

        if (UseMock)
        {
            _adapter = new MockPollingAdapter();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                ErrorMessage = "File path is required when Mock is off.";
                State = PollingState.Error;
                return;
            }
            _adapter = new JsonlPollingAdapter(FilePath);
        }

        _cts = new CancellationTokenSource();
        State = PollingState.Polling;

        try
        {
            await PumpAsync(_adapter, _cts.Token);
            State = PollingState.Disconnected;
        }
        catch (OperationCanceledException)
        {
            State = PollingState.Disconnected;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            State = PollingState.Error;
        }
    }

    private void Stop() => _cts?.Cancel();
}
