namespace LibNetworks.Telemetry;

public interface IServerTelemetry
{
    void RecordAccept();

    void RecordAcceptError();

    void RecordSessionDisconnected();

    void RecordReceived(int bytes);

    void RecordSent(int bytes);

    void RecordSendRequested(int bytes, int queuedBytes);

    void RecordSendCompleted();

    void RecordSendBackpressure();

    void RecordSendBufferSample(int queuedBytes);

    void RecordSocketError();

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
    private long _sendBackpressureEvents;
    private long _sendBufferBytes;
    private long _maxSendBufferBytes;
    private long _acceptErrors;
    private long _socketErrors;
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
        RecordSendCompleted();
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

    public void RecordSendBackpressure()
    {
        Interlocked.Increment(ref _sendBackpressureEvents);
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
        Interlocked.Increment(ref _socketErrors);
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
            SendBackpressureEvents: Interlocked.Read(ref _sendBackpressureEvents),
            SendBufferBytes: Interlocked.Read(ref _sendBufferBytes),
            MaxSendBufferBytes: Interlocked.Read(ref _maxSendBufferBytes));
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
        Interlocked.Exchange(ref _sendBackpressureEvents, 0);
        Interlocked.Exchange(ref _sendBufferBytes, 0);
        Interlocked.Exchange(ref _maxSendBufferBytes, 0);
        Interlocked.Exchange(ref _acceptErrors, 0);
        Interlocked.Exchange(ref _socketErrors, 0);
        Interlocked.Exchange(ref _parseErrors, 0);
        Interlocked.Exchange(ref _protocolErrors, 0);
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
}

public sealed class NullServerTelemetry : IServerTelemetry
{
    public static NullServerTelemetry Instance { get; } = new();

    private NullServerTelemetry()
    {
    }

    public void RecordAccept()
    {
    }

    public void RecordAcceptError()
    {
    }

    public void RecordSessionDisconnected()
    {
    }

    public void RecordReceived(int bytes)
    {
    }

    public void RecordSent(int bytes)
    {
    }

    public void RecordSendRequested(int bytes, int queuedBytes)
    {
    }

    public void RecordSendCompleted()
    {
    }

    public void RecordSendBackpressure()
    {
    }

    public void RecordSendBufferSample(int queuedBytes)
    {
    }

    public void RecordSocketError()
    {
    }

    public void RecordParseError()
    {
    }

    public void RecordProtocolError()
    {
    }

    public ServerTelemetrySnapshot CreateSnapshot()
    {
        return new ServerTelemetrySnapshot(DateTimeOffset.Now, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    public void Reset()
    {
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
    long SendBufferBytes = 0,
    long MaxSendBufferBytes = 0);
