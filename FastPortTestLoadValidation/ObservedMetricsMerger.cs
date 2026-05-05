using LibNetworks.Telemetry;

namespace FastPortTestLoadValidation;

internal sealed record ObservedMetricsMergeResult(
    IReadOnlyList<ObservedMetricsSnapshot> CombinedSamples,
    int MatchedSamples,
    int UnmatchedClientSamples,
    double MaxSkewMs);

internal sealed class ObservedMetricsMerger
{
    public ObservedMetricsMergeResult Merge(
        IReadOnlyList<ClientObservedMetricsSnapshot> clientSamples,
        IReadOnlyList<ServerObservedMetricsSnapshot> serverSamples,
        TimeSpan tolerance)
    {
        ClientObservedMetricsSnapshot[] orderedClientSamples = clientSamples
            .OrderBy(sample => sample.Timestamp)
            .ToArray();
        ServerObservedMetricsSnapshot[] orderedServerSamples = serverSamples
            .OrderBy(sample => sample.Timestamp)
            .ToArray();

        var combinedSamples = new List<ObservedMetricsSnapshot>(orderedClientSamples.Length);
        int matchedSamples = 0;
        int unmatchedClientSamples = 0;
        double maxSkewMs = 0;
        int serverIndex = 0;

        foreach (ClientObservedMetricsSnapshot client in orderedClientSamples)
        {
            ServerObservedMetricsSnapshot? nearest = FindNearestServerSample(
                client.Timestamp,
                orderedServerSamples,
                ref serverIndex);

            if (nearest is null)
            {
                unmatchedClientSamples++;
                combinedSamples.Add(new ObservedMetricsSnapshot(client.Timestamp, client, null));
                continue;
            }

            TimeSpan skew = (nearest.Timestamp - client.Timestamp).Duration();
            if (skew <= tolerance)
            {
                matchedSamples++;
                maxSkewMs = Math.Max(maxSkewMs, skew.TotalMilliseconds);
                combinedSamples.Add(new ObservedMetricsSnapshot(client.Timestamp, client, nearest));
            }
            else
            {
                unmatchedClientSamples++;
                combinedSamples.Add(new ObservedMetricsSnapshot(client.Timestamp, client, null));
            }
        }

        return new ObservedMetricsMergeResult(
            combinedSamples,
            matchedSamples,
            unmatchedClientSamples,
            maxSkewMs);
    }

    private static ServerObservedMetricsSnapshot? FindNearestServerSample(
        DateTimeOffset clientTimestamp,
        IReadOnlyList<ServerObservedMetricsSnapshot> serverSamples,
        ref int serverIndex)
    {
        if (serverSamples.Count == 0)
        {
            return null;
        }

        serverIndex = Math.Clamp(serverIndex, 0, serverSamples.Count - 1);
        while (serverIndex + 1 < serverSamples.Count)
        {
            TimeSpan currentSkew = (serverSamples[serverIndex].Timestamp - clientTimestamp).Duration();
            TimeSpan nextSkew = (serverSamples[serverIndex + 1].Timestamp - clientTimestamp).Duration();
            if (nextSkew > currentSkew)
            {
                break;
            }

            serverIndex++;
        }

        return serverSamples[serverIndex];
    }
}
