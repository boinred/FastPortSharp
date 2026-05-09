using FastPortGameServerTemplate.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace FastPortGameServerTemplate.Application;

// Design Ref: §4.1, §11.1 — Generic Host integration.
// Owns the GameServer (BaseMessageListener) lifecycle:
//   StartAsync → StartAccept(host, port)
//   StopAsync  → RequestShutdown()
public sealed class GameServerHostedService : BackgroundService
{
    private readonly ILogger<GameServerHostedService> m_Logger;
    private readonly GameServer m_GameServer;
    private readonly GameServerOptions m_Options;

    public GameServerHostedService(
        ILogger<GameServerHostedService> logger,
        GameServer gameServer,
        IOptions<GameServerOptions> options)
    {
        m_Logger = logger;
        m_GameServer = gameServer;
        m_Options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        m_Logger.LogInformation(
            "GameServer starting. ListenAddress={Address}, ListenPort={Port}, MaxSessions={Max}",
            m_Options.ListenAddress,
            m_Options.ListenPort,
            m_Options.MaxSessions);

        if (!m_GameServer.StartAccept(m_Options.ListenAddress, m_Options.ListenPort))
        {
            m_Logger.LogError(
                "GameServer.StartAccept failed. ListenAddress={Address}, ListenPort={Port}",
                m_Options.ListenAddress,
                m_Options.ListenPort);
            return;
        }

        m_Logger.LogInformation("GameServer listening. Press Ctrl+C to stop.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (System.OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        m_Logger.LogInformation("GameServer shutting down.");
        m_GameServer.RequestShutdown();
        return base.StopAsync(cancellationToken);
    }
}
