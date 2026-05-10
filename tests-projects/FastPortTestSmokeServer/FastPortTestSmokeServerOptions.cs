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
    // 목적: cloud 10K ramp-up에서 TCP listen queue 포화를 줄이기 위한 smoke server 기본 backlog
    public const int DefaultListenBacklog = 4096;
    // 목적: 기존 listener와 동일한 단일 outstanding accept 동작 보존
    public const int DefaultOutstandingAccepts = 1;

    // 설정: listener bind host
    public string Host { get; init; } = "0.0.0.0";

    // 설정: listener bind port
    public int Port { get; init; } = 6628;

    // 설정: Socket.Listen backlog
    public int ListenBacklog { get; init; } = DefaultListenBacklog;

    // 설정: 동시에 등록할 AcceptAsync 요청 수
    public int OutstandingAccepts { get; init; } = DefaultOutstandingAccepts;
}

public sealed class FastPortTestSmokeServerTelemetryOptions
{
    public string? Output { get; init; }

    public int IntervalSeconds { get; init; } = 1;
}
