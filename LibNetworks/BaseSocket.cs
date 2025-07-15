using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibNetworks;

public class BaseSocket
{
    protected System.Net.Sockets.Socket m_Socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
    protected System.Net.Sockets.SocketAsyncEventArgs m_SocketEvent = new System.Net.Sockets.SocketAsyncEventArgs();

    public void Shutdown()
    {

    }

}
