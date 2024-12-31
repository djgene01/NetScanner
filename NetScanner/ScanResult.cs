using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetScanner
{
    public class ScanResult
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
