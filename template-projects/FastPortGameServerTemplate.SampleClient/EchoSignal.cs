namespace FastPortGameServerTemplate.SampleClient;

// Shared signal so SampleClientSession can notify the hosted service that the echo round-trip
// is complete and the application can exit.
public sealed class EchoSignal
{
    private readonly TaskCompletionSource<EchoResult> m_Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<EchoResult> WaitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.Register(() => m_Tcs.TrySetCanceled(cancellationToken));
        return m_Tcs.Task;
    }

    public void Complete(EchoResult result) => m_Tcs.TrySetResult(result);
}

public readonly record struct EchoResult(string EchoedMessage, long ServerUnixMs, double RttMs);
