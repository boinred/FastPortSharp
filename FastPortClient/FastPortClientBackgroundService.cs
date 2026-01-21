// MyWorker.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FastPortClient; 

public class FastPortClientBackgroundService : BackgroundService
{
    private readonly ILogger<FastPortClientBackgroundService> _logger;

    private readonly FastPortConnector m_Connector;

    // 생성자를 통해 의존성 주입(DI)으로 Logger를 받습니다.
    public FastPortClientBackgroundService(ILogger<FastPortClientBackgroundService> logger, FastPortConnector connector)
    {
        _logger = logger;
        m_Connector = connector; 
    }

    // 실제 작업이 실행되는 부분입니다.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 FastPortClientBackgroundService 시작되었습니다. (시작 시간: {time})", DateTimeOffset.Now);

        m_Connector.StartConnect("127.0.0.1", 6628, 1);

        try
        {
            // stoppingToken.IsCancellationRequested를 체크하여 중간에 취소 요청이 오면 바로 종료할 수 있습니다.
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 정상적인 종료 요청 (Ctrl+C 등)
            _logger.LogInformation("FastPortClientBackgroundService가 종료 요청을 받았습니다.");
        }

        _logger.LogInformation("✅ FastPortClientBackgroundService 작업이 완료되었습니다. (종료 시간: {time})", DateTimeOffset.Now);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("FastPortClientBackgroundService가 종료됩니다.");

        m_Connector.RequestDisconnect();

        await base.StopAsync(cancellationToken);
    }
}