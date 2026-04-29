using System.Net;
using System.Net.Sockets;
using LibCommons;
using LibNetworks.Sessions;
using LibNetworks.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibCommonTest;

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

    private sealed class TestSession : BaseSession
    {
        private readonly Func<Socket, ReadOnlyMemory<byte>, CancellationToken, ValueTask<int>>? _sendOverride;

        public TestSession(
            Socket socket,
            IServerTelemetry telemetry,
            SessionSendOptions sendOptions,
            Func<Socket, ReadOnlyMemory<byte>, CancellationToken, ValueTask<int>>? sendOverride = null)
            : base(
                NullLogger<BaseSession>.Instance,
                socket,
                new ArrayPoolCircularBuffers(1024),
                new ArrayPoolCircularBuffers(1024),
                telemetry,
                sendOptions)
        {
            _sendOverride = sendOverride;
        }

        public bool TrySendBytes(byte[] bytes)
        {
            return TryRequestSendBuffers(bytes);
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
}
