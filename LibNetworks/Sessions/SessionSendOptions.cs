namespace LibNetworks.Sessions;

public sealed record SessionSendOptions(
    int MaxQueuedBytes = 1024 * 1024,
    int SendChunkBytes = 64 * 1024)
{
    public static SessionSendOptions Default { get; } = new();

    public int NormalizedMaxQueuedBytes => Math.Max(1, MaxQueuedBytes);

    public int NormalizedSendChunkBytes => Math.Max(1, SendChunkBytes);
}
