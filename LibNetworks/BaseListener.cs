using System.Net;
using Microsoft.Extensions.Logging;

namespace LibNetworks;

public class BaseListener(ILogger<BaseListener> logger)
{
    // TODO : 파일 설정에서 불러온다.
    private readonly int C_MaxConnections = 1000;
    private readonly int C_MaxBufferSize = 1024 * 8; // 8KB

    private System.Net.Sockets.Socket m_ListenerSocket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);

    public bool Start(string ip, int port)
    {
        if (!AddressConverter.TryToEndPoint(ip, port, out var endPoint))
        {
            logger.LogError($"BaseListener, Start, IP is not valid. ${ip}");
            return false;
        }

        try
        {
            m_ListenerSocket.Bind(endPoint!);

            m_ListenerSocket.Listen(100);

            return false;
        }
        catch (System.Exception ex)
        {

        }

        return true;
    }

}
