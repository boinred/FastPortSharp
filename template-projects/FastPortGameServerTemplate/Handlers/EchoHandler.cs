using FastPortGameServerTemplate.Protocols;
using FastPortGameServerTemplate.Sessions;
using LibCommons;
using LibNetworks.Extensions;
using Microsoft.Extensions.Logging;

namespace FastPortGameServerTemplate.Handlers;

// Design Ref: §4.1, §11.1 — sample handler that round-trips an EchoRequest as EchoResponse.
// Replace this with your own handlers when bootstrapping a game server from this template.
public sealed class EchoHandler : IPacketHandler
{
    private readonly ILogger<EchoHandler> m_Logger;

    public EchoHandler(ILogger<EchoHandler> logger)
    {
        m_Logger = logger;
    }

    public int PacketId => (int)PacketIds.EchoRequest;

    public void Handle(GameSession session, BasePacket packet)
    {
        if (!packet.ParseMessageFromPacket<EchoRequest>(out _, out var request) || request is null)
        {
            m_Logger.LogWarning(
                "EchoHandler failed to parse EchoRequest. SessionId={SessionId}, DataSize={DataSize}",
                session.Id, packet.DataSize);
            return;
        }

        var response = new EchoResponse
        {
            Message = request.Message,
            ServerUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        session.Send((int)PacketIds.EchoResponse, response);

        m_Logger.LogDebug(
            "Echoed back. SessionId={SessionId}, MessageLen={Len}",
            session.Id, request.Message.Length);
    }
}
