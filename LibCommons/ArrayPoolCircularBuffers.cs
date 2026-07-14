using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace LibCommons;

/// <summary>
/// Circular buffer backed by ArrayPool.
/// - Reuses rented arrays to reduce GC pressure.
/// - Uses System.Threading.Lock for short critical sections.
/// - Uses Span-based copy paths to avoid intermediate allocations.
/// </summary>
public sealed class ArrayPoolCircularBuffers : IBuffers, IDisposable
{
    private bool m_bDisposed;
    private byte[] m_Buffers;

    // Next read position.
    private int m_Head;

    // Next write position.
    private int m_Tail;

    // Logical capacity requested by the buffer. The rented array may be larger.
    private int m_Capacity;

    // Lightweight lock available on recent .NET versions.
    private readonly Lock m_Lock = new();

    // Number of bytes currently available to read.
    public int CanReadSize { get; private set; }

    // Number of bytes available before the next expansion.
    public int CanWriteSize => m_Capacity - CanReadSize;

    public ArrayPoolCircularBuffers(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0, nameof(capacity));

        m_Capacity = capacity;
        m_Buffers = ArrayPool<byte>.Shared.Rent(capacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Write(byte[] buffers, int offset, int count)
    {
        if (buffers == null || count == 0)
        {
            return 0;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(offset, nameof(offset));
        ArgumentOutOfRangeException.ThrowIfNegative(count, nameof(count));
        if (offset > buffers.Length || count > buffers.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "The offset and count range must fit within the source buffer.");
        }

        return Write(buffers.AsSpan(offset, count));
    }

    /// <summary>
    /// Writes bytes from a span without creating an intermediate array.
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
            ThrowIfDisposed();
            EnsureWritableCapacity(source.Length);
            WriteInternal(source);
            return source.Length;
        }
    }

    /// <summary>
    /// Copies readable bytes into the supplied array without removing them.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Peek(ref byte[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        return Peek(buffers.AsSpan());
    }

    /// <summary>
    /// Copies readable bytes into the supplied span without removing them.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Peek(Span<byte> destination)
    {
        if (destination.IsEmpty)
        {
            return 0;
        }

        lock (m_Lock)
        {
            ThrowIfDisposed();
            if (CanReadSize <= 0)
            {
                return 0;
            }

            int readSize = Math.Min(destination.Length, CanReadSize);
            ReadInternal(destination[..readSize]);
            return readSize;
        }
    }

    public int GetPacketBuffers(out byte[]? buffers, int size)
    {
        if (size <= 0)
        {
            buffers = null;
            return 0;
        }

        lock (m_Lock)
        {
            ThrowIfDisposed();
            return GetPacketBuffersCore(out buffers, size);
        }
    }

    /// <summary>
    /// Returns a buffer rented by GetPacketBuffers.
    /// </summary>
    public static void ReturnBuffer(byte[]? buffer)
    {
        if (buffer != null)
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Drain(int size)
    {
        if (size <= 0)
        {
            return 0;
        }

        lock (m_Lock)
        {
            ThrowIfDisposed();
            return DrainCore(size);
        }
    }

    public bool TryGetBasePackets(out List<BasePacket> basePackets)
    {
        basePackets = [];

        lock (m_Lock)
        {
            ThrowIfDisposed();

            while (CanReadSize >= BasePacket.HeaderSize)
            {
                int basePacketSize = GetPacketSizeInBuffersCore();
                if (basePacketSize < BasePacket.HeaderSize || CanReadSize < basePacketSize)
                {
                    break;
                }

                int readBufferSize = GetPacketBuffersCore(out var buffers, basePacketSize);
                if (readBufferSize <= 0 || buffers == null)
                {
                    break;
                }

                try
                {
                    basePackets.Add(new BasePacket(basePacketSize, buffers));
                }
                finally
                {
                    ReturnBuffer(buffers);
                }
            }
        }

        return basePackets.Count > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetPacketSizeInBuffers()
    {
        lock (m_Lock)
        {
            ThrowIfDisposed();
            return GetPacketSizeInBuffersCore();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool bDisposing)
    {
        if (!bDisposing)
        {
            return;
        }

        byte[]? bufferToReturn = null;
        lock (m_Lock)
        {
            if (m_bDisposed)
            {
                return;
            }

            bufferToReturn = m_Buffers;
            m_Buffers = [];
            m_Head = 0;
            m_Tail = 0;
            m_Capacity = 0;
            CanReadSize = 0;
            m_bDisposed = true;
        }

        ArrayPool<byte>.Shared.Return(bufferToReturn, clearArray: false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureWritableCapacity(int writeSize)
    {
        if (writeSize > CanWriteSize)
        {
            ExpandBuffer(writeSize - CanWriteSize);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteInternal(ReadOnlySpan<byte> source)
    {
        int writeSize = source.Length;
        int forwardSize = Math.Min(writeSize, m_Capacity - m_Tail);

        source[..forwardSize].CopyTo(m_Buffers.AsSpan(m_Tail, forwardSize));
        source[forwardSize..].CopyTo(m_Buffers.AsSpan(0, writeSize - forwardSize));

        m_Tail = (m_Tail + writeSize) % m_Capacity;
        CanReadSize += writeSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReadInternal(Span<byte> destination)
    {
        int readSize = destination.Length;
        int forwardSize = Math.Min(readSize, m_Capacity - m_Head);

        m_Buffers.AsSpan(m_Head, forwardSize).CopyTo(destination[..forwardSize]);
        m_Buffers.AsSpan(0, readSize - forwardSize).CopyTo(destination[forwardSize..]);
    }

    private void ExpandBuffer(int additionalSize)
    {
        int minimumCapacity = checked(m_Capacity + additionalSize);
        int newCapacity = GrowCapacity(m_Capacity, minimumCapacity);
        byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);

        if (CanReadSize > 0)
        {
            ReadInternal(newBuffer.AsSpan(0, CanReadSize));
        }

        ArrayPool<byte>.Shared.Return(m_Buffers, clearArray: false);

        m_Buffers = newBuffer;
        m_Head = 0;
        m_Tail = CanReadSize;
        m_Capacity = newCapacity;
    }

    private int GetPacketBuffersCore(out byte[]? buffers, int size)
    {
        if (CanReadSize < size)
        {
            buffers = null;
            return 0;
        }

        buffers = ArrayPool<byte>.Shared.Rent(size);
        ReadInternal(buffers.AsSpan(0, size));
        return DrainCore(size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int DrainCore(int size)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetPacketSizeInBuffersCore()
    {
        if (CanReadSize < BasePacket.HeaderSize)
        {
            return 0;
        }

        if (m_Head + BasePacket.HeaderSize <= m_Capacity)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(m_Buffers.AsSpan(m_Head, BasePacket.HeaderSize));
        }

        Span<byte> headerBytes = stackalloc byte[BasePacket.HeaderSize];
        ReadInternal(headerBytes);
        return BinaryPrimitives.ReadUInt16LittleEndian(headerBytes);
    }

    private static int GrowCapacity(int currentCapacity, int minimumCapacity)
    {
        int newCapacity = currentCapacity;
        while (newCapacity < minimumCapacity)
        {
            int nextCapacity = newCapacity <= Array.MaxLength / 2
                ? newCapacity * 2
                : Array.MaxLength;

            if (nextCapacity == newCapacity)
            {
                throw new OutOfMemoryException("The circular buffer cannot grow to the requested size.");
            }

            newCapacity = nextCapacity;
        }

        return newCapacity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_bDisposed, this);
    }
}
