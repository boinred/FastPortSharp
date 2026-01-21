using Google.Protobuf;
using LibCommons;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using LibNetworks.Extensions;
namespace FastPortClient.Sessions; 

public class FastPortServerSession : LibNetworks.Sessions.BaseSessionServer
{
    public FastPortServerSession(ILogger<FastPortServerSession> logger, System.Net.Sockets.Socket socket, LibCommons.IBuffers receivedBuffers, LibCommons.IBuffers sendBuffers)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {

    }

    public void SendMessage<T>(FastPort.Protocols.Commons.ProtocolId protocolId, T message) where T : IMessage<T> => RequestSendMessage((int)protocolId, message);

    public override void OnConnected()
    {
        base.OnConnected();

        FastPort.Protocols.Commons.Header header = new();
        header.RequestId = 1;
        header.TimestampMs = (ulong)Stopwatch.GetTimestamp();

        FastPort.Protocols.Tests.EchoRequest request = new()
        {
            Header = header,
            DataStr = "Hello FastPort Server",
        };

        request.Data = ByteString.CopyFrom(request.ToByteArray());

        SendMessage(FastPort.Protocols.Commons.ProtocolId.Tests, request);
    }

    protected override void OnReceived(BasePacket basePacket)
    {
        base.OnReceived(basePacket);

        if(!basePacket.ParseMessageFromPacket(out int packetId, out FastPort.Protocols.Tests.EchoResponse? response))
        {
            m_Logger.LogError($"FastPortServerSession, OnReceived, ParseMessageFromPacket failed.");

            return; 
        }

        m_Logger.LogInformation($"FastPortServerSession, OnReceived, Packet Size : {basePacket.PacketSize}, Date Size : {basePacket.DataSize}");

        FastPort.Protocols.Commons.Header header = new();
        header.RequestId = response!.Header.RequestId + 1;
        header.TimestampMs = (ulong)Stopwatch.GetTimestamp();

        FastPort.Protocols.Tests.EchoRequest request = new()
        {
            Header = header,
            DataStr = "Hello FastPort Server",
        };

        request.Data = ByteString.CopyFrom(request.ToByteArray());
        SendMessage(FastPort.Protocols.Commons.ProtocolId.Tests, request);
    }
}

