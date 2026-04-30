using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using LibNetworks.Telemetry;

namespace FastPortLoadRunner;

internal sealed class MetricsCollector(int targetSessions)
{
    private const int MaxRttSamples = 100_000;

    private readonly ConcurrentQueue<double> _rttSamplesMs = new();
    private readonly ConcurrentDictionary<string, long> _socketErrorCountsByPhase = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _socketErrorCountsByType = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _socketErrorCountsByCode = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _socketErrorCountsByClass = new(StringComparer.Ordinal);
    private long _connectedSessions;
    private long _totalSentPackets;
    private long _totalReceivedPackets;
    private long _totalSentBytes;
    private long _totalReceivedBytes;
    private long _acceptCount;
    private long _disconnectCount;
    private long _socketErrorCount;
    private long _connectAttemptCount;
    private long _connectFailureCount;
    private long _pendingRequestCount;
    private long _maxPendingRequestCount;
    private long _schedulerDriftSampleCount;
    private long _schedulerDriftTotalMicroseconds;
    private long _schedulerDriftMaxMicroseconds;
    private long _pacingWaitCount;
    private long _pacingWaitTicks;
    private long _pacingWindowIncreaseCount;
    private long _pacingWindowDecreaseCount;
    private long _minObservedPacingWindow;
    private long _maxObservedPacingWindow;

    public void RecordConnectAttempt()
    {
        Interlocked.Increment(ref _connectAttemptCount);
    }

    public void RecordConnectFailure()
    {
        Interlocked.Increment(ref _connectFailureCount);
    }

    public void RecordSessionConnected()
    {
        Interlocked.Increment(ref _connectedSessions);
        Interlocked.Increment(ref _acceptCount);
    }

    public void RecordSessionDisconnected()
    {
        long current;
        do
        {
            current = Interlocked.Read(ref _connectedSessions);
            if (current <= 0)
            {
                break;
            }
        }
        while (Interlocked.CompareExchange(ref _connectedSessions, current - 1, current) != current);

        Interlocked.Increment(ref _disconnectCount);
    }

    public void RecordSentPacket(int bytes)
    {
        Interlocked.Increment(ref _totalSentPackets);
        Interlocked.Add(ref _totalSentBytes, bytes);
        long pending = Interlocked.Increment(ref _pendingRequestCount);
        UpdateMax(ref _maxPendingRequestCount, pending);
    }

    public void RecordReceivedPacket(int bytes)
    {
        Interlocked.Increment(ref _totalReceivedPackets);
        Interlocked.Add(ref _totalReceivedBytes, bytes);
        DecrementIfPositive(ref _pendingRequestCount);
    }

    public void RecordSocketError()
    {
        RecordSocketError("unknown");
    }

    public void RecordSocketError(string phase, Exception? exception = null)
    {
        Interlocked.Increment(ref _socketErrorCount);
        RecordSocketErrorClassification(phase, GetExceptionType(exception), GetSocketErrorCode(exception));
    }

    public void RecordProtocolError(string reason)
    {
        Interlocked.Increment(ref _socketErrorCount);
        RecordSocketErrorClassification("protocol", NormalizeKey(reason), "none");
    }

    public void RecordSchedulerDrift(double driftMs)
    {
        if (driftMs <= 0)
        {
            return;
        }

        long driftMicroseconds = (long)Math.Round(driftMs * 1000, MidpointRounding.AwayFromZero);
        Interlocked.Increment(ref _schedulerDriftSampleCount);
        Interlocked.Add(ref _schedulerDriftTotalMicroseconds, driftMicroseconds);
        UpdateMax(ref _schedulerDriftMaxMicroseconds, driftMicroseconds);
    }

    public void RecordRtt(long clientSendTimestamp, long clientReceiveTimestamp)
    {
        long elapsedTicks = clientReceiveTimestamp - clientSendTimestamp;
        if (elapsedTicks <= 0)
        {
            return;
        }

        double elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;
        _rttSamplesMs.Enqueue(elapsedMs);

        while (_rttSamplesMs.Count > MaxRttSamples && _rttSamplesMs.TryDequeue(out _))
        {
        }
    }

    public void RecordPacingWait(TimeSpan elapsed)
    {
        Interlocked.Increment(ref _pacingWaitCount);
        Interlocked.Add(ref _pacingWaitTicks, Math.Max(0, elapsed.Ticks));
    }

    public void RecordPacingWindowIncrease()
    {
        Interlocked.Increment(ref _pacingWindowIncreaseCount);
    }

    public void RecordPacingWindowDecrease()
    {
        Interlocked.Increment(ref _pacingWindowDecreaseCount);
    }

    public void RecordPacingWindowSample(int window)
    {
        if (window <= 0)
        {
            return;
        }

        UpdateMin(ref _minObservedPacingWindow, window);
        UpdateMax(ref _maxObservedPacingWindow, window);
    }

    public MetricsSnapshot CreateSnapshot(MetricsSnapshot? previous = null)
    {
        DateTimeOffset timestamp = DateTimeOffset.Now;
        long sentPackets = Interlocked.Read(ref _totalSentPackets);
        long receivedPackets = Interlocked.Read(ref _totalReceivedPackets);
        long sentBytes = Interlocked.Read(ref _totalSentBytes);
        long receivedBytes = Interlocked.Read(ref _totalReceivedBytes);
        long socketErrors = Interlocked.Read(ref _socketErrorCount);
        long acceptCount = Interlocked.Read(ref _acceptCount);
        long disconnectCount = Interlocked.Read(ref _disconnectCount);
        long connectedSessions = Interlocked.Read(ref _connectedSessions);
        long schedulerDriftSampleCount = Interlocked.Read(ref _schedulerDriftSampleCount);
        long pacingWaitCount = Interlocked.Read(ref _pacingWaitCount);
        long pacingWaitTicks = Interlocked.Read(ref _pacingWaitTicks);
        double totalPacingWaitMs = pacingWaitTicks * 1000.0 / TimeSpan.TicksPerSecond;

        double elapsedSeconds = previous is null
            ? 0
            : Math.Max(0.001, (timestamp - previous.Timestamp).TotalSeconds);

        var rtt = CalculateRtt();
        double errorRate = socketErrors <= 0
            ? 0
            : socketErrors / (double)Math.Max(1, sentPackets + receivedPackets + socketErrors);

        return new MetricsSnapshot(
            timestamp,
            targetSessions,
            (int)Math.Min(int.MaxValue, connectedSessions),
            sentPackets,
            receivedPackets,
            sentBytes,
            receivedBytes,
            previous is null ? 0 : (sentPackets - previous.TotalSentPackets) / elapsedSeconds,
            previous is null ? 0 : (receivedPackets - previous.TotalReceivedPackets) / elapsedSeconds,
            previous is null ? 0 : (sentBytes - previous.TotalSentBytes) / elapsedSeconds,
            previous is null ? 0 : (receivedBytes - previous.TotalReceivedBytes) / elapsedSeconds,
            previous is null ? 0 : (receivedPackets - previous.TotalReceivedPackets) / elapsedSeconds,
            rtt.Average,
            rtt.P50,
            rtt.P95,
            rtt.P99,
            acceptCount,
            disconnectCount,
            socketErrors,
            errorRate,
            Interlocked.Read(ref _connectAttemptCount),
            Interlocked.Read(ref _connectFailureCount),
            Interlocked.Read(ref _pendingRequestCount),
            Interlocked.Read(ref _maxPendingRequestCount),
            targetSessions <= 0 ? 0 : connectedSessions / (double)targetSessions,
            schedulerDriftSampleCount <= 0 ? 0 : Interlocked.Read(ref _schedulerDriftTotalMicroseconds) / (double)schedulerDriftSampleCount / 1000,
            Interlocked.Read(ref _schedulerDriftMaxMicroseconds) / 1000.0,
            CopyCounters(_socketErrorCountsByPhase),
            CopyCounters(_socketErrorCountsByType),
            CopyCounters(_socketErrorCountsByCode),
            CopyCounters(_socketErrorCountsByClass),
            TotalPacingWaitCount: pacingWaitCount,
            PacingWaitsPerSecond: previous is null ? 0 : (pacingWaitCount - previous.TotalPacingWaitCount) / elapsedSeconds,
            TotalPacingWaitTimeMs: totalPacingWaitMs,
            PacingAverageWaitMs: pacingWaitCount <= 0 ? 0 : totalPacingWaitMs / pacingWaitCount,
            PacingWindowIncreaseCount: Interlocked.Read(ref _pacingWindowIncreaseCount),
            PacingWindowDecreaseCount: Interlocked.Read(ref _pacingWindowDecreaseCount),
            MinObservedPacingWindow: Interlocked.Read(ref _minObservedPacingWindow),
            MaxObservedPacingWindow: Interlocked.Read(ref _maxObservedPacingWindow));
    }

    private void RecordSocketErrorClassification(string phase, string exceptionType, string socketErrorCode)
    {
        string normalizedPhase = NormalizeKey(phase);
        string normalizedType = NormalizeKey(exceptionType);
        string normalizedCode = NormalizeKey(socketErrorCode);
        string classKey = $"{normalizedPhase}|{normalizedType}|{normalizedCode}";

        IncrementCounter(_socketErrorCountsByPhase, normalizedPhase);
        IncrementCounter(_socketErrorCountsByType, normalizedType);
        IncrementCounter(_socketErrorCountsByCode, normalizedCode);
        IncrementCounter(_socketErrorCountsByClass, classKey);
    }

    private static void IncrementCounter(ConcurrentDictionary<string, long> counters, string key)
    {
        counters.AddOrUpdate(key, 1, (_, current) => current + 1);
    }

    private static IReadOnlyDictionary<string, long> CopyCounters(ConcurrentDictionary<string, long> counters)
    {
        return counters
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static string GetExceptionType(Exception? exception)
    {
        if (exception is null)
        {
            return "none";
        }

        return exception.GetType().Name;
    }

    private static string GetSocketErrorCode(Exception? exception)
    {
        SocketException? socketException = FindSocketException(exception);
        return socketException?.SocketErrorCode.ToString() ?? "none";
    }

    private static SocketException? FindSocketException(Exception? exception)
    {
        Exception? current = exception;
        while (current is not null)
        {
            if (current is SocketException socketException)
            {
                return socketException;
            }

            current = current.InnerException;
        }

        return null;
    }

    private static string NormalizeKey(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
    }

    private static void UpdateMax(ref long target, long value)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref target);
            if (value <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private static void UpdateMin(ref long target, long value)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref target);
            if (current != 0 && value >= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private static void DecrementIfPositive(ref long target)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref target);
            if (current <= 0)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, current - 1, current) != current);
    }

    private RttStats CalculateRtt()
    {
        double[] values = _rttSamplesMs.ToArray();
        if (values.Length == 0)
        {
            return new RttStats(0, 0, 0, 0);
        }

        Array.Sort(values);
        return new RttStats(
            values.Average(),
            GetPercentile(values, 50),
            GetPercentile(values, 95),
            GetPercentile(values, 99));
    }

    private static double GetPercentile(double[] sortedValues, int percentile)
    {
        if (sortedValues.Length == 0)
        {
            return 0;
        }

        double position = (sortedValues.Length - 1) * percentile / 100.0;
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        double weight = position - lower;
        return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
    }

    private readonly record struct RttStats(double Average, double P50, double P95, double P99);
}

internal sealed record MetricsSnapshot(
    DateTimeOffset Timestamp,
    int TargetSessions,
    int ConnectedSessions,
    long TotalSentPackets,
    long TotalReceivedPackets,
    long TotalSentBytes,
    long TotalReceivedBytes,
    double SentPacketsPerSecond,
    double ReceivedPacketsPerSecond,
    double SentBytesPerSecond,
    double ReceivedBytesPerSecond,
    double Tps,
    double RttAverageMs,
    double RttP50Ms,
    double RttP95Ms,
    double RttP99Ms,
    long AcceptCount,
    long DisconnectCount,
    long SocketErrorCount,
    double SocketErrorRate,
    long ConnectAttemptCount = 0,
    long ConnectFailureCount = 0,
    long PendingRequestCount = 0,
    long MaxPendingRequestCount = 0,
    double ActiveSessionRatio = 0,
    double SchedulerDriftAverageMs = 0,
    double SchedulerDriftMaxMs = 0,
    IReadOnlyDictionary<string, long>? SocketErrorCountsByPhase = null,
    IReadOnlyDictionary<string, long>? SocketErrorCountsByType = null,
    IReadOnlyDictionary<string, long>? SocketErrorCountsByCode = null,
    IReadOnlyDictionary<string, long>? SocketErrorCountsByClass = null,
    long TotalPacingWaitCount = 0,
    double PacingWaitsPerSecond = 0,
    double TotalPacingWaitTimeMs = 0,
    double PacingAverageWaitMs = 0,
    long PacingWindowIncreaseCount = 0,
    long PacingWindowDecreaseCount = 0,
    long MinObservedPacingWindow = 0,
    long MaxObservedPacingWindow = 0);

internal interface IMetricsReporter
{
    Task RunAsync(MetricsCollector metricsCollector, TimeSpan interval, CancellationToken cancellationToken);
}

internal sealed class ConsoleMetricsReporter : IMetricsReporter
{
    public async Task RunAsync(MetricsCollector metricsCollector, TimeSpan interval, CancellationToken cancellationToken)
    {
        MetricsSnapshot? previous = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            long expectedTimestamp = MetricsReporterClock.GetExpectedTimestamp(interval);
            await Task.Delay(interval, cancellationToken);
            MetricsReporterClock.RecordDrift(metricsCollector, expectedTimestamp);
            MetricsSnapshot snapshot = metricsCollector.CreateSnapshot(previous);
            previous = snapshot;
            Console.WriteLine(Format(snapshot));
        }
    }

    private static string Format(MetricsSnapshot snapshot)
    {
        return string.Join(
            ' ',
            $"time={snapshot.Timestamp:HH:mm:ss}",
            $"sessions={snapshot.ConnectedSessions:N0}/{snapshot.TargetSessions:N0}",
            $"tps={snapshot.Tps:N0}",
            $"rtt_avg={snapshot.RttAverageMs:F2}ms",
            $"p95={snapshot.RttP95Ms:F2}ms",
            $"send={FormatBytes(snapshot.SentBytesPerSecond)}/s",
            $"recv={FormatBytes(snapshot.ReceivedBytesPerSecond)}/s",
            $"errors={snapshot.SocketErrorRate:P3}");
    }

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        int unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        return $"{bytes:F1}{units[unit]}";
    }
}

internal sealed class JsonMetricsReporter : IMetricsReporter, IDisposable
{
    private readonly StreamWriter _writer;

    public JsonMetricsReporter(string outputPath)
    {
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read));
    }

    public async Task RunAsync(MetricsCollector metricsCollector, TimeSpan interval, CancellationToken cancellationToken)
    {
        MetricsSnapshot? previous = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            long expectedTimestamp = MetricsReporterClock.GetExpectedTimestamp(interval);
            await Task.Delay(interval, cancellationToken);
            MetricsReporterClock.RecordDrift(metricsCollector, expectedTimestamp);
            MetricsSnapshot snapshot = metricsCollector.CreateSnapshot(previous);
            previous = snapshot;

            string json = SerializeSnapshot(snapshot);
            await _writer.WriteLineAsync(json.AsMemory(), cancellationToken);
            await _writer.FlushAsync(cancellationToken);
        }
    }

    internal static string SerializeSnapshot(MetricsSnapshot snapshot)
    {
        ClientObservedMetricsSnapshot clientObserved = snapshot.ToClientObservedMetricsSnapshot();
        ObservedMetricsSnapshot observed = ObservedMetricsSnapshot.FromClient(clientObserved);
        return ObservedMetricsJson.Serialize(observed);
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}

internal static class MetricsReporterClock
{
    public static long GetExpectedTimestamp(TimeSpan interval)
    {
        return Stopwatch.GetTimestamp() + ToStopwatchTicks(interval);
    }

    public static void RecordDrift(MetricsCollector metricsCollector, long expectedTimestamp)
    {
        long actualTimestamp = Stopwatch.GetTimestamp();
        long driftTicks = actualTimestamp - expectedTimestamp;
        if (driftTicks <= 0)
        {
            return;
        }

        metricsCollector.RecordSchedulerDrift(driftTicks * 1000.0 / Stopwatch.Frequency);
    }

    private static long ToStopwatchTicks(TimeSpan interval)
    {
        return Math.Max(0, (long)(interval.TotalSeconds * Stopwatch.Frequency));
    }
}
