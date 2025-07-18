using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibCommons;

public class BasePacket
{
    // [PACKET_SIZE][DATA...]
    // 
    public static int HeaderSize => 4; // Packet Header Size (4 bytes for int)
    
    private int m_DataSize = 0; 
    private byte[] m_Data;

    public int PacketSize => HeaderSize + m_DataSize; 
    

}
