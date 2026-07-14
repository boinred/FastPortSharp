namespace FastPortGameServerTemplate.Configuration;

// Design Ref: §3.2 — appsettings.json -> GameServer section binding.
public sealed class GameServerOptions
{
    public const string SectionName = "GameServer";

    public string ListenAddress { get; init; } = "0.0.0.0";

    public int ListenPort { get; init; } = 7777;

    public int MaxSessions { get; init; } = 1024;
}
