// Design Ref: §3.1 — Echo client value types (consolidated for footprint).
namespace FastPortDashboard.Maui.EchoClient;

public sealed record EchoClientOptions(string Host, int Port, string Message, int SendIntervalMs);

public enum EchoClientState { Disconnected, Connecting, Connected, Error }

public readonly record struct RttSample(DateTime TimestampUtc, double RttMs);

public sealed record EchoStatsSnapshot(
    long SendCount,
    long RecvCount,
    double SendRatePerSec,
    double RecvRatePerSec,
    long TotalBytesSent,
    long TotalBytesRecv,
    double LastRttMs,
    double AvgRttMs);
