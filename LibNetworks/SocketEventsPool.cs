using System.Net.Sockets;

namespace LibNetworks;

internal class SocketEventsPool(int capacity)
{
    private readonly Stack<SocketAsyncEventArgs> m_Pool = new(capacity);
    private readonly ReaderWriterLockSlim m_Lock = new();

    public void Push(SocketAsyncEventArgs item)
    {
        m_Lock.EnterWriteLock();

        m_Pool.Push(item);

        m_Lock.ExitWriteLock();
    }

    public SocketAsyncEventArgs Pop()
    {
        m_Lock.EnterWriteLock();
        m_Pool.TryPop(out var item);
        if (item == null)
        {
            item = new();
        }
        m_Lock.ExitWriteLock();

        return item;
    }
}