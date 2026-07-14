using Microsoft.Extensions.Logging;

namespace LibNetworks.Sessions;

public class BaseSessionServer : BaseSession
{
    public BaseSessionServer(ILogger<BaseSessionServer> logger, System.Net.Sockets.Socket socket, LibCommons.IBuffers receivedBuffers, LibCommons.IBuffers sendBuffers)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {
    }

    // Server session 옵션 overload: telemetry 제외, send policy 조정
    public BaseSessionServer(
        ILogger<BaseSessionServer> logger,
        System.Net.Sockets.Socket socket,
        LibCommons.IBuffers receivedBuffers,
        LibCommons.IBuffers sendBuffers,
        SessionSendOptions? sendOptions)
        : base(logger, socket, receivedBuffers, sendBuffers, sendOptions)
    {
    }

    public virtual void OnConnected()
    {
        RequestReceived();
    }
}
