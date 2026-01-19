using Google.Protobuf;
using LibCommons;
using LibNetworks;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Sockets;

namespace FastPortServer.Sessions;

public class FastPortClientSession : LibNetworks.Sessions.BaseSessionClient
{
    private static readonly LibCommons.IDGenerator m_IdGenerator = new IDGenerator();

    private readonly long m_Id = m_IdGenerator.GetNextGeneratedId();

    public override long Id => m_Id;


    public FastPortClientSession(ILogger<LibNetworks.Sessions.BaseSessionClient> logger, Socket socket, LibCommons.IBuffers receivedBuffers, LibCommons.IBuffers sendBuffers)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {

    }

    public void SendMessage<T>(FastPort.Protocols.EPacketId ePacketId, T message) where T : IMessage<T> => RequestSendMessage((int)ePacketId, message);

    protected override void OnReceived(BasePacket packet)
    {
        base.OnReceived(packet);

        if (!ParseMessageFromPacket(packet, out int packetId, out FastPort.Protocols.TestRequest? request))
        {
            m_Logger.LogError($"FastPortClientSession, OnReceived, ParseMessageFromPacket failed.");

            return; 
        }

        FastPort.Protocols.Character character = FastPort.Protocols.Character.Parser.ParseFrom(request!.Binaries);

        m_Logger.LogInformation($"FastPortClientSession, OnReceived, Packet Size : {packet.PacketSize}, Date Size : {packet.DataSize}, Elapsed Seconds : {Stopwatch.GetTimestamp() - character.TickCount}");

        FastPort.Protocols.TestResponse response = new();
        Debug.Assert(null != response);
        if(null != request )
        {
            response.Binaries = request.Binaries;
            response.Name = request.Name;
            SendMessage(FastPort.Protocols.EPacketId.TestResponse, response);
        }
    }

    public override void OnAccepted()
    {
        base.OnAccepted();

        m_Logger.LogInformation($"FastPortClientSession, OnAccepted. Id : {Id}");
    }

    protected override void OnDisconnected()
    {
        base.OnDisconnected();

        m_Logger.LogInformation($"FastPortClientSession, OnDisconnected. Remote End Point : {GetSessionAddress()}");
    }
}
