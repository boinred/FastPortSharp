using System.Runtime.CompilerServices;
using System.Text.Json;
using LibTestTelemetry;

namespace FastPortDashboard.Maui.Adapters;

// Design Ref: §3.2 — JSONL tail polling.
// Memory note (cycle: fix-server-telemetry-export-jsonl-flush-flakiness):
//   producer가 FileShare.ReadWrite로 열고 있을 수 있어 reader도 같은 share mode 명시 필수.
//   FileShare.Read default 사용하면 windows에서 IOException 무한 retry 발생.
public sealed class JsonlPollingAdapter : IPollingAdapter
{
    private readonly string _path;
    private readonly TimeSpan _interval;

    public JsonlPollingAdapter(string path, TimeSpan? interval = null)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _interval = interval ?? TimeSpan.FromSeconds(1);
    }

    public async IAsyncEnumerable<ObservedMetricsSnapshot> StreamAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        long lastReadOffset = 0;

        while (!ct.IsCancellationRequested)
        {
            // 한 번의 polling iteration에서 새로 추가된 line을 모두 read.
            ObservedMetricsSnapshot[] newSnapshots = await ReadNewSnapshotsAsync(lastReadOffset, ct);
            foreach (var snap in newSnapshots)
            {
                yield return snap;
            }

            // offset 갱신은 별도 try 안에 (file truncate 케이스 대응)
            try
            {
                if (File.Exists(_path))
                {
                    long currentLength = new FileInfo(_path).Length;
                    if (currentLength < lastReadOffset)
                    {
                        // truncated/rotated → 처음부터 다시
                        lastReadOffset = 0;
                    }
                    else
                    {
                        lastReadOffset = currentLength;
                    }
                }
            }
            catch (IOException) { /* 다음 polling에서 재시도 */ }

            try
            {
                await Task.Delay(_interval, ct);
            }
            catch (OperationCanceledException) { yield break; }
        }
    }

    private async Task<ObservedMetricsSnapshot[]> ReadNewSnapshotsAsync(
        long startOffset, CancellationToken ct)
    {
        if (!File.Exists(_path)) { return Array.Empty<ObservedMetricsSnapshot>(); }

        var results = new List<ObservedMetricsSnapshot>();
        try
        {
            using var fs = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                // 직전 cycle lesson: producer Write + reader Read 충돌 방지.
                FileShare.ReadWrite | FileShare.Delete);

            if (startOffset > 0 && startOffset <= fs.Length)
            {
                fs.Seek(startOffset, SeekOrigin.Begin);
            }

            using var sr = new StreamReader(fs);
            string? line;
            while ((line = await sr.ReadLineAsync(ct)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) { continue; }
                ObservedMetricsSnapshot? snap = TryDeserialize(line);
                if (snap is not null) { results.Add(snap); }
            }
        }
        catch (IOException) { /* 다음 polling에서 재시도 */ }
        catch (OperationCanceledException) { /* normal stop */ }

        return results.ToArray();
    }

    private static ObservedMetricsSnapshot? TryDeserialize(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<ObservedMetricsSnapshot>(
                line, ObservedMetricsJson.SerializerOptions);
        }
        catch (JsonException)
        {
            // partial / malformed line → skip
            return null;
        }
    }
}
