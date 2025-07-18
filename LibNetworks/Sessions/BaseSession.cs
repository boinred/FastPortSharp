using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace LibNetworks.Sessions;

public abstract class BaseSession
{
    protected ILogger m_Logger;
    private System.Net.Sockets.Socket m_Socket;

    private readonly byte[] m_ReceivedBuffers = new byte[1024 * 8]; // 8KB

    private readonly SocketAsyncEventArgs m_SocketEventsReceived = new SocketAsyncEventArgs();
    private readonly SocketAsyncEventArgs m_SockenEventsSent = new SocketAsyncEventArgs();

    // Session이 Disconnected 되었을 경우 호출 함수
    public Action? OnEventSessionDisconnected;

    private BlockingCollection<byte[]> m_ReceivedBuffersQueue = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>());

    private BlockingCollection<byte[]> m_SendBuffersQueue = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>());

    private CancellationTokenSource m_CancellationTokenSource = new CancellationTokenSource();

    private LibCommons.BaseCircularBuffer m_CircularBuffer = new LibCommons.BaseCircularBuffer(1024 * 8); // 8KB Circular Buffer



    public BaseSession(ILogger<BaseSession> logger, System.Net.Sockets.Socket socket)
    {
        BlockingCollection<byte[]> abc = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>());

        m_Logger = logger;
        m_Socket = socket;

        m_SocketEventsReceived.SetBuffer(m_ReceivedBuffers, 0, m_ReceivedBuffers.Length);
        m_SocketEventsReceived.Completed += OnSocketEventsReceivedCompleted;
        m_SocketEventsReceived.UserToken = this;

        m_SockenEventsSent.Completed += OnSocketEventsSentCompleted;
        m_SockenEventsSent.UserToken = this;

        StartWorkers();
    }

    public string GetSessionAddress() => m_Socket.RemoteEndPoint?.ToString() ?? " Unknown";



    protected virtual void OnReceived() { }

    protected virtual void OnSent() { }

    protected virtual void OnDisconnected() { }

    private void StartWorkers()
    {

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
        var wroteSize = m_CircularBuffer.Write(buffer, e.Offset, e.Count);

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

    public static void DoWorkReceived(LibCommons.BaseCircularBuffer circularBuffer, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (circularBuffer.CanReadSize < LibCommons.BasePacket.HeaderSize)
            {
                continue;
            }

            // CircularBuffer에서 패킷 사이즈만큼 가지고 와서 BlockingCollection에 넣어줘야 한다.
        }

    }
}