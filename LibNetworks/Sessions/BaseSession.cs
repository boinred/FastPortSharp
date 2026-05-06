using Google.Protobuf;
using LibCommons;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace LibNetworks.Sessions;

public abstract class BaseSession
{
    private const int SendBufferBackpressureThresholdBytes = 1024 * 1024;
    private const int MaxSendBatchSegments = 16;

    protected ILogger m_Logger;
    private System.Net.Sockets.Socket? m_Socket;

    private readonly byte[] m_ReceivedSocketBuffers = new byte[1024 * 8]; // 8KB

    private readonly SocketAsyncEventArgs m_SocketEventsReceived = new SocketAsyncEventArgs();

    // Session이 Disconnected 되었을 경우 호출 함수
    public Action? OnEventSessionDisconnected;

    private CancellationTokenSource m_CancellationTokenSource = new CancellationTokenSource();

    private readonly LibCommons.IBuffers m_ReceivedBuffers;
    private readonly SessionSendOptions m_SendOptions;
    private readonly Channel<SendQueueItem> m_SendQueue;
    private long m_QueuedSendBytes;
    private readonly Task m_TaskReceivedBuffers;
    private readonly Task m_TaskReceivedPackets;

    private readonly Task m_TaskSendBuffers;

    // Channel<T>로 변경 (BufferBlock<T> 대비 4배 빠르고 메모리 69% 절약)
    private readonly Channel<LibCommons.BasePacket> m_ReceivedPackets;

    private readonly System.Net.EndPoint? m_RemoteEndPoint;

    // Disconnect 중복 호출 방지를 위한 플래그
    private int m_DisconnectRequested = 0;

    private sealed class SendQueueItem
    {
        public SendQueueItem(byte[] buffer)
        {
            Buffer = buffer;
            Length = buffer.Length;
        }

        public byte[] Buffer { get; }

        public int Offset { get; private set; }

        public int Length { get; }

        public int Remaining => Length - Offset;

        public bool IsComplete => Offset >= Length;

        public void Advance(int sentBytes)
        {
            Offset += sentBytes;
        }
    }


    public BaseSession(ILogger<BaseSession> logger, System.Net.Sockets.Socket socket, LibCommons.IBuffers receivedBuffers, LibCommons.IBuffers sendbuffers)
        : this(logger, socket, receivedBuffers, sendbuffers, SessionSendOptions.Default)
    {
    }

    // BaseSession dependency: network engine 전용, telemetry protected hook 분리
    public BaseSession(
        ILogger<BaseSession> logger,
        System.Net.Sockets.Socket socket,
        LibCommons.IBuffers receivedBuffers,
        LibCommons.IBuffers sendbuffers,
        SessionSendOptions? sendOptions)
    {
        // Session engine state: logger, socket, options
        m_Logger = logger;
        m_Socket = socket;
        m_SendOptions = sendOptions ?? SessionSendOptions.Default;

        // 10초마다 KeepAlive 신호를 보내도록 설정 (Windows에서는 레지스트리 수정 필요)
        m_Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

        // 소켓을 닫을 때, 보내지 않은 데이터가 있으면 1초간 대기 후 닫음
        m_Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(true, 1));

        // Nagle 알고리즘 비활성화
        m_Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

        m_ReceivedBuffers = receivedBuffers;
        m_SendQueue = Channel.CreateUnbounded<SendQueueItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        m_SocketEventsReceived.SetBuffer(m_ReceivedSocketBuffers, 0, m_ReceivedSocketBuffers.Length);
        m_SocketEventsReceived.Completed += OnSocketEventsReceivedCompleted;
        m_SocketEventsReceived.UserToken = this;

        // Bounded Channel 생성 (용량 제한으로 메모리 사용 제어)
        m_ReceivedPackets = Channel.CreateBounded<LibCommons.BasePacket>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        m_TaskReceivedPackets = Task.Run(async () => await DoWorkReceivedPackets(m_CancellationTokenSource.Token));
        m_TaskReceivedBuffers = Task.Run(async () => await DoWorkReceivedBuffers(m_CancellationTokenSource.Token));
        m_TaskSendBuffers = Task.Run(async () => await DoWorkSendBuffers(m_CancellationTokenSource.Token));

        OnEventSessionDisconnected += OnDisconnected;

        m_RemoteEndPoint = m_Socket.RemoteEndPoint!;
    }

    /// <summary>
    /// 세션이 연결 해제되었는지 여부
    /// </summary>
    public bool IsDisconnected => m_DisconnectRequested == 1;

    public string GetSessionAddress() => m_RemoteEndPoint?.ToString() ?? " Unknown";


    protected virtual void OnReceived(BasePacket basePacket) { }

    protected virtual void OnSent() { }

    protected virtual void OnDisconnected()
    {
        OnEventSessionDisconnected -= OnDisconnected;
    }

    // Session disconnect hook: subclass 외부 telemetry 연결점
    protected virtual void OnNetworkSessionDisconnected()
    {
    }

    // Socket error hook: error code 및 exception context
    protected virtual void OnNetworkSocketError(SocketError? socketError, Exception? exception)
    {
    }

    // Packet parsed hook: packet size 기반 counter 갱신점
    protected virtual void OnNetworkPacketReceived(BasePacket packet)
    {
    }

    // Socket sent byte hook: send completion과 분리된 throughput 지표
    protected virtual void OnNetworkBytesSent(int bytes)
    {
    }

    // Send request hook: 요청 byte 및 enqueue 후 queue depth
    protected virtual void OnNetworkSendRequested(int bytes, int queuedBytes)
    {
    }

    // Send request completed hook: request 단위 pending count 감소점
    protected virtual void OnNetworkSendCompleted()
    {
    }

    // Send backpressure hook: queue limit 또는 transient socket pressure
    protected virtual void OnNetworkSendBackpressure()
    {
    }

    // Send rejection hook: drop byte 및 당시 queue depth
    protected virtual void OnNetworkSendRejected(int bytes, int queuedBytes)
    {
    }

    // Drain yield hook: send loop budget 소진 시 queue depth
    protected virtual void OnNetworkSendDrainYield(int queuedBytes)
    {
    }

    // Send buffer sample hook: 현재 queued byte gauge 갱신점
    protected virtual void OnNetworkSendBufferSample(int queuedBytes)
    {
    }

    public async Task WaitSession()
    {
        // 소켓 및 패킷 처리 대기
        await Task.WhenAll(m_TaskReceivedPackets, m_TaskReceivedBuffers, m_TaskSendBuffers);
    }


    private void OnSocketEventsReceivedCompleted(object? sender, SocketAsyncEventArgs e)
    {
        if (e.SocketError == SocketError.IOPending)
        {
            m_Logger.LogDebug($"BaseSession, OnSocketEventsReceivedCompleted, Socket IOPeding.");
            return;
        }

        if (e.BytesTransferred <= 0)
        {
            m_Logger.LogInformation($"BaseSession, OnSocketEventsReceivedCompleted, Disconnected. BytesTransferred is zero.");
            RequestDisconnect();

            return;
        }

        if (e.SocketError != SocketError.Success)
        {
            // Receive completion socket error: disconnect 전 hook 노출
            OnNetworkSocketError(e.SocketError, null);
            m_Logger.LogInformation($"BaseSession, OnSocketEventsReceivedCompleted, Disconnected. SocketError : {e.SocketError}");

            RequestDisconnect();

            return;
        }

        var buffer = e.Buffer;
        if (null == buffer)
        {
            m_Logger.LogInformation($"BaseSession, OnSocketEventsReceivedCompleted, Disconnected. Buffer is null.");

            RequestDisconnect();

            return;
        }

        // Process the received data
        var wroteSize = m_ReceivedBuffers.Write(buffer, e.Offset, e.BytesTransferred);

        m_Logger.LogDebug($"BaseSession, OnSocketEventsReceivedCompleted, Received {wroteSize} bytes from {GetSessionAddress()}");

        // 

        RequestReceived();
    }

    public void RequestDisconnect()
    {
        // Interlocked.CompareExchange: 원자적 중복 호출 방지
        // 0에서 1로 변경 시도, 이미 1이면 다른 스레드가 먼저 호출한 것
        if (Interlocked.CompareExchange(ref m_DisconnectRequested, 1, 0) != 0)
        {
            m_Logger.LogDebug("BaseSession, RequestDisconnect, Already disconnecting or disconnected.");
            return;
        }

        m_Logger.LogInformation($"BaseSession, RequestDisconnect.");
        // Disconnect 처리: engine 담당, 관측 hook 분리
        OnNetworkSessionDisconnected();

        // CancellationToken 취소
        try
        {
            m_CancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 이미 Dispose된 경우 무시
        }

        // 소켓 종료
        var socket = Interlocked.Exchange(ref m_Socket, null);
        if (socket != null)
        {
            try
            {
                if (socket.Connected)
                {
                    socket.Shutdown(SocketShutdown.Both);
                }
            }
            catch (SocketException ex)
            {
                m_Logger.LogDebug($"BaseSession, RequestDisconnect, Socket Shutdown Exception : {ex.Message}");
            }
            catch (ObjectDisposedException)
            {
                // 이미 Dispose된 경우 무시
            }

            try
            {
                socket.Close();
            }
            catch (Exception ex)
            {
                m_Logger.LogError($"BaseSession, RequestDisconnect, Socket Close Exception : {ex}");
            }
        }

        // Channel 완료 (Writer 닫기)
        m_SendQueue.Writer.TryComplete();
        m_ReceivedPackets.Writer.TryComplete();

        // 이벤트 호출 (한 번만 호출됨)
        OnEventSessionDisconnected?.Invoke();
    }

    protected void RequestReceived()
    {
        // 이미 연결 해제 중이면 무시
        if (IsDisconnected)
        {
            return;
        }

        var socket = m_Socket;
        if (socket == null || !socket.Connected)
        {
            return;
        }

        m_Logger.LogDebug($"BaseSession, RequestReceived");
        try
        {
            if (!socket.ReceiveAsync(m_SocketEventsReceived))
            {
                // If ReceiveAsync returns false, we handle the receive operation immediately
                OnSocketEventsReceivedCompleted(this, m_SocketEventsReceived);
            }
        }
        catch (ObjectDisposedException)
        {
            // 소켓이 이미 Dispose된 경우
            RequestDisconnect();
        }
        catch (SocketException ex)
        {
            m_Logger.LogDebug($"BaseSession, RequestReceived, SocketException : {ex.Message}");
            // Receive 요청 실패: socket error hook 기반 외부 관측
            OnNetworkSocketError(ex.SocketErrorCode, ex);
            RequestDisconnect();
        }
    }

    protected void RequestSendBuffers(ReadOnlySpan<byte> buffers)
    {
        _ = TryRequestSendBuffers(buffers);
    }

    protected bool TryRequestSendBuffers(ReadOnlySpan<byte> buffers)
    {
        if (buffers.Length <= 0)
        {
            m_Logger.LogError($"BaseSession, RequestSendBuffers, Buffers is zero.");
            return false;
        }

        int buffersSize = buffers.Length + BasePacket.HeaderSize;
        if (buffersSize > ushort.MaxValue)
        {
            m_Logger.LogError("BaseSession, RequestSendBuffers, Packet is too large. Buffer Length:{BufferLength}", buffers.Length);
            return false;
        }

        if (!TryReserveQueuedSendBytes(buffersSize, out int queuedBefore, out int queuedAfter))
        {
            // Queue byte limit 초과: backpressure 및 rejection 동시 관측
            OnNetworkSendBackpressure();
            OnNetworkSendRejected(buffersSize, queuedBefore);
            m_Logger.LogDebug(
                "BaseSession, RequestSendBuffers, Send rejected. Buffer Length:{BufferLength}, QueuedBytes:{QueuedBytes}, MaxQueuedBytes:{MaxQueuedBytes}",
                buffersSize,
                queuedBefore,
                m_SendOptions.NormalizedMaxQueuedBytes);
            return false;
        }

        // List로 바꾸는 것도 고려
        byte[] sendBuffers = new byte[buffersSize];

        // Insert Packet Size at the beginning of the buffer
        BitConverter.GetBytes(buffersSize).AsSpan().CopyTo(sendBuffers);
        buffers.CopyTo(sendBuffers.AsSpan(BasePacket.HeaderSize));


        m_Logger.LogDebug($"BaseSession, RequestSendBuffers, Buffer Length : {sendBuffers.Length}");

        var sendItem = new SendQueueItem(sendBuffers);
        if (!m_SendQueue.Writer.TryWrite(sendItem))
        {
            int queuedBytesAfterRollback = ReleaseQueuedSendBytes(buffersSize);
            // Queue writer closed: 예약 byte rollback 후 rejection 기록
            OnNetworkSendRejected(buffersSize, queuedBytesAfterRollback);
            m_Logger.LogDebug(
                "BaseSession, RequestSendBuffers, Send rejected because queue is closed. Buffer Length:{BufferLength}, QueuedBytes:{QueuedBytes}",
                buffersSize,
                queuedBytesAfterRollback);
            return false;
        }

        // Queue enqueue 성공: request counter 및 pending gauge 기준점
        OnNetworkSendRequested(buffersSize, queuedAfter);
        if (queuedAfter > SendBufferBackpressureThresholdBytes)
        {
            // High-watermark 초과: enqueue 성공과 분리된 backpressure 관측
            OnNetworkSendBackpressure();
        }

        return true;
    }

    protected void RequestSendString(string message)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message);

        RequestSendBuffers(bytes);
    }



    protected void RequestSendMessage<T>(int packetId, Google.Protobuf.IMessage<T> message) where T : IMessage<T>
    {
        _ = TryRequestSendMessage(packetId, message);
    }

    protected bool TryRequestSendMessage<T>(int packetId, Google.Protobuf.IMessage<T> message) where T : IMessage<T>
    {

        Span<byte> packetIdBuffers = BitConverter.GetBytes(packetId);
        ReadOnlySpan<byte> messageBuffers = message.ToByteArray();

        byte[] packetBuffers = new byte[packetIdBuffers.Length + messageBuffers.Length];

        packetIdBuffers.CopyTo(packetBuffers);
        messageBuffers.CopyTo(packetBuffers.AsSpan(packetIdBuffers.Length));

        return TryRequestSendBuffers(packetBuffers);
    }


    /// <summary>
    /// Channel에서 패킷을 읽어 처리하는 작업
    /// </summary>
    private async Task DoWorkReceivedPackets(CancellationToken cancellationToken)
    {
        try
        {
            // Channel이 완료될 때까지 패킷 처리
            await foreach (var packet in m_ReceivedPackets.Reader.ReadAllAsync(cancellationToken))
            {
                OnReceived(packet);
            }
        }
        catch (OperationCanceledException)
        {
            // 정상적인 취소
        }
        catch (ChannelClosedException)
        {
            // Channel이 닫힌 경우
        }
    }

    /// <summary>
    /// 버퍼에서 패킷을 파싱하여 Channel에 전달하는 작업
    /// </summary>
    private async Task DoWorkReceivedBuffers(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (m_ReceivedBuffers.CanReadSize < LibCommons.BasePacket.HeaderSize)
                {
                    await Task.Delay(1, cancellationToken); // CPU 사용량 감소
                    continue;
                }

                if (!m_ReceivedBuffers.TryGetBasePackets(out List<LibCommons.BasePacket> basePackets))
                {
                    await Task.Delay(1, cancellationToken);
                    continue;
                }

                foreach (var basePacket in basePackets)
                {
                    m_Logger.LogDebug($"BaseSession, DoWorkReceived, Received Packet Size : {basePacket.PacketSize}, Data Size : {basePacket.DataSize}");
                    // Packet complete: received packet/bytes hook 호출 기준
                    OnNetworkPacketReceived(basePacket);

                    // Channel에 패킷 전송
                    await m_ReceivedPackets.Writer.WriteAsync(basePacket, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 정상적인 취소
        }
        catch (ChannelClosedException)
        {
            // Channel이 닫힌 경우
        }
    }

    /// <summary>
    /// 송신 버퍼의 데이터를 소켓으로 전송하는 작업
    /// </summary>
    private async Task DoWorkSendBuffers(CancellationToken cancellationToken)
    {
        var pendingSendItems = new Queue<SendQueueItem>();
        var sendSegments = new List<ArraySegment<byte>>(MaxSendBatchSegments);

        try
        {
            while (await m_SendQueue.Reader.WaitToReadAsync(cancellationToken))
            {
                int drainedBytesThisCycle = 0;
                int sendOperationsThisCycle = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (pendingSendItems.Count == 0)
                    {
                        if (!m_SendQueue.Reader.TryRead(out SendQueueItem? sendItem))
                        {
                            break;
                        }

                        pendingSendItems.Enqueue(sendItem);
                    }

                    if (IsSendDrainBudgetExhausted(drainedBytesThisCycle, sendOperationsThisCycle))
                    {
                        RecordSendDrainYield();
                        await Task.Yield();
                        drainedBytesThisCycle = 0;
                        sendOperationsThisCycle = 0;
                    }

                    var socket = m_Socket;
                    if (socket == null || !socket.Connected)
                    {
                        return;
                    }

                    int queuedBytes = GetQueuedSendBytesSnapshot();
                    // Send 직전 queue depth: 외부 gauge 최신화 sample
                    OnNetworkSendBufferSample(queuedBytes);

                    int remainingBudgetBytes = m_SendOptions.NormalizedMaxDrainBytesPerSignal - drainedBytesThisCycle;
                    int maxSendBytes = Math.Min(m_SendOptions.NormalizedSendChunkBytes, remainingBudgetBytes);
                    int batchBytes = BuildSendSegments(pendingSendItems, maxSendBytes, sendSegments);
                    if (batchBytes <= 0)
                    {
                        continue;
                    }

                    m_Logger.LogDebug($"BaseSession, DoWorkSendBuffers, Buffer Length : {batchBytes}, Segments : {sendSegments.Count}");

                    int sentSize;
                    try
                    {
                        sentSize = await SendSocketAsync(socket, sendSegments, cancellationToken);
                    }
                    catch (SocketException ex) when (IsTransientSendBackpressure(ex.SocketErrorCode))
                    {
                        // Transient send pressure: socket error 및 backpressure 동시 관측
                        OnNetworkSocketError(ex.SocketErrorCode, ex);
                        OnNetworkSendBackpressure();
                        m_Logger.LogDebug($"BaseSession, DoWorkSendBuffers, Transient SocketException : {ex.SocketErrorCode}, {ex.Message}");
                        await WaitTransientSendBackoffAsync(cancellationToken);
                        continue;
                    }
                    catch (SocketException ex)
                    {
                        // Non-transient send socket error: disconnect 전 hook 노출
                        OnNetworkSocketError(ex.SocketErrorCode, ex);
                        m_Logger.LogDebug($"BaseSession, DoWorkSendBuffers, SocketException : {ex.Message}");
                        RequestDisconnect();
                        return;
                    }

                    if (sentSize <= 0)
                    {
                        m_Logger.LogInformation("BaseSession, DoWorkSendBuffers, Disconnected. Sent size is zero.");
                        RequestDisconnect();
                        return;
                    }

                    int advancedSize = Math.Min(sentSize, batchBytes);
                    int completedItems = AdvanceSendItems(pendingSendItems, advancedSize);
                    int queuedBytesAfterSend = ReleaseQueuedSendBytes(advancedSize);
                    // Sent throughput: 실제 socket 전달 byte 기준
                    OnNetworkBytesSent(advancedSize);
                    // Drain 후 queue depth: buffer gauge 하향 sample
                    OnNetworkSendBufferSample(queuedBytesAfterSend);
                    drainedBytesThisCycle += advancedSize;
                    sendOperationsThisCycle++;

                    for (int i = 0; i < completedItems; i++)
                    {
                        // Pending request completion: 완전 drain request 수 기준
                        OnNetworkSendCompleted();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 정상적인 취소
        }
        catch (ObjectDisposedException)
        {
            // 소켓이 이미 Dispose된 경우
        }
        catch (SocketException ex)
        {
            m_Logger.LogDebug($"BaseSession, DoWorkSendBuffers, SocketException : {ex.Message}");
            // Send worker 외부 socket error: 동일 hook 집계
            OnNetworkSocketError(ex.SocketErrorCode, ex);
            RequestDisconnect();
        }
    }

    private static bool IsTransientSendBackpressure(SocketError socketError)
    {
        return socketError == SocketError.NoBufferSpaceAvailable
            || socketError == SocketError.WouldBlock;
    }

    protected virtual ValueTask<int> SendSocketAsync(
        Socket socket,
        ReadOnlyMemory<byte> sendBuffers,
        CancellationToken cancellationToken)
    {
        return socket.SendAsync(sendBuffers, SocketFlags.None, cancellationToken);
    }

    protected virtual async ValueTask<int> SendSocketAsync(
        Socket socket,
        IList<ArraySegment<byte>> sendBuffers,
        CancellationToken cancellationToken)
    {
        if (sendBuffers.Count == 1)
        {
            ArraySegment<byte> segment = sendBuffers[0];
            return await SendSocketAsync(socket, segment.Array!.AsMemory(segment.Offset, segment.Count), cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        int totalBytes = 0;
        for (int i = 0; i < sendBuffers.Count; i++)
        {
            totalBytes += sendBuffers[i].Count;
        }

        byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(totalBytes);
        try
        {
            int offset = 0;
            for (int i = 0; i < sendBuffers.Count; i++)
            {
                ArraySegment<byte> segment = sendBuffers[i];
                Buffer.BlockCopy(segment.Array!, segment.Offset, rentedBuffer, offset, segment.Count);
                offset += segment.Count;
            }

            return await SendSocketAsync(socket, rentedBuffer.AsMemory(0, totalBytes), cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    private bool IsSendDrainBudgetExhausted(int drainedBytesThisWake, int sendOperationsThisWake)
    {
        return drainedBytesThisWake >= m_SendOptions.NormalizedMaxDrainBytesPerSignal
            || sendOperationsThisWake >= m_SendOptions.NormalizedMaxDrainOperationsPerSignal;
    }

    private int BuildSendSegments(
        Queue<SendQueueItem> pendingSendItems,
        int maxBytes,
        List<ArraySegment<byte>> sendSegments)
    {
        sendSegments.Clear();
        int totalBytes = 0;

        foreach (SendQueueItem item in pendingSendItems)
        {
            if (!TryAppendSendSegment(item, maxBytes, sendSegments, ref totalBytes))
            {
                return totalBytes;
            }
        }

        while (totalBytes < maxBytes
            && sendSegments.Count < MaxSendBatchSegments
            && m_SendQueue.Reader.TryRead(out SendQueueItem? item))
        {
            pendingSendItems.Enqueue(item);
            if (!TryAppendSendSegment(item, maxBytes, sendSegments, ref totalBytes))
            {
                break;
            }
        }

        return totalBytes;
    }

    private static bool TryAppendSendSegment(
        SendQueueItem item,
        int maxBytes,
        List<ArraySegment<byte>> sendSegments,
        ref int totalBytes)
    {
        int remainingBatchBytes = maxBytes - totalBytes;
        if (remainingBatchBytes <= 0 || sendSegments.Count >= MaxSendBatchSegments)
        {
            return false;
        }

        int segmentBytes = Math.Min(item.Remaining, remainingBatchBytes);
        if (segmentBytes <= 0)
        {
            return true;
        }

        sendSegments.Add(new ArraySegment<byte>(item.Buffer, item.Offset, segmentBytes));
        totalBytes += segmentBytes;

        return segmentBytes == item.Remaining
            && totalBytes < maxBytes
            && sendSegments.Count < MaxSendBatchSegments;
    }

    private static int AdvanceSendItems(Queue<SendQueueItem> pendingSendItems, int sentBytes)
    {
        int remainingBytes = sentBytes;
        int completedItems = 0;

        while (remainingBytes > 0 && pendingSendItems.Count > 0)
        {
            SendQueueItem item = pendingSendItems.Peek();
            int advancedBytes = Math.Min(remainingBytes, item.Remaining);
            item.Advance(advancedBytes);
            remainingBytes -= advancedBytes;

            if (item.IsComplete)
            {
                pendingSendItems.Dequeue();
                completedItems++;
            }
        }

        return completedItems;
    }

    private void RecordSendDrainYield()
    {
        int queuedBytes = GetQueuedSendBytesSnapshot();
        if (queuedBytes <= 0)
        {
            return;
        }

        // Drain budget yield: 당시 queue depth
        OnNetworkSendDrainYield(queuedBytes);
    }

    private async Task WaitTransientSendBackoffAsync(CancellationToken cancellationToken)
    {
        int backoffMs = m_SendOptions.NormalizedTransientSendBackoffMs;
        if (backoffMs <= 0)
        {
            await Task.Yield();
            return;
        }

        await Task.Delay(backoffMs, cancellationToken);
    }

    private bool TryReserveQueuedSendBytes(int bytes, out int queuedBefore, out int queuedAfter)
    {
        while (true)
        {
            long current = Volatile.Read(ref m_QueuedSendBytes);
            long next = current + bytes;
            queuedBefore = ToTelemetryQueuedBytes(current);
            queuedAfter = ToTelemetryQueuedBytes(next);

            if (next > m_SendOptions.NormalizedMaxQueuedBytes)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref m_QueuedSendBytes, next, current) == current)
            {
                return true;
            }
        }
    }

    private int ReleaseQueuedSendBytes(int bytes)
    {
        while (true)
        {
            long current = Volatile.Read(ref m_QueuedSendBytes);
            long next = Math.Max(0, current - bytes);

            if (Interlocked.CompareExchange(ref m_QueuedSendBytes, next, current) == current)
            {
                return ToTelemetryQueuedBytes(next);
            }
        }
    }

    private int GetQueuedSendBytesSnapshot()
    {
        return ToTelemetryQueuedBytes(Volatile.Read(ref m_QueuedSendBytes));
    }

    private static int ToTelemetryQueuedBytes(long queuedBytes)
    {
        return (int)Math.Min(int.MaxValue, Math.Max(0, queuedBytes));
    }
}
