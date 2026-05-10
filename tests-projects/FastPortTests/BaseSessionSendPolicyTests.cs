using System.Net;
using System.Net.Sockets;
using LibCommons;
using LibNetworks.Sessions;
using LibTestTelemetry;
using Microsoft.Extensions.Logging.Abstractions;

namespace FastPortTests;

[TestClass]
public sealed class BaseSessionSendPolicyTests
{
    [TestMethod]
    public async Task BaseSession_TryRequestSendBuffers_RejectsPacketOverQueuedByteLimit()
    {
        using SocketPair pair = await SocketPair.CreateAsync();
        var telemetry = new ServerTelemetryCollector();
        var session = new TestSession(
            pair.ServerSocket,
            telemetry,
            new SessionSendOptions(MaxQueuedBytes: 4, SendChunkBytes: 64));

        try
        {
            bool queued = session.TrySendBytes(new byte[8]);

            Assert.IsFalse(queued);
            ServerTelemetrySnapshot snapshot = telemetry.CreateSnapshot();
            Assert.AreEqual(0, snapshot.SendRequests);
            Assert.AreEqual(0, snapshot.PendingSendRequests);
            Assert.AreEqual(1, snapshot.SendBackpressureEvents);
            Assert.AreEqual(1, snapshot.SendRejectedRequests);
            Assert.AreEqual(10, snapshot.SendRejectedBytes);
        }
        finally
        {
            session.RequestDisconnect();
            await session.WaitSession();
        }
    }

    [TestMethod]
    public void SendCompletionTracker_CompletesOnlyAfterFullQueuedRequestBytesDrain()
    {
        var tracker = new SendCompletionTracker();

        tracker.Enqueue(6);
        tracker.Enqueue(7);

        Assert.AreEqual(0, tracker.Complete(3));
        Assert.AreEqual(1, tracker.Complete(3));
        Assert.AreEqual(0, tracker.Complete(6));
        Assert.AreEqual(1, tracker.Complete(1));
        Assert.AreEqual(0, tracker.Complete(10));
    }

    [TestMethod]
    public void SessionSendOptions_NormalizesDrainBudgetFields()
    {
        var options = new SessionSendOptions(
            MaxQueuedBytes: 0,
            SendChunkBytes: 8,
            MaxDrainBytesPerSignal: 1,
            MaxDrainOperationsPerSignal: 0,
            TransientSendBackoffMs: -1);

        Assert.AreEqual(1, options.NormalizedMaxQueuedBytes);
        Assert.AreEqual(8, options.NormalizedSendChunkBytes);
        Assert.AreEqual(8, options.NormalizedMaxDrainBytesPerSignal);
        Assert.AreEqual(1, options.NormalizedMaxDrainOperationsPerSignal);
        Assert.AreEqual(0, options.NormalizedTransientSendBackoffMs);
    }

    [TestMethod]
    public async Task BaseSession_DoWorkSendBuffers_RecordsDrainYieldWhenBudgetIsExhausted()
    {
        using SocketPair pair = await SocketPair.CreateAsync();
        var telemetry = new ServerTelemetryCollector();
        var session = new TestSession(
            pair.ServerSocket,
            telemetry,
            new SessionSendOptions(
                MaxQueuedBytes: 1024,
                SendChunkBytes: 4,
                MaxDrainBytesPerSignal: 4,
                MaxDrainOperationsPerSignal: 1));

        try
        {
            Assert.IsTrue(session.TrySendBytes(new byte[2]));
            Assert.IsTrue(session.TrySendBytes(new byte[2]));

            ServerTelemetrySnapshot snapshot = await WaitForSnapshotAsync(
                telemetry,
                current => current.SendDrainYieldCount > 0,
                TimeSpan.FromSeconds(3));

            Assert.IsTrue(snapshot.SendDrainYieldCount > 0);
            Assert.IsTrue(snapshot.MaxSendDrainYieldQueuedBytes > 0);
        }
        finally
        {
            session.RequestDisconnect();
            await session.WaitSession();
        }
    }

    [TestMethod]
    public async Task BaseSession_DoWorkSendBuffers_DoesNotDrainOrCompleteOnTransientBackpressure()
    {
        using SocketPair pair = await SocketPair.CreateAsync();
        var telemetry = new ServerTelemetryCollector();
        var allowSuccessfulSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int sendAttempts = 0;
        var session = new TestSession(
            pair.ServerSocket,
            telemetry,
            new SessionSendOptions(
                MaxQueuedBytes: 1024,
                SendChunkBytes: 4,
                TransientSendBackoffMs: 0),
            async (_, sendBuffers, cancellationToken) =>
            {
                if (Interlocked.Increment(ref sendAttempts) == 1)
                {
                    throw new SocketException((int)SocketError.NoBufferSpaceAvailable);
                }

                await allowSuccessfulSend.Task.WaitAsync(cancellationToken);
                return sendBuffers.Length;
            });

        try
        {
            Assert.IsTrue(session.TrySendBytes(new byte[2]));

            ServerTelemetrySnapshot transientSnapshot = await WaitForSnapshotAsync(
                telemetry,
                current => current.SocketErrors == 1 && current.SendBackpressureEvents == 1,
                TimeSpan.FromSeconds(3));

            Assert.AreEqual(1, transientSnapshot.SendRequests);
            Assert.AreEqual(1, transientSnapshot.PendingSendRequests);
            Assert.AreEqual(0, transientSnapshot.SentPackets);
            Assert.AreEqual(0, transientSnapshot.SentBytes);
            Assert.AreEqual(4, transientSnapshot.SendBufferBytes);
            Assert.AreEqual(1, transientSnapshot.SocketErrorCountsByPhase!["send-transient"]);
            Assert.AreEqual(1, transientSnapshot.SocketErrorCountsByCode!["NoBufferSpaceAvailable"]);

            allowSuccessfulSend.SetResult();
            ServerTelemetrySnapshot completedSnapshot = await WaitForSnapshotAsync(
                telemetry,
                current => current.SentPackets == 1 && current.PendingSendRequests == 0,
                TimeSpan.FromSeconds(3));

            Assert.AreEqual(1, completedSnapshot.SentPackets);
            Assert.AreEqual(4, completedSnapshot.SentBytes);
            Assert.AreEqual(0, completedSnapshot.SendBufferBytes);
        }
        finally
        {
            session.RequestDisconnect();
            await session.WaitSession();
        }
    }

    [TestMethod]
    public async Task BaseSession_DoWorkSendBuffers_CompletesOnlyAfterPartialSendFinishes()
    {
        using SocketPair pair = await SocketPair.CreateAsync();
        var telemetry = new ServerTelemetryCollector();
        var allowSecondSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int sendAttempts = 0;
        var session = new TestSession(
            pair.ServerSocket,
            telemetry,
            new SessionSendOptions(MaxQueuedBytes: 1024, SendChunkBytes: 64),
            async (_, sendBuffers, cancellationToken) =>
            {
                if (Interlocked.Increment(ref sendAttempts) == 1)
                {
                    return 2;
                }

                await allowSecondSend.Task.WaitAsync(cancellationToken);
                return sendBuffers.Length;
            });

        try
        {
            Assert.IsTrue(session.TrySendBytes(new byte[4]));

            ServerTelemetrySnapshot partialSnapshot = await WaitForSnapshotAsync(
                telemetry,
                current => current.SentBytes == 2 && current.PendingSendRequests == 1 && current.SendBufferBytes == 4,
                TimeSpan.FromSeconds(3));

            Assert.AreEqual(1, partialSnapshot.SentPackets);
            Assert.AreEqual(2, partialSnapshot.SentBytes);
            Assert.AreEqual(1, partialSnapshot.PendingSendRequests);
            Assert.AreEqual(4, partialSnapshot.SendBufferBytes);

            allowSecondSend.SetResult();
            ServerTelemetrySnapshot completedSnapshot = await WaitForSnapshotAsync(
                telemetry,
                current => current.SentBytes == 6 && current.PendingSendRequests == 0 && current.SendBufferBytes == 0,
                TimeSpan.FromSeconds(3));

            Assert.AreEqual(2, completedSnapshot.SentPackets);
            Assert.AreEqual(6, completedSnapshot.SentBytes);
            Assert.AreEqual(0, completedSnapshot.PendingSendRequests);
            Assert.AreEqual(0, completedSnapshot.SendBufferBytes);
        }
        finally
        {
            session.RequestDisconnect();
            await session.WaitSession();
        }
    }

    [TestMethod]
    public async Task BaseSession_DoWorkSendBuffers_CompletesMultipleAcceptedItemsInFifoOrder()
    {
        // Design Ref: §3.3 — observable wire outcome 검증으로 race 제거.
        // batching 구현이 한 batch에 1개 segment를 보내든 2개를 묶든
        // 누적 wire bytes는 동일하므로, 본 테스트는 그 누적 결과만 검증한다.
        using SocketPair pair = await SocketPair.CreateAsync();
        var telemetry = new ServerTelemetryCollector();
        var observer = new BatchedFifoObserver();
        var allowSecondPacket = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // 첫 packet ([1,2]) 의 wire 길이 = HeaderSize(2) + payload(2) = 4 bytes.
        const int FirstPacketWireLength = 4;

        var session = new TestSession(
            pair.ServerSocket,
            telemetry,
            new SessionSendOptions(MaxQueuedBytes: 1024, SendChunkBytes: 64),
            sendBatchOverride: async (_, sendBuffers, cancellationToken) =>
            {
                // Phase 1: 누적 4 bytes (= 첫 packet 전체)에 도달할 때까지는
                //          첫 segment의 일부/전부만 통과시켜 partial completion semantic 보존.
                if (observer.TotalAcceptedBytes < FirstPacketWireLength)
                {
                    int remaining = FirstPacketWireLength - observer.TotalAcceptedBytes;
                    int take = Math.Min(remaining, sendBuffers[0].Count);
                    observer.OnBatch(sendBuffers, take);
                    return take;
                }

                // Phase 2: 두 번째 packet 처리는 외부 gate 풀린 뒤에만 진행.
                await allowSecondPacket.Task.WaitAsync(cancellationToken);
                int batchTotal = 0;
                for (int i = 0; i < sendBuffers.Count; i++) { batchTotal += sendBuffers[i].Count; }
                observer.OnBatch(sendBuffers, batchTotal);
                return batchTotal;
            });

        try
        {
            Assert.IsTrue(session.TrySendBytes(new byte[] { 1, 2 }));
            Assert.IsTrue(session.TrySendBytes(new byte[] { 9, 8, 7 }));

            // Phase 1 검증: 첫 packet (4 wire bytes) 송신 완료 + 두 번째 packet pending 1.
            ServerTelemetrySnapshot firstSnapshot = await WaitForSnapshotAsync(
                telemetry,
                current => current.SentBytes == FirstPacketWireLength
                           && current.PendingSendRequests == 1,
                TimeSpan.FromSeconds(3));

            Assert.AreEqual(1, firstSnapshot.SentPackets);
            Assert.AreEqual(FirstPacketWireLength, firstSnapshot.SentBytes);
            Assert.AreEqual(1, firstSnapshot.PendingSendRequests);
            // 두 번째 packet wire 길이 = 2 + 3 = 5 bytes
            Assert.AreEqual(5, firstSnapshot.SendBufferBytes);

            // Phase 2: gate 풀고 두 번째 packet 완료까지 기다림.
            allowSecondPacket.SetResult();
            ServerTelemetrySnapshot allCompletedSnapshot = await WaitForSnapshotAsync(
                telemetry,
                current => current.SentBytes == 9 && current.PendingSendRequests == 0,
                TimeSpan.FromSeconds(3));

            Assert.AreEqual(2, allCompletedSnapshot.SentPackets);
            Assert.AreEqual(9, allCompletedSnapshot.SentBytes);
            Assert.AreEqual(0, allCompletedSnapshot.PendingSendRequests);
            Assert.AreEqual(0, allCompletedSnapshot.SendBufferBytes);

            // 본질 검증: wire bytes가 FIFO 순서로 누적되었는가.
            // BasePacket layout = [UInt16 LE PacketSize][payload]
            byte[] expected = BuildExpectedWire(
                new byte[] { 1, 2 },
                new byte[] { 9, 8, 7 });
            CollectionAssert.AreEqual(expected, observer.FlattenedBytes);
            Assert.AreEqual(9, observer.TotalAcceptedBytes);
        }
        finally
        {
            session.RequestDisconnect();
            await session.WaitSession();
        }
    }

    [TestMethod]
    public async Task BaseSession_DoWorkSendBuffers_BatchedSendRespectsChunkLimit()
    {
        // Design Ref: §3.4 — chunk limit는 batch별 max bytes 단언으로 검증.
        // batching 구성(1 segment×N batch / N segment×1 batch)에 의존하지 않음.
        const int ChunkLimit = 6;

        using SocketPair pair = await SocketPair.CreateAsync();
        var telemetry = new ServerTelemetryCollector();
        var observer = new BatchedFifoObserver();
        int batchExceededLimit = 0;

        var session = new TestSession(
            pair.ServerSocket,
            telemetry,
            new SessionSendOptions(
                MaxQueuedBytes: 1024,
                SendChunkBytes: ChunkLimit,
                MaxDrainBytesPerSignal: 64),
            sendBatchOverride: (_, sendBuffers, _) =>
            {
                // Worker가 NormalizedSendChunkBytes로 batch 크기를 제한했는지 검증.
                int batchTotal = 0;
                for (int i = 0; i < sendBuffers.Count; i++) { batchTotal += sendBuffers[i].Count; }
                if (batchTotal > ChunkLimit) { Interlocked.Increment(ref batchExceededLimit); }

                observer.OnBatch(sendBuffers, batchTotal);
                return ValueTask.FromResult(batchTotal);
            });

        try
        {
            Assert.IsTrue(session.TrySendBytes(new byte[] { 1, 2 }));
            Assert.IsTrue(session.TrySendBytes(new byte[] { 9, 8, 7 }));

            // 두 packet 합쳐 wire 9 bytes. ChunkLimit=6이므로 worker는 최소 2 batch로 나눠야 함.
            ServerTelemetrySnapshot completedSnapshot = await WaitForSnapshotAsync(
                telemetry,
                current => current.SentBytes == 9 && current.PendingSendRequests == 0,
                TimeSpan.FromSeconds(3));

            Assert.AreEqual(9, completedSnapshot.SentBytes);
            Assert.AreEqual(0, completedSnapshot.PendingSendRequests);
            Assert.AreEqual(0, completedSnapshot.SendBufferBytes);

            // 본질 1: chunk limit 위반 0.
            Assert.AreEqual(0, batchExceededLimit, "batch exceeded SendChunkBytes limit");

            // 본질 2: ChunkLimit < total wire(9) 이므로 batch는 최소 2회 일어났어야 한다.
            Assert.IsTrue(
                observer.BatchCount >= 2,
                $"expected at least 2 batches with ChunkLimit={ChunkLimit}, got {observer.BatchCount}");

            // 본질 3: wire FIFO 순서.
            byte[] expected = BuildExpectedWire(
                new byte[] { 1, 2 },
                new byte[] { 9, 8, 7 });
            CollectionAssert.AreEqual(expected, observer.FlattenedBytes);
            Assert.AreEqual(9, observer.TotalAcceptedBytes);
        }
        finally
        {
            session.RequestDisconnect();
            await session.WaitSession();
        }
    }

    [TestMethod]
    public async Task BaseSession_TryRequestSendBuffers_RejectsWhenSendQueueIsClosed()
    {
        using SocketPair pair = await SocketPair.CreateAsync();
        var telemetry = new ServerTelemetryCollector();
        var session = new TestSession(
            pair.ServerSocket,
            telemetry,
            new SessionSendOptions(MaxQueuedBytes: 1024, SendChunkBytes: 64));

        session.RequestDisconnect();

        bool queued = session.TrySendBytes(new byte[2]);
        ServerTelemetrySnapshot snapshot = telemetry.CreateSnapshot();

        Assert.IsFalse(queued);
        Assert.AreEqual(0, snapshot.SendRequests);
        Assert.AreEqual(0, snapshot.PendingSendRequests);
        Assert.AreEqual(1, snapshot.SendRejectedRequests);
        Assert.AreEqual(4, snapshot.SendRejectedBytes);
        Assert.AreEqual(0, snapshot.SendBufferBytes);

        await session.WaitSession();
    }

    [TestMethod]
    public async Task BaseSession_RequestDisconnect_WithReason_RecordsDisconnectReason()
    {
        using SocketPair pair = await SocketPair.CreateAsync();
        var telemetry = new ServerTelemetryCollector();
        var session = new TestSession(
            pair.ServerSocket,
            telemetry,
            new SessionSendOptions(MaxQueuedBytes: 1024, SendChunkBytes: 64));

        session.RequestDisconnect(NetworkDisconnectReason.IdleTimeout);
        await session.WaitSession();

        ServerTelemetrySnapshot snapshot = telemetry.CreateSnapshot();
        Assert.AreEqual(1, snapshot.DisconnectedSessions);
        Assert.AreEqual(1, snapshot.DisconnectCountsByReason!["idle-timeout"]);
    }

    [TestMethod]
    public async Task BaseSession_SuccessfulReceive_UpdatesLastReceivedTimestamp()
    {
        using SocketPair pair = await SocketPair.CreateAsync();
        var telemetry = new ServerTelemetryCollector();
        var session = new TestSession(
            pair.ServerSocket,
            telemetry,
            new SessionSendOptions(MaxQueuedBytes: 1024, SendChunkBytes: 64));
        long initialTimestamp = session.LastReceivedTimestamp;

        try
        {
            session.StartReceive();
            await pair.Client.GetStream().WriteAsync(new byte[] { 1 });

            await WaitUntilAsync(
                () => session.LastReceivedTimestamp > initialTimestamp,
                TimeSpan.FromSeconds(3));

            Assert.IsTrue(session.LastReceivedTimestamp > initialTimestamp);
        }
        finally
        {
            session.RequestDisconnect();
            await session.WaitSession();
        }
    }

    [TestMethod]
    public async Task BaseSession_RequestDisconnect_AbandonsPendingSendRequests()
    {
        using SocketPair pair = await SocketPair.CreateAsync();
        var telemetry = new ServerTelemetryCollector();
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new TestSession(
            pair.ServerSocket,
            telemetry,
            new SessionSendOptions(MaxQueuedBytes: 1024, SendChunkBytes: 64),
            async (_, sendBuffers, cancellationToken) =>
            {
                sendStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return sendBuffers.Length;
            });

        try
        {
            Assert.IsTrue(session.TrySendBytes(new byte[2]));
            await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

            ServerTelemetrySnapshot pendingSnapshot = await WaitForSnapshotAsync(
                telemetry,
                current => current.PendingSendRequests == 1 && current.SendBufferBytes == 4,
                TimeSpan.FromSeconds(3));

            Assert.AreEqual(1, pendingSnapshot.PendingSendRequests);
            Assert.AreEqual(0, pendingSnapshot.SendAbandonedRequests);

            session.RequestDisconnect();
            await session.WaitSession();

            ServerTelemetrySnapshot disconnectedSnapshot = telemetry.CreateSnapshot();
            Assert.AreEqual(0, disconnectedSnapshot.PendingSendRequests);
            Assert.AreEqual(1, disconnectedSnapshot.SendAbandonedRequests);
            Assert.AreEqual(0, disconnectedSnapshot.SendBufferBytes);
        }
        finally
        {
            session.RequestDisconnect();
            await session.WaitSession();
        }
    }

    private sealed class TestSession : BaseSession
    {
        private readonly IServerTelemetry _telemetry;
        private readonly Func<Socket, ReadOnlyMemory<byte>, CancellationToken, ValueTask<int>>? _sendOverride;
        private readonly Func<Socket, IList<ArraySegment<byte>>, CancellationToken, ValueTask<int>>? _sendBatchOverride;

        public TestSession(
            Socket socket,
            IServerTelemetry telemetry,
            SessionSendOptions sendOptions,
            Func<Socket, ReadOnlyMemory<byte>, CancellationToken, ValueTask<int>>? sendOverride = null,
            Func<Socket, IList<ArraySegment<byte>>, CancellationToken, ValueTask<int>>? sendBatchOverride = null)
            : base(
                NullLogger<BaseSession>.Instance,
                socket,
                new ArrayPoolCircularBuffers(1024),
                new ArrayPoolCircularBuffers(1024),
                sendOptions)
        {
            _telemetry = telemetry;
            _sendOverride = sendOverride;
            _sendBatchOverride = sendBatchOverride;
        }

        public bool TrySendBytes(byte[] bytes)
        {
            return TryRequestSendBuffers(bytes);
        }

        public void StartReceive()
        {
            RequestReceived();
        }

        protected override void OnNetworkSessionDisconnected(NetworkDisconnectReason reason)
        {
            _telemetry.RecordSessionDisconnected(ToTelemetryReason(reason));
        }

        protected override void OnNetworkSocketError(string phase, SocketError? socketError, Exception? exception)
        {
            _telemetry.RecordSocketError(phase, socketError, exception);
        }

        protected override void OnNetworkPacketReceived(BasePacket packet)
        {
            _telemetry.RecordReceived(packet.PacketSize);
        }

        protected override void OnNetworkBytesSent(int bytes)
        {
            _telemetry.RecordSent(bytes);
        }

        protected override void OnNetworkSendRequested(int bytes, int queuedBytes)
        {
            _telemetry.RecordSendRequested(bytes, queuedBytes);
        }

        protected override void OnNetworkSendCompleted()
        {
            _telemetry.RecordSendCompleted();
        }

        protected override void OnNetworkSendAbandoned(int count)
        {
            _telemetry.RecordSendAbandoned(count);
        }

        protected override void OnNetworkSendBackpressure()
        {
            _telemetry.RecordSendBackpressure();
        }

        protected override void OnNetworkSendRejected(int bytes, int queuedBytes)
        {
            _telemetry.RecordSendRejected(bytes, queuedBytes);
        }

        protected override void OnNetworkSendDrainYield(int queuedBytes)
        {
            _telemetry.RecordSendDrainYield(queuedBytes);
        }

        protected override void OnNetworkSendBufferSample(int queuedBytes)
        {
            _telemetry.RecordSendBufferSample(queuedBytes);
        }

        protected override ValueTask<int> SendSocketAsync(
            Socket socket,
            ReadOnlyMemory<byte> sendBuffers,
            CancellationToken cancellationToken)
        {
            return _sendOverride is null
                ? base.SendSocketAsync(socket, sendBuffers, cancellationToken)
                : _sendOverride(socket, sendBuffers, cancellationToken);
        }

        protected override ValueTask<int> SendSocketAsync(
            Socket socket,
            IList<ArraySegment<byte>> sendBuffers,
            CancellationToken cancellationToken)
        {
            if (_sendBatchOverride is not null)
            {
                return _sendBatchOverride(socket, sendBuffers, cancellationToken);
            }

            if (_sendOverride is not null && sendBuffers.Count == 1)
            {
                ArraySegment<byte> segment = sendBuffers[0];
                return _sendOverride(socket, segment.Array!.AsMemory(segment.Offset, segment.Count), cancellationToken);
            }

            return base.SendSocketAsync(socket, sendBuffers, cancellationToken);
        }

        private static string ToTelemetryReason(NetworkDisconnectReason reason)
        {
            return reason switch
            {
                NetworkDisconnectReason.IdleTimeout => "idle-timeout",
                NetworkDisconnectReason.RemoteClosed => "remote-closed",
                NetworkDisconnectReason.ReceiveSocketError => "receive-socket-error",
                NetworkDisconnectReason.ReceiveRequestError => "receive-request-error",
                NetworkDisconnectReason.SendSocketError => "send-socket-error",
                NetworkDisconnectReason.SendZeroBytes => "send-zero-bytes",
                NetworkDisconnectReason.LocalShutdown => "local-shutdown",
                _ => "unknown"
            };
        }
    }

    private sealed class SocketPair : IDisposable
    {
        private SocketPair(TcpClient client, Socket serverSocket)
        {
            Client = client;
            ServerSocket = serverSocket;
        }

        public TcpClient Client { get; }

        public Socket ServerSocket { get; }

        public static async Task<SocketPair> CreateAsync()
        {
            using var listener = new TcpListener(IPAddress.Loopback, port: 0);
            listener.Start();

            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<Socket> acceptTask = listener.AcceptSocketAsync();
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            Socket serverSocket = await acceptTask;

            return new SocketPair(client, serverSocket);
        }

        public void Dispose()
        {
            Client.Dispose();
            ServerSocket.Dispose();
        }
    }

    private static async Task<ServerTelemetrySnapshot> WaitForSnapshotAsync(
        IServerTelemetry telemetry,
        Func<ServerTelemetrySnapshot, bool> predicate,
        TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        ServerTelemetrySnapshot snapshot = telemetry.CreateSnapshot();

        while (!timeoutSource.IsCancellationRequested)
        {
            snapshot = telemetry.CreateSnapshot();
            if (predicate(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(10, CancellationToken.None);
        }

        return snapshot;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);

        while (!timeoutSource.IsCancellationRequested)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10, CancellationToken.None);
        }

        Assert.Fail("Condition was not met before timeout.");
    }

    private static void AssertSegmentPayload(ArraySegment<byte> segment, byte[] expectedPayload)
    {
        Assert.IsNotNull(segment.Array);
        Assert.AreEqual(expectedPayload.Length + BasePacket.HeaderSize, segment.Count);

        for (int i = 0; i < expectedPayload.Length; i++)
        {
            Assert.AreEqual(expectedPayload[i], segment.Array[segment.Offset + BasePacket.HeaderSize + i]);
        }
    }

    // Design Ref: §3.1 — race-free observer for batched send tests.
    // Worker가 sendBatchOverride에서 호출하면 wire에 실제 나간 bytes만 누적 기록.
    // batching 구현이 1×N batch이든 N×1 batch이든 누적 결과는 동일하므로
    // FIFO 검증은 batching 디테일에 의존하지 않음.
    private sealed class BatchedFifoObserver
    {
        private readonly object _gate = new();
        private readonly List<byte[]> _batches = new();
        private int _totalAcceptedBytes;

        public int BatchCount
        {
            get { lock (_gate) { return _batches.Count; } }
        }

        public int TotalAcceptedBytes
        {
            get { lock (_gate) { return _totalAcceptedBytes; } }
        }

        public byte[] FlattenedBytes
        {
            get
            {
                lock (_gate)
                {
                    int total = 0;
                    for (int i = 0; i < _batches.Count; i++)
                    {
                        total += _batches[i].Length;
                    }
                    var result = new byte[total];
                    int offset = 0;
                    for (int i = 0; i < _batches.Count; i++)
                    {
                        Buffer.BlockCopy(_batches[i], 0, result, offset, _batches[i].Length);
                        offset += _batches[i].Length;
                    }
                    return result;
                }
            }
        }

        // acceptedBytes는 sendBatchOverride return 값과 동일: worker에게 통보할
        // "이 batch에서 실제로 wire에 나간 bytes 수". segments 앞쪽부터 take.
        public void OnBatch(IList<ArraySegment<byte>> sendBuffers, int acceptedBytes)
        {
            if (acceptedBytes <= 0) { return; }
            lock (_gate)
            {
                int captured = 0;
                for (int i = 0; i < sendBuffers.Count && captured < acceptedBytes; i++)
                {
                    ArraySegment<byte> seg = sendBuffers[i];
                    int take = Math.Min(seg.Count, acceptedBytes - captured);
                    if (take <= 0) { break; }
                    var copy = new byte[take];
                    Buffer.BlockCopy(seg.Array!, seg.Offset, copy, 0, take);
                    _batches.Add(copy);
                    captured += take;
                }
                _totalAcceptedBytes += captured;
            }
        }
    }

    // FIFO 검증 헬퍼: BasePacket wire layout([UInt16 LE PacketSize][payload]) 두 packet의
    // 직렬화된 expected bytes 생성. 본 클래스 테스트의 두 곳에서 재사용.
    private static byte[] BuildExpectedWire(params byte[][] payloads)
    {
        int total = 0;
        for (int i = 0; i < payloads.Length; i++)
        {
            total += BasePacket.HeaderSize + payloads[i].Length;
        }
        var wire = new byte[total];
        int offset = 0;
        for (int i = 0; i < payloads.Length; i++)
        {
            int packetSize = BasePacket.HeaderSize + payloads[i].Length;
            // UInt16 little-endian (BasePacket header convention)
            wire[offset + 0] = (byte)(packetSize & 0xFF);
            wire[offset + 1] = (byte)((packetSize >> 8) & 0xFF);
            Buffer.BlockCopy(payloads[i], 0, wire, offset + BasePacket.HeaderSize, payloads[i].Length);
            offset += packetSize;
        }
        return wire;
    }
}
