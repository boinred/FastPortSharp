namespace FastPortSmokeServer;

public sealed class FastPortSmokeServerOptions
{
    public string Host { get; init; } = "0.0.0.0";

    public int Port { get; init; } = 6628;
}

public sealed class FastPortSmokeServerTelemetryOptions
{
    public string? Output { get; init; }

    public int IntervalSeconds { get; init; } = 1;
}
