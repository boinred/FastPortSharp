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
        // Diagnostic instrumentation (cycle: fix-server-telemetry-export-jsonl-flush-flakiness).
        // Tracks each branch / iteration boundary. Emit cost: 1 LogInformation per iteration.
        _logger.LogInformation("Server telemetry export ExecuteAsync entered.");

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
        _logger.LogInformation("Server telemetry export directory ensured: {Directory}", directory ?? "(none)");

        _logger.LogInformation(
            "Server telemetry export enabled. Output:{OutputPath}, Interval:{Interval}",
            outputPath,
            interval);

        // Windows file cache 일관성: WriteThrough로 OS write cache 우회.
        // Async + WriteThrough 조합으로 다른 reader handle이 매 flush 즉시 가시.
        await using FileStream stream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.WriteThrough | FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream);
        _logger.LogInformation("Server telemetry export file opened: {OutputPath}", outputPath);

        ServerObservedMetricsSnapshot? previous = null;
        int iter = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                iter++;
                _logger.LogInformation("Server telemetry export iter {Iter} delay-start", iter);
                await Task.Delay(interval, stoppingToken);
                _logger.LogInformation("Server telemetry export iter {Iter} delay-done", iter);

                ObservedMetricsSnapshot observed = _exporter.CreateObservedSnapshot(previous);
                previous = observed.ServerObserved;
                _logger.LogInformation("Server telemetry export iter {Iter} snapshot-created", iter);

                string json = _exporter.SerializeSnapshot(observed);
                _logger.LogInformation("Server telemetry export iter {Iter} json-serialized len={Len}", iter, json.Length);

                await writer.WriteLineAsync(json.AsMemory(), stoppingToken);
                _logger.LogInformation("Server telemetry export iter {Iter} writeline-done", iter);

                await writer.FlushAsync(stoppingToken);
                _logger.LogInformation("Server telemetry export iter {Iter} flush-done", iter);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Server telemetry export stopping (cancelled at iter={Iter}).", iter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Server telemetry export ExecuteAsync threw at iter={Iter}.", iter);
            throw;
        }
        finally
        {
            await writer.FlushAsync(CancellationToken.None);
            _logger.LogInformation("Server telemetry export ExecuteAsync exit. lastIter={Iter}", iter);
        }
    }
}
