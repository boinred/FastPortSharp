namespace FastPortTestLoadRunner;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            LoadRunnerOptions.PrintUsage();
            return 0;
        }

        if (!LoadRunnerOptions.TryParse(args, out var options, out var errorMessage))
        {
            Console.Error.WriteLine(errorMessage);
            Console.Error.WriteLine();
            LoadRunnerOptions.PrintUsage();
            return 1;
        }

        var scenario = options.ToScenario();
        PrintPlan(scenario);

        using var cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        var metricsCollector = new MetricsCollector(scenario.Sessions);
        var reporters = CreateReporters(scenario).ToArray();
        JsonConnectEventReporter? connectEventReporter = CreateConnectEventReporter(scenario);
        var loadRunner = new LoadRunner(scenario, metricsCollector, reporters, connectEventReporter);

        try
        {
            await loadRunner.RunAsync(cancellationTokenSource.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Load runner failed: {ex.Message}");
            return 1;
        }
        finally
        {
            foreach (var reporter in reporters.OfType<IDisposable>())
            {
                reporter.Dispose();
            }

            connectEventReporter?.Dispose();
        }
    }

    private static IEnumerable<IMetricsReporter> CreateReporters(LoadScenario scenario)
    {
        yield return new ConsoleMetricsReporter();

        if (!string.IsNullOrWhiteSpace(scenario.OutputPath))
        {
            yield return new JsonMetricsReporter(scenario.OutputPath);
        }
    }

    private static JsonConnectEventReporter? CreateConnectEventReporter(LoadScenario scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario.ConnectEventsOutputPath))
        {
            return null;
        }

        return new JsonConnectEventReporter(scenario.ConnectEventsOutputPath);
    }

    private static void PrintPlan(LoadScenario scenario)
    {
        Console.WriteLine("FastPortTestLoadRunner");
        Console.WriteLine("------------------");
        Console.WriteLine($"Target              : {scenario.Host}:{scenario.Port}");
        Console.WriteLine($"Sessions            : {scenario.Sessions:N0}");
        Console.WriteLine($"Payload             : {scenario.Payload}");
        Console.WriteLine($"Send rate/session   : {scenario.SendRatePerSession:N0} packets/sec");
        Console.WriteLine($"Ramp-up             : {scenario.RampUp}");
        Console.WriteLine($"Duration            : {scenario.Duration}");
        Console.WriteLine($"Metrics interval    : {scenario.MetricsInterval}");
        Console.WriteLine($"Output              : {scenario.OutputPath ?? "console only"}");
        Console.WriteLine($"Connect events      : {scenario.ConnectEventsOutputPath ?? "disabled"}");
        Console.WriteLine($"Heartbeat interval  : {(scenario.HeartbeatInterval > TimeSpan.Zero ? scenario.HeartbeatInterval.ToString() : "disabled")}");
        Console.WriteLine($"Pacing              : {scenario.Pacing.ToDisplayString()}");
        Console.WriteLine();
    }
}
