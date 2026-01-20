using System.Buffers;
using System.Runtime.CompilerServices;

namespace LibCommons;

/// <summary>
/// ArrayPool 기반 고성능 순환 버퍼
/// - ArrayPool을 사용하여 메모리 재사용
/// - .NET 9+ Lock 클래스 사용
/// - Span 기반 Zero-copy 최적화
/// </summary>
public sealed class ArrayPoolCircularBuffers : IBuffers, IDisposable
{
    private bool m_bDisposed = false;

    private byte[] m_Buffers;

    // 읽기 시작 위치
    private int m_Head = 0;

    // 쓰기 시작 위치
    private int m_Tail = 0;

    // 버퍼의 총 크기 (ArrayPool에서 받은 실제 크기와 다를 수 있음)
    private int m_Capacity;

    // 실제 ArrayPool에서 받은 버퍼 크기
    private int m_ActualBufferSize;

    // .NET 9+ 경량 Lock 클래스 사용
    private readonly Lock m_Lock = new();


    // 버퍼에 현재 데이터 사이즈
    public int CanReadSize { get; private set; } = 0;

    // 버퍼의 여유 공간
    public int CanWriteSize => m_Capacity - CanReadSize;

    public ArrayPoolCircularBuffers(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0, nameof(capacity));

        m_Capacity = capacity;
        m_Buffers = ArrayPool<byte>.Shared.Rent(capacity);
        m_ActualBufferSize = m_Buffers.Length;
        CanReadSize = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Write(byte[] buffers, int offset, int count)
    {
        if (buffers == null || count == 0)
        {
            return 0;
        }

        lock (m_Lock)
        {
            if (count > CanWriteSize)
            {
                ExpandBuffer(count - CanWriteSize);
            }

            WriteInternal(buffers.AsSpan(offset, count));
            return count;
        }
    }

    /// <summary>
    /// Span 기반 Write - Zero-copy 최적화
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Write(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
        {
            return 0;
        }

        lock (m_Lock)
        {
            if (source.Length > CanWriteSize)
            {
                ExpandBuffer(source.Length - CanWriteSize);
            }

            WriteInternal(source);
            return source.Length;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteInternal(ReadOnlySpan<byte> source)
    {
        int writeSize = source.Length;

        // 데이터가 버퍼의 끝을 넘어서 순환해야 하는 경우
        if (m_Tail + writeSize > m_Capacity)
        {
            int forwardSize = m_Capacity - m_Tail;
            source[..forwardSize].CopyTo(m_Buffers.AsSpan(m_Tail));
            source[forwardSize..].CopyTo(m_Buffers.AsSpan(0));
        }
        else
        {
            source.CopyTo(m_Buffers.AsSpan(m_Tail));
        }

        m_Tail = (m_Tail + writeSize) % m_Capacity;
        CanReadSize += writeSize;
    }

    /// <summary>
    /// 버퍼 확장 - ArrayPool 기반
    /// </summary>
    private void ExpandBuffer(int additionalSize)
    {
        // 최소 2배 또는 필요한 크기만큼 증가
        int newCapacity = Math.Max(m_Capacity + additionalSize, m_Capacity * 2);
        byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);

        // 기존 데이터 복사 (순환 버퍼 고려)
        if (CanReadSize > 0)
        {
            if (m_Head + CanReadSize > m_Capacity)
            {
                // 데이터가 버퍼 끝을 넘어 순환하는 경우
                int forwardSize = m_Capacity - m_Head;
                m_Buffers.AsSpan(m_Head, forwardSize).CopyTo(newBuffer);
                m_Buffers.AsSpan(0, CanReadSize - forwardSize).CopyTo(newBuffer.AsSpan(forwardSize));
            }
            else
            {
                m_Buffers.AsSpan(m_Head, CanReadSize).CopyTo(newBuffer);
            }
        }

        // 기존 버퍼 반환
        ArrayPool<byte>.Shared.Return(m_Buffers);

        m_Buffers = newBuffer;
        m_ActualBufferSize = newBuffer.Length;
        m_Head = 0;
        m_Tail = CanReadSize;
        m_Capacity = newCapacity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Peek(ref byte[] buffers)
    {
        lock (m_Lock)
        {
            if (CanReadSize <= 0)
            {
                return 0;
            }

            int buffersSize = Math.Min(buffers.Length, CanReadSize);
            ReadInternal(buffers.AsSpan(0, buffersSize));
            return buffersSize;
        }
    }

    /// <summary>
    /// Span 기반 Peek - Zero-copy 최적화
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Peek(Span<byte> destination)
    {
        lock (m_Lock)
        {
            if (CanReadSize <= 0)
            {
                return 0;
            }

            int readSize = Math.Min(destination.Length, CanReadSize);
            ReadInternal(destination[..readSize]);
            return readSize;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReadInternal(Span<byte> destination)
    {
        if (m_Head + destination.Length > m_Capacity)
        {
            // 데이터가 버퍼 끝을 넘어 순환하는 경우
            int forwardSize = m_Capacity - m_Head;
            m_Buffers.AsSpan(m_Head, forwardSize).CopyTo(destination);
            m_Buffers.AsSpan(0, destination.Length - forwardSize).CopyTo(destination[forwardSize..]);
        }
        else
        {
            m_Buffers.AsSpan(m_Head, destination.Length).CopyTo(destination);
        }
    }

    public int GetPacketBuffers(out byte[]? buffers, int size)
    {
        if (CanReadSize < size)
        {
            buffers = null;
            return 0;
        }

        // ArrayPool에서 버퍼 대여
        buffers = ArrayPool<byte>.Shared.Rent(size);
        ReadInternal(buffers.AsSpan(0, size));

        return Drain(size);
    }

    /// <summary>
    /// ArrayPool 버퍼를 반환하는 정적 메서드
    /// GetPacketBuffers로 받은 버퍼는 사용 후 반드시 반환해야 함
    /// </summary>
    public static void ReturnBuffer(byte[] buffer)
    {
        if (buffer != null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Drain(int size)
    {
        size = Math.Min(size, CanReadSize);
        if (size <= 0)
        {
            return 0;
        }

        m_Head = (m_Head + size) % m_Capacity;
        CanReadSize -= size;

        return size;
    }

    public bool TryGetBasePackets(out List<BasePacket> basePackets)
    {
        basePackets = [];

        lock (m_Lock)
        {
            while (CanReadSize > BasePacket.HeaderSize)
            {
                int basePacketSize = GetPacketSizeInBuffers();
                if (CanReadSize < basePacketSize || basePacketSize <= 0)
                {
                    break;
                }

                int readBufferSize = GetPacketBuffers(out var buffers, basePacketSize);
                if (readBufferSize <= 0 || buffers == null)
                {
                    break;
                }

                // Drain은 GetPacketBuffers에서 이미 호출됨

                var basePacket = new BasePacket(basePacketSize, buffers);
                basePackets.Add(basePacket);

                // ArrayPool 버퍼 반환
                ReturnBuffer(buffers);
            }
        }

        return basePackets.Count > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetPacketSizeInBuffers()
    {
        if (CanReadSize < BasePacket.HeaderSize)
        {
            return 0;
        }

        // stackalloc으로 헤더 읽기 (힙 할당 없음)
        Span<byte> headerBytes = stackalloc byte[BasePacket.HeaderSize];

        int index = m_Head;
        for (int i = 0; i < BasePacket.HeaderSize; i++)
        {
            headerBytes[i] = m_Buffers[index];
            index = (index + 1) % m_Capacity;
        }

        return BitConverter.ToUInt16(headerBytes);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool bDisposing)
    {
        if (m_bDisposed)
        {
            return;
        }

        if (bDisposing)
        {
            // ArrayPool에 버퍼 반환
            ArrayPool<byte>.Shared.Return(m_Buffers);
        }

        m_bDisposed = true;
    }
}
