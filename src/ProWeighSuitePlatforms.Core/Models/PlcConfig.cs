using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text.Json.Serialization;

namespace ProWeighSuitePlatforms.Models
{
    /// <summary>Single row of the Holding Register mapping table (mirrors mapping.html: Address | Value | WPF Define).</summary>
    public class HoldingRegisterMapEntry
    {
        public ushort Address { get; set; } = 0;
        public ushort Value { get; set; } = 0;
        public string WpfDefine { get; set; } = string.Empty;
    }

    /// <summary>Single row of the Discrete Input mapping table (same Address | Value | WPF Define pattern; WPF Define = weighingInProcess|capture).</summary>
    public class DiscreteInputMapEntry
    {
        public ushort Address { get; set; } = 0;
        public ushort Value { get; set; } = 0;
        public string WpfDefine { get; set; } = string.Empty;
        /// <summary>Input friendly name (3-50 chars, user-editable).</summary>
        public string InputName { get; set; } = string.Empty;
        /// <summary>Cut off time in milliseconds (numeric only).</summary>
        public uint CutOffTimeMs { get; set; } = 50;
    }

    /// <summary>
    /// Configuration for the Modbus RTU (RS-485) PLC integration used by the
    /// conveyor-integrated weighing system. Serialised to/from plc-config.json.
    /// </summary>
    public class PlcConfig
    {
        // ---- Serial link parameters -------------------------------------------

        /// <summary>RS-485 adapter COM port, e.g. "COM3".</summary>
        public string PortName { get; set; } = "COM1";

        public int BaudRate { get; set; } = 9600;

        public int DataBits { get; set; } = 8;

        public StopBits StopBits { get; set; } = StopBits.One;

        public Parity Parity { get; set; } = Parity.None;

        /// <summary>Modbus RTU slave address of the PLC (valid range 1-247).</summary>
        public byte SlaveAddress { get; set; } = 1;

        // ---- Register map -----------------------------------------------------------
        // Target hardware: Wecon LX3V PLC running as a Modbus RTU slave (server);
        // this application is the Modbus master (client).
        //
        // Refactored for WRITING ADDRESS 0: ModbusPoll screenshots prove FC06 @ 0
        // succeeds as HOLDING REGISTER D0 (01 06 00 00 00 01 48 0A => Response ok, F=03 poll
        // 0-7 all readable). FC02 (Discrete Input) and FC03/FC06 (Holding Register) are
        // INDEPENDENT address spaces, so numeric 0 as FC02 (sensor) != numeric 0 as FC06 (D0).
        // The earlier map 11-16 (0x000B-0x0010) required PLC D11..D16 which many WECON
        // programs do not allocate - write to 11 returned 0x02 Illegal address.
        // New verified base is 0 as proven by customer Poll images.
        //
        // Digital INPUTS  : Modbus discrete inputs 0-7 (8 photo-eye / sensor pins, read-only, FC02).
        // Digital OUTPUTS : Modbus holding registers (relay/buzzer outputs, driven by FC06 Write Single Register):
        //
        //   Register 0 (0x0000) -> output 1 (under-weight buzzer) - within 0-5 block (was 11)
        //   Register 1 (0x0001) -> output 2 (over-weight buzzer)  - within 0-5 block (was 12)
        //   Register 2 (0x0002) -> output 3 (accept signal)       - within 0-5 block (was 13)
        //   Registers 3-5 (0x0003-0x0005) -> outputs 4-6 (spare, writable via FC06 manual write / future mapping)
        //   Block is 0-5 (6 registers D0-D5) - inputs 0-7 (FC02) remain independent.
        //
        // Previous firmware used FC05 Write Single Coil at 11-16; then FC06 at 11-16;
        // both rejected by customer PLC. Any 0-65535 now allowed; 0-5 is the default output window.

        /// <summary>First Modbus address of the verified PLC digital input block (FC02 default hint).</summary>
        public const ushort InputAddressBase = 0;

        /// <summary>Last Modbus address of the verified PLC digital input block (FC02 default hint).</summary>
        public const ushort InputAddressLast = 7;

        /// <summary>True when the address falls inside the hint PLC input range 0-7 (FC02). Does NOT preclude FC03/FC06 at same number.</summary>
        public static bool IsInputAddress(ushort address)
            => address >= InputAddressBase && address <= InputAddressLast;

        /// <summary>First holding register address of the verified PLC output block (0x0000) D0 via FC06. Proven by Poll @0.</summary>
        public const ushort OutputRegisterBase = 0;

        /// <summary>Last holding register address of the verified PLC output block (0x0005) via FC06 - refactored to 0-5 block (D0-D5).</summary>
        public const ushort OutputRegisterLast = 5;

        /// <summary>True when the address falls inside the default PLC output holding-register range 0-5 (hint, any 0-65535 still valid).</summary>
        public static bool IsOutputRegister(ushort address)
            => address >= OutputRegisterBase && address <= OutputRegisterLast;

        /// <summary>True for any valid Modbus holding-register address (0-65535). FC06 can target any register.</summary>
        public static bool IsValidHoldingRegisterAddress(ushort address) => true; // ushort range is already 0-65535

        // --- Legacy coil aliases (kept for backward compatibility, no longer used for writes) ---
        [Obsolete("Use OutputRegisterBase - outputs are now holding registers (FC06).")]
        public const ushort OutputCoilBase = OutputRegisterBase;
        [Obsolete("Use OutputRegisterLast - outputs are now holding registers (FC06).")]
        public const ushort OutputCoilLast = OutputRegisterLast;
        [Obsolete("Use IsOutputRegister - outputs are now holding registers (FC06).")]
        public static bool IsOutputCoil(ushort address) => IsOutputRegister(address);

        /// <summary>
        /// Discrete input raised by the PLC photo-eye when an item arrives on the scale.
        /// Valid range: 0-7 (verified PLC input pins).
        /// </summary>
        public ushort ItemArrivedSensorAddress { get; set; } = 0;

        /// <summary>Holding register driven by this app (FC06) to sound the under-weight reject buzzer. D0 within 0-5 (was 11).</summary>
        public ushort UnderWeightBuzzerAddress { get; set; } = OutputRegisterBase;              // 0

        /// <summary>Holding register driven by this app (FC06) to sound the over-weight reject buzzer. D1 within 0-5 (was 12).</summary>
        public ushort OverWeightBuzzerAddress { get; set; } = (ushort)(OutputRegisterBase + 1); // 1

        /// <summary>Holding register driven by this app (FC06) to tell the PLC the weight was accepted. D2 within 0-5 (was 13).</summary>
        public ushort AcceptSignalAddress { get; set; } = (ushort)(OutputRegisterBase + 2);     // 2

        /// <summary>
        /// Configurable Holding Register mapping table — mirrors mapping.html (Address | Value | WPF Define).
        /// Default mirrors the 6-row reference: Address 0 with Values 0-5 mapped to idleReset/ok/under/over/buzzer/reject.
        /// Storage value is normalized short key (e.g. idleReset); display text stays human-readable (Idle/ Reset).
        /// </summary>
        public List<HoldingRegisterMapEntry> HoldingRegisterMaps { get; set; } = GetDefaultHoldingRegisterMaps();

        public static List<HoldingRegisterMapEntry> GetDefaultHoldingRegisterMaps() => new()
        {
            new() { Address = 0, Value = 0, WpfDefine = "idleReset" },
            new() { Address = 0, Value = 1, WpfDefine = "ok" },
            new() { Address = 0, Value = 2, WpfDefine = "under" },
            new() { Address = 0, Value = 3, WpfDefine = "over" },
            new() { Address = 0, Value = 4, WpfDefine = "buzzer" },
            new() { Address = 0, Value = 5, WpfDefine = "reject" },
        };

        // Canonical short keys — Storage values (stable for JSON/code); Display stays friendly.
        public static string[] WpfDefineKeys { get; } =
        {
            "idleReset", "ok", "under", "over", "buzzer", "reject"
        };

        // Kept for backward compat / quick string checks; prefer WpfDefineOptions / Normalize.
        public static string[] WpfDefineOptions => WpfDefineKeys;

        public record WpfDefineOption(string Key, string Display);

        public static WpfDefineOption[] WpfDefineOptionList { get; } =
        {
            new("idleReset", "Idle/ Reset"),
            new("ok", "Weight OK"),
            new("under", "Weight UNDER"),
            new("over", "Weight OVER"),
            new("buzzer", "Buzzer"),
            new("reject", "Reject Output"),
        };

        private static readonly Dictionary<string, string> WpfDefineDisplayMap = WpfDefineOptionList.ToDictionary(o => o.Key, o => o.Display, StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> WpfDefineKeyFromDisplay = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Idle/ Reset"] = "idleReset", ["Idle/Reset"] = "idleReset", ["idleReset"] = "idleReset", ["idle/reset"] = "idleReset",
            ["Weight OK"] = "ok", ["ok"] = "ok", ["WeightOK"] = "ok",
            ["Weight UNDER"] = "under", ["under"] = "under", ["WeightUNDER"] = "under",
            ["Weight OVER"] = "over", ["over"] = "over", ["WeightOVER"] = "over",
            ["Buzzer"] = "buzzer", ["buzzer"] = "buzzer",
            ["Reject Output"] = "reject", ["reject"] = "reject", ["RejectOutput"] = "reject",
        };

        public static string NormalizeWpfDefine(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var t = raw.Trim();
            if (WpfDefineKeyFromDisplay.TryGetValue(t, out var k)) return k;
            // already short key lowercased?
            var lower = t.ToLowerInvariant();
            if (WpfDefineDisplayMap.ContainsKey(lower)) return lower;
            return t; // unknown — keep as-is for forward compat
        }

        public static string GetWpfDefineDisplay(string? key) =>
            !string.IsNullOrWhiteSpace(key) && WpfDefineDisplayMap.TryGetValue(key.Trim(), out var d) ? d : (key ?? string.Empty);

        // ---- Discrete Input WPF mapping (Weighing in process / Capture / Scan) -----------------
        public static string[] DiscreteWpfKeys { get; } = { "weighingInProcess", "capture", "scan" };

        public static WpfDefineOption[] DiscreteWpfOptionList { get; } =
        {
            new("weighingInProcess", "Weighing in process"),
            new("capture", "Capture"),
            new("scan", "Scan"),
        };

        private static readonly Dictionary<string, string> DiscreteWpfDisplayMap = DiscreteWpfOptionList.ToDictionary(o => o.Key, o => o.Display, StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> DiscreteWpfKeyFromDisplay = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Weighing in process"] = "weighingInProcess", ["WeighingInProcess"] = "weighingInProcess", ["weighingInProcess"] = "weighingInProcess", ["weighing in process"] = "weighingInProcess",
            ["Capture"] = "capture", ["capture"] = "capture",
            ["Scan"] = "scan", ["scan"] = "scan",
        };

        public static string NormalizeDiscreteWpfDefine(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var t = raw.Trim();
            if (DiscreteWpfKeyFromDisplay.TryGetValue(t, out var k)) return k;
            var lower = t.ToLowerInvariant();
            if (DiscreteWpfDisplayMap.ContainsKey(lower)) return lower;
            return t;
        }

        public static string GetDiscreteWpfDisplay(string? key) =>
            !string.IsNullOrWhiteSpace(key) && DiscreteWpfDisplayMap.TryGetValue(key.Trim(), out var d) ? d : (key ?? string.Empty);

        public List<DiscreteInputMapEntry> DiscreteInputMaps { get; set; } = GetDefaultDiscreteInputMaps();

        public static List<DiscreteInputMapEntry> GetDefaultDiscreteInputMaps() => new()
        {
            new() { Address = 0, Value = 0, WpfDefine = "scan", InputName = "Scan", CutOffTimeMs = 50 },
            new() { Address = 1, Value = 0, WpfDefine = "weighingInProcess", InputName = "Weighing In Process", CutOffTimeMs = 50 },
            new() { Address = 2, Value = 0, WpfDefine = "capture", InputName = "Capture", CutOffTimeMs = 50 },
        };

        /// <summary>UTC/local timestamp updated automatically on every save.</summary>
        public DateTime LastModified { get; set; } = DateTime.Now;

        /// <summary>Factory defaults matching the shipped sample plc-config.json.</summary>
        public static PlcConfig GetDefault() => new();

        /// <summary>
        /// Returns human-readable validation errors; empty when the configuration is usable.
        /// Called before every save so a typo can never push wrong coil addresses to the line.
        /// </summary>
        public IEnumerable<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(PortName))
                errors.Add("Serial port name must not be empty.");
            if (BaudRate <= 0)
                errors.Add("Baud rate must be greater than zero.");
            if (DataBits < 7 || DataBits > 8)
                errors.Add("Data bits must be 7 or 8.");
            if (SlaveAddress < 1 || SlaveAddress > 247)
                errors.Add("Modbus slave address must be between 1 and 247.");

            // FC02 (discrete input) and FC06 (holding register) are independent spaces,
            // so ItemArrivedSensorAddress (FC02) may share numeric 0 with a holding
            // register (e.g. D0) without conflict. Only check duplicates AMONG holding
            // registers themselves.
            var holdingMap = new (string Name, ushort Address)[]
            {
                (nameof(UnderWeightBuzzerAddress), UnderWeightBuzzerAddress),
                (nameof(OverWeightBuzzerAddress), OverWeightBuzzerAddress),
                (nameof(AcceptSignalAddress), AcceptSignalAddress)
            };

            foreach (var clash in holdingMap.GroupBy(m => m.Address).Where(g => g.Count() > 1))
                errors.Add($"Duplicate holding register address {clash.Key} used by: {string.Join(", ", clash.Select(m => m.Name))} (FC06).");

            // Any ushort 0-65535 is accepted; default 0-5 is the writable output window (0-2 proven by Poll 01 06 00 00 00 01 48 0A).

            return errors;
        }
    }
}
