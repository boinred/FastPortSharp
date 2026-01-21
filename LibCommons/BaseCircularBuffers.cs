using System.Buffers;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace LibCommons;

public class BaseCircularBuffers : IBuffers, IDisposable
{
    private bool m_bDisposed = false; // Dispose 여부 확인

    private byte[] m_Buffers;

    // 읽기 시작 위치
    private int m_Head = 0;

    // 쓰기 시작 위치
    private int m_Tail = 0;

    // 버퍼의 총 크기
    private int m_Capacity;

    private readonly ReaderWriterLockSlim m_Lock = new ReaderWriterLockSlim();

    // 버퍼에 현재 데이터 사이즈
    public int CanReadSize { get; private set; } = 0;

    // 버퍼의 여유 공간
    public int CanWriteSize => m_Capacity - CanReadSize;


    public BaseCircularBuffers(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentException("CirtularBuffer, Capacity must be greater than zero.", nameof(capacity));
        }

        m_Capacity = capacity;
        m_Buffers = new byte[capacity];
        CanReadSize = 0;
    }

    public int Write(byte[] buffers, int offset, int count)
    {
        int writeSize = count;

        m_Lock.EnterWriteLock();
        try
        {
            if (buffers == null || writeSize == 0)
            {
                return 0; // No data to write
            }

            if (writeSize > CanWriteSize)
            {
                // 용량을 증가 시켜 준다.
                var increaseSize = writeSize - CanWriteSize; // 증가시켜야 하는 크기
                ExpandBuffer(increaseSize);
            }

            // 순환 버퍼에 데이터 복사
            CopyToCircularBuffer(buffers.AsSpan(offset, writeSize), m_Tail);

            m_Tail = (m_Tail + writeSize) % m_Capacity; // 쓰기 위치 업데이트
            CanReadSize += writeSize;
        }
        finally
        {
            m_Lock.ExitWriteLock();
        }

        return writeSize;
    }

    // 버퍼에서 데이터를 읽어오는 메서드, 데이터는 버퍼에서 제거하지 않는다.
    public int Peek(ref byte[] buffers)
    {
        int buffersSize = buffers.Length;

        m_Lock.EnterReadLock();
        try
        {
            if (CanReadSize <= 0)
            {
                return 0; // 읽을 데이터가 없는 경우
            }

            if (buffersSize > CanReadSize)
            {
                // 읽을 수 있는 데이터가 부족한 경우
                buffersSize = CanReadSize;
            }

            // 순환 버퍼에서 데이터 복사
            CopyFromCircularBuffer(buffers.AsSpan(0, buffersSize), m_Head);
        }
        finally
        {
            m_Lock.ExitReadLock();
        }

        return buffersSize;
    }

    public int GetPacketBuffers(out byte[]? buffers, int size)
    {
        if (CanReadSize < size)
        {
            buffers = null;
            return 0;
        }

        buffers = new byte[size]; // 읽을 버퍼 생성
        
        // 순환 버퍼에서 데이터 복사
        CopyFromCircularBuffer(buffers.AsSpan(), m_Head);

        return Drain(size);
    }

    public int Drain(int size)
    {
        if (size > CanReadSize)
        {
            size = CanReadSize;
        }

        if (size <= 0)
        {
            return 0;
        }

        m_Head = (m_Head + size) % m_Capacity; // 읽기 위치 업데이트
        CanReadSize -= size; // 읽은 데이터 크기만큼 감소

        return size;
    }

    public bool TryGetBasePackets(out List<BasePacket> basePackets)
    {
        basePackets = new();

        int basePacketSize = 0;

        m_Lock.EnterWriteLock();
        try
        {
            // 패킷이 전체다 읽을 수 있는지 확인
            do
            {
                int currentBuffersSize = CanReadSize;
                if (CanReadSize <= BasePacket.HeaderSize)
                {
                    break;
                }

                // 패킷 크기 계산
                basePacketSize = GetPacketSizeInBuffers();
                if (currentBuffersSize < basePacketSize)
                {
                    break;
                }

                int readBufferSize = GetPacketBuffers(out var buffers, basePacketSize);
                if (readBufferSize <= 0 || null == buffers)
                {
                    break;
                }

                Drain(readBufferSize);

                var basePacket = new BasePacket(basePacketSize, buffers);
                basePackets.Add(basePacket);

            }
            while (CanReadSize >= basePacketSize);
        }
        finally
        {
            m_Lock.ExitWriteLock();
        }

        return basePackets.Count > 0;
    }

    public int GetPacketSizeInBuffers()
    {
        int size = BasePacket.HeaderSize;
        if (CanReadSize < size)
        {
            return 0; // 읽을 수 있는 패킷이 없음
        }

        int packetSize = 0;

        var index = m_Head;
        for (int i = 0; i < size; i++)
        {

            if (index > m_Tail)
            {
                index = 0;
            }
            packetSize |= m_Buffers[index] << i;
            index = (index + 1) % m_Capacity;
        }

        return packetSize;
    }

    #region Private Helper Methods

    /// <summary>
    /// 순환 버퍼에 데이터를 복사합니다 (쓰기용)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CopyToCircularBuffer(ReadOnlySpan<byte> source, int startIndex)
    {
        int size = source.Length;

        // 데이터가 버퍼의 끝을 넘어서 순환해야 하는 경우
        if (startIndex + size > m_Capacity)
        {
            int forwardSize = m_Capacity - startIndex;
            source[..forwardSize].CopyTo(m_Buffers.AsSpan(startIndex));
            source[forwardSize..].CopyTo(m_Buffers.AsSpan(0));
        }
        else
        {
            source.CopyTo(m_Buffers.AsSpan(startIndex));
        }
    }

    /// <summary>
    /// 순환 버퍼에서 데이터를 복사합니다 (읽기용)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CopyFromCircularBuffer(Span<byte> destination, int startIndex)
    {
        int size = destination.Length;

        // 데이터가 버퍼의 끝을 넘어서 순환해야 하는 경우
        if (startIndex + size > m_Capacity)
        {
            int forwardSize = m_Capacity - startIndex;
            m_Buffers.AsSpan(startIndex, forwardSize).CopyTo(destination);
            m_Buffers.AsSpan(0, size - forwardSize).CopyTo(destination[forwardSize..]);
        }
        else
        {
            m_Buffers.AsSpan(startIndex, size).CopyTo(destination);
        }
    }

    /// <summary>
    /// 버퍼 용량을 확장합니다
    /// </summary>
    private void ExpandBuffer(int requiredSize)
    {
        int newCapacity = m_Capacity;
        while (newCapacity - CanReadSize < requiredSize)
        {
            newCapacity *= 2; // 버퍼 크기를 두 배로 증가
        }

        byte[] newBuffers = new byte[newCapacity];

        // 기존 데이터를 새 버퍼로 복사
        if (CanReadSize > 0)
        {
            CopyFromCircularBuffer(newBuffers.AsSpan(0, CanReadSize), m_Head);
        }

        m_Buffers = newBuffers;
        m_Capacity = newCapacity;
        m_Head = 0;
        m_Tail = CanReadSize;
    }

    #endregion

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
