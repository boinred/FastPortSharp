using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FastPortClient.Sessions; 

public class FastPortServerSession : LibNetworks.Sessions.BaseSessionServer
{
    public FastPortServerSession(ILogger<FastPortServerSession> logger, System.Net.Sockets.Socket socket, LibCommons.IBuffers receivedBuffers, LibCommons.IBuffers sendBuffers)
        : base(logger, socket, receivedBuffers, sendBuffers)
    {

    }

    public override void OnConnected()
    {
        RequestSendString("Hello world. FastPortServerSession connected.");

    }
}

