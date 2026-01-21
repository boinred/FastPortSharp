using Google.Protobuf;
using LibCommons;
using LibNetworks.Extensions;
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

    public void SendMessage<T>(FastPort.Protocols.Commons.ProtocolId protocolId, T message) where T : IMessage<T> => RequestSendMessage((int)protocolId, message);

    protected override void OnReceived(BasePacket packet)
    {
        base.OnReceived(packet);

        if (!packet.ParseMessageFromPacket(out int packetId, out FastPort.Protocols.Tests.EchoRequest? request))
        {
            m_Logger.LogError($"FastPortClientSession, OnReceived, ParseMessageFromPacket failed.");

            return;
        }

        var header = request!.Header;


        m_Logger.LogInformation($"FastPortClientSession, OnReceived, Packet Size : {packet.PacketSize}, Date Size : {packet.DataSize}, Elapsed Seconds : {(ulong)Stopwatch.GetTimestamp() - header.TimestampMs}, Packet Data : {request.DataStr}");

        FastPort.Protocols.Tests.EchoResponse response = new FastPort.Protocols.Tests.EchoResponse
        {
            Header = new FastPort.Protocols.Commons.Header
            {
                TimestampMs = (ulong)Stopwatch.GetTimestamp(),
                RequestId = header.RequestId + 1,
            },
            Result = FastPort.Protocols.Commons.ResultCode.Ok,
        };


        response.DataStr = request.DataStr;
        response.Data = request.Data;
        SendMessage(FastPort.Protocols.Commons.ProtocolId.Tests, response);
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
