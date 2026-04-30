using System.Diagnostics;

namespace FastPortLoadRunner;

internal sealed class OutstandingRequestPacer
{
    private readonly object _sync = new();
    private readonly LoadPacingOptions _options;
    private readonly MetricsCollector _metricsCollector;
    private TaskCompletionSource _waitSignal = CreateSignal();
    private int _currentWindow;
    private int _inFlight;
    private int _stableResponseCount;

    public OutstandingRequestPacer(LoadPacingOptions options, MetricsCollector metricsCollector)
    {
        _options = options;
        _metricsCollector = metricsCollector;
        _currentWindow = options.Policy switch
        {
            LoadPacingPolicy.FixedWindow => options.FixedWindow!.Value,
            LoadPacingPolicy.AdaptiveWindow => options.InitialWindow,
            _ => int.MaxValue
        };

        if (IsEnabled)
        {
            _metricsCollector.RecordPacingWindowSample(_currentWindow);
        }
    }

    public int InFlight
    {
        get
        {
            lock (_sync)
            {
                return _inFlight;
            }
        }
    }

    public int CurrentWindow
    {
        get
        {
            lock (_sync)
            {
                return _currentWindow;
            }
        }
    }

    public async ValueTask WaitForPermitAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        while (true)
        {
            Task waitTask;
            lock (_sync)
            {
                if (_inFlight < _currentWindow)
                {
                    _inFlight++;
                    return;
                }

                waitTask = _waitSignal.Task;
            }

            long startedAt = Stopwatch.GetTimestamp();
            await waitTask.WaitAsync(cancellationToken);
            _metricsCollector.RecordPacingWait(Stopwatch.GetElapsedTime(startedAt));
        }
    }

    public void OnRequestSent()
    {
    }

    public void OnRequestAbandoned()
    {
        if (!IsEnabled)
        {
            return;
        }

        ReleaseInFlight();
    }

    public void OnResponse(double rttMs)
    {
        if (!IsEnabled)
        {
            return;
        }

        TaskCompletionSource? signal;
        lock (_sync)
        {
            if (_inFlight > 0)
            {
                _inFlight--;
            }

            if (_options.Policy == LoadPacingPolicy.AdaptiveWindow)
            {
                AdjustAdaptiveWindow(rttMs);
            }

            signal = CreateSignalIfReady();
        }

        signal?.TrySetResult();
    }

    internal void ReserveForTest()
    {
        if (!IsEnabled)
        {
            return;
        }

        lock (_sync)
        {
            _inFlight++;
        }
    }

    private bool IsEnabled => _options.Policy != LoadPacingPolicy.None;

    private void ReleaseInFlight()
    {
        TaskCompletionSource? signal;
        lock (_sync)
        {
            if (_inFlight > 0)
            {
                _inFlight--;
            }

            signal = CreateSignalIfReady();
        }

        signal?.TrySetResult();
    }

    private void AdjustAdaptiveWindow(double rttMs)
    {
        int originalWindow = _currentWindow;
        if (rttMs >= _options.RttHighMs)
        {
            _currentWindow = Math.Max(_options.MinWindow, Math.Max(1, _currentWindow / 2));
            _stableResponseCount = 0;
            if (_currentWindow < originalWindow)
            {
                _metricsCollector.RecordPacingWindowDecrease();
            }
        }
        else if (rttMs <= _options.RttTargetMs)
        {
            _stableResponseCount++;
            if (_stableResponseCount >= _options.IncreaseEveryResponses)
            {
                _currentWindow = Math.Min(_options.MaxWindow, _currentWindow + 1);
                _stableResponseCount = 0;
                if (_currentWindow > originalWindow)
                {
                    _metricsCollector.RecordPacingWindowIncrease();
                }
            }
        }
        else
        {
            _stableResponseCount = 0;
        }

        if (_currentWindow != originalWindow)
        {
            _metricsCollector.RecordPacingWindowSample(_currentWindow);
        }
    }

    private TaskCompletionSource? CreateSignalIfReady()
    {
        if (_inFlight >= _currentWindow)
        {
            return null;
        }

        TaskCompletionSource signal = _waitSignal;
        _waitSignal = CreateSignal();
        return signal;
    }

    private static TaskCompletionSource CreateSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
