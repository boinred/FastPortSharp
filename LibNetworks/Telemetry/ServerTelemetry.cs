namespace LibNetworks.Telemetry;

public interface IServerTelemetry
{
    void RecordAccept();

    void RecordAcceptError();

    void RecordSessionDisconnected();

    void RecordReceived(int bytes);

    void RecordSent(int bytes);

    void RecordSocketError();

    void RecordParseError();

    void RecordProtocolError();

    ServerTelemetrySnapshot CreateSnapshot();

    void Reset();
}

public sealed class ServerTelemetryCollector : IServerTelemetry
{
    private long _acceptedSessions;
    private long _disconnectedSessions;
    private long _receivedPackets;
    private long _sentPackets;
    private long _receivedBytes;
    private long _sentBytes;
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
            socketErrorRate);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _acceptedSessions, 0);
        Interlocked.Exchange(ref _disconnectedSessions, 0);
        Interlocked.Exchange(ref _receivedPackets, 0);
        Interlocked.Exchange(ref _sentPackets, 0);
        Interlocked.Exchange(ref _receivedBytes, 0);
        Interlocked.Exchange(ref _sentBytes, 0);
        Interlocked.Exchange(ref _acceptErrors, 0);
        Interlocked.Exchange(ref _socketErrors, 0);
        Interlocked.Exchange(ref _parseErrors, 0);
        Interlocked.Exchange(ref _protocolErrors, 0);
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
    double SocketErrorRate);
