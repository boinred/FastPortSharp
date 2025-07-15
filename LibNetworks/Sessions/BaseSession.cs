using Microsoft.Extensions.Logging;
using System.Net.Sockets;

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

    public BaseSession(ILogger<BaseSession> logger, System.Net.Sockets.Socket socket)
    {
        m_Logger = logger;
        m_Socket = socket;

        m_SocketEventsReceived.SetBuffer(m_ReceivedBuffers, 0, m_ReceivedBuffers.Length);
        m_SocketEventsReceived.Completed += OnSocketEventsReceivedCompleted;
        m_SocketEventsReceived.UserToken = this;

        m_SockenEventsSent.Completed += OnSocketEventsSentCompleted;
        m_SockenEventsSent.UserToken = this;

        RequestReceived();
    }



    protected virtual void OnReceived() { }

    protected virtual void OnSent() { }

    protected virtual void OnDisconnected() { }

    private void OnSocketEventsReceivedCompleted(object? sender, SocketAsyncEventArgs e)
    {
        if (e.SocketError == SocketError.IOPending)
        {
            return;
        }

        if (e.BytesTransferred <= 0)
        {
            RequestDisconnect();

            return;
        }

        if (e.SocketError != SocketError.Success)
        {
            RequestDisconnect();

            return;
        }

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

    private void RequestReceived()
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
}