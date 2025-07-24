using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace LibCommons;

public class BasePacket
{

    // [PACKET_SIZE][DATA...]
    // 
    public static int HeaderSize => 2; // Packet Header Size (2 bytes for int)
    
    private readonly int m_DataSize = 0; 
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
        m_Data = buffers.Skip(HeaderSize).Take(packetSize - HeaderSize).ToArray();
    }
}
