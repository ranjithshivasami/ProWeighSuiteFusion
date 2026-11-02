using System;
using System.Collections.Generic;

namespace ProWeighSuitePlatforms.Models
{
    public class AppSettings
    {
        public string TargetUOM { get; set; } = "count";

        // Product Presence Threshold (in kg). Readings below this are reported as
        // "No Product" and skip the under/over tolerance checks entirely. Kept
        // separate from the material-level under/over tolerance bands.
        public double PresenceThreshold { get; set; } = 0.05;

        // Smallest measurable value of the weighing machine and the software.
        public decimal ScaleResolution { get; set; }

        // Maximum capacity of the weighing scale. Any weight above this value
        // should be considered an error or overflow.
        public decimal MaxWeight { get; set; }

        public bool PrintAtAccept { get; set; } = true;
        public bool PrintAtUnderWeight { get; set; } = false;
        public bool PrintAtOverWeight { get; set; } = false;

        public bool RecordAtAccept { get; set; } = true;
        public bool RecordAtUnderWeight { get; set; } = false;
        public bool RecordAtOverWeight { get; set; } = false;

        public bool EnableOTP { get; set; } = false;

// Weight Capture — moved from ScaleSettings (per-scale) to AppSettings (global).
// Manual = operator presses Accept; Auto = weight auto-captured after interval; Hybrid = either manual or auto.
private string _weightCaptureMode = "Manual";
public string WeightCaptureMode
{
    get => _weightCaptureMode;
    set => _weightCaptureMode = NormalizeWeightCaptureMode(value);
}

// Backward-compatible bool — maps to WeightCaptureMode. Kept for JSON compat (old configs with IsAutoCapture).
private bool _isAutoCaptureLegacy = false;
public bool IsAutoCapture
{
    get => WeightCaptureMode != "Manual";
    set
    {
        _isAutoCaptureLegacy = value;
        if (string.IsNullOrWhiteSpace(_weightCaptureMode) || _weightCaptureMode == "Manual")
            _weightCaptureMode = value ? "Auto" : "Manual";
    }
}

public static string NormalizeWeightCaptureMode(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return "Manual";
    var t = raw.Trim().ToLowerInvariant();
    return t switch
    {
        "manual" => "Manual",
        "auto" => "Auto",
        "hybrid" or "hybride" or "hybird" => "Hybrid",
        _ => "Manual"
    };
}

public int CaptureIntervalMs { get; set; } = 500;

// Product Scan — shown only when WeightCaptureMode == Hybrid (Checkweigher or Integrate PLC workflow)
private string _scanMode = "Manual";
public string ScanMode
{
    get => _scanMode;
    set => _scanMode = NormalizeScanMode(value);
}

public static string NormalizeScanMode(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return "Manual";
    var t = raw.Trim().ToLowerInvariant();
    return t switch
    {
        "manual" => "Manual",
        "auto" => "Auto",
        _ => "Manual"
    };
}

public int ScanIntervalMs { get; set; } = 1000;

// Velopack update feed. Phase 1: local directory (e.g. C:\ProWeighSuitePlatformsUpdates or \\server\share\Releases). Phase 2: http URL (e.g. https://updates.proweighsuite.com/releases).
public string UpdateFeedPath { get; set; } = @"C:\ProWeighSuitePlatformsUpdates";

        /// <summary>Optional explicit channel (default: stable).</summary>
        public string UpdateChannel { get; set; } = "stable";

        // Date (yyyy-MM-dd) of the last automatic daily reset of the sync queue
        // and dead-letter (failed sync) records. Managed by DailyResetService.
        public string? LastDailyResetDate { get; set; }

        // Industrial scanner selection – decoupled via DI. Brand = None|Zebra|Honeywell|Datalogic|Wedge
        public ScannerSettings Scanner { get; set; } = new();

        // Brand theme — admin-switchable. Single hex drives all Brand* brushes via ThemeService.
        // e.g. "#FF9F00" (default orange), "#086BE8" (BridgeX blue).
        public string BrandPrimaryHex { get; set; } = "#FF9F00";

        // Friendly theme name (Orange, Blue, Green, ... or Custom). Kept for display; hex is source of truth.
        public string BrandThemeName { get; set; } = "Orange";

        // True only after an admin explicitly changes the brand theme in App Settings.
        // When false, startup ignores the values above and uses the build-stamped
        // brand color (Hosts/<flavor>/branding.json), so a new build always shows
        // its own brand color instead of a stale color saved by an earlier build.
        public bool BrandThemeCustom { get; set; }

        public static string NormalizeBrandHex(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "#FF9F00";
            var t = raw.Trim();
            if (!t.StartsWith("#")) t = "#" + t;
            // Accept #RGB, #RRGGBB, #AARRGGBB
            if (t.Length == 4 || t.Length == 7 || t.Length == 9) return t.ToUpperInvariant();
            return "#FF9F00";
        }

        // Phase 1 (upgradeplan.md §2): runtime module selection (PREFERENCE, not entitlement).
        // Entitlement is enforced by ILicenseService; Effective = Requested ∩ Entitled.
        // Default = all modules so old JSON files without this section keep working.
        public OperationSettings Operation { get; set; } = new();
    }

    /// <summary>
    /// Phase 1 additive. Which transaction modules the site *requests*.
    /// The IDs are persisted, so they must remain stable.
    /// </summary>
    public class OperationSettings
    {
        public List<string> ActiveModules { get; set; } = new()
        {
            "StandardWeighing", "HybridPlc", "HybridPlcRfid", "Inventory", "Nexus"
        };
    }

    public class ScannerSettings
    {
        public string Brand { get; set; } = "None";
        public string ScannerIdFilter { get; set; } = string.Empty;
        public bool AutoTriggerOnScan { get; set; } = true;

        // SRK-SYMD6L UHF (EPC C1 GEN2) – RJ45 LAN or RS232/RS485 serial.
        public string ConnectionType { get; set; } = "LAN";
        public string IpAddress { get; set; } = "192.168.1.250";
        public int TcpPort { get; set; } = 60000;
        public string ComPort { get; set; } = "COM1";
        public int BaudRate { get; set; } = 115200;
        public int PowerDbm { get; set; } = 30;
        public string InventoryMode { get; set; } = "Continuous";
        public int DedupMs { get; set; } = 2000;
        public string EpcFilterPrefix { get; set; } = string.Empty;

        public static string NormalizeBrand(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "None";
            var t = raw.Trim().ToLowerInvariant();
            return t switch
            {
                "none" => "None",
                "zebra" => "Zebra",
                "honeywell" => "Honeywell",
                "datalogic" => "Datalogic",
                "wedge" => "Wedge",
                "srk" or "srk-symd6l" or "symd6l" or "srk_symd6l" or "uhf" => "Srk",
                _ => "None"
            };
        }

        public static string NormalizeConnection(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "LAN";
            var t = raw.Trim().ToLowerInvariant();
            return t switch
            {
                "lan" or "tcp" or "rj45" or "ethernet" => "LAN",
                "rs232" or "serial" or "com" => "RS232",
                "rs485" => "RS485",
                _ => "LAN"
            };
        }

        public bool IsEnabled => !string.Equals(Brand, "None", StringComparison.OrdinalIgnoreCase);
    }
}
