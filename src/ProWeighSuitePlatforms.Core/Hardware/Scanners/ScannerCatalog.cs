using System.Collections.Generic;
using System.Linq;

namespace ProWeighSuitePlatforms.Services.Scanners
{
    public sealed class ScannerBrandDescriptor
    {
        public string Id { get; }
        public string DisplayName { get; }
        public bool RequiresSdk { get; }
        public string DriverInfo { get; }
        public string SupportedModes { get; }

        public ScannerBrandDescriptor(string id, string displayName, bool requiresSdk, string driverInfo, string supportedModes)
        {
            Id = id;
            DisplayName = displayName;
            RequiresSdk = requiresSdk;
            DriverInfo = driverInfo;
            SupportedModes = supportedModes;
        }
    }

    public static class ScannerCatalog
    {
        public static IReadOnlyList<ScannerBrandDescriptor> Brands { get; } = new List<ScannerBrandDescriptor>
        {
            new("None", "None (Keyboard Wedge only)", false, "No driver", "HID"),
            new("Zebra", "Zebra (DS36xx, FS40, DS2200)", true, "CoreScanner 64-bit v3.07+ (SNAPI)", "SNAPI/SSI"),
            new("Honeywell", "Honeywell (Voyager, Granit)", true, "Honeywell SDK – future", "USB COM/HID"),
            new("Datalogic", "Datalogic (Gryphon, Matrix)", true, "Datalogic SDK – future", "USB-COM"),
            new("Wedge", "Generic Wedge (Raw HID)", false, "No driver", "HID"),
            new("Srk", "SRK-SYMD6L UHF (LAN / RS232 / RS485)", false, "Syrotech demo + TCP/COM, no CoreScanner", "LAN/RS232/RS485"),
        };

        public static ScannerBrandDescriptor? Find(string? id)
            => Brands.FirstOrDefault(b => string.Equals(b.Id, id, System.StringComparison.OrdinalIgnoreCase));
    }
}
