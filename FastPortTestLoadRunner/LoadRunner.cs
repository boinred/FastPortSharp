namespace FastPortTestLoadRunner;

internal sealed class LoadRunner(
    LoadScenario scenario,
    MetricsCollector metricsCollector,
    IReadOnlyCollection<IMetricsReporter> reporters,
    IConnectEventSink? connectEventSink = null)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sessionTasks = new List<Task>(scenario.Sessions);
        var reporterTasks = reporters
            .Select(reporter => reporter.RunAsync(metricsCollector, scenario.MetricsInterval, runCancellation.Token))
            .ToArray();

        try
        {
            await StartSessionsAsync(sessionTasks, runCancellation.Token);
            await Task.Delay(scenario.Duration, runCancellation.Token);
        }
        finally
        {
            await runCancellation.CancelAsync();
            await WaitForTasksAsync(sessionTasks);
            await WaitForTasksAsync(reporterTasks);
        }
    }

    private async Task StartSessionsAsync(List<Task> sessionTasks, CancellationToken cancellationToken)
    {
        TimeSpan connectDelay = scenario.Sessions <= 1
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(scenario.RampUp.Ticks / scenario.Sessions);

        for (int i = 0; i < scenario.Sessions; i++)
        {
            var payloadGenerator = new PayloadGenerator(scenario.Payload, seed: Environment.TickCount ^ i);
            var session = new LoadSession(i + 1, scenario, payloadGenerator, metricsCollector, connectEventSink);
            sessionTasks.Add(Task.Run(() => session.RunAsync(cancellationToken), CancellationToken.None));

            if (connectDelay > TimeSpan.Zero && i + 1 < scenario.Sessions)
            {
                await Task.Delay(connectDelay, cancellationToken);
            }
        }
    }

    private static async Task WaitForTasksAsync(IReadOnlyCollection<Task> tasks)
    {
        if (tasks.Count == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Background load task failed: {ex.Message}");
        }
    }
}

internal sealed class PayloadGenerator(PayloadProfile profile, int seed)
{
    private readonly Random _random = new(seed);

    public byte[] CreatePayload()
    {
        int size = profile.GetNextSize(_random);
        return new byte[size];
    }
}
