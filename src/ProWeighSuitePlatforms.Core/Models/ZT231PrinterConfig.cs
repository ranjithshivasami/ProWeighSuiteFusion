using System;

namespace ProWeighSuitePlatforms.Models
{
    public class ZT231PrinterConfig
    {
        public string ConnectionType { get; set; } = "USB"; // "USB", "COM", or "Ethernet"

        // Serial / USB settings
        public string ComPort { get; set; } = string.Empty;
        public int BaudRate { get; set; } = 9600;
        public int DataBits { get; set; } = 8;
        public string Parity { get; set; } = "None";
        public string StopBits { get; set; } = "1";
        public string FlowControl { get; set; } = "None";

        // Ethernet settings
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; } = 9100;
        public string SubnetMask { get; set; } = string.Empty;
        public string Gateway { get; set; } = string.Empty;

        public DateTime LastModified { get; set; } = DateTime.Now;
    }
}
