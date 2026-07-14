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
            // Design Ref: §3.2 (dashboard-jsonl-offset-fix) —
            // offset을 yield BEFORE에 확정해 consumer-generator yield/resume gap race를 회피.
            // 기존 패턴(yield → FileInfo.Length 재캡처)에선 consumer가 yield 사이에 append하면
            // 새 데이터가 offset jump로 영구 skip됨.
            (ObservedMetricsSnapshot[] snapshots, long newOffset) = await ReadNewSnapshotsAsync(lastReadOffset, ct);
            lastReadOffset = newOffset;

            foreach (var snap in snapshots)
            {
                yield return snap;
            }

            try
            {
                await Task.Delay(_interval, ct);
            }
            catch (OperationCanceledException) { yield break; }
        }
    }

    private async Task<(ObservedMetricsSnapshot[] Snapshots, long NewOffset)> ReadNewSnapshotsAsync(
        long startOffset, CancellationToken ct)
    {
        if (!File.Exists(_path))
        {
            return (Array.Empty<ObservedMetricsSnapshot>(), startOffset);
        }

        var results = new List<ObservedMetricsSnapshot>();
        long newOffset = startOffset;
        try
        {
            using var fs = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                // Memory: fileshare-windows-gotcha — producer Write + reader Read 충돌 방지.
                FileShare.ReadWrite | FileShare.Delete);

            // Design Ref: §3.2 — truncation detection을 ReadNew 안에서 처리 (yield 후 별도 처리 제거).
            if (fs.Length < startOffset)
            {
                // truncated/rotated → 처음부터 다시
                startOffset = 0;
            }

            // open 직후 length를 stable snapshot으로 capture.
            // 이후 producer append는 다음 iteration에서 처리 (race window 좁힘).
            long fileLength = fs.Length;

            if (startOffset > 0 && startOffset <= fileLength)
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

            newOffset = fileLength;
        }
        catch (IOException) { /* 다음 polling에서 재시도 */ }
        catch (OperationCanceledException) { /* normal stop */ }

        return (results.ToArray(), newOffset);
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
