using System.Diagnostics;
using System.Globalization;

namespace FastPortTestLoadValidation;

internal sealed class LoadRunnerCommandBuilder
{
    public LoadRunnerCommand Build(LoadValidationOptions options, LoadValidationStage stage)
    {
        string metricsPath = GetMetricsPath(options.OutputDirectory, stage);
        string connectEventsPath = GetConnectEventsPath(options.OutputDirectory, stage);
        var arguments = new List<string>
        {
            "run"
        };

        if (options.RunnerNoBuild)
        {
            arguments.Add("--no-build");
        }

        arguments.AddRange(
        [
            "-c",
            options.Configuration,
            "--project",
            options.RunnerProject,
            "--",
            "--host",
            options.Host,
            "--port",
            options.Port.ToString(CultureInfo.InvariantCulture),
            "--sessions",
            stage.Sessions.ToString(CultureInfo.InvariantCulture),
            "--payload",
            stage.Payload,
            "--rate",
            stage.SendRatePerSession.ToString(CultureInfo.InvariantCulture),
            "--ramp-up",
            FormatSeconds(stage.RampUp),
            "--duration",
            FormatDuration(stage.Duration),
            "--metrics-interval",
            FormatDuration(stage.MetricsInterval),
            "--output",
            metricsPath,
            "--connect-events-output",
            connectEventsPath
        ]);

        AddPacingArguments(arguments, options.Pacing);

        return new LoadRunnerCommand(
            "dotnet",
            arguments);
    }

    private static void AddPacingArguments(List<string> arguments, LoadValidationPacingOptions pacing)
    {
        if (pacing.Policy == LoadValidationPacingPolicy.None)
        {
            return;
        }

        arguments.Add("--pacing-policy");
        arguments.Add(pacing.ToRunnerPolicyArgument());

        if (pacing.Policy == LoadValidationPacingPolicy.FixedWindow)
        {
            arguments.Add("--pacing-fixed-window");
            arguments.Add(pacing.FixedWindow!.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (pacing.Policy == LoadValidationPacingPolicy.AdaptiveWindow)
        {
            arguments.Add("--pacing-min-window");
            arguments.Add(pacing.MinWindow.ToString(CultureInfo.InvariantCulture));
            arguments.Add("--pacing-initial-window");
            arguments.Add(pacing.InitialWindow.ToString(CultureInfo.InvariantCulture));
            arguments.Add("--pacing-max-window");
            arguments.Add(pacing.MaxWindow.ToString(CultureInfo.InvariantCulture));
            arguments.Add("--pacing-rtt-target-ms");
            arguments.Add(pacing.RttTargetMs.ToString(CultureInfo.InvariantCulture));
            arguments.Add("--pacing-rtt-high-ms");
            arguments.Add(pacing.RttHighMs.ToString(CultureInfo.InvariantCulture));
            arguments.Add("--pacing-increase-every");
            arguments.Add(pacing.IncreaseEveryResponses.ToString(CultureInfo.InvariantCulture));
        }
    }

    public static string GetMetricsPath(string outputDirectory, LoadValidationStage stage)
    {
        return Path.Combine(outputDirectory, $"{stage.Id}.metrics.jsonl");
    }

    public static string GetConnectEventsPath(string outputDirectory, LoadValidationStage stage)
    {
        return Path.Combine(outputDirectory, $"{stage.Id}.connect-events.jsonl");
    }

    internal static string FormatSeconds(TimeSpan duration)
    {
        return $"{Math.Max(1, (int)duration.TotalSeconds)}s";
    }

    internal static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1 && duration.TotalMinutes % 60 == 0)
        {
            return $"{(int)duration.TotalHours}h";
        }

        if (duration.TotalMinutes >= 1 && duration.TotalSeconds % 60 == 0)
        {
            return $"{(int)duration.TotalMinutes}m";
        }

        return $"{Math.Max(1, (int)duration.TotalSeconds)}s";
    }
}

internal sealed record LoadRunnerCommand(string FileName, IReadOnlyList<string> Arguments)
{
    public ProcessStartInfo ToStartInfo()
    {
        var startInfo = new ProcessStartInfo(FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public string ToDisplayString()
    {
        return string.Join(' ', new[] { FileName }.Concat(Arguments.Select(QuoteIfNeeded)));
    }

    private static string QuoteIfNeeded(string value)
    {
        if (value.Length == 0 || value.Any(char.IsWhiteSpace))
        {
            return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }
}
