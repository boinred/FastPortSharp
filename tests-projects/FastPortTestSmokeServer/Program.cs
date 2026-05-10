using FastPortTestSmokeServer;
using FastPortTestSmokeServer.Sessions;
using LibCommons.Timers;
using LibTestTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHostBuilder builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    var serverSection = FastPortTestSmokeServerConfiguration.GetServerSection(context.Configuration);
    string host = serverSection["Host"] ?? "0.0.0.0";
    int port = int.TryParse(serverSection["Port"], out int configuredPort) ? configuredPort : 6628;

    // 설정: ListenBacklog는 0 이하 또는 파싱 실패 시 smoke server 기본값으로 보정
    int listenBacklog = int.TryParse(serverSection["ListenBacklog"], out int configuredListenBacklog) && configuredListenBacklog > 0
        ? configuredListenBacklog
        : FastPortTestSmokeServerOptions.DefaultListenBacklog;
    // 설정: OutstandingAccepts는 0 이하 또는 파싱 실패 시 기존 단일 accept 기본값으로 보정
    int outstandingAccepts = int.TryParse(serverSection["OutstandingAccepts"], out int configuredOutstandingAccepts) && configuredOutstandingAccepts > 0
        ? configuredOutstandingAccepts
        : FastPortTestSmokeServerOptions.DefaultOutstandingAccepts;
    var telemetrySection = context.Configuration.GetSection("Telemetry");
    string? telemetryOutput = telemetrySection["Output"];
    int telemetryIntervalSeconds = int.TryParse(telemetrySection["IntervalSeconds"], out int configuredIntervalSeconds)
        ? configuredIntervalSeconds
        : 1;
    var sessionIdleCleanupSection = context.Configuration.GetSection("SessionIdleCleanup");
    bool sessionIdleCleanupEnabled = !bool.TryParse(sessionIdleCleanupSection["Enabled"], out bool configuredIdleCleanupEnabled)
        || configuredIdleCleanupEnabled;
    int idleTimeoutSeconds = int.TryParse(sessionIdleCleanupSection["IdleTimeoutSeconds"], out int configuredIdleTimeoutSeconds)
        ? configuredIdleTimeoutSeconds
        : 120;
    int scanIntervalSeconds = int.TryParse(sessionIdleCleanupSection["ScanIntervalSeconds"], out int configuredScanIntervalSeconds)
        ? configuredScanIntervalSeconds
        : 5;

    services.AddSingleton(new FastPortTestSmokeServerOptions
    {
        Host = host,
        Port = port,
        ListenBacklog = listenBacklog,
        OutstandingAccepts = outstandingAccepts
    });
    services.AddSingleton(new FastPortTestSmokeServerTelemetryOptions
    {
        Output = telemetryOutput,
        IntervalSeconds = telemetryIntervalSeconds
    });
    services.AddSingleton(new SessionIdleTrackerOptions
    {
        Enabled = sessionIdleCleanupEnabled,
        IdleTimeout = TimeSpan.FromSeconds(idleTimeoutSeconds),
        ScanInterval = TimeSpan.FromSeconds(scanIntervalSeconds)
    });
    services.AddSingleton<IMonotonicTimeSource>(StopwatchMonotonicTimeSource.Instance);
    services.AddSingleton(TimerQueueOptions.Default);
    services.AddSingleton<TimerQueue>();
    services.AddSingleton<ITimerQueue>(provider => provider.GetRequiredService<TimerQueue>());
    services.AddSingleton<SessionIdleTracker>();
    services.AddSingleton<IServerTelemetry, ServerTelemetryCollector>();
    services.AddSingleton<IServerTelemetryExporter, ServerTelemetryExporter>();
    services.AddHostedService<ServerTelemetryExportBackgroundService>();
    services.AddHostedService<FastPortTestSmokeServer.FastPortTestSmokeServerBackgroundService>();
    services.AddSingleton<LibNetworks.Sessions.IClientSessionFactory, FastPortTestSmokeServer.Sessions.FastPortTestSmokeClientSessionFactory>();
    services.AddSingleton<FastPortTestSmokeServer.FastPortTestSmokeServer>();
});

var host = builder.Build();

await host.RunAsync();
