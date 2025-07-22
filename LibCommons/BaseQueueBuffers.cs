using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibCommons;

public class BaseQueueBuffers(int capacity) : IBuffers, IDisposable
{
    private Queue<byte> m_QueueBuffers = new(capacity);

    /// <summary>
    /// A ReaderWriterLockSlim to ensure thread safety with concurrent read support.
    /// </summary>
    private readonly ReaderWriterLockSlim m_Lock = new();

    private bool m_bDisposed = false;

    public int CanReadSize
    {
        get
        {
            m_Lock.EnterReadLock();

            try
            {
                return m_QueueBuffers.Count;
            }
            finally
            {
                m_Lock.ExitReadLock();
            }
        }

    }

    public int CanWriteSize => m_QueueBuffers.Capacity - CanReadSize;

    public int Write(byte[] buffers, int offset, int count)
    {
        var data = new ReadOnlySpan<byte>(buffers, offset, count);

        m_Lock.EnterWriteLock();
        try
        {
            foreach (byte b in data)
            {
                m_QueueBuffers.Enqueue(b);
            }
            return count;
        }
        finally
        {
            m_Lock.ExitWriteLock();
        }
    }

    public int Peek(ref byte[] buffers)
    {
        m_Lock.EnterReadLock();

        try
        {
            buffers = m_QueueBuffers.ToArray();
            return buffers.Length;
        }
        finally
        {
            m_Lock.ExitReadLock();
        }
    }


    public int Drain(int size)
    {
        m_Lock.EnterReadLock();
        try
        {
            int bytesInDrain = Math.Min(size, m_QueueBuffers.Count);
            if (0 >= bytesInDrain)
            {
                return 0;
            }

            for (int i = 0; i < bytesInDrain; i++)
            {
                m_QueueBuffers.Dequeue();
            }

            return bytesInDrain;
        }
        finally
        {
            m_Lock.ExitReadLock();
        }
    }

    public bool TryGetBasePackets(out List<BasePacket> basePackets)
    {
        m_Lock.EnterReadLock();
        int readBuffersSize = 0;

        try
        {
            basePackets = new();

            while (readBuffersSize <= m_QueueBuffers.Count)
            {
                if (readBuffersSize + 1 >= m_QueueBuffers.Count)
                {
                    break;
                }

                // Read the packet size from the queue
                byte[] packetIdBuffers = new byte[BasePacket.HeaderSize];
                m_QueueBuffers.CopyTo(packetIdBuffers, BasePacket.HeaderSize);

                int basePacketSize = packetIdBuffers[0] << 0 | packetIdBuffers[1] << 8;
                readBuffersSize += BasePacket.HeaderSize;

                if (basePacketSize + readBuffersSize >= m_QueueBuffers.Count)
                {
                    break;
                }

                // Consume 
                for (int i = 0; i < BasePacket.HeaderSize; i++)
                {
                    m_QueueBuffers.Dequeue();
                }

                // Assuming the first two bytes represent the packet size
                byte[] packetDataBuffers = new byte[basePacketSize];
                for (int i = 0; i < basePacketSize; i++)
                {
                    if (m_QueueBuffers.Count == 0)
                    {
                        break;
                    }
                    packetDataBuffers[i] = m_QueueBuffers.Dequeue();
                }
            }

        }
        finally
        {
            m_Lock.ExitReadLock();
        }

        return basePackets.Count > 0;
    }


    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);

    }

    protected virtual void Dispose(bool bDisposing)
    {
        if (m_bDisposed)
        {
            return;
        }

        if (bDisposing)
        {
            m_Lock.Dispose();
        }

        m_bDisposed = true;

    }


}
