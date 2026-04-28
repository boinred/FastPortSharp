using System.Text.Json;
using LibNetworks.Telemetry;

namespace FastPortLoadValidation;

internal sealed record JsonlReadResult(
    IReadOnlyList<ClientObservedMetricsSnapshot> Samples,
    IReadOnlyList<string> Errors);

internal sealed class JsonlObservedMetricsReader
{
    public async Task<JsonlReadResult> ReadClientSamplesAsync(string path, CancellationToken cancellationToken = default)
    {
        var samples = new List<ClientObservedMetricsSnapshot>();
        var errors = new List<string>();

        if (!File.Exists(path))
        {
            errors.Add($"Metrics file not found: {path}");
            return new JsonlReadResult(samples, errors);
        }

        int lineNumber = 0;
        await foreach (string line in File.ReadLinesAsync(path, cancellationToken))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                ObservedMetricsSnapshot? snapshot = JsonSerializer.Deserialize<ObservedMetricsSnapshot>(
                    line,
                    ObservedMetricsJson.SerializerOptions);

                if (snapshot?.ClientObserved is null)
                {
                    errors.Add($"Line {lineNumber}: missing clientObserved.");
                    continue;
                }

                samples.Add(snapshot.ClientObserved);
            }
            catch (JsonException ex)
            {
                errors.Add($"Line {lineNumber}: invalid JSONL metrics payload. {ex.Message}");
            }
        }

        return new JsonlReadResult(samples, errors);
    }
}
