using Google.Protobuf;
using LibCommons;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FastPortClient.Sessions; 

public class FastPortServerSession : LibNetworks.Sessions.BaseSessionServer
{
    public FastPortServerSession(ILogger<FastPortServerSession> logger, System.Net.Sockets.Socket socket, LibCommons.IBuffers receivedBuffers, LibCommons.IBuffers sendBuffers)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {

    }

    public void SendMessage<T>(FastPort.Protocols.EPacketId ePacketId, T message) where T : IMessage<T> => RequestSendMessage((int)ePacketId, message);

    public override void OnConnected()
    {
        base.OnConnected();

        FastPort.Protocols.TestRequest request = new()
        {
            Name = Protocols.TestMessage.C_MESSAGE_CLIENT
        };
        FastPort.Protocols.Character character = new()
        {
            TickCount = Stopwatch.GetTimestamp(),
            Name = request.Name
        };
        request.Binaries = ByteString.CopyFrom(character.ToByteArray());

        SendMessage(FastPort.Protocols.EPacketId.TestRequest, request);
    }

    protected override void OnReceived(BasePacket basePacket)
    {
        base.OnReceived(basePacket);

        if(!ParseMessageFromPacket(basePacket, out int packetId, out FastPort.Protocols.TestResponse? response))
        {
            m_Logger.LogError($"FastPortServerSession, OnReceived, ParseMessageFromPacket failed.");

            return; 
        }

        Debug.Assert(response!.Name != Protocols.TestMessage.C_MESSAGE_SERVER);

        m_Logger.LogInformation($"FastPortServerSession, OnReceived, Packet Size : {basePacket.PacketSize}, Date Size : {basePacket.DataSize}");

        FastPort.Protocols.TestRequest request = new()
        {
            Name = Protocols.TestMessage.C_MESSAGE_CLIENT
        };
        FastPort.Protocols.Character character = new()
        {
            TickCount = Stopwatch.GetTimestamp(),
            Name = request.Name
        };
        request.Binaries = ByteString.CopyFrom(character.ToByteArray());

        SendMessage(FastPort.Protocols.EPacketId.TestRequest, request);
    }
}

