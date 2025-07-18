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
    public FastPortServerSession(ILogger<FastPortServerSession> logger, System.Net.Sockets.Socket socket)
        : base(logger, socket)
    {

    }

    public override void OnConnected()
    {
        
    }
}

