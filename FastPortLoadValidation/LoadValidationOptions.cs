namespace FastPortLoadValidation;

internal sealed record LoadValidationOptions(
    string Profile,
    string Host,
    int Port,
    string OutputDirectory,
    string? StageId,
    string RunnerProject,
    string Configuration,
    string? ServerMetricsPath,
    TimeSpan MergeTolerance,
    int? MaxPendingRequestsPerSession,
    bool DryRun,
    bool ContinueOnFailure)
{
    public static bool TryParse(string[] args, out LoadValidationOptions options, out string errorMessage)
    {
        string profile = LoadValidationProfiles.Smoke;
        string host = "127.0.0.1";
        int port = 6628;
        string? outputDirectory = null;
        string? stageId = null;
        string runnerProject = "FastPortLoadRunner";
        string configuration = "Release";
        string? serverMetricsPath = null;
        TimeSpan mergeTolerance = TimeSpan.FromMilliseconds(1500);
        int? maxPendingRequestsPerSession = null;
        bool dryRun = false;
        bool continueOnFailure = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--dry-run":
                    dryRun = true;
                    continue;
                case "--continue-on-failure":
                    continueOnFailure = true;
                    continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                options = default!;
                errorMessage = $"Unexpected argument '{arg}'.";
                return false;
            }

            if (i + 1 >= args.Length)
            {
                options = default!;
                errorMessage = $"Missing value for '{arg}'.";
                return false;
            }

            string value = args[++i];
            switch (arg)
            {
                case "--profile":
                    if (!LoadValidationProfiles.IsKnownProfile(value))
                    {
                        options = default!;
                        errorMessage = "--profile must be smoke or staged.";
                        return false;
                    }

                    profile = value.ToLowerInvariant();
                    break;
                case "--host":
                    host = value;
                    break;
                case "--port":
                    if (!int.TryParse(value, out port) || port <= 0 || port > 65535)
                    {
                        options = default!;
                        errorMessage = "--port must be between 1 and 65535.";
                        return false;
                    }

                    break;
                case "--output":
                    outputDirectory = value;
                    break;
                case "--stage":
                    stageId = value;
                    break;
                case "--runner-project":
                    runnerProject = value;
                    break;
                case "--configuration":
                    configuration = value;
                    break;
                case "--server-metrics":
                    serverMetricsPath = value;
                    break;
                case "--merge-tolerance-ms":
                    if (!int.TryParse(value, out int mergeToleranceMs) || mergeToleranceMs <= 0)
                    {
                        options = default!;
                        errorMessage = "--merge-tolerance-ms must be greater than zero.";
                        return false;
                    }

                    mergeTolerance = TimeSpan.FromMilliseconds(mergeToleranceMs);
                    break;
                case "--max-pending-requests-per-session":
                    if (!int.TryParse(value, out int parsedMaxPendingRequestsPerSession) || parsedMaxPendingRequestsPerSession <= 0)
                    {
                        options = default!;
                        errorMessage = "--max-pending-requests-per-session must be greater than zero.";
                        return false;
                    }

                    maxPendingRequestsPerSession = parsedMaxPendingRequestsPerSession;
                    break;
                default:
                    options = default!;
                    errorMessage = $"Unknown option '{arg}'.";
                    return false;
            }
        }

        outputDirectory ??= CreateDefaultOutputDirectory(profile);
        options = new LoadValidationOptions(
            profile,
            host,
            port,
            outputDirectory,
            stageId,
            runnerProject,
            configuration,
            serverMetricsPath,
            mergeTolerance,
            maxPendingRequestsPerSession,
            dryRun,
            continueOnFailure);
        errorMessage = string.Empty;
        return true;
    }

    public static string CreateDefaultOutputDirectory(string profile)
    {
        string timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        return Path.Combine("artifacts", "load-validation", $"{timestamp}-{profile}");
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
        Usage:
          dotnet run -c Release --project FastPortLoadValidation -- [options]

        Options:
          --profile <smoke|staged>       Validation profile. Default: smoke
          --host <host>                  Target server host. Default: 127.0.0.1
          --port <port>                  Target server port. Default: 6628
          --output <dir>                 Output directory. Default: artifacts/load-validation/{timestamp}-{profile}
          --stage <id>                   Run only one stage from the profile.
          --runner-project <path>        FastPortLoadRunner project path. Default: FastPortLoadRunner
          --configuration <name>         dotnet configuration. Default: Release
          --server-metrics <path>        Optional server observed JSONL path to merge into stage summaries.
          --merge-tolerance-ms <ms>      Timestamp merge tolerance for client/server samples. Default: 1500
          --max-pending-requests-per-session <count>
                                          Optional per-session load runner outstanding request cap.
          --dry-run                      Print stage commands without running them.
          --continue-on-failure          Continue after a failed stage.
          --help                         Show help.

        Examples:
          dotnet run -c Release --project FastPortLoadValidation -- --profile smoke
          dotnet run -c Release --project FastPortLoadValidation -- --profile staged --stage s5-random-10k
        """);
    }
}
