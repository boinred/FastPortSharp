namespace FastPortLoadValidation;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            LoadValidationOptions.PrintUsage();
            return 0;
        }

        if (!LoadValidationOptions.TryParse(args, out LoadValidationOptions options, out string errorMessage))
        {
            Console.Error.WriteLine(errorMessage);
            Console.Error.WriteLine();
            LoadValidationOptions.PrintUsage();
            return 1;
        }

        LoadValidationProfile profile = LoadValidationProfiles.Get(options.Profile);
        IReadOnlyList<LoadValidationStage> stages = SelectStages(profile, options.StageId);
        if (stages.Count == 0)
        {
            Console.Error.WriteLine($"Stage '{options.StageId}' was not found in profile '{options.Profile}'.");
            return 1;
        }

        var commandBuilder = new LoadRunnerCommandBuilder();
        if (options.DryRun)
        {
            foreach (LoadValidationStage stage in stages)
            {
                Console.WriteLine(commandBuilder.Build(options, stage).ToDisplayString());
            }

            return 0;
        }

        DateTimeOffset startedAt = DateTimeOffset.Now;
        string runId = $"{startedAt:yyyyMMdd-HHmmss}-{options.Profile}";
        Directory.CreateDirectory(options.OutputDirectory);

        var manifest = new LoadValidationRunManifest(
            runId,
            startedAt,
            options.Profile,
            options.Host,
            options.Port,
            stages);

        var writer = new LoadValidationSummaryWriter();
        await writer.WriteManifestAsync(options.OutputDirectory, manifest);

        var processRunner = new ProcessRunner();
        var reader = new JsonlObservedMetricsReader();
        var evaluator = new LoadValidationEvaluator();
        var summaries = new List<LoadValidationStageSummary>();

        using var cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        foreach (LoadValidationStage stage in stages)
        {
            LoadRunnerCommand command = commandBuilder.Build(options, stage);
            string stdoutPath = Path.Combine(options.OutputDirectory, $"{stage.Id}.stdout.log");
            string stderrPath = Path.Combine(options.OutputDirectory, $"{stage.Id}.stderr.log");
            string metricsPath = LoadRunnerCommandBuilder.GetMetricsPath(options.OutputDirectory, stage);

            Console.WriteLine($"Running {stage.Id}: {command.ToDisplayString()}");
            int exitCode = await processRunner.RunAsync(command, stdoutPath, stderrPath, cancellationTokenSource.Token);
            JsonlReadResult readResult = await reader.ReadClientSamplesAsync(metricsPath, cancellationTokenSource.Token);
            LoadValidationStageSummary summary = evaluator.Evaluate(stage, metricsPath, readResult, exitCode);
            summaries.Add(summary);

            if (!summary.Passed && !options.ContinueOnFailure)
            {
                break;
            }
        }

        var runSummary = new LoadValidationRunSummary(
            runId,
            summaries.Count > 0 && summaries.All(stage => stage.Passed),
            startedAt,
            DateTimeOffset.Now,
            summaries);

        await writer.WriteSummaryAsync(options.OutputDirectory, runSummary, cancellationTokenSource.Token);
        Console.WriteLine($"Summary: {Path.Combine(options.OutputDirectory, "summary.md")}");
        return runSummary.Passed ? 0 : 2;
    }

    private static IReadOnlyList<LoadValidationStage> SelectStages(LoadValidationProfile profile, string? stageId)
    {
        if (string.IsNullOrWhiteSpace(stageId))
        {
            return profile.Stages;
        }

        return profile.Stages
            .Where(stage => string.Equals(stage.Id, stageId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
