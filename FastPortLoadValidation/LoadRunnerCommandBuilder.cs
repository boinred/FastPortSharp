using System.Diagnostics;
using System.Globalization;

namespace FastPortLoadValidation;

internal sealed class LoadRunnerCommandBuilder
{
    public LoadRunnerCommand Build(LoadValidationOptions options, LoadValidationStage stage)
    {
        string metricsPath = GetMetricsPath(options.OutputDirectory, stage);
        return new LoadRunnerCommand(
            "dotnet",
            [
                "run",
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
                metricsPath
            ]);
    }

    public static string GetMetricsPath(string outputDirectory, LoadValidationStage stage)
    {
        return Path.Combine(outputDirectory, $"{stage.Id}.metrics.jsonl");
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
