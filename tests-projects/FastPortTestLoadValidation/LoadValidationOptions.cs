namespace FastPortTestLoadValidation;

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
    LoadValidationPacingOptions Pacing,
    bool RunnerNoBuild,
    bool DryRun,
    bool ContinueOnFailure)
{
    public int? MaxPendingRequestsPerSession =>
        Pacing.Policy == LoadValidationPacingPolicy.FixedWindow ? Pacing.FixedWindow : null;

    public static bool TryParse(string[] args, out LoadValidationOptions options, out string errorMessage)
    {
        string profile = LoadValidationProfiles.Smoke;
        string host = "127.0.0.1";
        int port = 6628;
        string? outputDirectory = null;
        string? stageId = null;
        string runnerProject = "FastPortTestLoadRunner";
        string configuration = "Release";
        string? serverMetricsPath = null;
        TimeSpan mergeTolerance = TimeSpan.FromMilliseconds(1500);
        int? maxPendingRequestsPerSession = null;
        LoadValidationPacingPolicy? pacingPolicy = null;
        int? pacingFixedWindow = null;
        int pacingMinWindow = LoadValidationPacingOptions.DefaultMinWindow;
        int pacingInitialWindow = LoadValidationPacingOptions.DefaultInitialWindow;
        int pacingMaxWindow = LoadValidationPacingOptions.DefaultMaxWindow;
        double pacingRttTargetMs = LoadValidationPacingOptions.DefaultRttTargetMs;
        double pacingRttHighMs = LoadValidationPacingOptions.DefaultRttHighMs;
        int pacingIncreaseEveryResponses = LoadValidationPacingOptions.DefaultIncreaseEveryResponses;
        bool dryRun = false;
        bool continueOnFailure = false;
        bool runnerNoBuild = false;

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
                case "--runner-no-build":
                    runnerNoBuild = true;
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
                case "--pacing-policy":
                    if (!LoadValidationPacingOptions.TryParsePolicy(value, out LoadValidationPacingPolicy parsedPolicy))
                    {
                        options = default!;
                        errorMessage = "--pacing-policy must be none, fixed-window, or adaptive-window.";
                        return false;
                    }

                    pacingPolicy = parsedPolicy;
                    break;
                case "--pacing-fixed-window":
                    if (!int.TryParse(value, out int parsedFixedWindow) || parsedFixedWindow <= 0)
                    {
                        options = default!;
                        errorMessage = "--pacing-fixed-window must be greater than zero.";
                        return false;
                    }

                    pacingFixedWindow = parsedFixedWindow;
                    break;
                case "--pacing-min-window":
                    if (!int.TryParse(value, out pacingMinWindow) || pacingMinWindow <= 0)
                    {
                        options = default!;
                        errorMessage = "--pacing-min-window must be greater than zero.";
                        return false;
                    }

                    break;
                case "--pacing-initial-window":
                    if (!int.TryParse(value, out pacingInitialWindow) || pacingInitialWindow <= 0)
                    {
                        options = default!;
                        errorMessage = "--pacing-initial-window must be greater than zero.";
                        return false;
                    }

                    break;
                case "--pacing-max-window":
                    if (!int.TryParse(value, out pacingMaxWindow) || pacingMaxWindow <= 0)
                    {
                        options = default!;
                        errorMessage = "--pacing-max-window must be greater than zero.";
                        return false;
                    }

                    break;
                case "--pacing-rtt-target-ms":
                    if (!double.TryParse(value, out pacingRttTargetMs) || pacingRttTargetMs <= 0)
                    {
                        options = default!;
                        errorMessage = "--pacing-rtt-target-ms must be greater than zero.";
                        return false;
                    }

                    break;
                case "--pacing-rtt-high-ms":
                    if (!double.TryParse(value, out pacingRttHighMs) || pacingRttHighMs <= 0)
                    {
                        options = default!;
                        errorMessage = "--pacing-rtt-high-ms must be greater than zero.";
                        return false;
                    }

                    break;
                case "--pacing-increase-every":
                    if (!int.TryParse(value, out pacingIncreaseEveryResponses) || pacingIncreaseEveryResponses <= 0)
                    {
                        options = default!;
                        errorMessage = "--pacing-increase-every must be greater than zero.";
                        return false;
                    }

                    break;
                default:
                    options = default!;
                    errorMessage = $"Unknown option '{arg}'.";
                    return false;
            }
        }

        if (!LoadValidationPacingOptions.TryCreate(
            pacingPolicy,
            maxPendingRequestsPerSession,
            pacingFixedWindow,
            pacingMinWindow,
            pacingInitialWindow,
            pacingMaxWindow,
            pacingRttTargetMs,
            pacingRttHighMs,
            pacingIncreaseEveryResponses,
            out LoadValidationPacingOptions pacing,
            out errorMessage))
        {
            options = default!;
            return false;
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
            pacing,
            runnerNoBuild,
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
          dotnet run -c Release --project FastPortTestLoadValidation -- [options]

        Options:
          --profile <smoke|staged>       Validation profile. Default: smoke
          --host <host>                  Target server host. Default: 127.0.0.1
          --port <port>                  Target server port. Default: 6628
          --output <dir>                 Output directory. Default: artifacts/load-validation/{timestamp}-{profile}
          --stage <id>                   Run only one stage from the profile.
          --runner-project <path>        FastPortTestLoadRunner project path. Default: FastPortTestLoadRunner
          --configuration <name>         dotnet configuration. Default: Release
          --runner-no-build              Run FastPortTestLoadRunner with dotnet run --no-build.
          --server-metrics <path>        Optional server observed JSONL path to merge into stage summaries.
          --merge-tolerance-ms <ms>      Timestamp merge tolerance for client/server samples. Default: 1500
          --max-pending-requests-per-session <count>
                                          Legacy shortcut for --pacing-policy fixed-window.
          --pacing-policy <policy>       none, fixed-window, or adaptive-window. Default: none
          --pacing-fixed-window <count>  Fixed outstanding request window.
          --pacing-min-window <count>    Adaptive minimum window. Default: 1
          --pacing-initial-window <count>
                                          Adaptive initial window. Default: 4
          --pacing-max-window <count>    Adaptive maximum window. Default: 8
          --pacing-rtt-target-ms <ms>    RTT target for adaptive increase. Default: 14000
          --pacing-rtt-high-ms <ms>      RTT high watermark for adaptive decrease. Default: 24000
          --pacing-increase-every <count>
                                          Stable responses before window increase. Default: 128
          --dry-run                      Print stage commands without running them.
          --continue-on-failure          Continue after a failed stage.
          --help                         Show help.

        Examples:
          dotnet run -c Release --project FastPortTestLoadValidation -- --profile smoke
          dotnet run -c Release --project FastPortTestLoadValidation -- --profile staged --stage s5-random-10k
        """);
    }
}

internal enum LoadValidationPacingPolicy
{
    None,
    FixedWindow,
    AdaptiveWindow
}

internal sealed record LoadValidationPacingOptions(
    LoadValidationPacingPolicy Policy,
    int? FixedWindow,
    int MinWindow,
    int InitialWindow,
    int MaxWindow,
    double RttTargetMs,
    double RttHighMs,
    int IncreaseEveryResponses)
{
    public const int DefaultMinWindow = 1;
    public const int DefaultInitialWindow = 4;
    public const int DefaultMaxWindow = 8;
    public const double DefaultRttTargetMs = 14_000;
    public const double DefaultRttHighMs = 24_000;
    public const int DefaultIncreaseEveryResponses = 128;

    public static LoadValidationPacingOptions None { get; } = new(
        LoadValidationPacingPolicy.None,
        FixedWindow: null,
        DefaultMinWindow,
        DefaultInitialWindow,
        DefaultMaxWindow,
        DefaultRttTargetMs,
        DefaultRttHighMs,
        DefaultIncreaseEveryResponses);

    public static bool TryParsePolicy(string value, out LoadValidationPacingPolicy policy)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "none":
                policy = LoadValidationPacingPolicy.None;
                return true;
            case "fixed-window":
                policy = LoadValidationPacingPolicy.FixedWindow;
                return true;
            case "adaptive-window":
                policy = LoadValidationPacingPolicy.AdaptiveWindow;
                return true;
            default:
                policy = default;
                return false;
        }
    }

    public static bool TryCreate(
        LoadValidationPacingPolicy? parsedPolicy,
        int? legacyFixedWindow,
        int? pacingFixedWindow,
        int minWindow,
        int initialWindow,
        int maxWindow,
        double rttTargetMs,
        double rttHighMs,
        int increaseEveryResponses,
        out LoadValidationPacingOptions options,
        out string errorMessage)
    {
        LoadValidationPacingPolicy policy = parsedPolicy
            ?? (legacyFixedWindow.HasValue || pacingFixedWindow.HasValue
                ? LoadValidationPacingPolicy.FixedWindow
                : LoadValidationPacingPolicy.None);

        if (rttHighMs < rttTargetMs)
        {
            options = default!;
            errorMessage = "--pacing-rtt-high-ms must be greater than or equal to --pacing-rtt-target-ms.";
            return false;
        }

        switch (policy)
        {
            case LoadValidationPacingPolicy.None:
                if (legacyFixedWindow.HasValue || pacingFixedWindow.HasValue)
                {
                    options = default!;
                    errorMessage = "Fixed window options cannot be used with --pacing-policy none.";
                    return false;
                }

                options = None with
                {
                    MinWindow = minWindow,
                    InitialWindow = initialWindow,
                    MaxWindow = maxWindow,
                    RttTargetMs = rttTargetMs,
                    RttHighMs = rttHighMs,
                    IncreaseEveryResponses = increaseEveryResponses
                };
                errorMessage = string.Empty;
                return true;
            case LoadValidationPacingPolicy.FixedWindow:
                int? fixedWindow = pacingFixedWindow ?? legacyFixedWindow;
                if (fixedWindow is null)
                {
                    options = default!;
                    errorMessage = "--pacing-policy fixed-window requires --pacing-fixed-window or --max-pending-requests-per-session.";
                    return false;
                }

                options = new LoadValidationPacingOptions(
                    policy,
                    fixedWindow.Value,
                    minWindow,
                    initialWindow,
                    maxWindow,
                    rttTargetMs,
                    rttHighMs,
                    increaseEveryResponses);
                errorMessage = string.Empty;
                return true;
            case LoadValidationPacingPolicy.AdaptiveWindow:
                if (legacyFixedWindow.HasValue || pacingFixedWindow.HasValue)
                {
                    options = default!;
                    errorMessage = "Fixed window options cannot be used with --pacing-policy adaptive-window.";
                    return false;
                }

                if (minWindow > initialWindow || initialWindow > maxWindow)
                {
                    options = default!;
                    errorMessage = "Adaptive pacing windows must satisfy min <= initial <= max.";
                    return false;
                }

                options = new LoadValidationPacingOptions(
                    policy,
                    FixedWindow: null,
                    minWindow,
                    initialWindow,
                    maxWindow,
                    rttTargetMs,
                    rttHighMs,
                    increaseEveryResponses);
                errorMessage = string.Empty;
                return true;
            default:
                options = default!;
                errorMessage = "Unsupported pacing policy.";
                return false;
        }
    }

    public string ToRunnerPolicyArgument()
    {
        return Policy switch
        {
            LoadValidationPacingPolicy.None => "none",
            LoadValidationPacingPolicy.FixedWindow => "fixed-window",
            LoadValidationPacingPolicy.AdaptiveWindow => "adaptive-window",
            _ => Policy.ToString()
        };
    }
}
