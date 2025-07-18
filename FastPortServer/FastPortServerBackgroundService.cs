// MyWorker.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FastPortServer;

public class FastPortServerBackgroundService : BackgroundService
{
    private readonly ILogger<FastPortServerBackgroundService> _logger;
    private readonly FastPortServer m_FastPortServer;

    // 생성자를 통해 의존성 주입(DI)으로 Logger를 받습니다.
    public FastPortServerBackgroundService(ILogger<FastPortServerBackgroundService> logger, FastPortServer fastPortServer)
    {
        _logger = logger;
        m_FastPortServer = fastPortServer;
    }

    // 실제 작업이 실행되는 부분입니다.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 FastPortServerBackgroundService 시작되었습니다. (시작 시간: {time})", DateTimeOffset.Now);

        if (!m_FastPortServer.StartAccept("0.0.0.0", 6628))
        {
            _logger.LogError("FastPortServerBackgroundService, StartAccept 실패");
            return;
        }

        // 5초 동안 대기합니다.
        // stoppingToken.IsCancellationRequested를 체크하여 중간에 취소 요청이 오면 바로 종료할 수 있습니다.
        while(!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        _logger.LogInformation("✅ FastPortServerBackgroundService 작업이 완료되었습니다. (종료 시간: {time})", DateTimeOffset.Now);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        m_FastPortServer.RequestShutdown(); 

        _logger.LogInformation("MyWorker가 종료됩니다.");


        return base.StopAsync(cancellationToken);
    }
}