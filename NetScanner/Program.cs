using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Gui;

class Program
{
    private static int[] CommonPorts = new[] { 22, 80, 443 };
    private const int ConnectTimeoutMs = 300;
    private const int PingTimeoutMs = 300;
    private const int SsdpTimeoutMs = 1000;

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint physicalAddrLen);

    static TextField subnetField;
    static TextField startHostField;
    static TextField endHostField;
    static TextField portsField;
    static TextField threadsField;
    static TextView resultsView;
    static Button scanButton;
    static Button exportButton;
    static ProgressBar progressBar;

    // A lock to prevent interleaving of results in the UI
    private static readonly object resultsLock = new object();

    static List<ScanResult> scanResults = new List<ScanResult>();

    static async Task Main()
    {
        Application.Init();
        var top = Application.Top;

        var win = new Window("Network Scanner")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        var subnetLabel = new Label("Subnet (e.g. 192.168.1): ") { X = 1, Y = 1 };
        subnetField = new TextField("192.168.1")
        {
            X = Pos.Right(subnetLabel) + 1,
            Y = Pos.Top(subnetLabel),
            Width = 20
        };

        var startLabel = new Label("Start IP: ") { X = 1, Y = 3 };
        startHostField = new TextField("1")
        {
            X = Pos.Right(startLabel) + 1,
            Y = Pos.Top(startLabel),
            Width = 5
        };

        var endLabel = new Label("End IP: ") { X = 1, Y = 5 };
        endHostField = new TextField("254")
        {
            X = Pos.Right(endLabel) + 1,
            Y = Pos.Top(endLabel),
            Width = 5
        };

        var portsLabel = new Label("Ports (comma sep): ") { X = 1, Y = 7 };
        portsField = new TextField("22,80,443")
        {
            X = Pos.Right(portsLabel) + 1,
            Y = Pos.Top(portsLabel),
            Width = 20
        };

        var threadsLabel = new Label("Threads (default 16): ") { X = 1, Y = 9 };
        threadsField = new TextField("16")
        {
            X = Pos.Right(threadsLabel) + 1,
            Y = Pos.Top(threadsLabel),
            Width = 5
        };

        scanButton = new Button("Start Scan")
        {
            X = 1,
            Y = 11
        };
        scanButton.Clicked += async () => await StartScan();

        exportButton = new Button("Export CSV")
        {
            X = Pos.Right(scanButton) + 5,
            Y = 11
        };
        exportButton.Clicked += ExportToCsv;

        progressBar = new ProgressBar()
        {
            X = 1,
            Y = 13,
            Width = Dim.Fill() - 2,
            Height = 1,
            Fraction = 0f
        };

        resultsView = new TextView()
        {
            X = 1,
            Y = 15,
            Width = Dim.Fill() - 2,
            Height = Dim.Fill() - 1,
            ReadOnly = true,
            WordWrap = true,
            Multiline = true,
        };

        win.Add(
            subnetLabel, subnetField,
            startLabel, startHostField,
            endLabel, endHostField,
            portsLabel, portsField,
            threadsLabel, threadsField,
            scanButton, exportButton,
            progressBar,
            resultsView
        );

        top.Add(win);
        Application.Run();
    }

    private static async Task StartScan()
    {
        // Clear old results
        scanResults.Clear();
        resultsView.Text = "";

        // Parse inputs
        string subnet = subnetField.Text.ToString().Trim();
        if (string.IsNullOrWhiteSpace(subnet))
        {
            AppendResult("Invalid subnet.", ConsoleColor.Red);
            return;
        }

        if (!int.TryParse(startHostField.Text.ToString().Trim(), out int startHost))
            startHost = 1;
        if (!int.TryParse(endHostField.Text.ToString().Trim(), out int endHost))
            endHost = 254;
        if (!int.TryParse(threadsField.Text.ToString().Trim(), out int threadCount))
            threadCount = 16;

        var portsInput = portsField.Text.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(portsInput))
        {
            var parts = portsInput.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var portList = new List<int>();
            foreach (var p in parts)
            {
                if (int.TryParse(p.Trim(), out int portVal))
                    portList.Add(portVal);
            }
            if (portList.Count > 0)
                CommonPorts = portList.ToArray();
        }

        AppendResult("Scanning...\n", ConsoleColor.White);

        await Task.Run(async () =>
        {
            int total = endHost - startHost + 1;
            int count = 0;

            var tasks = new List<Task>();
            var sem = new SemaphoreSlim(threadCount);

            for (int i = startHost; i <= endHost; i++)
            {
                string ipStr = $"{subnet}.{i}";

                tasks.Add(Task.Run(async () =>
                {
                    await sem.WaitAsync();
                    try
                    {
                        if (await IsHostReachable(ipStr))
                        {
                            string fqdn = GetFqdn(ipStr);
                            string mac = GetMacAddress(ipStr);
                            string ssdpInfo = await GetSsdpInfo(ipStr);
                            string mdnsInfo = GetMdnsInfo(ipStr);
                            string snmpInfo = GetSnmpInfo(ipStr);

                            bool anyPortOpen = false;
                            List<int> openPorts = new List<int>();

                            foreach (var port in CommonPorts)
                            {
                                if (await IsPortOpenAsync(ipStr, port))
                                {
                                    anyPortOpen = true;
                                    openPorts.Add(port);
                                    AppendDeviceInfo(ipStr, fqdn, mac, port, ssdpInfo, mdnsInfo, snmpInfo, true);
                                }
                            }

                            if (!anyPortOpen)
                            {
                                AppendDeviceInfo(ipStr, fqdn, mac, 0, ssdpInfo, mdnsInfo, snmpInfo, false);
                            }

                            scanResults.Add(new ScanResult
                            {
                                IP = ipStr,
                                FQDN = fqdn,
                                MAC = mac,
                                OpenPorts = openPorts.Count > 0 ? string.Join(";", openPorts) : "",
                                SSDP = ssdpInfo,
                                MDNS = mdnsInfo,
                                SNMP = snmpInfo
                            });
                        }
                    }
                    finally
                    {
                        int newCount = Interlocked.Increment(ref count);
                        float fraction = (float)newCount / total;
                        Application.MainLoop.Invoke(() =>
                        {
                            progressBar.Fraction = fraction;
                        });
                        sem.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);
            AppendResult("\nScan complete.", ConsoleColor.White);
        });
    }

    private static void ExportToCsv()
    {
        var saveDialog = new SaveDialog("Export CSV", "Save scan results to CSV");
        Application.Run(saveDialog);

        if (!saveDialog.Canceled)
        {
            var path = saveDialog.FilePath.ToString();
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    using var sw = new StreamWriter(path);
                    sw.WriteLine("IP,FQDN,MAC,OpenPorts,SSDP,MDNS,SNMP");
                    foreach (var r in scanResults)
                    {
                        sw.WriteLine($"{EscapeCsv(r.IP)},{EscapeCsv(r.FQDN)},{EscapeCsv(r.MAC)},{EscapeCsv(r.OpenPorts)},{EscapeCsv(r.SSDP)},{EscapeCsv(r.MDNS)},{EscapeCsv(r.SNMP)}");
                    }
                    AppendResult($"Exported to {path}", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    AppendResult($"Error exporting: {ex.Message}", ConsoleColor.Red);
                }
            }
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value == null) return "";
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private static async Task<bool> IsPortOpenAsync(string ip, int port)
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

    private static async Task<bool> IsHostReachable(string ip)
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

    private static string GetFqdn(string ip)
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

    private static string GetMacAddress(string ip)
    {
        try
        {
            IPAddress ipAddress = IPAddress.Parse(ip);
            byte[] addrBytes = ipAddress.GetAddressBytes();
            uint destIp = BitConverter.ToUInt32(addrBytes, 0);

            byte[] macAddr = new byte[6];
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

    private static async Task<string> GetSsdpInfo(string ip)
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
            byte[] requestBytes = Encoding.ASCII.GetBytes(string.Format(ssdpRequest, ip));
            await udpClient.SendAsync(requestBytes, requestBytes.Length, remoteEndpoint);

            var result = await udpClient.ReceiveAsync().WaitAsync(TimeSpan.FromMilliseconds(SsdpTimeoutMs));
            string response = Encoding.ASCII.GetString(result.Buffer);

            string[] lines = response.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
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

    private static string GetMdnsInfo(string ip)
    {
        return "-"; // Placeholder
    }

    private static string GetSnmpInfo(string ip)
    {
        return "-"; // Placeholder
    }

    private static void AppendResult(string text, ConsoleColor color)
    {
        lock (resultsLock)
        {
            Application.MainLoop.Invoke(() =>
            {
                resultsView.ColorScheme = GetColorSchemeFor(color);
                resultsView.ReadOnly = false;
                resultsView.Text += text + "\n";
                resultsView.ReadOnly = true;

                if (resultsView.CanFocus)
                {
                    resultsView.CursorPosition = new Point(0, resultsView.Lines - 1);
                }
                resultsView.SetNeedsDisplay();
            });
        }
    }

    private static void AppendDeviceInfo(
        string ip, string fqdn, string mac,
        int port, string ssdp, string mdns, string snmp, bool portOpen)
    {
        // For each IP, we make multiple calls to AppendResult.
        // Because AppendResult is locked, we won't interleave lines from other threads.
        AppendResult($"IP: {ip}", ConsoleColor.Yellow);
        AppendResult($"FQDN: {fqdn}", ConsoleColor.Green);
        AppendResult($"MAC: {mac}", ConsoleColor.Cyan);

        if (portOpen && port != 0)
        {
            AppendResult($"Port {port} open", ConsoleColor.Magenta);
        }
        else if (!portOpen)
        {
            AppendResult($"No common ports open", ConsoleColor.DarkGray);
        }

        AppendResult($"SSDP: {ssdp}", ConsoleColor.Gray);
        AppendResult($"mDNS: {mdns}", ConsoleColor.Gray);
        AppendResult($"SNMP: {snmp}", ConsoleColor.Gray);
        AppendResult("---------------------------------", ConsoleColor.White);
    }

    private static ColorScheme GetColorSchemeFor(ConsoleColor consoleColor)
    {
        var scheme = new ColorScheme();
        Terminal.Gui.Attribute fg;
        switch (consoleColor)
        {
            case ConsoleColor.Red:
                fg = Terminal.Gui.Attribute.Make(Color.BrightRed, Color.Black);
                break;
            case ConsoleColor.Yellow:
                fg = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Black);
                break;
            case ConsoleColor.Green:
                fg = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Black);
                break;
            case ConsoleColor.Cyan:
                fg = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black);
                break;
            case ConsoleColor.Magenta:
                fg = Terminal.Gui.Attribute.Make(Color.BrightMagenta, Color.Black);
                break;
            case ConsoleColor.DarkGray:
                fg = Terminal.Gui.Attribute.Make(Color.Gray, Color.Black);
                break;
            case ConsoleColor.Gray:
                fg = Terminal.Gui.Attribute.Make(Color.White, Color.Black);
                break;
            case ConsoleColor.White:
            default:
                fg = Terminal.Gui.Attribute.Make(Color.White, Color.Black);
                break;
        }

        scheme.Normal = fg;
        scheme.Focus = fg;
        scheme.HotNormal = fg;
        scheme.HotFocus = fg;
        return scheme;
    }

    private class ScanResult
    {
        public string IP { get; set; }
        public string FQDN { get; set; }
        public string MAC { get; set; }
        public string OpenPorts { get; set; }
        public string SSDP { get; set; }
        public string MDNS { get; set; }
        public string SNMP { get; set; }
    }
}
