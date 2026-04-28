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

    services.AddSingleton(new FastPortSmokeServerOptions { Host = host, Port = port });
    services.AddSingleton<IServerTelemetry, ServerTelemetryCollector>();
    services.AddSingleton<IServerTelemetryExporter, ServerTelemetryExporter>();
    services.AddHostedService<FastPortSmokeServer.FastPortSmokeServerBackgroundService>();
    services.AddSingleton<LibNetworks.Sessions.IClientSessionFactory, FastPortSmokeServer.Sessions.FastPortSmokeClientSessionFactory>();
    services.AddSingleton<FastPortSmokeServer.FastPortSmokeServer>();
});

var host = builder.Build();

await host.RunAsync();
