using System;

namespace LibCommons;

public class BasePacket
{

    // [PACKET_SIZE][DATA...]
    // 
    public static int HeaderSize => 2; // Packet Header Size (2 bytes for int)
    
    // 상태: packet header를 제외한 payload byte 수
    private readonly int m_DataSize = 0;
    // 상태: packet payload 복사본, 수신 버퍼 반환 이후에도 안전하게 유지
    private readonly byte[] m_Data;

    // Packet Size 
    public int PacketSize => HeaderSize + m_DataSize;
    
    // Data Size 
    public int DataSize => m_DataSize; 
    
    // Data 
    public ReadOnlySpan<byte> Data => m_Data.AsSpan();

    public BasePacket(int packetSize, byte[] buffers)
    {
        m_DataSize = packetSize - BasePacket.HeaderSize;
        m_Data = new byte[m_DataSize];
        // 목적: LINQ iterator와 ToArray 추가 비용 없이 payload만 직접 복사
        buffers.AsSpan(HeaderSize, m_DataSize).CopyTo(m_Data);
    }
}
