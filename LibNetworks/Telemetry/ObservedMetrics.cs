using System.Text.Json;

namespace LibNetworks.Telemetry;

public sealed record ObservedMetricsSnapshot(
    DateTimeOffset Timestamp,
    ClientObservedMetricsSnapshot? ClientObserved,
    ServerObservedMetricsSnapshot? ServerObserved)
{
    public static ObservedMetricsSnapshot FromServer(ServerObservedMetricsSnapshot serverObserved)
    {
        return new ObservedMetricsSnapshot(serverObserved.Timestamp, null, serverObserved);
    }

    public static ObservedMetricsSnapshot FromClient(ClientObservedMetricsSnapshot clientObserved)
    {
        return new ObservedMetricsSnapshot(clientObserved.Timestamp, clientObserved, null);
    }

    public static ObservedMetricsSnapshot Combined(
        ClientObservedMetricsSnapshot? clientObserved,
        ServerObservedMetricsSnapshot? serverObserved)
    {
        DateTimeOffset timestamp = serverObserved?.Timestamp
            ?? clientObserved?.Timestamp
            ?? DateTimeOffset.Now;

        return new ObservedMetricsSnapshot(timestamp, clientObserved, serverObserved);
    }
}

public sealed record ClientObservedMetricsSnapshot(
    DateTimeOffset Timestamp,
    int TargetSessions,
    int CurrentSessions,
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
    long ConnectCount,
    long DisconnectCount,
    long SocketErrorCount,
    double SocketErrorRate,
    long ConnectAttemptCount = 0,
    long ConnectFailureCount = 0,
    long PendingRequestCount = 0,
    long MaxPendingRequestCount = 0,
    double ActiveSessionRatio = 0,
    double SchedulerDriftAverageMs = 0,
    double SchedulerDriftMaxMs = 0);

public sealed record ServerObservedMetricsSnapshot(
    DateTimeOffset Timestamp,
    long CurrentSessions,
    long TotalAcceptedSessions,
    long TotalDisconnectedSessions,
    long TotalReceivedPackets,
    long TotalSendCompletions,
    long TotalParsedPacketBytes,
    long TotalSentBytes,
    double ReceivedPacketsPerSecond,
    double SendCompletionsPerSecond,
    double ParsedPacketBytesPerSecond,
    double SentBytesPerSecond,
    double AcceptedSessionsPerSecond,
    double DisconnectedSessionsPerSecond,
    long AcceptErrorCount,
    long SocketErrorCount,
    long ParseErrorCount,
    long ProtocolErrorCount,
    double SocketErrorRate,
    long TotalSendRequests = 0,
    long PendingSendRequests = 0,
    long MaxPendingSendRequests = 0,
    double SendRequestsPerSecond = 0,
    long SendBackpressureEvents = 0,
    double SendBackpressureEventsPerSecond = 0,
    long SendBufferBytes = 0,
    long MaxSendBufferBytes = 0)
{
    public static ServerObservedMetricsSnapshot FromTelemetry(
        ServerTelemetrySnapshot current,
        ServerObservedMetricsSnapshot? previous = null)
    {
        double elapsedSeconds = previous is null
            ? 0
            : Math.Max(0.001, (current.Timestamp - previous.Timestamp).TotalSeconds);

        return new ServerObservedMetricsSnapshot(
            current.Timestamp,
            current.ConnectedSessions,
            current.AcceptedSessions,
            current.DisconnectedSessions,
            current.ReceivedPackets,
            current.SentPackets,
            current.ReceivedBytes,
            current.SentBytes,
            Rate(current.ReceivedPackets, previous?.TotalReceivedPackets, elapsedSeconds),
            Rate(current.SentPackets, previous?.TotalSendCompletions, elapsedSeconds),
            Rate(current.ReceivedBytes, previous?.TotalParsedPacketBytes, elapsedSeconds),
            Rate(current.SentBytes, previous?.TotalSentBytes, elapsedSeconds),
            Rate(current.AcceptedSessions, previous?.TotalAcceptedSessions, elapsedSeconds),
            Rate(current.DisconnectedSessions, previous?.TotalDisconnectedSessions, elapsedSeconds),
            current.AcceptErrors,
            current.SocketErrors,
            current.ParseErrors,
            current.ProtocolErrors,
            current.SocketErrorRate,
            TotalSendRequests: current.SendRequests,
            PendingSendRequests: current.PendingSendRequests,
            MaxPendingSendRequests: current.MaxPendingSendRequests,
            SendRequestsPerSecond: Rate(current.SendRequests, previous?.TotalSendRequests, elapsedSeconds),
            SendBackpressureEvents: current.SendBackpressureEvents,
            SendBackpressureEventsPerSecond: Rate(current.SendBackpressureEvents, previous?.SendBackpressureEvents, elapsedSeconds),
            SendBufferBytes: current.SendBufferBytes,
            MaxSendBufferBytes: current.MaxSendBufferBytes);
    }

    private static double Rate(long current, long? previous, double elapsedSeconds)
    {
        if (previous is null || elapsedSeconds <= 0)
        {
            return 0;
        }

        return (current - previous.Value) / elapsedSeconds;
    }
}

public interface IServerTelemetryExporter
{
    ServerObservedMetricsSnapshot CreateSnapshot(ServerObservedMetricsSnapshot? previous = null);

    ObservedMetricsSnapshot CreateObservedSnapshot(ServerObservedMetricsSnapshot? previous = null);

    string SerializeSnapshot(ServerObservedMetricsSnapshot snapshot);

    string SerializeSnapshot(ObservedMetricsSnapshot snapshot);
}

public sealed class ServerTelemetryExporter : IServerTelemetryExporter
{
    private readonly IServerTelemetry _telemetry;

    public ServerTelemetryExporter(IServerTelemetry telemetry)
    {
        _telemetry = telemetry;
    }

    public ServerObservedMetricsSnapshot CreateSnapshot(ServerObservedMetricsSnapshot? previous = null)
    {
        return ServerObservedMetricsSnapshot.FromTelemetry(_telemetry.CreateSnapshot(), previous);
    }

    public ObservedMetricsSnapshot CreateObservedSnapshot(ServerObservedMetricsSnapshot? previous = null)
    {
        return ObservedMetricsSnapshot.FromServer(CreateSnapshot(previous));
    }

    public string SerializeSnapshot(ServerObservedMetricsSnapshot snapshot)
    {
        return ObservedMetricsJson.Serialize(snapshot);
    }

    public string SerializeSnapshot(ObservedMetricsSnapshot snapshot)
    {
        return ObservedMetricsJson.Serialize(snapshot);
    }
}

public static class ObservedMetricsJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize<TSnapshot>(TSnapshot snapshot)
    {
        return JsonSerializer.Serialize(snapshot, SerializerOptions);
    }
}
