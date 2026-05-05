using Microsoft.Extensions.Configuration;

namespace FastPortTestSmokeServer;

public static class FastPortTestSmokeServerConfiguration
{
    public const string SectionName = "FastPortTestSmokeServer";

    public const string LegacySectionName = "FastPortSmokeServer";

    public static IConfigurationSection GetServerSection(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(SectionName);
        if (section.Exists())
        {
            return section;
        }

        return configuration.GetSection(LegacySectionName);
    }
}

public sealed class FastPortTestSmokeServerOptions
{
    public string Host { get; init; } = "0.0.0.0";

    public int Port { get; init; } = 6628;
}

public sealed class FastPortTestSmokeServerTelemetryOptions
{
    public string? Output { get; init; }

    public int IntervalSeconds { get; init; } = 1;
}
