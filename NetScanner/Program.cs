using NetScanner;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    // UI fields
    static TextField subnetField;
    static TextField startHostField;
    static TextField endHostField;
    static TextField portsField;
    static TextField threadsField;
    static Button scanButton;
    static Button exportButton;
    static Button traceButton;

    // Checkbox to hide hosts that do NOT have open ports
    static CheckBox hideNonOpenCheckBox;

    // Progress bar
    static ProgressBar progressBar;

    // Scrollable view for results
    static ScrollView resultsScrollView;

    // We'll track the current "row" we’re placing new labels/buttons at
    static int contentHeight = 0;

    // Lock for thread-safety
    private static readonly object resultsLock = new object();

    // For CSV export
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

        // Subnet input
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
        var threadsLabel = new Label("Threads (default 16): ") { X = 1, Y = 9 };
        threadsField = new TextField("16")
        {
            X = Pos.Right(threadsLabel) + 1,
            Y = Pos.Top(threadsLabel),
            Width = 5
        };

        // NEW: Hide non-open checkbox
        hideNonOpenCheckBox = new CheckBox("Hide non-open?", false)
        {
            X = Pos.Right(threadsField) + 5,
            Y = Pos.Top(threadsField)
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
        progressBar = new ProgressBar
        {
            X = 1,
            Y = 13,
            Width = Dim.Fill() - 2,
            Height = 1,
            Fraction = 0f
        };

        // ScrollView for results
        resultsScrollView = new ScrollView
        {
            X = 1,
            Y = 15,
            Width = Dim.Fill() - 2,
            Height = Dim.Fill() - 1,

            ShowVerticalScrollIndicator = true,
            ShowHorizontalScrollIndicator = false,
            ContentOffset = new Point(0, 0),
            ContentSize = new Size(0, 0),
            AutoHideScrollBars = false
        };

        // Add controls
        win.Add(
            subnetLabel, subnetField,
            startLabel, startHostField,
            endLabel, endHostField,
            portsLabel, portsField,
            threadsLabel, threadsField,
            hideNonOpenCheckBox,
            scanButton, exportButton, traceButton,
            progressBar,
            resultsScrollView
        );

        top.Add(win);
        Application.Run();
    }

    // Clears old results
    private static void ClearResults()
    {
        lock (resultsLock)
        {
            resultsScrollView.RemoveAll();
            contentHeight = 0;
            resultsScrollView.ContentSize = new Size(0, 0);
        }
    }

    // HELPER METHOD: Add a colored label in the results (with a custom color).
    private static void AddResultLineColored(string text, Color fgColor)
    {
        lock (resultsLock)
        {
            Application.MainLoop.Invoke(() =>
            {
                var scheme = new ColorScheme
                {
                    Normal = Terminal.Gui.Attribute.Make(fgColor, Color.Blue),
                    Focus = Terminal.Gui.Attribute.Make(fgColor, Color.DarkGray),
                    HotNormal = Terminal.Gui.Attribute.Make(fgColor, Color.Blue),
                    HotFocus = Terminal.Gui.Attribute.Make(fgColor, Color.DarkGray)
                };

                var lbl = new Label(text)
                {
                    X = 0,
                    Y = contentHeight,
                    AutoSize = true,
                    ColorScheme = scheme
                };

                resultsScrollView.Add(lbl);
                contentHeight++;
                resultsScrollView.ContentSize = new Size(resultsScrollView.Bounds.Width, contentHeight);
                resultsScrollView.SetNeedsDisplay();
            });
        }
    }

    // Adds a Label line. We'll still use this for generic lines.
    private static void AddResultLine(string text)
    {
        // For generic messages, let's just call our colored method with White:
        AddResultLineColored(text, Color.White);
    }

    // Adds a clickable button (for example, for port 80 or 443)
    private static void AddOpenUrlButton(string text, string url)
    {
        lock (resultsLock)
        {
            Application.MainLoop.Invoke(() =>
            {
                var scheme = new ColorScheme
                {
                    Normal = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Blue),
                    Focus = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.DarkGray),
                    HotNormal = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Blue),
                    HotFocus = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.DarkGray)
                };

                var btn = new Button(text)
                {
                    X = 0,
                    Y = contentHeight,
                    ColorScheme = scheme
                };
                btn.Clicked += () => OpenUrlInDefaultBrowser(url);

                resultsScrollView.Add(btn);

                contentHeight++;
                resultsScrollView.ContentSize = new Size(resultsScrollView.ContentSize.Width, contentHeight);
                resultsScrollView.SetNeedsDisplay();
            });
        }
    }

    // Cross-platform open URL
    private static void OpenUrlInDefaultBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
        }
        catch (Exception ex)
        {
            AddResultLine($"Could not open browser: {ex.Message}");
        }
    }

    // Show the trace route dialog
    private static void ShowTraceRouteDialog()
    {
        var dialog = new Dialog("Trace Route", 60, 20);

        var ipLabel = new Label("IP/Host:") { X = 1, Y = 1 };
        var ipField = new TextField("") { X = Pos.Right(ipLabel), Y = Pos.Top(ipLabel), Width = 25 };

        var startButton = new Button("Start") { X = 1, Y = 3 };

        var traceProgress = new ProgressBar
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

    // The primary scanning logic
    private static async Task StartScan()
    {
        scanResults.Clear();
        ClearResults();

        string subnet = subnetField.Text.ToString().Trim();
        if (string.IsNullOrWhiteSpace(subnet))
        {
            AddResultLine("Invalid subnet.");
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

        AddResultLine("Scanning...");

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
                        // Instead of local methods, you'd call your external
                        // e.g., NetworkUtils methods here if you have them in a separate file
                        if (await NetworkUtils.IsHostReachable(ipStr))
                        {
                            string fqdn = NetworkUtils.GetFqdn(ipStr);
                            string mac = NetworkUtils.GetMacAddress(ipStr);
                            string ssdpInfo = await NetworkUtils.GetSsdpInfo(ipStr);

                            bool anyPortOpen = false;
                            var openPorts = new List<int>();

                            // Check each port
                            foreach (var port in CommonPorts)
                            {
                                if (await NetworkUtils.IsPortOpenAsync(ipStr, port))
                                {
                                    anyPortOpen = true;
                                    openPorts.Add(port);
                                }
                            }

                            bool skipDisplay = (!anyPortOpen && hideNonOpenCheckBox.Checked);

                            if (!skipDisplay)
                            {
                                if (anyPortOpen)
                                {
                                    foreach (var p in openPorts)
                                    {
                                        AddResultLineColored($"IP: {ipStr}", Color.BrightYellow);
                                        AddResultLineColored($"FQDN: {fqdn}", Color.BrightGreen);
                                        AddResultLineColored($"MAC: {mac}", Color.BrightCyan);
                                        AddResultLineColored($"Port {p} open", Color.Red);

                                        if (p == 80)
                                        {
                                            string url = $"http://{ipStr}/";
                                            AddOpenUrlButton($"Open {url}", url);
                                        }
                                        else if (p == 443)
                                        {
                                            string url = $"https://{ipStr}/";
                                            AddOpenUrlButton($"Open {url}", url);
                                        }

                                        AddResultLine("---------------------------------");
                                    }
                                }
                                else
                                {
                                    AddResultLineColored($"IP: {ipStr}", Color.BrightYellow);
                                    AddResultLineColored($"FQDN: {fqdn}", Color.BrightGreen);
                                    AddResultLineColored($"MAC: {mac}", Color.BrightCyan);
                                    AddResultLineColored("No common ports open", Color.Gray);
                                    AddResultLine("---------------------------------");
                                }
                            }

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

            AddResultLine("Scan complete.");
        });
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
            AddResultLine($"Exported to {path}");
        }
        catch (Exception ex)
        {
            AddResultLine($"Error exporting: {ex.Message}");
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
}

