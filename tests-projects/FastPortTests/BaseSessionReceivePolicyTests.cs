using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LibCommons;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FastPortTests;

// 목적: BaseSession 수신 경로의 방어 정책(malformed header, 수신 버퍼 상한, handler 예외 격리) 검증
[TestClass]
public sealed class BaseSessionReceivePolicyTests
{
    // 설정: 비동기 disconnect/worker 종료 대기 상한
    private static readonly TimeSpan s_Timeout = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task BaseSession_InvalidPacketHeaderAfterValidPacket_Disconnects()
    {
        using SocketPair pair = await SocketPair.CreateAsync();
        var session = new ReceiveTestSession(pair.ServerSocket);
        session.StartReceive();

        // 입력: 정상 packet 1개 + packet size 0인 malformed header
        byte[] wire = [.. BuildPacket([0x41, 0x42]), 0x00, 0x00, 0x7F, 0x7F];
        await pair.Client.GetStream().WriteAsync(wire);

        await WaitSessionAsync(session);

        Assert.AreEqual(NetworkDisconnectReason.InvalidPacketHeader, session.DisconnectReason);
        // 상태: 선행 정상 packet은 disconnect cancellation과 경합하므로 전달 여부는 비결정적
        Assert.IsTrue(session.ReceivedPayloads.Count <= 1);
        // 검증: malformed 구간이 packet으로 잘못 전달되지 않음
        foreach (byte[] payload in session.ReceivedPayloads)
        {
            CollectionAssert.AreEqual(new byte[] { 0x41, 0x42 }, payload);
        }
    }

    [TestMethod]
    public async Task BaseSession_InvalidPacketHeaderOnly_DisconnectsWithoutDelivery()
    {
        using SocketPair pair = await SocketPair.CreateAsync();
        var session = new ReceiveTestSession(pair.ServerSocket);
        session.StartReceive();

        // 입력: packet size 1 (header 크기 미만) — 기존 구현에서는 parser가 영구 정체
        await pair.Client.GetStream().WriteAsync(new byte[] { 0x01, 0x00, 0xFF });

        await WaitSessionAsync(session);

        Assert.AreEqual(NetworkDisconnectReason.InvalidPacketHeader, session.DisconnectReason);
        Assert.AreEqual(0, session.ReceivedPayloads.Count);
    }

    [TestMethod]
    public async Task BaseSession_PartialPacketWithinLimit_IsDeliveredWithoutDisconnect()
    {
        using SocketPair pair = await SocketPair.CreateAsync();
        var session = new ReceiveTestSession(pair.ServerSocket);
        session.StartReceive();

        try
        {
            // 입력: header와 payload 일부를 먼저 보내고, 나머지를 나중에 보내 partial 경로 확인
            byte[] payload = Enumerable.Range(0, 3000).Select(i => (byte)i).ToArray();
            byte[] packet = BuildPacket(payload);
            await pair.Client.GetStream().WriteAsync(packet.AsMemory(0, 1000));
            await Task.Delay(100);
            Assert.IsFalse(session.IsDisconnected);

            await pair.Client.GetStream().WriteAsync(packet.AsMemory(1000));
            await WaitUntilAsync(() => session.ReceivedPayloads.Count == 1, s_Timeout);

            Assert.IsFalse(session.IsDisconnected);
            CollectionAssert.AreEqual(payload, session.ReceivedPayloads.Single());
        }
        finally
        {
            session.RequestDisconnect();
            await session.WaitSession();
        }
    }

    [TestMethod]
    public async Task BaseSession_OneByteFragmentedStream_DeliversAllPacketsWithoutDisconnect()
    {
        using SocketPair pair = await SocketPair.CreateAsync();
        // 설정: Nagle 비활성화로 1 byte write가 개별 TCP segment로 전송되도록 유도
        pair.Client.NoDelay = true;
        var session = new ReceiveTestSession(pair.ServerSocket);
        session.StartReceive();

        try
        {
            // 입력: header 2 byte까지 포함해 연속 packet 2개를 1 byte 단위로 쪼개 전송
            byte[] firstPayload = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();
            byte[] secondPayload = [0x10, 0x20, 0x30];
            byte[] wire = [.. BuildPacket(firstPayload), .. BuildPacket(secondPayload)];
            NetworkStream stream = pair.Client.GetStream();
            foreach (byte value in wire)
            {
                await stream.WriteAsync(new[] { value });
                // 흐름: 짧은 간격으로 server receive completion이 1 byte 단위로도 발생하게 함
                await Task.Delay(1);
            }

            await WaitUntilAsync(() => session.ReceivedPayloads.Count == 2, s_Timeout);

            Assert.IsFalse(session.IsDisconnected);
            byte[][] payloads = session.ReceivedPayloads.ToArray();
            CollectionAssert.AreEqual(firstPayload, payloads[0]);
            CollectionAssert.AreEqual(secondPayload, payloads[1]);
        }
        finally
        {
            session.RequestDisconnect();
            await session.WaitSession();
        }
    }

    [TestMethod]
    public async Task BaseSession_ReceiveBufferOverLimit_DisconnectsWithOverflowReason()
    {
        using SocketPair pair = await SocketPair.CreateAsync();
        var session = new ReceiveTestSession(pair.ServerSocket, maxReceiveBufferedBytes: 16 * 1024);
        session.StartReceive();

        // 입력: 60000 byte packet을 선언하고 상한(16KB)을 넘는 20000 byte만 전송해 미완성 상태 유지
        byte[] header = new byte[BasePacket.HeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(header, 60000);
        await pair.Client.GetStream().WriteAsync(header);
        await pair.Client.GetStream().WriteAsync(new byte[20000]);

        await WaitSessionAsync(session);

        Assert.AreEqual(NetworkDisconnectReason.ReceiveBufferOverflow, session.DisconnectReason);
        Assert.AreEqual(0, session.ReceivedPayloads.Count);
    }

    [TestMethod]
    public async Task BaseSession_PacketHandlerThrows_DisconnectsAndWorkersComplete()
    {
        using SocketPair pair = await SocketPair.CreateAsync();
        var session = new ReceiveTestSession(
            pair.ServerSocket,
            onReceived: _ => throw new InvalidOperationException("handler failure"));
        session.StartReceive();

        await pair.Client.GetStream().WriteAsync(BuildPacket([0x01]));

        // 검증: handler 예외가 worker task를 fault시키지 않고 세션 종료로 귀결
        await WaitSessionAsync(session);

        Assert.AreEqual(NetworkDisconnectReason.PacketHandlerError, session.DisconnectReason);
    }

    // 용도: 수신 경로 정책 관측용 최소 BaseSession 구현
    private sealed class ReceiveTestSession : BaseSession
    {
        // 설정: null이면 BaseSession 기본 수신 버퍼 상한 사용
        private readonly int? m_MaxReceiveBufferedBytes;
        // 용도: 수신 packet별 추가 동작(예: 예외 발생) 주입
        private readonly Action<BasePacket>? m_OnReceived;

        public ReceiveTestSession(
            Socket socket,
            int? maxReceiveBufferedBytes = null,
            Action<BasePacket>? onReceived = null)
            : base(
                NullLogger<BaseSession>.Instance,
                socket,
                new ArrayPoolCircularBuffers(1024),
                new ArrayPoolCircularBuffers(1024),
                SessionSendOptions.Default)
        {
            m_MaxReceiveBufferedBytes = maxReceiveBufferedBytes;
            m_OnReceived = onReceived;
        }

        // 상태: handler에 전달된 payload 사본 목록
        public ConcurrentQueue<byte[]> ReceivedPayloads { get; } = new();

        // 상태: 마지막으로 관측된 disconnect 원인
        public NetworkDisconnectReason? DisconnectReason { get; private set; }

        protected override int MaxReceiveBufferedBytes => m_MaxReceiveBufferedBytes ?? base.MaxReceiveBufferedBytes;

        // 용도: protected RequestReceived를 테스트에서 시작
        public void StartReceive() => RequestReceived();

        protected override void OnReceived(BasePacket basePacket)
        {
            ReceivedPayloads.Enqueue(basePacket.Data.ToArray());
            m_OnReceived?.Invoke(basePacket);
        }

        protected override void OnNetworkSessionDisconnected(NetworkDisconnectReason reason)
        {
            DisconnectReason = reason;
        }
    }

    // 용도: loopback TCP 연결 한 쌍 (client ↔ accepted server socket)
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

    // 형식: BaseSession 송신 규약과 동일한 [UInt16 LE 전체 길이][payload]
    private static byte[] BuildPacket(byte[] payload)
    {
        byte[] packet = new byte[BasePacket.HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(packet, (ushort)packet.Length);
        payload.CopyTo(packet.AsSpan(BasePacket.HeaderSize));
        return packet;
    }

    // 검증: 세션이 스스로 disconnect되고 모든 worker task가 제한 시간 내 정상 종료
    private static async Task WaitSessionAsync(BaseSession session)
    {
        Task waitTask = session.WaitSession();
        Task completed = await Task.WhenAny(waitTask, Task.Delay(s_Timeout));

        Assert.AreSame(waitTask, completed, "Session workers did not complete before timeout.");
        await waitTask;
        Assert.IsTrue(session.IsDisconnected);
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
}
