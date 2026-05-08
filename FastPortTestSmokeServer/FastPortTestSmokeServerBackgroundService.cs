using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FastPortTestSmokeServer;

public class FastPortTestSmokeServerBackgroundService : BackgroundService
{
    private readonly ILogger<FastPortTestSmokeServerBackgroundService> _logger;
    private readonly FastPortTestSmokeServer m_FastPortTestSmokeServer;
    private readonly FastPortTestSmokeServerOptions m_Options;

    public FastPortTestSmokeServerBackgroundService(
        ILogger<FastPortTestSmokeServerBackgroundService> logger,
        FastPortTestSmokeServer fastPortSmokeServer,
        FastPortTestSmokeServerOptions options)
    {
        _logger = logger;
        m_FastPortTestSmokeServer = fastPortSmokeServer;
        m_Options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "FastPortTestSmokeServerBackgroundService, StartAccept. Host:{Host}, Port:{Port}, ListenBacklog:{ListenBacklog}",
            m_Options.Host,
            m_Options.Port,
            m_Options.ListenBacklog);

        if (!m_FastPortTestSmokeServer.StartAccept(m_Options.Host, m_Options.Port, m_Options.ListenBacklog))
        {
            _logger.LogError("FastPortTestSmokeServerBackgroundService, StartAccept failed.");
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
            _logger.LogInformation("FastPortTestSmokeServerBackgroundService received shutdown.");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("FastPortTestSmokeServerBackgroundService stopping.");

        m_FastPortTestSmokeServer.RequestShutdown();

        return base.StopAsync(cancellationToken);
    }
}
