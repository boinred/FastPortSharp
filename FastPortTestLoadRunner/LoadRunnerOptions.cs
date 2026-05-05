namespace FastPortTestLoadRunner;

internal sealed record LoadRunnerOptions(
    string Host,
    int Port,
    int Sessions,
    PayloadProfile Payload,
    int SendRatePerSession,
    TimeSpan RampUp,
    TimeSpan Duration,
    TimeSpan MetricsInterval,
    string? OutputPath,
    LoadPacingOptions Pacing)
{
    public int? MaxPendingRequestsPerSession =>
        Pacing.Policy == LoadPacingPolicy.FixedWindow ? Pacing.FixedWindow : null;

    public LoadScenario ToScenario()
    {
        return new LoadScenario(
            Host,
            Port,
            Sessions,
            Payload,
            SendRatePerSession,
            RampUp,
            Duration,
            MetricsInterval,
            OutputPath,
            Pacing);
    }

    public static bool TryParse(string[] args, out LoadRunnerOptions options, out string errorMessage)
    {
        string host = "127.0.0.1";
        int port = 6628;
        int sessions = 1;
        PayloadProfile payload = PayloadProfile.Fixed(8192);
        int sendRatePerSession = 1;
        TimeSpan rampUp = TimeSpan.FromSeconds(10);
        TimeSpan duration = TimeSpan.FromMinutes(1);
        TimeSpan metricsInterval = TimeSpan.FromSeconds(1);
        string? outputPath = null;
        int? maxPendingRequestsPerSession = null;
        LoadPacingPolicy? pacingPolicy = null;
        int? pacingFixedWindow = null;
        int pacingMinWindow = LoadPacingOptions.DefaultMinWindow;
        int pacingInitialWindow = LoadPacingOptions.DefaultInitialWindow;
        int pacingMaxWindow = LoadPacingOptions.DefaultMaxWindow;
        double pacingRttTargetMs = LoadPacingOptions.DefaultRttTargetMs;
        double pacingRttHighMs = LoadPacingOptions.DefaultRttHighMs;
        int pacingIncreaseEveryResponses = LoadPacingOptions.DefaultIncreaseEveryResponses;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
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
                case "--sessions":
                    if (!int.TryParse(value, out sessions) || sessions <= 0)
                    {
                        options = default!;
                        errorMessage = "--sessions must be greater than zero.";
                        return false;
                    }
                    break;
                case "--payload":
                    if (!PayloadProfile.TryParse(value, out payload))
                    {
                        options = default!;
                        errorMessage = "--payload must be fixed:<bytes> or random:<min>-<max>.";
                        return false;
                    }
                    break;
                case "--rate":
                    if (!int.TryParse(value, out sendRatePerSession) || sendRatePerSession <= 0)
                    {
                        options = default!;
                        errorMessage = "--rate must be greater than zero.";
                        return false;
                    }
                    break;
                case "--ramp-up":
                    if (!DurationParser.TryParse(value, out rampUp))
                    {
                        options = default!;
                        errorMessage = "--ramp-up must be a duration like 30s, 5m, or 1h.";
                        return false;
                    }
                    break;
                case "--duration":
                    if (!DurationParser.TryParse(value, out duration))
                    {
                        options = default!;
                        errorMessage = "--duration must be a duration like 30s, 5m, or 1h.";
                        return false;
                    }
                    break;
                case "--metrics-interval":
                    if (!DurationParser.TryParse(value, out metricsInterval))
                    {
                        options = default!;
                        errorMessage = "--metrics-interval must be a duration like 1s, 30s, or 1m.";
                        return false;
                    }
                    break;
                case "--output":
                    outputPath = value;
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
                    if (!LoadPacingOptions.TryParsePolicy(value, out LoadPacingPolicy parsedPolicy))
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

        if (!LoadPacingOptions.TryCreate(
            pacingPolicy,
            maxPendingRequestsPerSession,
            pacingFixedWindow,
            pacingMinWindow,
            pacingInitialWindow,
            pacingMaxWindow,
            pacingRttTargetMs,
            pacingRttHighMs,
            pacingIncreaseEveryResponses,
            out LoadPacingOptions pacing,
            out errorMessage))
        {
            options = default!;
            return false;
        }

        options = new LoadRunnerOptions(
            host,
            port,
            sessions,
            payload,
            sendRatePerSession,
            rampUp,
            duration,
            metricsInterval,
            outputPath,
            pacing);
        errorMessage = string.Empty;
        return true;
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
        Usage:
          dotnet run -c Release --project FastPortTestLoadRunner -- [options]

        Options:
          --host <host>                  Server host. Default: 127.0.0.1
          --port <port>                  Server port. Default: 6628
          --sessions <count>             Concurrent session count. Default: 1
          --payload fixed:<bytes>        Fixed payload size. Example: fixed:8192
          --payload random:<min>-<max>   Random payload size range. Example: random:4096-16384
          --rate <count>                 Packets per second per session. Default: 1
          --ramp-up <duration>           Ramp-up duration. Examples: 30s, 1m. Default: 10s
          --duration <duration>          Test duration. Examples: 5m, 1h. Default: 1m
          --metrics-interval <duration>  Metrics reporting interval. Default: 1s
          --output <path>                Optional JSONL metrics output file.
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
          --help                         Show help.

        Examples:
          dotnet run -c Release --project FastPortTestLoadRunner -- --sessions 10000 --payload random:4096-16384 --duration 5m --ramp-up 60s
          dotnet run -c Release --project FastPortTestLoadRunner -- --sessions 10000 --payload fixed:8192 --rate 20 --metrics-interval 1s
        """);
    }
}

internal sealed record LoadScenario(
    string Host,
    int Port,
    int Sessions,
    PayloadProfile Payload,
    int SendRatePerSession,
    TimeSpan RampUp,
    TimeSpan Duration,
    TimeSpan MetricsInterval,
    string? OutputPath,
    LoadPacingOptions Pacing);

internal enum LoadPacingPolicy
{
    None,
    FixedWindow,
    AdaptiveWindow
}

internal sealed record LoadPacingOptions(
    LoadPacingPolicy Policy,
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

    public static LoadPacingOptions None { get; } = new(
        LoadPacingPolicy.None,
        FixedWindow: null,
        DefaultMinWindow,
        DefaultInitialWindow,
        DefaultMaxWindow,
        DefaultRttTargetMs,
        DefaultRttHighMs,
        DefaultIncreaseEveryResponses);

    public static LoadPacingOptions Fixed(int window) => new(
        LoadPacingPolicy.FixedWindow,
        FixedWindow: window,
        DefaultMinWindow,
        DefaultInitialWindow,
        DefaultMaxWindow,
        DefaultRttTargetMs,
        DefaultRttHighMs,
        DefaultIncreaseEveryResponses);

    public static bool TryParsePolicy(string value, out LoadPacingPolicy policy)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "none":
                policy = LoadPacingPolicy.None;
                return true;
            case "fixed-window":
                policy = LoadPacingPolicy.FixedWindow;
                return true;
            case "adaptive-window":
                policy = LoadPacingPolicy.AdaptiveWindow;
                return true;
            default:
                policy = default;
                return false;
        }
    }

    public static bool TryCreate(
        LoadPacingPolicy? parsedPolicy,
        int? legacyFixedWindow,
        int? pacingFixedWindow,
        int minWindow,
        int initialWindow,
        int maxWindow,
        double rttTargetMs,
        double rttHighMs,
        int increaseEveryResponses,
        out LoadPacingOptions options,
        out string errorMessage)
    {
        LoadPacingPolicy policy = parsedPolicy
            ?? (legacyFixedWindow.HasValue || pacingFixedWindow.HasValue
                ? LoadPacingPolicy.FixedWindow
                : LoadPacingPolicy.None);

        if (rttHighMs < rttTargetMs)
        {
            options = default!;
            errorMessage = "--pacing-rtt-high-ms must be greater than or equal to --pacing-rtt-target-ms.";
            return false;
        }

        switch (policy)
        {
            case LoadPacingPolicy.None:
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
            case LoadPacingPolicy.FixedWindow:
                int? fixedWindow = pacingFixedWindow ?? legacyFixedWindow;
                if (fixedWindow is null)
                {
                    options = default!;
                    errorMessage = "--pacing-policy fixed-window requires --pacing-fixed-window or --max-pending-requests-per-session.";
                    return false;
                }

                options = Fixed(fixedWindow.Value) with
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
            case LoadPacingPolicy.AdaptiveWindow:
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

                options = new LoadPacingOptions(
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

    public string ToDisplayString()
    {
        return Policy switch
        {
            LoadPacingPolicy.None => "none",
            LoadPacingPolicy.FixedWindow => $"fixed-window:{FixedWindow}",
            LoadPacingPolicy.AdaptiveWindow => $"adaptive-window:{MinWindow}/{InitialWindow}/{MaxWindow}, rtt={RttTargetMs:F0}/{RttHighMs:F0}ms, inc={IncreaseEveryResponses}",
            _ => Policy.ToString()
        };
    }
}

internal enum PayloadMode
{
    Fixed,
    Random
}

internal readonly record struct PayloadProfile(PayloadMode Mode, int MinBytes, int MaxBytes)
{
    public static PayloadProfile Fixed(int bytes) => new(PayloadMode.Fixed, bytes, bytes);

    public int GetNextSize(Random random)
    {
        return Mode switch
        {
            PayloadMode.Fixed => MinBytes,
            PayloadMode.Random => random.Next(MinBytes, MaxBytes + 1),
            _ => MinBytes
        };
    }

    public static bool TryParse(string value, out PayloadProfile profile)
    {
        if (value.StartsWith("fixed:", StringComparison.OrdinalIgnoreCase))
        {
            string bytesText = value["fixed:".Length..];
            if (int.TryParse(bytesText, out int bytes) && bytes > 0)
            {
                profile = Fixed(bytes);
                return true;
            }
        }

        if (value.StartsWith("random:", StringComparison.OrdinalIgnoreCase))
        {
            string rangeText = value["random:".Length..];
            string[] rangeParts = rangeText.Split('-', 2, StringSplitOptions.TrimEntries);
            if (rangeParts.Length == 2
                && int.TryParse(rangeParts[0], out int minBytes)
                && int.TryParse(rangeParts[1], out int maxBytes)
                && minBytes > 0
                && maxBytes >= minBytes)
            {
                profile = new PayloadProfile(PayloadMode.Random, minBytes, maxBytes);
                return true;
            }
        }

        profile = default;
        return false;
    }

    public override string ToString()
    {
        return Mode switch
        {
            PayloadMode.Fixed => $"fixed:{MinBytes}",
            PayloadMode.Random => $"random:{MinBytes}-{MaxBytes}",
            _ => $"{MinBytes}-{MaxBytes}"
        };
    }
}

internal static class DurationParser
{
    public static bool TryParse(string value, out TimeSpan duration)
    {
        duration = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        char unit = value[^1];
        string numberText = value[..^1];
        if (!double.TryParse(numberText, out double number) || number <= 0)
        {
            return false;
        }

        duration = unit switch
        {
            's' or 'S' => TimeSpan.FromSeconds(number),
            'm' or 'M' => TimeSpan.FromMinutes(number),
            'h' or 'H' => TimeSpan.FromHours(number),
            _ => default
        };

        return duration != default;
    }
}
