using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FastPortDashboard.Maui.Adapters;
using LibTestTelemetry;

namespace FastPortDashboard.Maui.ViewModels;

// Design Ref: §3.4 — MVVM dashboard ViewModel.
// View와 Adapter 사이의 application 계층. UI 의존 0이라 unit test 용이.
public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private const int MaxChartPoints = 600; // 10분치 (1초 간격 가정)

    private IPollingAdapter? _adapter;
    private CancellationTokenSource? _cts;

    public ObservableCollection<TimedDoublePoint> ThroughputSeries { get; } = new();

    private long _currentSessions;
    public long CurrentSessions
    {
        get => _currentSessions;
        private set { if (_currentSessions != value) { _currentSessions = value; OnPropertyChanged(); } }
    }

    private long _totalAcceptedSessions;
    public long TotalAcceptedSessions
    {
        get => _totalAcceptedSessions;
        private set { if (_totalAcceptedSessions != value) { _totalAcceptedSessions = value; OnPropertyChanged(); } }
    }

    private long _totalSentBytes;
    public long TotalSentBytes
    {
        get => _totalSentBytes;
        private set { if (_totalSentBytes != value) { _totalSentBytes = value; OnPropertyChanged(); } }
    }

    private long _pendingSendRequests;
    public long PendingSendRequests
    {
        get => _pendingSendRequests;
        private set { if (_pendingSendRequests != value) { _pendingSendRequests = value; OnPropertyChanged(); } }
    }

    private long _sendBufferBytes;
    public long SendBufferBytes
    {
        get => _sendBufferBytes;
        private set { if (_sendBufferBytes != value) { _sendBufferBytes = value; OnPropertyChanged(); } }
    }

    private DateTimeOffset _lastUpdate = DateTimeOffset.MinValue;
    public DateTimeOffset LastUpdate
    {
        get => _lastUpdate;
        private set { if (_lastUpdate != value) { _lastUpdate = value; OnPropertyChanged(); } }
    }

    private PollingState _state = PollingState.Idle;
    public PollingState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged();
                ((Command)ConnectCommand).ChangeCanExecute();
                ((Command)DisconnectCommand).ChangeCanExecute();
            }
        }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set { if (_errorMessage != value) { _errorMessage = value; OnPropertyChanged(); } }
    }

    private bool _useMock;
    public bool UseMock
    {
        get => _useMock;
        set { if (_useMock != value) { _useMock = value; OnPropertyChanged(); } }
    }

    private string _filePath = string.Empty;
    public string FilePath
    {
        get => _filePath;
        set { if (_filePath != value) { _filePath = value; OnPropertyChanged(); } }
    }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }

    public DashboardViewModel()
    {
        ConnectCommand = new Command(
            execute: async () => await StartAsync(),
            canExecute: () => State == PollingState.Idle || State == PollingState.Disconnected || State == PollingState.Error);

        DisconnectCommand = new Command(
            execute: () => Stop(),
            canExecute: () => State == PollingState.Polling);
    }

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
        var server = snap.ServerObserved;
        if (server is null) { return; }

        CurrentSessions = server.CurrentSessions;
        TotalAcceptedSessions = server.TotalAcceptedSessions;
        TotalSentBytes = server.TotalSentBytes;
        PendingSendRequests = server.PendingSendRequests;
        SendBufferBytes = server.SendBufferBytes;
        LastUpdate = server.Timestamp;

        // Chart point 추가 (recent N개만 유지)
        double tsMs = server.Timestamp.ToUnixTimeMilliseconds();
        ThroughputSeries.Add(new TimedDoublePoint(tsMs, server.SentBytesPerSecond));
        while (ThroughputSeries.Count > MaxChartPoints)
        {
            ThroughputSeries.RemoveAt(0);
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

    private void Stop()
    {
        _cts?.Cancel();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
