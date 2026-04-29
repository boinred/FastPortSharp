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

    private sealed class TestSession : BaseSession
    {
        public TestSession(Socket socket, IServerTelemetry telemetry, SessionSendOptions sendOptions)
            : base(
                NullLogger<BaseSession>.Instance,
                socket,
                new ArrayPoolCircularBuffers(1024),
                new ArrayPoolCircularBuffers(1024),
                telemetry,
                sendOptions)
        {
        }

        public bool TrySendBytes(byte[] bytes)
        {
            return TryRequestSendBuffers(bytes);
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
}
