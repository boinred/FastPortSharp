using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibCommons;

public interface IBuffers
{
    int CanReadSize { get; }
    int CanWriteSize { get; }

    // 버퍼에 데이터 쓰기.
    int Write(byte[] buffers, int offset, int count);

    // 데이터를 읽기. 실제 읽은 버퍼를 제거 하지 않는다.
    int Peek(ref byte[] buffers);

    // 버퍼에서 데이터를 제거.
    int Drain(int size);

    bool TryGetBasePackets(out List<BasePacket> basePackets);
}
