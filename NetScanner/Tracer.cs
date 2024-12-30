using System.Net.NetworkInformation;
using System.Net;
using System.Text;

static class Tracer
{
    public static async Task<List<string>> TraceRoute(
        string targetHostOrIp,
        Action<int, int> onHopProgress = null)
    {
        var results = new List<string>();
        const int maxHops = 30;
        const int timeout = 3000; // 3 seconds per hop

        try
        {
            // 1) Resolve input (host or IP)
            IPAddress dest;
            if (!IPAddress.TryParse(targetHostOrIp, out dest))
            {
                var entry = await Dns.GetHostEntryAsync(targetHostOrIp);
                if (entry.AddressList.Length == 0)
                {
                    results.Add($"Could not resolve '{targetHostOrIp}'.");
                    return results;
                }
                dest = entry.AddressList[0];
            }

            // 2) Perform traceroute
            for (int ttl = 1; ttl <= maxHops; ttl++)
            {
                // Notify caller about our current TTL progress
                onHopProgress?.Invoke(ttl, maxHops);

                using var ping = new Ping();
                var options = new PingOptions(ttl, true);
                var buffer = Encoding.ASCII.GetBytes("Tracing route...");
                var reply = await ping.SendPingAsync(dest, timeout, buffer, options);

                if (reply.Status == IPStatus.TtlExpired || reply.Status == IPStatus.Success)
                {
                    string hopIp = reply.Address?.ToString() ?? "No IP";
                    // Attempt reverse DNS
                    string hopDnsName;
                    try
                    {
                        if (reply.Address != null)
                        {
                            var hostEntry = await Dns.GetHostEntryAsync(reply.Address);
                            hopDnsName = hostEntry.HostName;
                        }
                        else
                        {
                            hopDnsName = "(Unknown)";
                        }
                    }
                    catch
                    {
                        hopDnsName = "(No DNS)";
                    }

                    results.Add($"Hop {ttl}: {hopIp} [{hopDnsName}]");
                    if (reply.Status == IPStatus.Success)
                    {
                        results.Add("Trace complete.");
                        break;
                    }
                }
                else
                {
                    results.Add($"Hop {ttl}: Request timed out.");
                }
            }
        }
        catch (Exception ex)
        {
            results.Add($"TraceRoute error: {ex.Message}");
        }

        return results;
    }
}
