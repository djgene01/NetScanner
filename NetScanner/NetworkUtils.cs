using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetScanner
{
    public static class NetworkUtils
    {
        private const int ConnectTimeoutMs = 300;
        private const int PingTimeoutMs = 300;
        private const int SsdpTimeoutMs = 1000;

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint physicalAddrLen);

        /// <summary>
        /// Checks if a given port is open by attempting a TcpClient connection within a short timeout.
        /// </summary>
        public static async Task<bool> IsPortOpenAsync(string ip, int port)
        {
            using var cts = new CancellationTokenSource(ConnectTimeoutMs);
            using var client = new TcpClient();
            try
            {
                var connectTask = client.ConnectAsync(ip, port);
                using (cts.Token.Register(() => client.Close()))
                {
                    await connectTask;
                }
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if host is reachable by Ping within a short timeout.
        /// </summary>
        public static async Task<bool> IsHostReachable(string ip)
        {
            using var ping = new Ping();
            try
            {
                var reply = await ping.SendPingAsync(ip, PingTimeoutMs);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Retrieves the FQDN for a given IP, or returns fallback text if not found.
        /// </summary>
        public static string GetFqdn(string ip)
        {
            try
            {
                var hostEntry = Dns.GetHostEntry(ip);
                return !string.IsNullOrWhiteSpace(hostEntry.HostName) ? hostEntry.HostName : "Unknown Host";
            }
            catch
            {
                return "No Dns Name";
            }
        }

        /// <summary>
        /// Uses ARP to get the MAC address for a given IP on the local network (Windows-only).
        /// </summary>
        public static string GetMacAddress(string ip)
        {
            try
            {
                var ipAddress = IPAddress.Parse(ip);
                var addrBytes = ipAddress.GetAddressBytes();
                uint destIp = BitConverter.ToUInt32(addrBytes, 0);

                var macAddr = new byte[6];
                uint macLen = (uint)macAddr.Length;
                int result = SendARP(destIp, 0, macAddr, ref macLen);
                if (result != 0)
                {
                    return "Unknown MAC";
                }

                return BitConverter.ToString(macAddr, 0, (int)macLen);
            }
            catch
            {
                return "Unknown MAC";
            }
        }

        /// <summary>
        /// Attempts a unicast SSDP M-SEARCH request to gather basic server or location info.
        /// </summary>
        public static async Task<string> GetSsdpInfo(string ip)
        {
            const string ssdpRequest =
                "M-SEARCH * HTTP/1.1\r\n" +
                "HOST: {0}:1900\r\n" +
                "MAN: \"ssdp:discover\"\r\n" +
                "MX: 1\r\n" +
                "ST: ssdp:all\r\n\r\n";

            try
            {
                using var udpClient = new UdpClient();
                udpClient.Client.SendTimeout = SsdpTimeoutMs;
                udpClient.Client.ReceiveTimeout = SsdpTimeoutMs;

                var remoteEndpoint = new IPEndPoint(IPAddress.Parse(ip), 1900);
                var requestBytes = Encoding.ASCII.GetBytes(string.Format(ssdpRequest, ip));
                await udpClient.SendAsync(requestBytes, requestBytes.Length, remoteEndpoint);

                var result = await udpClient.ReceiveAsync().WaitAsync(TimeSpan.FromMilliseconds(SsdpTimeoutMs));
                var response = Encoding.ASCII.GetString(result.Buffer);

                var lines = response.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("SERVER:", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase))
                    {
                        return line;
                    }
                }
                return "-";
            }
            catch
            {
                return "-";
            }
        }
    }
}
