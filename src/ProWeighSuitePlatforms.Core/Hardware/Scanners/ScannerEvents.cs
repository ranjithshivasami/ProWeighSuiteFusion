using System;

namespace ProWeighSuitePlatforms.Services.Scanners
{
    public sealed class ScannerBarcodeEventArgs : EventArgs
    {
        public string Label { get; }
        public string RawHex { get; }
        public int DataType { get; }
        public string Model { get; }
        public string Serial { get; }
        public int ScannerId { get; }
        public string Brand { get; }

        public ScannerBarcodeEventArgs(string label, string rawHex, int dataType, string model, string serial, int scannerId, string brand)
        {
            Label = label ?? string.Empty;
            RawHex = rawHex ?? string.Empty;
            DataType = dataType;
            Model = model ?? string.Empty;
            Serial = serial ?? string.Empty;
            ScannerId = scannerId;
            Brand = brand ?? string.Empty;
        }
    }

    public sealed class DiscoveredScanner
    {
        public int ScannerId { get; set; }
        public string Model { get; set; } = string.Empty;
        public string Serial { get; set; } = string.Empty;
        public string Guid { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}
