using System.Collections.Concurrent;

namespace LibCommons;

public class BaseCircularBuffer
{
    private readonly byte[] m_Buffers;

    private int m_Head = 0;     // 읽기 시작 위치
    private int m_Tail = 0;     // 쓰기 시작 위치
    private readonly int m_Capacity; // 버퍼의 총 크기
    private readonly ReaderWriterLockSlim m_Lock = new ReaderWriterLockSlim();

    // 버퍼에 현재 데이터 사이즈
    public int CanReadSize { get; private set; } = 0;

    // 버퍼의 여유 공간
    public int CanWriteSize => m_Capacity - CanReadSize;

    public BaseCircularBuffer(int capacity)
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
        if (buffers == null || writeSize == 0)
        {
            return 0; // No data to write
        }

        if (writeSize > CanWriteSize)
        {
            // 용량을 증가 시켜 준다.
        }

        // 데이터가 버퍼의 끝을 넘어서 순환해야 하는 경우
        if (m_Tail + writeSize > m_Capacity)
        {
            int forwardSize = m_Capacity - m_Tail; // 버퍼의 끝까지 쓸 수 있는 크기

            Buffer.BlockCopy(buffers, offset, m_Buffers, m_Tail, forwardSize); // 첫 번째 부분 복사

            int remainSize = writeSize - forwardSize; // 남은 크기
            Buffer.BlockCopy(buffers, offset + forwardSize, m_Buffers, 0, remainSize);
        }
        else // 데이터가 버퍼의 끝을 넘지 않는 경우
        {
            Buffer.BlockCopy(buffers, offset, m_Buffers, m_Tail, writeSize);
        }

        m_Tail = (m_Tail + writeSize) % m_Capacity; // 쓰기 위치 업데이트
        CanReadSize += writeSize;


        return writeSize;
    }

    public int Read(ref byte[] buffers)
    {
        if (CanReadSize <= 0)
        {
            return 0; // 읽을 데이터가 없는 경우
        }

        int buffersSize = buffers.Length;
        if (buffersSize > CanReadSize)
        {
            // 읽을 수 있는 데이터가 부족한 경우
            buffersSize = CanReadSize;
        }

        if (m_Head + buffersSize > m_Capacity) // 데이터가 버퍼의 끝을 넘어서 순환해야 하는 경우
        {
            int forwardSize = m_Capacity - m_Head; // 버퍼의 끝까지 읽을 수 있는 크기
            Buffer.BlockCopy(m_Buffers, m_Head, buffers, 0, forwardSize); // 첫 번째 부분 복사

            int remainSize = (int)buffersSize - forwardSize; // 남은 크기
            Buffer.BlockCopy(m_Buffers, 0, buffers, forwardSize, remainSize);
        }
        else // 데이터가 버퍼의 끝을 넘지 않는 경우
        {
            Buffer.BlockCopy(m_Buffers, m_Head, buffers, 0, buffersSize);
        }

        m_Head = (m_Head + buffersSize) % m_Capacity; // 읽기 위치 업데이트
        CanReadSize -= buffersSize;
        return buffersSize; 
    }
}
