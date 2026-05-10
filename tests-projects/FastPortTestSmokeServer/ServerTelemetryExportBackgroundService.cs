using LibTestTelemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FastPortTestSmokeServer;

public sealed class ServerTelemetryExportBackgroundService : BackgroundService
{
    private readonly ILogger<ServerTelemetryExportBackgroundService> _logger;
    private readonly IServerTelemetryExporter _exporter;
    private readonly FastPortTestSmokeServerTelemetryOptions _options;

    public ServerTelemetryExportBackgroundService(
        ILogger<ServerTelemetryExportBackgroundService> logger,
        IServerTelemetryExporter exporter,
        FastPortTestSmokeServerTelemetryOptions options)
    {
        _logger = logger;
        _exporter = exporter;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Output))
        {
            _logger.LogInformation("Server telemetry export disabled.");
            return;
        }

        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(0.05, _options.IntervalSeconds));
        string outputPath = _options.Output;
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _logger.LogInformation(
            "Server telemetry export enabled. Output:{OutputPath}, Interval:{Interval}",
            outputPath,
            interval);

        // Windows에서 producer write cache가 같은 process의 다른 reader handle에
        // 즉시 보이지 않을 수 있어 WriteThrough로 OS write cache를 우회.
        // FileShare.ReadWrite로 reader가 동시 open할 수 있도록 허용 (test/외부 dump tool 친화).
        await using FileStream stream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 4096,
            options: FileOptions.WriteThrough | FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream);

        ServerObservedMetricsSnapshot? previous = null;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(interval, stoppingToken);

                ObservedMetricsSnapshot observed = _exporter.CreateObservedSnapshot(previous);
                previous = observed.ServerObserved;

                string json = _exporter.SerializeSnapshot(observed);
                await writer.WriteLineAsync(json.AsMemory(), stoppingToken);
                await writer.FlushAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Server telemetry export stopping.");
        }
        finally
        {
            await writer.FlushAsync(CancellationToken.None);
        }
    }
}
