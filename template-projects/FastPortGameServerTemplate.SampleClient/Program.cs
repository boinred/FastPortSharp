using FastPortGameServerTemplate.SampleClient;
using FastPortGameServerTemplate.SampleClient.Sessions;
using LibNetworks.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

var serilogLogger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
Log.Logger = serilogLogger;

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(serilogLogger, dispose: true);

builder.Services
    .AddOptions<SampleClientOptions>()
    .Bind(builder.Configuration.GetSection(SampleClientOptions.SectionName));

builder.Services.AddSingleton<EchoSignal>();
builder.Services.AddSingleton<IServerSessionFactory, SampleClientSessionFactory>();
builder.Services.AddSingleton<SampleClientConnector>();
builder.Services.AddHostedService<SampleClientHostedService>();

using var host = builder.Build();

try
{
    await host.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}
