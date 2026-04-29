using LibNetworks.Telemetry;
using Microsoft.Extensions.Logging;

namespace LibNetworks.Sessions;

public class BaseSessionServer : BaseSession
{
    public BaseSessionServer(ILogger<BaseSessionServer> logger, System.Net.Sockets.Socket socket, LibCommons.IBuffers receivedBuffers, LibCommons.IBuffers sendBuffers)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {
    }

    public BaseSessionServer(ILogger<BaseSessionServer> logger, System.Net.Sockets.Socket socket, LibCommons.IBuffers receivedBuffers, LibCommons.IBuffers sendBuffers, IServerTelemetry serverTelemetry)
        : base(logger, socket, receivedBuffers, sendBuffers, serverTelemetry)
    {
    }

    public BaseSessionServer(
        ILogger<BaseSessionServer> logger,
        System.Net.Sockets.Socket socket,
        LibCommons.IBuffers receivedBuffers,
        LibCommons.IBuffers sendBuffers,
        IServerTelemetry serverTelemetry,
        SessionSendOptions? sendOptions)
        : base(logger, socket, receivedBuffers, sendBuffers, serverTelemetry, sendOptions)
    {
    }

    public virtual void OnConnected()
    {
        RequestReceived();
    }
}
