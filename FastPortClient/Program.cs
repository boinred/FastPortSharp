// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FastPortClient.Sessions;
using LibCommons;

// 1. Host Builder를 생성한다.
IHostBuilder builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    // LatencyStats 옵션 바인딩
    var latencyOptions = context.Configuration
        .GetSection("LatencyStats")
        .Get<LatencyStatsOptions>() ?? new LatencyStatsOptions();

    // LatencyStats를 싱글톤으로 등록
    services.AddSingleton(sp =>
    {
        var logger = sp.GetService<ILogger<LatencyStats>>();
        return new LatencyStats(latencyOptions, logger);
    });

    services.AddHostedService<FastPortClient.FastPortClientBackgroundService>();
    services.AddSingleton<LibNetworks.Sessions.IServerSessionFactory, FastPortServerSessionFactory>();
    services.AddTransient<FastPortClient.FastPortConnector>();
});

// 빌더를 사용하여 호스트 빌드
var host = builder.Build();

// LatencyStats 설정
var latencyStats = host.Services.GetRequiredService<LatencyStats>();
FastPortServerSession.ConfigureLatencyStats(latencyStats);

Console.WriteLine($"[LatencyStats] Output File: {latencyStats.OutputFilePath}");

// Ctrl+C 또는 종료 시 통계 출력 및 저장
Console.CancelKeyPress += async (sender, e) =>
{
    e.Cancel = true; // 즉시 종료 방지
    Console.WriteLine("\n\n[Latency Statistics Summary]");
    FastPortServerSession.PrintLatencyStats();
    await FastPortServerSession.SaveLatencyStatsAsync();
    Environment.Exit(0);
};

AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
{
    Console.WriteLine("\n\n[Final Latency Statistics]");
    FastPortServerSession.PrintLatencyStats();
    FastPortServerSession.SaveLatencyStatsAsync().GetAwaiter().GetResult();
};

await host.RunAsync();

