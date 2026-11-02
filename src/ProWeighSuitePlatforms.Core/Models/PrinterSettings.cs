namespace ProWeighSuitePlatforms.Models
{
    public class PrinterSettings
    {
        public int Id { get; set; }
        public string PrinterType { get; set; } = "ZPL"; // ZPL, Windows, Network
        public string PrinterName { get; set; } = string.Empty;
        public string PrinterIp { get; set; } = string.Empty;
        public int PrinterPort { get; set; } = 9100;
        public string ComPort { get; set; } = string.Empty;
        public int BaudRate { get; set; } = 9600;
        public bool AutoPrint { get; set; } = true;
    }
}
