namespace FastPortServer;

public sealed class FastPortServerOptions
{
    public string Host { get; init; } = "0.0.0.0";

    public int Port { get; init; } = 6628;
}
