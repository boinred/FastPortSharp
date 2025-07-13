using System.Net;

namespace LibNetworks;

public static class AddressConverter
{
    
    public static bool TryToEndPoint(string ip, int port, out IPEndPoint? endPoint)
    {
        endPoint = default;

        if (!IPAddress.TryParse(ip, out var ipAddress))
        {
            // LOGGER 
            return false; 
        }

        try
        {
            endPoint = new System.Net.IPEndPoint(ipAddress, port);
            return true; 
        }
        catch (System.Exception ex)
        {

        }


        return false;
    }
}