// See https://aka.ms/new-console-template for more information



// 1. Host Builder를 생성한다.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHostBuilder builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices(s =>
{
    s.AddHostedService<FastPortServer.FastPortServerBackgroundService>();
    s.AddSingleton<LibNetworks.Sessions.IClientSessionFactory, FastPortServer.Sessions.FastPortClientSessionFactory>();
    s.AddSingleton<FastPortServer.FastPortServer>();
});


// 빌더를 사용하여 호스트 빌드
var host = builder.Build();

await host.RunAsync();