using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CouchPilot.Server
{
    public static class NetworkInfo
    {
        public static IReadOnlyList<string> LanUrls(int port)
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => $"http://{a.Address}:{port}/")
                .Distinct()
                .ToList();
        }
    }
}
