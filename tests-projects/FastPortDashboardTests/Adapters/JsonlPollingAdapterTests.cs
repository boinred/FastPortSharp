using System.Text;
using System.Text.Json;
using FastPortDashboard.Maui.Adapters;
using LibTestTelemetry;

namespace FastPortDashboardTests.Adapters;

// Design Ref: §3.5 — JsonlPollingAdapter offset + truncation + FileShare 검증.
[TestClass]
public class JsonlPollingAdapterTests
{
    private string _path = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), $"fp-jsonl-test-{Guid.NewGuid():N}.jsonl");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (File.Exists(_path)) { File.Delete(_path); } } catch { /* ignore */ }
    }

    // ─── Helper ──────────────────────────────────────────────────
    private static ServerObservedMetricsSnapshot MakeServerSnap(long sessions)
        => new(
            Timestamp: DateTimeOffset.UtcNow,
            CurrentSessions: sessions,
            TotalAcceptedSessions: sessions * 2,
            TotalDisconnectedSessions: 0,
            TotalReceivedPackets: 0,
            TotalSendCompletions: 0,
            TotalParsedPacketBytes: 0,
            TotalSentBytes: 0,
            ReceivedPacketsPerSecond: 0,
            SendCompletionsPerSecond: 0,
            ParsedPacketBytesPerSecond: 0,
            SentBytesPerSecond: 0,
            AcceptedSessionsPerSecond: 0,
            DisconnectedSessionsPerSecond: 0,
            AcceptErrorCount: 0,
            SocketErrorCount: 0,
            ParseErrorCount: 0,
            ProtocolErrorCount: 0,
            SocketErrorRate: 0);

    private static void AppendJsonl(string path, params long[] sessions)
    {
        // FileShare.ReadWrite로 열어 reader와 충돌 회피 (memory: fileshare-windows-gotcha).
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        using var sw = new StreamWriter(fs, Encoding.UTF8);
        foreach (var n in sessions)
        {
            var snap = ObservedMetricsSnapshot.FromServer(MakeServerSnap(n));
            sw.WriteLine(JsonSerializer.Serialize(snap, ObservedMetricsJson.SerializerOptions));
        }
    }

    private static async Task<List<ObservedMetricsSnapshot>> CollectAsync(
        JsonlPollingAdapter adapter, int targetCount, TimeSpan timeout)
    {
        var results = new List<ObservedMetricsSnapshot>();
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await foreach (var snap in adapter.StreamAsync(cts.Token))
            {
                results.Add(snap);
                if (results.Count >= targetCount) { break; }
            }
        }
        catch (OperationCanceledException) { /* timeout — caller asserts count */ }
        return results;
    }

    // T-JA-1
    [TestMethod]
    public async Task Stream_3Lines_Yields3Snapshots()
    {
        AppendJsonl(_path, 1, 2, 3);
        var adapter = new JsonlPollingAdapter(_path, interval: TimeSpan.FromMilliseconds(30));

        var got = await CollectAsync(adapter, 3, TimeSpan.FromSeconds(3));

        Assert.AreEqual(3, got.Count);
        CollectionAssert.AreEqual(
            new long[] { 1, 2, 3 },
            got.Select(s => s.ServerObserved!.CurrentSessions).ToArray());
    }

    // 용도: enumerator에서 snapshot 하나를 받아 CurrentSessions 반환 (안전 상한은 호출자 CTS)
    private static async Task<long> NextSessionsAsync(IAsyncEnumerator<ObservedMetricsSnapshot> enumerator)
    {
        Assert.IsTrue(await enumerator.MoveNextAsync(), "snapshot 수신");
        return enumerator.Current.ServerObserved!.CurrentSessions;
    }

    // T-JA-2: offset이 polling 사이클 간 유지되어 새 line만 yield.
    // 흐름: 기존 2줄을 받은 뒤에 append → 고정 delay 없이 소비 순서로 동기화.
    [TestMethod]
    public async Task Stream_OffsetPersistsBetweenIntervals()
    {
        AppendJsonl(_path, 10, 20);
        var adapter = new JsonlPollingAdapter(_path, interval: TimeSpan.FromMilliseconds(50));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var enumerator = adapter.StreamAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        var got = new List<long> { await NextSessionsAsync(enumerator), await NextSessionsAsync(enumerator) };

        // 상태: generator가 첫 2줄을 내보내고 멈춘 사이 새 2줄 기록
        AppendJsonl(_path, 30, 40);
        got.Add(await NextSessionsAsync(enumerator));
        got.Add(await NextSessionsAsync(enumerator));

        CollectionAssert.AreEqual(new long[] { 10, 20, 30, 40 }, got, "기존 2 + 신규 2, 중복·누락 없음");
    }

    // T-JA-3: truncate 후 offset 리셋, 처음부터 다시 읽기.
    // 흐름: 기존 3줄을 모두 받은 뒤에 truncate → 첫 polling보다 truncate가 먼저 일어나는 경합 제거.
    [TestMethod]
    public async Task Stream_FileTruncated_RestartsFromBeginning()
    {
        AppendJsonl(_path, 1, 2, 3);
        var adapter = new JsonlPollingAdapter(_path, interval: TimeSpan.FromMilliseconds(50));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var enumerator = adapter.StreamAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        for (long expected = 1; expected <= 3; expected++)
        {
            Assert.AreEqual(expected, await NextSessionsAsync(enumerator));
        }

        // 상태: truncate + 새 1줄 (새 파일이 기존 offset보다 짧아야 truncation으로 감지됨)
        File.WriteAllText(_path, string.Empty);
        AppendJsonl(_path, 99);

        Assert.AreEqual(99, await NextSessionsAsync(enumerator), "Truncate 이후 처음부터 읽어 99 수신");
    }

    // T-JA-4
    [TestMethod]
    public async Task Stream_MalformedLine_SkipsAndContinues()
    {
        // 1번째: malformed, 2번째: valid, 3번째: malformed, 4번째: valid
        using (var fs = new FileStream(_path, FileMode.Create, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete))
        using (var sw = new StreamWriter(fs, Encoding.UTF8))
        {
            sw.WriteLine("{not json");
            sw.WriteLine(JsonSerializer.Serialize(
                ObservedMetricsSnapshot.FromServer(MakeServerSnap(7)),
                ObservedMetricsJson.SerializerOptions));
            sw.WriteLine("===garbage===");
            sw.WriteLine(JsonSerializer.Serialize(
                ObservedMetricsSnapshot.FromServer(MakeServerSnap(8)),
                ObservedMetricsJson.SerializerOptions));
        }

        var adapter = new JsonlPollingAdapter(_path, interval: TimeSpan.FromMilliseconds(30));
        var got = await CollectAsync(adapter, 2, TimeSpan.FromSeconds(3));

        Assert.AreEqual(2, got.Count, "malformed 2개 skip, valid 2개만 수신");
        CollectionAssert.AreEqual(
            new long[] { 7, 8 },
            got.Select(s => s.ServerObserved!.CurrentSessions).ToArray());
    }

    // T-JA-6: producer가 줄을 쓰는 도중(줄바꿈 전)에 polling해도 그 줄을 잃지 않아야 함.
    [TestMethod]
    public async Task Stream_PartialTrailingLine_DeliveredAfterLineCompletes()
    {
        // 준비: 완성된 1번째 줄 + 줄바꿈 없는 2번째 줄의 앞부분
        string first = JsonSerializer.Serialize(
            ObservedMetricsSnapshot.FromServer(MakeServerSnap(1)), ObservedMetricsJson.SerializerOptions);
        string second = JsonSerializer.Serialize(
            ObservedMetricsSnapshot.FromServer(MakeServerSnap(2)), ObservedMetricsJson.SerializerOptions);
        int splitAt = second.Length / 2;
        File.WriteAllText(_path, first + "\n" + second[..splitAt]);

        var adapter = new JsonlPollingAdapter(_path, interval: TimeSpan.FromMilliseconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var enumerator = adapter.StreamAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        // 흐름: 첫 polling은 완성된 1번째 줄만 내보냄
        Assert.IsTrue(await enumerator.MoveNextAsync(), "1번째 줄 수신");
        Assert.AreEqual(1, enumerator.Current.ServerObserved!.CurrentSessions);

        // 흐름: generator가 멈춘 사이 2번째 줄의 나머지 + 줄바꿈 기록
        File.AppendAllText(_path, second[splitAt..] + "\n");

        // 기대: 다음 polling에서 2번째 줄 전체를 받음 (중간부터 읽어 버리지 않음)
        bool gotSecond;
        try { gotSecond = await enumerator.MoveNextAsync(); }
        catch (OperationCanceledException) { gotSecond = false; }
        Assert.IsTrue(gotSecond, "줄이 완성된 뒤 2번째 줄 수신");
        Assert.AreEqual(2, enumerator.Current.ServerObserved!.CurrentSessions);
    }

    // T-JA-5
    [TestMethod]
    public async Task Stream_ConcurrentWriteWithReadWriteShare_NoIOException()
    {
        // memory: fileshare-windows-gotcha — producer가 FileShare.ReadWrite로 write 중이어도
        // reader도 FileShare.ReadWrite 명시했으니 IOException 없이 진행돼야 함.
        AppendJsonl(_path, 1);
        var adapter = new JsonlPollingAdapter(_path, interval: TimeSpan.FromMilliseconds(20));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // 백그라운드에서 동시 write
        var writerTask = Task.Run(async () =>
        {
            for (long i = 2; i <= 6 && !cts.IsCancellationRequested; i++)
            {
                AppendJsonl(_path, i);
                await Task.Delay(15, cts.Token).ContinueWith(_ => { });
            }
        }, cts.Token);

        var got = await CollectAsync(adapter, 5, TimeSpan.FromSeconds(4));

        try { await writerTask; } catch { /* canceled */ }

        Assert.IsTrue(got.Count >= 5,
            $"동시 read/write 중 IOException 없이 ≥5 snapshot 수신 (actual={got.Count})");
    }
}
