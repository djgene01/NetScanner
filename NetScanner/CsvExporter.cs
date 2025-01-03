using System;
using System.Collections.Generic;
using System.IO;
using Terminal.Gui;

namespace NetScanner
{
    public static class CsvExporter
    {
        /// <summary>
        /// Exports the given scan results to CSV. 
        /// Uses a SaveDialog to pick the filename, then writes the file. 
        /// Provides status feedback via the addResultLine callback.
        /// </summary>
        /// <param name="scanResults">The list of ScanResult to export.</param>
        /// <param name="addResultLine">
        /// A callback method (like Program.AddResultLine) to show messages in the UI.
        /// </param>
        public static void ExportToCsv(
            List<ScanResult> scanResults,
            Action<string> addResultLine)
        {
            // Show a SaveDialog
            var saveDialog = new SaveDialog("Export CSV", "Save scan results to CSV");
            Application.Run(saveDialog);

            // If canceled or empty path -> bail
            if (saveDialog.Canceled) return;
            var path = saveDialog.FilePath.ToString();
            if (string.IsNullOrWhiteSpace(path)) return;

            // Try writing the CSV
            try
            {
                using var sw = new StreamWriter(path);
                sw.WriteLine("IP,FQDN,MAC,OpenPorts,SSDP,MDNS,SNMP");
                foreach (var r in scanResults)
                {
                    sw.WriteLine($"{EscapeCsv(r.IP)},{EscapeCsv(r.FQDN)},{EscapeCsv(r.MAC)}," +
                                 $"{EscapeCsv(r.OpenPorts)},{EscapeCsv(r.SSDP)}," +
                                 $"{EscapeCsv(r.MDNS)},{EscapeCsv(r.SNMP)}");
                }
                addResultLine($"Exported to {path}");
            }
            catch (Exception ex)
            {
                addResultLine($"Error exporting: {ex.Message}");
            }
        }

        /// <summary>
        /// Simple CSV escaping. Encloses fields containing commas, quotes, or newlines in quotes.
        /// Also doubles any embedded quotes.
        /// </summary>
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
}
