using FastPortTestSmokeServer;
using LibNetworks.Telemetry;
using LibTestTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHostBuilder builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    var serverSection = FastPortTestSmokeServerConfiguration.GetServerSection(context.Configuration);
    string host = serverSection["Host"] ?? "0.0.0.0";
    int port = int.TryParse(serverSection["Port"], out int configuredPort) ? configuredPort : 6628;
    var telemetrySection = context.Configuration.GetSection("Telemetry");
    string? telemetryOutput = telemetrySection["Output"];
    int telemetryIntervalSeconds = int.TryParse(telemetrySection["IntervalSeconds"], out int configuredIntervalSeconds)
        ? configuredIntervalSeconds
        : 1;

    services.AddSingleton(new FastPortTestSmokeServerOptions { Host = host, Port = port });
    services.AddSingleton(new FastPortTestSmokeServerTelemetryOptions
    {
        Output = telemetryOutput,
        IntervalSeconds = telemetryIntervalSeconds
    });
    services.AddSingleton<IServerTelemetry, ServerTelemetryCollector>();
    services.AddSingleton<IServerTelemetryExporter, ServerTelemetryExporter>();
    services.AddHostedService<ServerTelemetryExportBackgroundService>();
    services.AddHostedService<FastPortTestSmokeServer.FastPortTestSmokeServerBackgroundService>();
    services.AddSingleton<LibNetworks.Sessions.IClientSessionFactory, FastPortTestSmokeServer.Sessions.FastPortTestSmokeClientSessionFactory>();
    services.AddSingleton<FastPortTestSmokeServer.FastPortTestSmokeServer>();
});

var host = builder.Build();

await host.RunAsync();
