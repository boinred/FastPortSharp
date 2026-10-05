using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LibTestTelemetry;

namespace FastPortDashboard.Maui.Adapters;

// Design Ref: §3.2 — JSONL tail polling.
// Memory note (cycle: fix-server-telemetry-export-jsonl-flush-flakiness):
//   producer가 FileShare.ReadWrite로 열고 있을 수 있어 reader도 같은 share mode 명시 필수.
//   FileShare.Read default 사용하면 windows에서 IOException 무한 retry 발생.
public sealed class JsonlPollingAdapter : IPollingAdapter
{
    // 용도: polling 한 번에 파일을 나눠 읽는 chunk 크기
    private const int ReadChunkBytes = 64 * 1024;

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

            fs.Seek(startOffset, SeekOrigin.Begin);
            newOffset = startOffset;

            // 범위: [startOffset, fileLength)만 읽음 — capture 이후 append된 바이트를 이번에 읽으면
            //       offset(fileLength)과 어긋나 다음 polling에서 같은 줄을 다시 yield함.
            // 규칙: '\n'으로 끝난 완성된 줄만 처리하고 offset을 그 줄 끝으로 이동.
            //       producer가 쓰는 중인 마지막 줄(줄바꿈 전)은 남겨 두고 다음 polling에서 다시 읽음.
            var buffer = new byte[ReadChunkBytes];
            var pendingLine = new MemoryStream();
            long position = startOffset;
            while (position < fileLength)
            {
                int toRead = (int)Math.Min(buffer.Length, fileLength - position);
                int read = await fs.ReadAsync(buffer.AsMemory(0, toRead), ct);
                // 상태: 파일이 capture 이후 줄어든 경우 — 다음 polling의 truncation 감지에 맡김
                if (read == 0) { break; }

                int segmentStart = 0;
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] != (byte)'\n') { continue; }

                    // 흐름: 줄 하나 완성 → 앞서 쌓인 조각과 합쳐 역직렬화
                    pendingLine.Write(buffer, segmentStart, i - segmentStart);
                    AddSnapshot(results, pendingLine);
                    pendingLine.SetLength(0);
                    segmentStart = i + 1;
                    newOffset = position + i + 1;
                }

                // 상태: 줄바꿈 없는 나머지는 다음 chunk 또는 다음 polling까지 보류
                pendingLine.Write(buffer, segmentStart, read - segmentStart);
                position += read;
            }
        }
        catch (IOException) { /* 다음 polling에서 재시도 */ }
        catch (OperationCanceledException) { /* normal stop */ }

        return (results.ToArray(), newOffset);
    }

    // 용도: 완성된 줄 바이트(줄바꿈 제외)를 UTF-8로 풀어 snapshot 목록에 추가
    private static void AddSnapshot(List<ObservedMetricsSnapshot> results, MemoryStream lineBytes)
    {
        // 정리: StreamWriter가 파일 맨 앞에 쓰는 UTF-8 BOM, Windows 줄 끝 '\r' 제거
        string line = Encoding.UTF8.GetString(lineBytes.GetBuffer(), 0, (int)lineBytes.Length)
            .TrimStart('﻿')
            .TrimEnd('\r');
        if (string.IsNullOrWhiteSpace(line)) { return; }

        ObservedMetricsSnapshot? snap = TryDeserialize(line);
        if (snap is not null) { results.Add(snap); }
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
