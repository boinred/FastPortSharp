// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// 1. Host Builder를 생성한다.
IHostBuilder builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices(s =>
{
    s.AddHostedService<FastPortClient.FastPortClientBackgroundService>();
    s.AddSingleton<LibNetworks.Sessions.IServerSessionFactory, FastPortClient.Sessions.FastPortServerSessionFactory>();
    s.AddTransient<FastPortClient.FastPortConnector>(); 
});


// 빌더를 사용하여 호스트 빌드
var host = builder.Build();

await host.RunAsync();

