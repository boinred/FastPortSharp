using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace LibNetworks.Extensions;

public static class SocketExtensions
{
    public static void SetKeepAlive(this System.Net.Sockets.Socket socket, int keepAliveTime = 1000, int keepAliveInterval = 1000)
    {

        // Windows 환경에서 TCP Keep-Alive 상세 설정 예시
        // 1. KeepAlive 활성화
        // 2. KeepAlive 신호를 보내기 시작하기까지의 유휴 시간 (ms)
        // 3. KeepAlive 신호 재전송 간격 (ms)

        byte[] keepAliveOptions = new byte[12];
        // 1. KeepAlive 활성화 (1 = true)
        BitConverter.GetBytes(1).CopyTo(keepAliveOptions, 0);
        // 2. 유휴 시간: 30초 (30000 ms)
        BitConverter.GetBytes(keepAliveTime).CopyTo(keepAliveOptions, 4);
        // 3. 재전송 간격: 5초 (5000 ms)
        BitConverter.GetBytes(keepAliveInterval).CopyTo(keepAliveOptions, 8);

        // 소켓에 IOControl을 사용하여 옵션 적용
        // SIO_KEEPALIVE_VALS 코드를 사용
        socket.IOControl(IOControlCode.KeepAliveValues, keepAliveOptions, null);

    }
}
