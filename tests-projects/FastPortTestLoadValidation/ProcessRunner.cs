using System.Diagnostics;

namespace FastPortTestLoadValidation;

internal sealed class ProcessRunner
{
    public async Task<int> RunAsync(
        LoadRunnerCommand command,
        string stdoutPath,
        string stderrPath,
        CancellationToken cancellationToken = default)
    {
        EnsureDirectory(stdoutPath);
        EnsureDirectory(stderrPath);

        using var process = new Process
        {
            StartInfo = command.ToStartInfo()
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {command.ToDisplayString()}");
        }

        Task stdoutTask = CopyToFileAsync(process.StandardOutput, stdoutPath, cancellationToken);
        Task stderrTask = CopyToFileAsync(process.StandardError, stderrPath, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        await Task.WhenAll(stdoutTask, stderrTask);
        return process.ExitCode;
    }

    private static async Task CopyToFileAsync(StreamReader reader, string path, CancellationToken cancellationToken)
    {
        await using var fileStream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(fileStream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        }
    }

    private static void EnsureDirectory(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
