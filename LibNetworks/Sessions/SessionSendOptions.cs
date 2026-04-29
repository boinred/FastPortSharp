namespace LibNetworks.Sessions;

public sealed record SessionSendOptions(
    int MaxQueuedBytes = 1024 * 1024,
    int SendChunkBytes = 64 * 1024,
    int MaxDrainBytesPerSignal = 256 * 1024,
    int MaxDrainOperationsPerSignal = 4,
    int TransientSendBackoffMs = 1)
{
    public static SessionSendOptions Default { get; } = new();

    public int NormalizedMaxQueuedBytes => Math.Max(1, MaxQueuedBytes);

    public int NormalizedSendChunkBytes => Math.Max(1, SendChunkBytes);

    public int NormalizedMaxDrainBytesPerSignal => Math.Max(NormalizedSendChunkBytes, MaxDrainBytesPerSignal);

    public int NormalizedMaxDrainOperationsPerSignal => Math.Max(1, MaxDrainOperationsPerSignal);

    public int NormalizedTransientSendBackoffMs => Math.Max(0, TransientSendBackoffMs);
}
