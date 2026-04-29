using FastPortSmokeServer;
using LibNetworks.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHostBuilder builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    var serverSection = context.Configuration.GetSection("FastPortSmokeServer");
    string host = serverSection["Host"] ?? "0.0.0.0";
    int port = int.TryParse(serverSection["Port"], out int configuredPort) ? configuredPort : 6628;
    var telemetrySection = context.Configuration.GetSection("Telemetry");
    string? telemetryOutput = telemetrySection["Output"];
    int telemetryIntervalSeconds = int.TryParse(telemetrySection["IntervalSeconds"], out int configuredIntervalSeconds)
        ? configuredIntervalSeconds
        : 1;

    services.AddSingleton(new FastPortSmokeServerOptions { Host = host, Port = port });
    services.AddSingleton(new FastPortSmokeServerTelemetryOptions
    {
        Output = telemetryOutput,
        IntervalSeconds = telemetryIntervalSeconds
    });
    services.AddSingleton<IServerTelemetry, ServerTelemetryCollector>();
    services.AddSingleton<IServerTelemetryExporter, ServerTelemetryExporter>();
    services.AddHostedService<ServerTelemetryExportBackgroundService>();
    services.AddHostedService<FastPortSmokeServer.FastPortSmokeServerBackgroundService>();
    services.AddSingleton<LibNetworks.Sessions.IClientSessionFactory, FastPortSmokeServer.Sessions.FastPortSmokeClientSessionFactory>();
    services.AddSingleton<FastPortSmokeServer.FastPortSmokeServer>();
});

var host = builder.Build();

await host.RunAsync();
