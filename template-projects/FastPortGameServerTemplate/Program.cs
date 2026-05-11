using FastPortGameServerTemplate.Application;
using FastPortGameServerTemplate.Configuration;
using FastPortGameServerTemplate.Handlers;
using FastPortGameServerTemplate.Sessions;
using FastPortGameServerTemplate.Telemetry;
using LibNetworks.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

// Design Ref: §11.1, §11.2 #10 — Generic Host wiring with Serilog sink and full DI graph.

var builder = Host.CreateApplicationBuilder(args);

// Serilog from configuration (appsettings.json `Serilog` section) -> Microsoft.Extensions.Logging pipeline.
var serilogLogger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
Log.Logger = serilogLogger;

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(serilogLogger, dispose: true);

builder.Services
    .AddOptions<GameServerOptions>()
    .Bind(builder.Configuration.GetSection(GameServerOptions.SectionName));

// Telemetry: default Null impl. Replace via DI in your own game server.
builder.Services.AddSingleton<IGameServerTelemetry, NullGameServerTelemetry>();

// Handlers: register every IPacketHandler implementation. PacketDispatcher consumes IEnumerable<IPacketHandler>.
builder.Services.AddSingleton<IPacketHandler, EchoHandler>();
builder.Services.AddSingleton<PacketDispatcher>();

// Sessions: factory creates GameSession per accepted socket.
builder.Services.AddSingleton<IClientSessionFactory, GameSessionFactory>();

// Listener + hosted service.
builder.Services.AddSingleton<GameServer>();
builder.Services.AddHostedService<GameServerHostedService>();

using var host = builder.Build();

try
{
    await host.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}
