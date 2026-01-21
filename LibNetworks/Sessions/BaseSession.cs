using Google.Protobuf;
using LibCommons;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace LibNetworks.Sessions;

public abstract class BaseSession
{

    protected ILogger m_Logger;
    private System.Net.Sockets.Socket? m_Socket;

    private readonly byte[] m_ReceivedSocketBuffers = new byte[1024 * 8]; // 8KB

    private readonly SocketAsyncEventArgs m_SocketEventsReceived = new SocketAsyncEventArgs();
    private readonly SocketAsyncEventArgs m_SockenEventsSent = new SocketAsyncEventArgs();

    // Session이 Disconnected 되었을 경우 호출 함수
    public Action? OnEventSessionDisconnected;

    private CancellationTokenSource m_CancellationTokenSource = new CancellationTokenSource();

    private readonly LibCommons.IBuffers m_ReceivedBuffers;
    private readonly LibCommons.IBuffers m_SendBuffers;

    private readonly Task m_TaskReceivedBuffers;
    private readonly Task m_TaskReceivedPackets;

    private readonly Task m_TaskSendBuffers;

    // Channel<T>로 변경 (BufferBlock<T> 대비 4배 빠르고 메모리 69% 절약)
    private readonly Channel<LibCommons.BasePacket> m_ReceivedPackets;

    private readonly System.Net.EndPoint? m_RemoteEndPoint;

    // Disconnect 중복 호출 방지를 위한 플래그
    private int m_DisconnectRequested = 0;


    public BaseSession(ILogger<BaseSession> logger, System.Net.Sockets.Socket socket, LibCommons.IBuffers receivedBuffers, LibCommons.IBuffers sendbuffers)
    {
        m_Logger = logger;
        m_Socket = socket;

        // 10초마다 KeepAlive 신호를 보내도록 설정 (Windows에서는 레지스트리 수정 필요)
        m_Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

        // 소켓을 닫을 때, 보내지 않은 데이터가 있으면 1초간 대기 후 닫음
        m_Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(true, 1));

        // Nagle 알고리즘 비활성화
        m_Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

        m_ReceivedBuffers = receivedBuffers;
        m_SendBuffers = sendbuffers;

        m_SocketEventsReceived.SetBuffer(m_ReceivedSocketBuffers, 0, m_ReceivedSocketBuffers.Length);
        m_SocketEventsReceived.Completed += OnSocketEventsReceivedCompleted;
        m_SocketEventsReceived.UserToken = this;

        m_SockenEventsSent.Completed += OnSocketEventsSentCompleted;
        m_SockenEventsSent.UserToken = this;

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

    private void OnSocketEventsSentCompleted(object? sender, SocketAsyncEventArgs e)
    {
        if (e.SocketError == SocketError.IOPending)
        {
            m_Logger.LogDebug($"BaseSession, OnSocketEventsReceivedCompleted, Socket IOPeding.");
            return;
        }

        if (e.BytesTransferred <= 0)
        {
            m_Logger.LogInformation($"BaseSession, OnSocketEventsSentCompleted, Disconnected. BytesTransferred is zero.");
            RequestDisconnect();

            return;
        }

        if (e.SocketError != SocketError.Success)
        {
            m_Logger.LogInformation($"BaseSession, OnSocketEventsSentCompleted, Disconnected. SocketError : {e.SocketError}");

            RequestDisconnect();

            return;
        }

        m_Logger.LogDebug($"BaseSession, OnSocketEventsSentCompleted, Dran Buffer Length : {e.BytesTransferred}");
        m_SendBuffers.Drain(e.BytesTransferred);
    }


    public void RequestDisconnect()
    {
        // Interlocked.CompareExchange를 사용하여 원자적으로 중복 호출 방지
        // 0에서 1로 변경 시도, 이미 1이면 다른 스레드가 먼저 호출한 것
        if (Interlocked.CompareExchange(ref m_DisconnectRequested, 1, 0) != 0)
        {
            m_Logger.LogDebug("BaseSession, RequestDisconnect, Already disconnecting or disconnected.");
            return;
        }

        m_Logger.LogInformation($"BaseSession, RequestDisconnect.");

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
            RequestDisconnect();
        }
    }

    protected void RequestSendBuffers(ReadOnlySpan<byte> buffers)
    {
        if (buffers.Length <= 0)
        {
            m_Logger.LogError($"BaseSession, RequestSendBuffers, Buffers is zero.");
            return;
        }

        ushort buffersSize = (ushort)(buffers.Length + BasePacket.HeaderSize);

        // List로 바꾸는 것도 고려
        byte[] sendBuffers = new byte[buffersSize];

        // Insert Packet Size at the beginning of the buffer
        BitConverter.GetBytes(buffersSize).AsSpan().CopyTo(sendBuffers);
        buffers.CopyTo(sendBuffers.AsSpan(BasePacket.HeaderSize));


        m_Logger.LogDebug($"BaseSession, RequestSendBuffers, Buffer Length : {sendBuffers.Length}");

        m_SendBuffers.Write(sendBuffers, 0, sendBuffers.Length);
    }

    protected void RequestSendString(string message)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message);

        RequestSendBuffers(bytes);
    }



    protected void RequestSendMessage<T>(int packetId, Google.Protobuf.IMessage<T> message) where T : IMessage<T>
    {

        Span<byte> packetIdBuffers = BitConverter.GetBytes(packetId);
        ReadOnlySpan<byte> messageBuffers = message.ToByteArray();

        byte[] packetBuffers = new byte[packetIdBuffers.Length + messageBuffers.Length];

        packetIdBuffers.CopyTo(packetBuffers);
        messageBuffers.CopyTo(packetBuffers.AsSpan(packetIdBuffers.Length));

        RequestSendBuffers(packetBuffers);
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
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (m_SendBuffers.CanReadSize <= 0)
                {
                    await Task.Delay(1, cancellationToken); // CPU 사용량 감소
                    continue;
                }

                var socket = m_Socket;
                if (socket == null || !socket.Connected)
                {
                    break;
                }

                byte[] sendBuffers = new byte[m_SendBuffers.CanReadSize];
                m_SendBuffers.Peek(ref sendBuffers);

                m_SockenEventsSent.SetBuffer(sendBuffers, 0, sendBuffers.Length);

                m_Logger.LogDebug($"BaseSession, DoWorkSendBuffers, Buffer Length : {sendBuffers.Length}");

                if (!socket.SendAsync(m_SockenEventsSent))
                {
                    OnSocketEventsSentCompleted(socket, m_SockenEventsSent);
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
        }
    }
}