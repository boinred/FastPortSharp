using LibCommons;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks.Dataflow;

namespace LibNetworks.Sessions;

public abstract class BaseSession
{

    protected ILogger m_Logger;
    private System.Net.Sockets.Socket m_Socket;

    private readonly byte[] m_ReceivedSocketBuffers = new byte[1024 * 8]; // 8KB

    private readonly SocketAsyncEventArgs m_SocketEventsReceived = new SocketAsyncEventArgs();
    private readonly SocketAsyncEventArgs m_SockenEventsSent = new SocketAsyncEventArgs();

    // Session이 Disconnected 되었을 경우 호출 함수
    public Action? OnEventSessionDisconnected;

    private CancellationTokenSource m_CancellationTokenSource = new CancellationTokenSource();

    private readonly LibCommons.IBuffers m_ReceivedBuffers;

    private Task m_TaskReceivedBuffers;
    private Task m_TaskReceivedPackets;

    private readonly BufferBlock<LibCommons.BasePacket> m_ReceivedPackets;
    private readonly ActionBlock<LibCommons.BasePacket> m_ReceivedWorks;


    public BaseSession(ILogger<BaseSession> logger, System.Net.Sockets.Socket socket, LibCommons.IBuffers buffers)
    {
        m_Logger = logger;
        m_Socket = socket;
        m_ReceivedBuffers = buffers;

        m_SocketEventsReceived.SetBuffer(m_ReceivedSocketBuffers, 0, m_ReceivedSocketBuffers.Length);
        m_SocketEventsReceived.Completed += OnSocketEventsReceivedCompleted;
        m_SocketEventsReceived.UserToken = this;

        m_SockenEventsSent.Completed += OnSocketEventsSentCompleted;
        m_SockenEventsSent.UserToken = this;

        m_ReceivedPackets = new(new System.Threading.Tasks.Dataflow.DataflowBlockOptions { BoundedCapacity = 1000, CancellationToken = m_CancellationTokenSource.Token });
        m_ReceivedWorks = new ActionBlock<LibCommons.BasePacket>(OnReceived);

        m_TaskReceivedPackets = Task.Run(async () => await DoWorkReceivedPackets(m_CancellationTokenSource.Token));
        m_TaskReceivedBuffers = Task.Run(async () => await DoWorkReceivedBuffers(m_CancellationTokenSource.Token));
    }

    public string GetSessionAddress() => m_Socket.RemoteEndPoint?.ToString() ?? " Unknown";


    protected virtual void OnReceived(BasePacket basePacket) { }

    protected virtual void OnSent() { }

    protected virtual void OnDisconnected() { }

    public async Task WaitSession()
    {
        // 소켓 및 패킷 처리 대기
        await Task.WhenAll(m_TaskReceivedPackets, m_TaskReceivedBuffers);

        // Wait for all received works to complete
        await m_ReceivedWorks.Completion;
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
        var wroteSize = m_ReceivedBuffers.Write(buffer, e.Offset, e.Count);

        m_Logger.LogDebug($"BaseSession, OnSocketEventsReceivedCompleted, Received {wroteSize} bytes from {GetSessionAddress()}");

        // 

        RequestReceived();
    }

    private void OnSocketEventsSentCompleted(object? sender, SocketAsyncEventArgs e)
    {

    }

    private void RequestDisconnect()
    {
        if (null == m_Socket)
        {
            return;
        }

        m_CancellationTokenSource.Cancel();

        try
        {
            m_Socket.Shutdown(SocketShutdown.Both);
            m_Socket.Close();
        }
        catch (Exception ex)
        {
            m_Logger.LogError($"BaseSession, RequestDisconnect, Exception : {ex}");
        }

        OnEventSessionDisconnected?.Invoke(); // Return With Id
    }

    protected void RequestReceived()
    {
        if (!m_Socket.ReceiveAsync(m_SocketEventsReceived))
        {
            // If ReceiveAsync returns false, we handle the receive operation immediately
            OnSocketEventsReceivedCompleted(this, m_SocketEventsReceived);
        }
    }

    protected void RequestSendBuffers(byte[] buffers)
    {
        if (null == buffers || buffers.Length <= 0)
        {
            return;
        }

        // Insert Queue 
    }

    protected void RequestSendString(string message)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message);

        RequestSendBuffers(bytes);
    }

    public async Task DoWorkReceivedPackets(CancellationToken cancellationToken)
    {
        // 블록이 완료되고 버퍼가 비워질 때까지 계속해서 데이터를 수신합니다.
        while (await m_ReceivedPackets.OutputAvailableAsync(cancellationToken))
        {
            var packet = await m_ReceivedPackets.ReceiveAsync();
        }

    }

    public async Task DoWorkReceivedBuffers(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (m_ReceivedBuffers.CanReadSize < LibCommons.BasePacket.HeaderSize)
            {
                continue;
            }

            if (!m_ReceivedBuffers.TryGetBasePackets(out List<LibCommons.BasePacket> basePackets))
            {
                continue;
            }

            foreach (var basePacket in basePackets)
            {
                m_Logger.LogDebug($"BaseSession, DoWorkReceived, Received Packet Size : {basePacket.PacketSize}, Data Size : {basePacket.DataSize}");
                
                await m_ReceivedPackets.SendAsync(basePacket);
            }

            m_ReceivedPackets.Complete(); // Complete the block when done processing
        }
    }
}