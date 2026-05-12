namespace FastPortGameServerTemplate.Handlers;

// Design Ref: §3.3 — packet id registry for the template's sample protocol.
// Reserved range: 1000-1999 for sample / template-built-in packets.
// User-defined game packets should start at 2000+.
public static class PacketIds
{
    public const int EchoRequest = 1001;
    public const int EchoResponse = 1002;
}
