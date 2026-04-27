namespace FastPortLoadRunner;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        if (!LoadRunnerOptions.TryParse(args, out var options, out var errorMessage))
        {
            Console.Error.WriteLine(errorMessage);
            Console.Error.WriteLine();
            PrintUsage();
            return 1;
        }

        PrintPlan(options);
        return 0;
    }

    private static void PrintPlan(LoadRunnerOptions options)
    {
        Console.WriteLine("FastPortLoadRunner");
        Console.WriteLine("------------------");
        Console.WriteLine($"Target              : {options.Host}:{options.Port}");
        Console.WriteLine($"Sessions            : {options.Sessions:N0}");
        Console.WriteLine($"Payload             : {options.Payload}");
        Console.WriteLine($"Send rate/session   : {options.SendRatePerSession:N0} packets/sec");
        Console.WriteLine($"Ramp-up             : {options.RampUp}");
        Console.WriteLine($"Duration            : {options.Duration}");
        Console.WriteLine();
        Console.WriteLine("Load execution is not wired yet. This project is now reserved for FastPort load testing.");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
        Usage:
          dotnet run -c Release --project FastPortLoadRunner -- [options]

        Options:
          --host <host>                  Server host. Default: 127.0.0.1
          --port <port>                  Server port. Default: 6628
          --sessions <count>             Concurrent session count. Default: 1
          --payload fixed:<bytes>        Fixed payload size. Example: fixed:8192
          --payload random:<min>-<max>   Random payload size range. Example: random:4096-16384
          --rate <count>                 Packets per second per session. Default: 1
          --ramp-up <duration>           Ramp-up duration. Examples: 30s, 1m. Default: 10s
          --duration <duration>          Test duration. Examples: 5m, 1h. Default: 1m
          --help                         Show help.

        Examples:
          dotnet run -c Release --project FastPortLoadRunner -- --sessions 10000 --payload random:4096-16384 --duration 5m --ramp-up 60s
          dotnet run -c Release --project FastPortLoadRunner -- --sessions 10000 --payload fixed:8192 --rate 20
        """);
    }
}

internal sealed record LoadRunnerOptions(
    string Host,
    int Port,
    int Sessions,
    PayloadProfile Payload,
    int SendRatePerSession,
    TimeSpan RampUp,
    TimeSpan Duration)
{
    public static bool TryParse(string[] args, out LoadRunnerOptions options, out string errorMessage)
    {
        string host = "127.0.0.1";
        int port = 6628;
        int sessions = 1;
        PayloadProfile payload = PayloadProfile.Fixed(8192);
        int sendRatePerSession = 1;
        TimeSpan rampUp = TimeSpan.FromSeconds(10);
        TimeSpan duration = TimeSpan.FromMinutes(1);

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
                default:
                    options = default!;
                    errorMessage = $"Unknown option '{arg}'.";
                    return false;
            }
        }

        options = new LoadRunnerOptions(host, port, sessions, payload, sendRatePerSession, rampUp, duration);
        errorMessage = string.Empty;
        return true;
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
