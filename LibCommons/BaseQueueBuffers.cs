using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibCommons;

/// <summary>
/// Queue 기반 버퍼 구현 (성능이 낮아 CircularBuffer 권장)
/// </summary>
public class BaseQueueBuffers(int capacity) : IBuffers, IDisposable
{
    private Queue<byte> m_QueueBuffers = new(capacity);

    // .NET 9+ 경량 Lock 클래스 사용
    private readonly Lock m_Lock = new();

    private bool m_bDisposed = false;

    /// <summary>읽을 수 있는 데이터 크기</summary>
    public int CanReadSize
    {
        get
        {
            lock (m_Lock)
            {
                return m_QueueBuffers.Count;
            }
        }
    }

    /// <summary>쓸 수 있는 여유 공간 크기</summary>
    public int CanWriteSize => m_QueueBuffers.Capacity - CanReadSize;

    /// <summary>버퍼에 데이터를 씁니다</summary>
    public int Write(byte[] buffers, int offset, int count)
    {
        var data = new ReadOnlySpan<byte>(buffers, offset, count);

        lock (m_Lock)
        {
            foreach (byte b in data)
            {
                m_QueueBuffers.Enqueue(b);
            }
            return count;
        }
    }

    /// <summary>버퍼에서 데이터를 읽습니다 (제거하지 않음)</summary>
    public int Peek(ref byte[] buffers)
    {
        lock (m_Lock)
        {
            buffers = m_QueueBuffers.ToArray();
            return buffers.Length;
        }
    }

    /// <summary>버퍼에서 지정된 크기만큼 데이터를 제거합니다</summary>
    public int Drain(int size)
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

    /// <summary>버퍼에서 완전한 패킷들을 추출합니다</summary>
    public bool TryGetBasePackets(out List<BasePacket> basePackets)
    {
        int readBuffersSize = 0;

        lock (m_Lock)
        {
            basePackets = [];

            while (readBuffersSize <= m_QueueBuffers.Count)
            {
                if (readBuffersSize + 1 >= m_QueueBuffers.Count)
                {
                    break;
                }

                // 패킷 크기를 큐에서 읽음
                byte[] packetIdBuffers = new byte[BasePacket.HeaderSize];
                m_QueueBuffers.CopyTo(packetIdBuffers, BasePacket.HeaderSize);

                int basePacketSize = packetIdBuffers[0] << 0 | packetIdBuffers[1] << 8;
                readBuffersSize += BasePacket.HeaderSize;

                if (basePacketSize + readBuffersSize >= m_QueueBuffers.Count)
                {
                    break;
                }

                // 헤더 제거
                for (int i = 0; i < BasePacket.HeaderSize; i++)
                {
                    m_QueueBuffers.Dequeue();
                }
                Drain(BasePacket.HeaderSize);

                // 패킷 데이터 추출
                byte[] packetDataBuffers = new byte[basePacketSize];
                for (int i = 0; i < basePacketSize; i++)
                {
                    if (m_QueueBuffers.Count == 0)
                    {
                        break;
                    }
                    packetDataBuffers[i] = m_QueueBuffers.Dequeue();
                }
                Drain(basePacketSize);
            }
        }

        return basePackets.Count > 0;
    }

    /// <summary>리소스를 해제합니다</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>리소스 해제 구현</summary>
    protected virtual void Dispose(bool bDisposing)
    {
        if (m_bDisposed)
        {
            return;
        }

        // Lock은 IDisposable을 구현하지 않으므로 Dispose 불필요
        m_bDisposed = true;
    }
}
