﻿using NetScanner;
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
    private static int[] CommonPorts = { 22, 80, 443 };
    private const int ConnectTimeoutMs = 200;
    private const int PingTimeoutMs = 200;
    private const int SsdpTimeoutMs = 200;

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
    static Button traceButton;

    static CheckBox hideNonOpenCheckBox;

    private static readonly object resultsLock = new object();

    static List<ScanResult> scanResults = new List<ScanResult>();

    static async Task Main()
    {
        Application.Init();
        var top = Application.Top;

        var win = new Window("NetScanner")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        // Subnet
        var subnetLabel = new Label("Subnet (e.g. 192.168.1): ") { X = 1, Y = 1 };
        subnetField = new TextField("192.168.1")
        {
            X = Pos.Right(subnetLabel) + 1,
            Y = Pos.Top(subnetLabel),
            Width = 20
        };

        // Start IP
        var startLabel = new Label("Start IP: ") { X = 1, Y = 3 };
        startHostField = new TextField("1")
        {
            X = Pos.Right(startLabel) + 1,
            Y = Pos.Top(startLabel),
            Width = 5
        };

        // End IP
        var endLabel = new Label("End IP: ") { X = 1, Y = 5 };
        endHostField = new TextField("254")
        {
            X = Pos.Right(endLabel) + 1,
            Y = Pos.Top(endLabel),
            Width = 5
        };

        // Ports
        var portsLabel = new Label("Ports (comma sep): ") { X = 1, Y = 7 };
        portsField = new TextField("22,80,443")
        {
            X = Pos.Right(portsLabel) + 1,
            Y = Pos.Top(portsLabel),
            Width = 20
        };

        // Threads
        var threadsLabel = new Label("Threads (default 32): ") { X = 1, Y = 9 };
        threadsField = new TextField("32")
        {
            X = Pos.Right(threadsLabel) + 1,
            Y = Pos.Top(threadsLabel),
            Width = 5
        };

        // NEW: Hide-non-open checkbox
        hideNonOpenCheckBox = new CheckBox("Hide non-open?", false)
        {
            X = Pos.Right(threadsField) + 5,
            Y = 9
        };

        // Buttons
        scanButton = new Button("Start Scan") { X = 1, Y = 11 };
        scanButton.Clicked += async () => await StartScan();

        exportButton = new Button("Export CSV")
        {
            X = Pos.Right(scanButton) + 5,
            Y = 11
        };
        exportButton.Clicked += ExportToCsv;

        traceButton = new Button("Trace Route")
        {
            X = Pos.Right(exportButton) + 5,
            Y = 11
        };
        traceButton.Clicked += ShowTraceRouteDialog;

        // Progress bar
        progressBar = new ProgressBar()
        {
            X = 1,
            Y = 13,
            Width = Dim.Fill() - 2,
            Height = 1,
            Fraction = 0f
        };

        // Results text view
        resultsView = new TextView()
        {
            X = 1,
            Y = 15,
            Width = Dim.Fill() - 2,
            Height = Dim.Fill() - 1,
            ReadOnly = true,
            WordWrap = true,
            Multiline = true
        };

        // Add controls to window
        win.Add(
            subnetLabel, subnetField,
            startLabel, startHostField,
            endLabel, endHostField,
            portsLabel, portsField,
            threadsLabel, threadsField,
            hideNonOpenCheckBox, // <--- new checkbox
            scanButton, exportButton, traceButton,
            progressBar,
            resultsView
        );

        top.Add(win);
        Application.Run();
    }

    private static void ShowTraceRouteDialog()
    {
        var dialog = new Dialog("Trace Route", 60, 20);

        var ipLabel = new Label("IP/Host:") { X = 1, Y = 1 };
        var ipField = new TextField("")
        {
            X = Pos.Right(ipLabel),
            Y = Pos.Top(ipLabel),
            Width = 25
        };

        var startButton = new Button("Start") { X = 1, Y = 3 };

        var traceProgress = new ProgressBar()
        {
            X = Pos.Right(startButton) + 2,
            Y = Pos.Top(startButton),
            Width = 15,
            Visible = false
        };

        var traceResults = new TextView
        {
            X = 1,
            Y = 5,
            Width = Dim.Fill() - 2,
            Height = Dim.Fill() - 1,
            ReadOnly = true,
            WordWrap = false,
            Multiline = true
        };

        startButton.Clicked += async () =>
        {
            traceResults.Text = "";
            string input = ipField.Text.ToString().Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                traceResults.Text = "Invalid IP/Host.";
                return;
            }

            traceProgress.Visible = true;
            traceProgress.Fraction = 0f;

            var hops = await Tracer.TraceRoute(input, (currentTtl, maxHops) =>
            {
                Application.MainLoop.Invoke(() => traceProgress.Fraction = (float)currentTtl / maxHops);
            });

            traceProgress.Visible = false;

            foreach (var hop in hops)
            {
                traceResults.Text += hop + "\n";
            }
        };

        dialog.Add(ipLabel, ipField, startButton, traceProgress, traceResults);
        Application.Run(dialog);
    }

    private static async Task StartScan()
    {
        scanResults.Clear();
        resultsView.Text = "";

        string subnet = subnetField.Text.ToString().Trim();
        if (string.IsNullOrWhiteSpace(subnet))
        {
            AppendResult("Invalid subnet.");
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

        AppendResult("Scanning...\n");

        await Task.Run(async () =>
        {
            int total = endHost - startHost + 1;
            int count = 0;

            var tasks = new List<Task>(total);
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

                            bool anyPortOpen = false;
                            var openPorts = new List<int>();

                            foreach (var port in CommonPorts)
                            {
                                if (await IsPortOpenAsync(ipStr, port))
                                {
                                    anyPortOpen = true;
                                    openPorts.Add(port);
                                }
                            }

                            // only show or store this device if:
                            // either it has open ports or the user didn't check "Hide non-open"
                            if (anyPortOpen || !hideNonOpenCheckBox.Checked)
                            {
                                // append the results to UI
                                AppendDeviceInfo(ipStr, fqdn, mac, openPorts, ssdpInfo, anyPortOpen);
                            }

                            // Always store them in scanResults, but we might skip displaying
                            // if hideNonOpen is set
                            scanResults.Add(new ScanResult
                            {
                                IP = ipStr,
                                FQDN = fqdn,
                                MAC = mac,
                                OpenPorts = anyPortOpen ? string.Join(";", openPorts) : "",
                                SSDP = ssdpInfo
                            });
                        }
                    }
                    finally
                    {
                        int newCount = Interlocked.Increment(ref count);
                        float fraction = (float)newCount / total;
                        Application.MainLoop.Invoke(() => progressBar.Fraction = fraction);
                        sem.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);
            AppendResult("\nScan complete.");
        });
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
            return !string.IsNullOrWhiteSpace(hostEntry.HostName)
                ? hostEntry.HostName
                : "Unknown Host";
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

    private static void ExportToCsv()
    {
        var saveDialog = new SaveDialog("Export CSV", "Save scan results to CSV");
        Application.Run(saveDialog);

        if (saveDialog.Canceled) return;

        var path = saveDialog.FilePath.ToString();
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            using var sw = new StreamWriter(path);
            sw.WriteLine("IP,FQDN,MAC,OpenPorts,SSDP,MDNS,SNMP");
            foreach (var r in scanResults)
            {
                sw.WriteLine($"{EscapeCsv(r.IP)},{EscapeCsv(r.FQDN)},{EscapeCsv(r.MAC)},{EscapeCsv(r.OpenPorts)},{EscapeCsv(r.SSDP)},{EscapeCsv(r.MDNS)},{EscapeCsv(r.SNMP)}");
            }
            AppendResult($"Exported to {path}");
        }
        catch (Exception ex)
        {
            AppendResult($"Error exporting: {ex.Message}");
        }
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private static void AppendResult(string text)
    {
        lock (resultsLock)
        {
            Application.MainLoop.Invoke(() =>
            {
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

    private static void AppendDeviceInfo(string ip, string fqdn, string mac, List<int> openPorts, string ssdp, bool anyPortOpen)
    {
        AppendResult($"IP: {ip}");
        AppendResult($"FQDN: {fqdn}");
        AppendResult($"MAC: {mac}");

        if (anyPortOpen)
            AppendResult($"Open Ports: {string.Join(",", openPorts)}");
        else
            AppendResult("No common ports open");

        AppendResult("---------------------------------");
    }


}
