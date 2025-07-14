using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace LibNetworks.Sessions;

public interface IClientSessionFactory
{
    BaseSessionClient Create(Socket clientSocket);
}
