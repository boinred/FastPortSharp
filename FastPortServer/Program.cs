using FastPortServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHostBuilder builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, s) =>
{
    var serverSection = context.Configuration.GetSection("FastPortServer");
    string host = serverSection["Host"] ?? "0.0.0.0";
    int port = int.TryParse(serverSection["Port"], out int configuredPort) ? configuredPort : 6628;

    s.AddSingleton(new FastPortServerOptions { Host = host, Port = port });
    s.AddHostedService<FastPortServer.FastPortServerBackgroundService>();
    s.AddSingleton<LibNetworks.Sessions.IClientSessionFactory, FastPortServer.Sessions.FastPortClientSessionFactory>();
    s.AddSingleton<FastPortServer.FastPortServer>();
});

var host = builder.Build();

await host.RunAsync();
