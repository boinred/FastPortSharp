using Google.Protobuf;
using LibCommons;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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
    // Signal: recv callback이 buffer write 이후 parser task를 즉시 깨우는 용도
    private readonly SemaphoreSlim m_ReceivedBufferSignal = new(0);
    // Version: stale signal 소비 중 새 receive 여부를 구분하는 변경 카운터
    private long m_ReceivedBufferVersion;
    // 계측: 마지막 ReceiveAsync 요청 시작 timestamp
    private long m_ReceiveRequestedTimestamp;
    // 계측: 마지막 receive buffer write 완료 timestamp
    private long m_LastReceiveBufferWriteTimestamp;
    // 계측: parser worker가 이미 관측한 receive buffer write timestamp
    private long m_LastParsedReceiveBufferWriteTimestamp;
    private readonly SessionSendOptions m_SendOptions;
    private readonly Channel<SendQueueItem> m_SendQueue;
    private long m_QueuedSendBytes;
    // 상태: telemetry에 등록된 send request 중 아직 완료 또는 abandon 처리되지 않은 수
    private long m_PendingSendRequests;
    private readonly Task m_TaskReceivedBuffers;
    private readonly Task m_TaskReceivedPackets;

    private readonly Task m_TaskSendBuffers;

    // Channel<T>로 변경 (BufferBlock<T> 대비 4배 빠르고 메모리 69% 절약)
    private readonly Channel<ReceivedPacketItem> m_ReceivedPackets;

    private readonly System.Net.EndPoint? m_RemoteEndPoint;

    // 상태: successful receive 또는 accepted 시점 기준 마지막 activity timestamp
    private long m_LastReceivedTimestamp;

    // Disconnect 중복 호출 방지를 위한 플래그
    private int m_DisconnectRequested = 0;

    private sealed class SendQueueItem
    {
        // 상태: send worker가 telemetry 등록 이후 drain하도록 조율하는 플래그
        private int m_IsTelemetryRegistered;
        // 상태: pooled buffer double-return 방지를 위한 one-shot 플래그
        private int m_IsBufferReturned;

        // 생성: ArrayPool rented array와 실제 packet byte 길이를 분리
        public SendQueueItem(byte[] buffer, int length)
        {
            Buffer = buffer;
            Length = length;
        }

        // 용도: socket send segment가 참조하는 rented backing array
        public byte[] Buffer { get; }

        // 상태: partial send 이후 다음 전송 시작 위치
        public int Offset { get; private set; }

        // 상태: rented array 길이가 아닌 실제 전송해야 할 packet byte 길이
        public int Length { get; }

        // 상태: 아직 socket으로 drain되지 않은 byte 수
        public int Remaining => Length - Offset;

        // 상태: logical packet byte가 모두 drain되었는지 여부
        public bool IsComplete => Offset >= Length;

        // 상태: telemetry pending 등록 완료 여부
        public bool IsTelemetryRegistered => Volatile.Read(ref m_IsTelemetryRegistered) == 1;

        // 목적: telemetry pending 등록 완료 publish
        public void MarkTelemetryRegistered()
        {
            Volatile.Write(ref m_IsTelemetryRegistered, 1);
        }

        // 목적: socket send 성공 byte만큼 logical offset 전진
        public void Advance(int sentBytes)
        {
            Offset += sentBytes;
        }

        // 목적: send queue item 소유 pooled buffer를 정확히 한 번 반환
        public void ReturnBuffer()
        {
            if (Interlocked.Exchange(ref m_IsBufferReturned, 1) != 0)
            {
                return;
            }

            ArrayPool<byte>.Shared.Return(Buffer);
        }
    }

    private readonly record struct ReceivedPacketItem(
        LibCommons.BasePacket Packet,
        long ParserReadyTimestamp);


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
        m_ReceivedPackets = Channel.CreateBounded<ReceivedPacketItem>(new BoundedChannelOptions(1000)
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
        // 초기값: accept 직후 첫 scan에서 즉시 idle로 오판하지 않도록 생성 시각 기록
        MarkNetworkActivity();
    }

    /// <summary>
    /// 세션이 연결 해제되었는지 여부
    /// </summary>
    public bool IsDisconnected => m_DisconnectRequested == 1;

    // 상태: idle scanner가 읽는 마지막 receive/activity timestamp
    public long LastReceivedTimestamp => Volatile.Read(ref m_LastReceivedTimestamp);

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

    // Session disconnect hook: reason-aware telemetry 연결점
    protected virtual void OnNetworkSessionDisconnected(NetworkDisconnectReason reason)
    {
        OnNetworkSessionDisconnected();
    }

    // Socket error hook: phase 포함 error classification 연결점
    protected virtual void OnNetworkSocketError(string phase, SocketError? socketError, Exception? exception)
    {
        OnNetworkSocketError(socketError, exception);
    }

    // Socket error hook: 기존 subclass 호환용 error code 및 exception context
    protected virtual void OnNetworkSocketError(SocketError? socketError, Exception? exception)
    {
    }

    // Packet parsed hook: packet size 기반 counter 갱신점
    protected virtual void OnNetworkPacketReceived(BasePacket packet)
    {
    }

    // Receive completion hook: socket receive await 구간 계측 연결점
    protected virtual void OnNetworkReceiveCompleted(int bytes, TimeSpan duration)
    {
    }

    // Operation duration hook: session 내부 단계별 latency summary 연결점
    protected virtual void OnNetworkOperationDuration(string operation, TimeSpan duration)
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

    // Send request abandoned hook: disconnect로 미완료 pending request를 정리한 수
    protected virtual void OnNetworkSendAbandoned(int count)
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

    // 용도: idle scanner와 receive path가 공유하는 monotonic timestamp 생성
    protected virtual long GetNetworkTimestamp()
    {
        return Stopwatch.GetTimestamp();
    }

    // 용도: accepted/receive activity timestamp 갱신
    protected void MarkNetworkActivity()
    {
        Volatile.Write(ref m_LastReceivedTimestamp, GetNetworkTimestamp());
    }

    public async Task WaitSession()
    {
        // 소켓 및 패킷 처리 대기
        await Task.WhenAll(m_TaskReceivedPackets, m_TaskReceivedBuffers, m_TaskSendBuffers);
    }


    private void OnSocketEventsReceivedCompleted(object? sender, SocketAsyncEventArgs e)
    {
        // 계측 기준점: socket receive completion callback 진입 시각
        long receiveCompletedTimestamp = GetNetworkTimestamp();

        if (e.SocketError == SocketError.IOPending)
        {
            m_Logger.LogDebug($"BaseSession, OnSocketEventsReceivedCompleted, Socket IOPeding.");
            return;
        }

        if (e.BytesTransferred <= 0)
        {
            m_Logger.LogInformation($"BaseSession, OnSocketEventsReceivedCompleted, Disconnected. BytesTransferred is zero.");
            RequestDisconnect(NetworkDisconnectReason.RemoteClosed);

            return;
        }

        if (e.SocketError != SocketError.Success)
        {
            // Receive completion socket error: disconnect 전 hook 노출
            OnNetworkSocketError("receive-completion", e.SocketError, null);
            m_Logger.LogInformation($"BaseSession, OnSocketEventsReceivedCompleted, Disconnected. SocketError : {e.SocketError}");

            RequestDisconnect(NetworkDisconnectReason.ReceiveSocketError);

            return;
        }

        var buffer = e.Buffer;
        if (null == buffer)
        {
            m_Logger.LogInformation($"BaseSession, OnSocketEventsReceivedCompleted, Disconnected. Buffer is null.");

            RequestDisconnect(NetworkDisconnectReason.Unknown);

            return;
        }

        // 계측: ReceiveAsync 요청부터 socket byte 수신 완료까지의 대기 시간
        RecordReceiveCompletedDuration(e.BytesTransferred, receiveCompletedTimestamp);

        // Process the received data
        long bufferWriteStartedTimestamp = GetNetworkTimestamp();
        var wroteSize = m_ReceivedBuffers.Write(buffer, e.Offset, e.BytesTransferred);
        // 계측: socket buffer에서 session receive buffer로 복사/확장하는 비용
        OnNetworkOperationDuration("receive-buffer-write", Stopwatch.GetElapsedTime(bufferWriteStartedTimestamp));

        // 계측 기준점: parser worker wake-up 및 packet extraction 대기 시간 계산용
        Volatile.Write(ref m_LastReceiveBufferWriteTimestamp, GetNetworkTimestamp());
        // Activity: packet 완성 여부와 무관하게 socket byte 수신 성공 시 idle 기준 갱신
        MarkNetworkActivity();
        if (wroteSize > 0)
        {
            // Flow: receive bytes publish 이후 polling 없이 parser task 깨움
            SignalReceivedBufferWritten();
        }

        m_Logger.LogDebug($"BaseSession, OnSocketEventsReceivedCompleted, Received {wroteSize} bytes from {GetSessionAddress()}");

        // 

        RequestReceived();
    }

    public bool RequestDisconnect()
    {
        return RequestDisconnect(NetworkDisconnectReason.Unknown);
    }

    public bool RequestDisconnect(NetworkDisconnectReason reason)
    {
        // Interlocked.CompareExchange: 원자적 중복 호출 방지
        // 0에서 1로 변경 시도, 이미 1이면 다른 스레드가 먼저 호출한 것
        if (Interlocked.CompareExchange(ref m_DisconnectRequested, 1, 0) != 0)
        {
            m_Logger.LogDebug("BaseSession, RequestDisconnect, Already disconnecting or disconnected.");
            return false;
        }

        m_Logger.LogInformation($"BaseSession, RequestDisconnect.");
        // Disconnect 처리: engine 담당, 관측 hook 분리
        OnNetworkSessionDisconnected(reason);
        // Disconnect cleanup: 미완료 send request를 완료와 구분해 pending gauge에서 제거
        AbandonPendingSendRequests();
        // Disconnect cleanup: 미전송 queued byte gauge를 세션 종료 시점에 0으로 내림
        ClearQueuedSendBytesForDisconnect();

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
        return true;
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
            // 계측 기준점: ReceiveAsync 요청 시작 시각
            Volatile.Write(ref m_ReceiveRequestedTimestamp, GetNetworkTimestamp());
            if (!socket.ReceiveAsync(m_SocketEventsReceived))
            {
                // If ReceiveAsync returns false, we handle the receive operation immediately
                OnSocketEventsReceivedCompleted(this, m_SocketEventsReceived);
            }
        }
        catch (ObjectDisposedException)
        {
            // 소켓이 이미 Dispose된 경우
            RequestDisconnect(NetworkDisconnectReason.Unknown);
        }
        catch (SocketException ex)
        {
            m_Logger.LogDebug($"BaseSession, RequestReceived, SocketException : {ex.Message}");
            // Receive 요청 실패: socket error hook 기반 외부 관측
            OnNetworkSocketError("receive-request", ex.SocketErrorCode, ex);
            RequestDisconnect(NetworkDisconnectReason.ReceiveRequestError);
        }
    }

    // 목적: receive buffer 변경 publish 및 대기 중인 parser task wake-up
    private void SignalReceivedBufferWritten()
    {
        // 순서: buffer write 완료 후 version 증가로 reader가 최신 write를 관측
        Interlocked.Increment(ref m_ReceivedBufferVersion);
        // 흐름: parser task가 Task.Delay polling 대신 즉시 재시도
        m_ReceivedBufferSignal.Release();
    }

    // 목적: receive request부터 completion까지의 socket wait duration 기록
    private void RecordReceiveCompletedDuration(int bytes, long receiveCompletedTimestamp)
    {
        long receiveRequestedTimestamp = Volatile.Read(ref m_ReceiveRequestedTimestamp);
        if (receiveRequestedTimestamp <= 0 || receiveCompletedTimestamp <= receiveRequestedTimestamp)
        {
            return;
        }

        OnNetworkReceiveCompleted(
            bytes,
            Stopwatch.GetElapsedTime(receiveRequestedTimestamp, receiveCompletedTimestamp));
    }

    // 목적: buffer write 완료 후 parser worker가 처음 관측하기까지의 delay 기록
    private void RecordReceiveSignalToParseDuration()
    {
        long bufferWriteTimestamp = Volatile.Read(ref m_LastReceiveBufferWriteTimestamp);
        if (bufferWriteTimestamp <= 0)
        {
            return;
        }

        long previousTimestamp = Interlocked.Exchange(
            ref m_LastParsedReceiveBufferWriteTimestamp,
            bufferWriteTimestamp);
        if (previousTimestamp == bufferWriteTimestamp)
        {
            return;
        }

        OnNetworkOperationDuration(
            "receive-signal-to-parse",
            Stopwatch.GetElapsedTime(bufferWriteTimestamp));
    }

    // 목적: 관측한 version 이후 새 receive write가 있을 때까지 비동기 대기
    private async ValueTask WaitForReceivedBufferChangeAsync(long observedVersion, CancellationToken cancellationToken)
    {
        // 상태: 남아 있는 semaphore count가 오래된 signal이면 version이 바뀔 때까지 계속 소비
        while (Volatile.Read(ref m_ReceivedBufferVersion) == observedVersion)
        {
            await m_ReceivedBufferSignal.WaitAsync(cancellationToken);
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

        if (IsDisconnected)
        {
            int queuedBytes = GetQueuedSendBytesSnapshot();
            // Disconnect 이후 send 요청: enqueue하지 않고 rejection으로만 관측
            OnNetworkSendRejected(buffersSize, queuedBytes);
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

        // 계측 기준점: packet buffer 생성부터 send queue 등록까지의 enqueue 비용
        long sendEnqueueStartedTimestamp = GetNetworkTimestamp();

        // 목적: per-send packet byte[] 할당 대신 pool에서 backing array 대여
        byte[] sendBuffers = ArrayPool<byte>.Shared.Rent(buffersSize);

        try
        {
            // 목적: 임시 byte[] 할당 없이 UInt16 packet size header 직접 기록
            BinaryPrimitives.WriteUInt16LittleEndian(sendBuffers.AsSpan(0, BasePacket.HeaderSize), (ushort)buffersSize);
            buffers.CopyTo(sendBuffers.AsSpan(BasePacket.HeaderSize));
        }
        catch
        {
            // 예외 보정: queue 소유권 이전 전 실패한 rented buffer 반환
            ArrayPool<byte>.Shared.Return(sendBuffers);
            // 예외 보정: queue byte 예약 후 enqueue 전 실패한 경우 gauge rollback
            ReleaseQueuedSendBytes(buffersSize);
            throw;
        }


        m_Logger.LogDebug($"BaseSession, RequestSendBuffers, Buffer Length : {buffersSize}");

        var sendItem = new SendQueueItem(sendBuffers, buffersSize);
        if (!m_SendQueue.Writer.TryWrite(sendItem))
        {
            // Queue writer closed: queue가 소유하지 못한 pooled buffer 즉시 반환
            sendItem.ReturnBuffer();
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
        RecordPendingSendRequested(buffersSize, queuedAfter);
        // 상태: send worker가 telemetry pending 등록 이후에만 item을 drain하도록 표시
        sendItem.MarkTelemetryRegistered();
        // 계측: send request가 send queue에 안전하게 등록되는 데 걸린 시간
        OnNetworkOperationDuration("send-enqueue", Stopwatch.GetElapsedTime(sendEnqueueStartedTimestamp));
        if (queuedAfter > SendBufferBackpressureThresholdBytes)
        {
            // High-watermark 초과: enqueue 성공과 분리된 backpressure 관측
            OnNetworkSendBackpressure();
        }

        return true;
    }

    // 목적: telemetry pending 등록과 disconnect race 보정
    private void RecordPendingSendRequested(int bytes, int queuedBytes)
    {
        // 순서: 외부 telemetry counter를 먼저 올려 disconnect 후 등록되는 pending을 즉시 abandon 가능하게 함
        OnNetworkSendRequested(bytes, queuedBytes);
        // 상태: 이 세션이 책임지는 request 단위 pending 수
        Interlocked.Increment(ref m_PendingSendRequests);

        if (!IsDisconnected)
        {
            return;
        }

        // Race 보정: disconnect 이후 enqueue 성공이면 completed가 아닌 abandoned로 즉시 정리
        if (TryDecrementPendingSendRequest())
        {
            OnNetworkSendAbandoned(1);
        }

        // Race 보정: disconnect 이후 enqueue 성공한 byte 예약을 즉시 rollback
        int queuedBytesAfterRollback = ReleaseQueuedSendBytes(bytes);
        OnNetworkSendBufferSample(queuedBytesAfterRollback);
    }

    // 목적: request payload 전체가 socket으로 drain된 경우 pending request 완료 처리
    private void CompletePendingSendRequest()
    {
        if (TryDecrementPendingSendRequest())
        {
            OnNetworkSendCompleted();
        }
    }

    // 목적: disconnect 시점에 남은 pending request를 완료와 구분해 정리
    private void AbandonPendingSendRequests()
    {
        long abandoned = Interlocked.Exchange(ref m_PendingSendRequests, 0);
        if (abandoned <= 0)
        {
            return;
        }

        OnNetworkSendAbandoned(ToTelemetryCount(abandoned));
    }

    // 목적: send 완료와 disconnect abandon 간 중복 차감 방지
    private bool TryDecrementPendingSendRequest()
    {
        long current;
        do
        {
            current = Volatile.Read(ref m_PendingSendRequests);
            if (current <= 0)
            {
                return false;
            }
        }
        while (Interlocked.CompareExchange(ref m_PendingSendRequests, current - 1, current) != current);

        return true;
    }

    // 목적: session 종료 후 남은 queued byte gauge를 즉시 0으로 샘플링
    private void ClearQueuedSendBytesForDisconnect()
    {
        long clearedBytes = Interlocked.Exchange(ref m_QueuedSendBytes, 0);
        if (clearedBytes <= 0)
        {
            return;
        }

        OnNetworkSendBufferSample(0);
    }

    // 목적: long 기반 내부 counter를 telemetry hook의 int 범위로 clamp
    private static int ToTelemetryCount(long count)
    {
        return (int)Math.Min(int.MaxValue, Math.Max(0, count));
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
        ReadOnlySpan<byte> messageBuffers = message.ToByteArray();

        byte[] packetBuffers = new byte[sizeof(int) + messageBuffers.Length];

        // 목적: 임시 packet id byte[] 없이 protocol id header 직접 기록
        BinaryPrimitives.WriteInt32LittleEndian(packetBuffers.AsSpan(0, sizeof(int)), packetId);
        messageBuffers.CopyTo(packetBuffers.AsSpan(sizeof(int)));

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
            await foreach (var packetItem in m_ReceivedPackets.Reader.ReadAllAsync(cancellationToken))
            {
                // 계측: parser가 packet을 준비한 뒤 handler task가 소비하기까지의 queue delay
                OnNetworkOperationDuration(
                    "receive-packet-queue-delay",
                    Stopwatch.GetElapsedTime(packetItem.ParserReadyTimestamp));

                // 계측 기준점: protocol parse 및 echo response enqueue 비용
                long handlerStartedTimestamp = GetNetworkTimestamp();
                OnReceived(packetItem.Packet);
                // 계측: application-level packet handler 처리 시간
                OnNetworkOperationDuration(
                    "receive-packet-handler",
                    Stopwatch.GetElapsedTime(handlerStartedTimestamp));
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
                // 상태: 부족 판단과 대기 사이 새 receive를 놓치지 않기 위한 기준 version
                long observedVersion = Volatile.Read(ref m_ReceivedBufferVersion);
                if (m_ReceivedBuffers.CanReadSize < LibCommons.BasePacket.HeaderSize)
                {
                    // 흐름: header 미만이면 새 receive write까지 대기
                    await WaitForReceivedBufferChangeAsync(observedVersion, cancellationToken);
                    continue;
                }

                // 계측: receive write signal 이후 parser worker가 readable buffer를 관측한 시점
                RecordReceiveSignalToParseDuration();

                // 계측 기준점: complete packet extraction 및 BasePacket payload copy 비용
                long packetExtractStartedTimestamp = GetNetworkTimestamp();
                if (!m_ReceivedBuffers.TryGetBasePackets(out List<LibCommons.BasePacket> basePackets))
                {
                    // 흐름: partial packet이면 추가 receive write까지 대기
                    await WaitForReceivedBufferChangeAsync(observedVersion, cancellationToken);
                    continue;
                }
                // 계측: buffer에서 complete packet list를 추출하는 비용
                OnNetworkOperationDuration(
                    "receive-packet-extract",
                    Stopwatch.GetElapsedTime(packetExtractStartedTimestamp));

                foreach (var basePacket in basePackets)
                {
                    m_Logger.LogDebug($"BaseSession, DoWorkReceived, Received Packet Size : {basePacket.PacketSize}, Data Size : {basePacket.DataSize}");
                    // Packet complete: received packet/bytes hook 호출 기준
                    OnNetworkPacketReceived(basePacket);

                    // 계측 기준점: parser ready부터 bounded channel enqueue 완료까지의 비용
                    long channelWriteStartedTimestamp = GetNetworkTimestamp();
                    var packetItem = new ReceivedPacketItem(basePacket, channelWriteStartedTimestamp);
                    // Channel에 패킷 전송
                    await m_ReceivedPackets.Writer.WriteAsync(packetItem, cancellationToken);
                    // 계측: channel backpressure로 parser worker가 대기한 시간
                    OnNetworkOperationDuration(
                        "receive-packet-channel-write",
                        Stopwatch.GetElapsedTime(channelWriteStartedTimestamp));
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

                        // 소유권: channel에서 꺼낸 item은 cancellation 경로에서도 반환되도록 pending에 먼저 보관
                        pendingSendItems.Enqueue(sendItem);
                        // 순서: telemetry pending 등록 전 send completion이 먼저 찍히지 않도록 대기
                        await WaitForSendTelemetryRegistrationAsync(sendItem, cancellationToken);
                    }

                    if (IsSendDrainBudgetExhausted(drainedBytesThisCycle, sendOperationsThisCycle))
                    {
                        RecordSendDrainYield();
                        await Task.Yield();
                        drainedBytesThisCycle = 0;
                        sendOperationsThisCycle = 0;
                    }

                    if (IsDisconnected)
                    {
                        return;
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
                    int batchBytes = await BuildSendSegmentsAsync(pendingSendItems, maxSendBytes, sendSegments, cancellationToken);
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
                        OnNetworkSocketError("send-transient", ex.SocketErrorCode, ex);
                        OnNetworkSendBackpressure();
                        m_Logger.LogDebug($"BaseSession, DoWorkSendBuffers, Transient SocketException : {ex.SocketErrorCode}, {ex.Message}");
                        await WaitTransientSendBackoffAsync(cancellationToken);
                        continue;
                    }
                    catch (SocketException ex)
                    {
                        // Non-transient send socket error: disconnect 전 hook 노출
                        OnNetworkSocketError("send", ex.SocketErrorCode, ex);
                        m_Logger.LogDebug($"BaseSession, DoWorkSendBuffers, SocketException : {ex.Message}");
                        RequestDisconnect(NetworkDisconnectReason.SendSocketError);
                        return;
                    }

                    if (sentSize <= 0)
                    {
                        m_Logger.LogInformation("BaseSession, DoWorkSendBuffers, Disconnected. Sent size is zero.");
                        RequestDisconnect(NetworkDisconnectReason.SendZeroBytes);
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
                        CompletePendingSendRequest();
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
            OnNetworkSocketError("send-worker", ex.SocketErrorCode, ex);
            RequestDisconnect(NetworkDisconnectReason.SendSocketError);
        }
        finally
        {
            // 정리: sendSegments가 pooled buffer를 더 이상 참조하지 않도록 먼저 비움
            sendSegments.Clear();
            // 정리: partial send 또는 cancellation으로 남은 pending item buffer 반환
            ReturnPendingSendBuffers(pendingSendItems);
            // 정리: disconnect/worker 종료 시 channel에 남은 item buffer 반환
            DrainQueuedSendBuffers();
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

    private async ValueTask<int> BuildSendSegmentsAsync(
        Queue<SendQueueItem> pendingSendItems,
        int maxBytes,
        List<ArraySegment<byte>> sendSegments,
        CancellationToken cancellationToken)
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
            // 소유권: channel에서 꺼낸 item은 build 중 cancellation에도 반환 가능하도록 pending에 먼저 등록
            pendingSendItems.Enqueue(item);
            // 순서: batch 추가 item도 pending 등록 완료 후 segment 생성
            await WaitForSendTelemetryRegistrationAsync(item, cancellationToken);
            if (!TryAppendSendSegment(item, maxBytes, sendSegments, ref totalBytes))
            {
                break;
            }
        }

        return totalBytes;
    }

    // 목적: send queue publish와 telemetry pending 등록 사이의 짧은 race 차단
    private static async ValueTask WaitForSendTelemetryRegistrationAsync(
        SendQueueItem item,
        CancellationToken cancellationToken)
    {
        while (!item.IsTelemetryRegistered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }
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
                SendQueueItem completedItem = pendingSendItems.Dequeue();
                // 완료: fully-drained packet backing array를 pool로 반환
                completedItem.ReturnBuffer();
                completedItems++;
            }
        }

        return completedItems;
    }

    // 목적: send worker 종료 시 pending queue가 소유한 pooled buffer 반환
    private static void ReturnPendingSendBuffers(Queue<SendQueueItem> pendingSendItems)
    {
        while (pendingSendItems.Count > 0)
        {
            pendingSendItems.Dequeue().ReturnBuffer();
        }
    }

    // 목적: send worker 종료 시 channel에 drain되지 않은 pooled buffer 반환
    private void DrainQueuedSendBuffers()
    {
        while (m_SendQueue.Reader.TryRead(out SendQueueItem? item))
        {
            item.ReturnBuffer();
        }
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
