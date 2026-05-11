using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FastPortGameServerTemplate.SampleClient;

public sealed class SampleClientHostedService : BackgroundService
{
    private readonly ILogger<SampleClientHostedService> m_Logger;
    private readonly SampleClientConnector m_Connector;
    private readonly EchoSignal m_Signal;
    private readonly SampleClientOptions m_Options;
    private readonly IHostApplicationLifetime m_Lifetime;

    public SampleClientHostedService(
        ILogger<SampleClientHostedService> logger,
        SampleClientConnector connector,
        EchoSignal signal,
        IOptions<SampleClientOptions> options,
        IHostApplicationLifetime lifetime)
    {
        m_Logger = logger;
        m_Connector = connector;
        m_Signal = signal;
        m_Options = options.Value;
        m_Lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        m_Logger.LogInformation(
            "SampleClient connecting. Host={Host}, Port={Port}",
            m_Options.Host, m_Options.Port);

        if (!m_Connector.StartConnect(m_Options.Host, m_Options.Port, 1))
        {
            m_Logger.LogError("StartConnect failed.");
            m_Lifetime.StopApplication();
            return;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var result = await m_Signal.WaitAsync(timeoutCts.Token);

            m_Logger.LogInformation(
                "Echo round-trip succeeded. Echoed=\"{Echoed}\", RTT={RttMs:F3}ms",
                result.EchoedMessage, result.RttMs);

            if (m_Options.ExitAfterOneEcho)
            {
                m_Logger.LogInformation("ExitAfterOneEcho=true, stopping application.");
                m_Lifetime.StopApplication();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ctrl+C — normal shutdown.
        }
        catch (OperationCanceledException)
        {
            m_Logger.LogError("Echo round-trip timed out (10s).");
            m_Lifetime.StopApplication();
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        m_Connector.RequestDisconnect();
        return base.StopAsync(cancellationToken);
    }
}
