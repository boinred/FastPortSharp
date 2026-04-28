using System.Collections.Concurrent;
using System.Diagnostics;
using LibNetworks.Telemetry;

namespace FastPortLoadRunner;

internal sealed class MetricsCollector(int targetSessions)
{
    private const int MaxRttSamples = 100_000;

    private readonly ConcurrentQueue<double> _rttSamplesMs = new();
    private long _connectedSessions;
    private long _totalSentPackets;
    private long _totalReceivedPackets;
    private long _totalSentBytes;
    private long _totalReceivedBytes;
    private long _acceptCount;
    private long _disconnectCount;
    private long _socketErrorCount;

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
    }

    public void RecordReceivedPacket(int bytes)
    {
        Interlocked.Increment(ref _totalReceivedPackets);
        Interlocked.Add(ref _totalReceivedBytes, bytes);
    }

    public void RecordSocketError()
    {
        Interlocked.Increment(ref _socketErrorCount);
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
            errorRate);
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
    double SocketErrorRate);

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
            await Task.Delay(interval, cancellationToken);
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
            await Task.Delay(interval, cancellationToken);
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
