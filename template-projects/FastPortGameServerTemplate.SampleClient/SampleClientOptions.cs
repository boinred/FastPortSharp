namespace FastPortGameServerTemplate.SampleClient;

public sealed class SampleClientOptions
{
    public const string SectionName = "SampleClient";

    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 7777;

    public string Message { get; init; } = "Hello, FastPort!";

    public bool ExitAfterOneEcho { get; init; } = true;
}
