using FastPortGameServerTemplate.Sessions;

namespace FastPortGameServerTemplate.Handlers;

// Design Ref: §4.1 — domain-level handler contract.
// Pure POCO interface, no Microsoft.Extensions.* / LibNetworks dependency.
// Add new handlers by implementing this interface and registering them in PacketDispatcher.
public interface IPacketHandler
{
    int PacketId { get; }

    void Handle(GameSession session, LibCommons.BasePacket packet);
}
