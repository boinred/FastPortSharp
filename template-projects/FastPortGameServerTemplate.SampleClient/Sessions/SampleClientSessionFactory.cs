using LibCommons;
using LibNetworks.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Sockets;

namespace FastPortGameServerTemplate.SampleClient.Sessions;

public sealed class SampleClientSessionFactory : IServerSessionFactory
{
    private const int BufferCapacityBytes = 8 * 1024;

    private readonly ILogger<BaseSessionServer> m_Logger;
    private readonly EchoSignal m_Signal;
    private readonly IOptions<SampleClientOptions> m_Options;

    public SampleClientSessionFactory(
        ILogger<BaseSessionServer> logger,
        EchoSignal signal,
        IOptions<SampleClientOptions> options)
    {
        m_Logger = logger;
        m_Signal = signal;
        m_Options = options;
    }

    public BaseSessionServer Create(Socket connectedSocket) => new SampleClientSession(
        m_Logger,
        connectedSocket,
        new ArrayPoolCircularBuffers(BufferCapacityBytes),
        new ArrayPoolCircularBuffers(BufferCapacityBytes),
        m_Signal,
        m_Options);
}
