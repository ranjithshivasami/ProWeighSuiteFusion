namespace ProWeighSuitePlatforms.Models
{
    public class ScaleSettings
    {
        public int Id { get; set; }

        private string _scaleName = string.Empty;
        public string ScaleName
        {
            get => _scaleName;
            set => _scaleName = value?.Trim() ?? string.Empty;
        }

        private string _location = string.Empty;
        public string Location
        {
            get => _location;
            set => _location = value?.Trim() ?? string.Empty;
        }

        public int ScaleCode { get; set; } = 0;
        
        // Connection type: SerialPort or TCP
        public string ConnectionType { get; set; } = "SerialPort";
        
        // Serial Port settings
        public string PortName { get; set; } = string.Empty;
        public int BaudRate { get; set; } = 9600;
        public int DataBits { get; set; } = 8;
        public int Parity { get; set; } = 0;
        public int StopBits { get; set; } = 1;
        
        // TCP/IP settings
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; } = 5000;
        
        public string Mode { get; set; } = "Continuous";
        public bool ZeroReturn { get; set; } = false;
        public double ZeroReturnToleranceMinus { get; set; } = 20;
        public double ZeroReturnTolerancePlus { get; set; } = 20;
        // Nexus additions: raw example string received from the scale + the
        // delimiter used to split its values when parsing.
        public string SampleScaleData { get; set; } = string.Empty;
        public string Delimiter { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public bool IsAutoCapture { get; set; } = false;
        public int CaptureIntervalMs { get; set; } = 500;
        public System.DateTime CreatedAt { get; set; } = System.DateTime.Now;
        
        // Display properties
        public int SerialNumber { get; set; }
        public string StatusText => IsActive ? "Active" : "Inactive";
        public string ParityText => ((System.IO.Ports.Parity)Parity).ToString();
        public string StopBitsText => ((System.IO.Ports.StopBits)StopBits).ToString();
        public string ModeText => Mode ?? "Continuous";
        public string ConnectionInfo => ConnectionType == "TCP" 
            ? $"{IpAddress}:{Port}" 
            : PortName;
    }
}
