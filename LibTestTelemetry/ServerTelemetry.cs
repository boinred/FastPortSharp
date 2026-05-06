using System.Collections.Concurrent;
using System.Net.Sockets;

namespace LibTestTelemetry;

public interface IServerTelemetry
{
    void RecordAccept();

    void RecordAcceptError();

    void RecordSessionDisconnected();

    void RecordReceived(int bytes);

    void RecordSent(int bytes);

    void RecordSendRequested(int bytes, int queuedBytes);

    void RecordSendCompleted();

    void RecordSendAbandoned(int count);

    void RecordSendBackpressure();

    void RecordSendRejected(int bytes, int queuedBytes);

    void RecordSendDrainYield(int queuedBytes);

    void RecordSendBufferSample(int queuedBytes);

    void RecordSocketError();

    void RecordSocketError(string phase, SocketError? socketError, Exception? exception);

    void RecordParseError();

    void RecordProtocolError();

    ServerTelemetrySnapshot CreateSnapshot();

    void Reset();
}

public sealed class ServerTelemetryCollector : IServerTelemetry
{
    private const long SendPendingBackpressureThreshold = 2_000;

    private long _acceptedSessions;
    private long _disconnectedSessions;
    private long _receivedPackets;
    private long _sentPackets;
    private long _receivedBytes;
    private long _sentBytes;
    private long _sendRequests;
    private long _pendingSendRequests;
    private long _maxPendingSendRequests;
    private long _sendAbandonedRequests;
    private long _sendBackpressureEvents;
    private long _sendRejectedRequests;
    private long _sendRejectedBytes;
    private long _sendDrainYieldCount;
    private long _maxSendDrainYieldQueuedBytes;
    private long _sendBufferBytes;
    private long _maxSendBufferBytes;
    private long _acceptErrors;
    private long _socketErrors;
    private readonly ConcurrentDictionary<string, long> _socketErrorCountsByPhase = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _socketErrorCountsByType = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _socketErrorCountsByCode = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _socketErrorCountsByClass = new(StringComparer.Ordinal);
    private long _parseErrors;
    private long _protocolErrors;

    public void RecordAccept()
    {
        Interlocked.Increment(ref _acceptedSessions);
    }

    public void RecordAcceptError()
    {
        Interlocked.Increment(ref _acceptErrors);
    }

    public void RecordSessionDisconnected()
    {
        Interlocked.Increment(ref _disconnectedSessions);
    }

    public void RecordReceived(int bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        Interlocked.Increment(ref _receivedPackets);
        Interlocked.Add(ref _receivedBytes, bytes);
    }

    public void RecordSent(int bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        Interlocked.Increment(ref _sentPackets);
        Interlocked.Add(ref _sentBytes, bytes);
    }

    public void RecordSendRequested(int bytes, int queuedBytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        Interlocked.Increment(ref _sendRequests);
        long pending = Interlocked.Increment(ref _pendingSendRequests);
        UpdateMax(ref _maxPendingSendRequests, pending);
        if (pending > SendPendingBackpressureThreshold)
        {
            RecordSendBackpressure();
        }

        RecordSendBufferSample(queuedBytes);
    }

    public void RecordSendCompleted()
    {
        DecrementIfPositive(ref _pendingSendRequests);
    }

    public void RecordSendAbandoned(int count)
    {
        if (count <= 0)
        {
            return;
        }

        Interlocked.Add(ref _sendAbandonedRequests, count);
        DecrementByAtMost(ref _pendingSendRequests, count);
    }

    public void RecordSendBackpressure()
    {
        Interlocked.Increment(ref _sendBackpressureEvents);
    }

    public void RecordSendRejected(int bytes, int queuedBytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        Interlocked.Increment(ref _sendRejectedRequests);
        Interlocked.Add(ref _sendRejectedBytes, bytes);
        RecordSendBufferSample(queuedBytes);
    }

    public void RecordSendDrainYield(int queuedBytes)
    {
        Interlocked.Increment(ref _sendDrainYieldCount);
        if (queuedBytes >= 0)
        {
            UpdateMax(ref _maxSendDrainYieldQueuedBytes, queuedBytes);
            RecordSendBufferSample(queuedBytes);
        }
    }

    public void RecordSendBufferSample(int queuedBytes)
    {
        if (queuedBytes < 0)
        {
            return;
        }

        Interlocked.Exchange(ref _sendBufferBytes, queuedBytes);
        UpdateMax(ref _maxSendBufferBytes, queuedBytes);
    }

    public void RecordSocketError()
    {
        RecordSocketError("unknown", null, null);
    }

    public void RecordSocketError(string phase, SocketError? socketError, Exception? exception)
    {
        Interlocked.Increment(ref _socketErrors);
        RecordSocketErrorClassification(
            phase,
            GetExceptionType(exception),
            GetSocketErrorCode(socketError, exception));
    }

    public void RecordParseError()
    {
        Interlocked.Increment(ref _parseErrors);
    }

    public void RecordProtocolError()
    {
        Interlocked.Increment(ref _protocolErrors);
    }

    public ServerTelemetrySnapshot CreateSnapshot()
    {
        DateTimeOffset timestamp = DateTimeOffset.Now;
        long acceptedSessions = Interlocked.Read(ref _acceptedSessions);
        long disconnectedSessions = Interlocked.Read(ref _disconnectedSessions);
        long receivedPackets = Interlocked.Read(ref _receivedPackets);
        long sentPackets = Interlocked.Read(ref _sentPackets);
        long socketErrors = Interlocked.Read(ref _socketErrors);
        long connectedSessions = Math.Max(0, acceptedSessions - disconnectedSessions);
        double socketErrorRate = socketErrors <= 0
            ? 0
            : socketErrors / (double)Math.Max(1, receivedPackets + sentPackets + socketErrors);

        return new ServerTelemetrySnapshot(
            timestamp,
            acceptedSessions,
            disconnectedSessions,
            connectedSessions,
            receivedPackets,
            sentPackets,
            Interlocked.Read(ref _receivedBytes),
            Interlocked.Read(ref _sentBytes),
            Interlocked.Read(ref _acceptErrors),
            socketErrors,
            Interlocked.Read(ref _parseErrors),
            Interlocked.Read(ref _protocolErrors),
            socketErrorRate,
            SendRequests: Interlocked.Read(ref _sendRequests),
            PendingSendRequests: Interlocked.Read(ref _pendingSendRequests),
            MaxPendingSendRequests: Interlocked.Read(ref _maxPendingSendRequests),
            SendAbandonedRequests: Interlocked.Read(ref _sendAbandonedRequests),
            SendBackpressureEvents: Interlocked.Read(ref _sendBackpressureEvents),
            SendRejectedRequests: Interlocked.Read(ref _sendRejectedRequests),
            SendRejectedBytes: Interlocked.Read(ref _sendRejectedBytes),
            SendDrainYieldCount: Interlocked.Read(ref _sendDrainYieldCount),
            MaxSendDrainYieldQueuedBytes: Interlocked.Read(ref _maxSendDrainYieldQueuedBytes),
            SendBufferBytes: Interlocked.Read(ref _sendBufferBytes),
            MaxSendBufferBytes: Interlocked.Read(ref _maxSendBufferBytes),
            SocketErrorCountsByPhase: CopyCounters(_socketErrorCountsByPhase),
            SocketErrorCountsByType: CopyCounters(_socketErrorCountsByType),
            SocketErrorCountsByCode: CopyCounters(_socketErrorCountsByCode),
            SocketErrorCountsByClass: CopyCounters(_socketErrorCountsByClass));
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _acceptedSessions, 0);
        Interlocked.Exchange(ref _disconnectedSessions, 0);
        Interlocked.Exchange(ref _receivedPackets, 0);
        Interlocked.Exchange(ref _sentPackets, 0);
        Interlocked.Exchange(ref _receivedBytes, 0);
        Interlocked.Exchange(ref _sentBytes, 0);
        Interlocked.Exchange(ref _sendRequests, 0);
        Interlocked.Exchange(ref _pendingSendRequests, 0);
        Interlocked.Exchange(ref _maxPendingSendRequests, 0);
        Interlocked.Exchange(ref _sendAbandonedRequests, 0);
        Interlocked.Exchange(ref _sendBackpressureEvents, 0);
        Interlocked.Exchange(ref _sendRejectedRequests, 0);
        Interlocked.Exchange(ref _sendRejectedBytes, 0);
        Interlocked.Exchange(ref _sendDrainYieldCount, 0);
        Interlocked.Exchange(ref _maxSendDrainYieldQueuedBytes, 0);
        Interlocked.Exchange(ref _sendBufferBytes, 0);
        Interlocked.Exchange(ref _maxSendBufferBytes, 0);
        Interlocked.Exchange(ref _acceptErrors, 0);
        Interlocked.Exchange(ref _socketErrors, 0);
        _socketErrorCountsByPhase.Clear();
        _socketErrorCountsByType.Clear();
        _socketErrorCountsByCode.Clear();
        _socketErrorCountsByClass.Clear();
        Interlocked.Exchange(ref _parseErrors, 0);
        Interlocked.Exchange(ref _protocolErrors, 0);
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
        return exception?.GetType().Name ?? "none";
    }

    private static string GetSocketErrorCode(SocketError? socketError, Exception? exception)
    {
        if (socketError.HasValue)
        {
            return socketError.Value.ToString();
        }

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

    private static void DecrementByAtMost(ref long target, long count)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref target);
            if (current <= 0)
            {
                return;
            }

            long decrement = Math.Min(current, count);
            long next = current - decrement;
            if (Interlocked.CompareExchange(ref target, next, current) == current)
            {
                return;
            }
        }
        while (true);
    }
}

public sealed record ServerTelemetrySnapshot(
    DateTimeOffset Timestamp,
    long AcceptedSessions,
    long DisconnectedSessions,
    long ConnectedSessions,
    long ReceivedPackets,
    long SentPackets,
    long ReceivedBytes,
    long SentBytes,
    long AcceptErrors,
    long SocketErrors,
    long ParseErrors,
    long ProtocolErrors,
    double SocketErrorRate,
    long SendRequests = 0,
    long PendingSendRequests = 0,
    long MaxPendingSendRequests = 0,
    long SendBackpressureEvents = 0,
    long SendRejectedRequests = 0,
    long SendRejectedBytes = 0,
    long SendDrainYieldCount = 0,
    long MaxSendDrainYieldQueuedBytes = 0,
    long SendBufferBytes = 0,
    long MaxSendBufferBytes = 0,
    long SendAbandonedRequests = 0,
    IReadOnlyDictionary<string, long>? SocketErrorCountsByPhase = null,
    IReadOnlyDictionary<string, long>? SocketErrorCountsByType = null,
    IReadOnlyDictionary<string, long>? SocketErrorCountsByCode = null,
    IReadOnlyDictionary<string, long>? SocketErrorCountsByClass = null);
