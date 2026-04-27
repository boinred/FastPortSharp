using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FastPortSmokeServer;

public class FastPortSmokeServerBackgroundService : BackgroundService
{
    private readonly ILogger<FastPortSmokeServerBackgroundService> _logger;
    private readonly FastPortSmokeServer m_FastPortSmokeServer;
    private readonly FastPortSmokeServerOptions m_Options;

    public FastPortSmokeServerBackgroundService(
        ILogger<FastPortSmokeServerBackgroundService> logger,
        FastPortSmokeServer fastPortSmokeServer,
        FastPortSmokeServerOptions options)
    {
        _logger = logger;
        m_FastPortSmokeServer = fastPortSmokeServer;
        m_Options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "FastPortSmokeServerBackgroundService, StartAccept. Host:{Host}, Port:{Port}",
            m_Options.Host,
            m_Options.Port);

        if (!m_FastPortSmokeServer.StartAccept(m_Options.Host, m_Options.Port))
        {
            _logger.LogError("FastPortSmokeServerBackgroundService, StartAccept failed.");
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("FastPortSmokeServerBackgroundService received shutdown.");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("FastPortSmokeServerBackgroundService stopping.");

        m_FastPortSmokeServer.RequestShutdown();

        return base.StopAsync(cancellationToken);
    }
}
