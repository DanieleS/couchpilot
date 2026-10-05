using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CouchPilot.Server
{
    public static class NetworkInfo
    {
        // hostName: the certificate's name, listed first since it's the only address the
        // certificate is sure to cover.
        public static IReadOnlyList<string> LanUrls(int port, bool secure = false, string hostName = null)
        {
            var scheme = secure ? "https" : "http";
            var hosts = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString());
            if (hostName != null) hosts = new[] { hostName }.Concat(hosts);
            return hosts.Select(h => $"{scheme}://{h}:{port}/").Distinct().ToList();
        }
    }
}
