using System;
using System.Runtime.InteropServices;

namespace ProWeighSuitePlatforms.Services.Scanners
{
    /// <summary>
    /// P/Invoke surface for SW_SDK_V3.2 X64 (Drivers/SRK).
    /// LAN uses SWNetApi.dll, serial RS232/485 uses SWComApi.dll.
    /// DLLs are copied next to the exe via csproj Content; VC++ 2008 x64 required.
    /// Signatures mirror vendor C# samples (SWNetApi.cs / SWComApi.cs).
    /// </summary>
    internal static class SrkNativeApi
    {
        private const string NetDll = "SWNetApi.dll";
        private const string ComDll = "SWComApi.dll";
        public const byte DevAddr = 0xFF;

        // Ensure Drivers/SRK is probed (dev runs from bin/, published single-file extracts beside exe).
        static SrkNativeApi()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var srk = System.IO.Path.Combine(baseDir, "Drivers", "SRK");
                if (System.IO.Directory.Exists(srk)) SetDllDirectory(srk);
            }
            catch { }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        public static class Net
        {
            [DllImport(NetDll, CallingConvention = CallingConvention.StdCall)]
            public static extern bool SWNet_OpenDevice([MarshalAs(UnmanagedType.LPStr)] string ip, UInt16 port);
            [DllImport(NetDll)] public static extern bool SWNet_CloseDevice();
            [DllImport(NetDll)] public static extern bool SWNet_GetDeviceSystemInfo(byte devAdr, byte[] sysInfo);
            [DllImport(NetDll)] public static extern bool SWNet_ReadDeviceOneParam(byte devAdr, byte addr, byte[] value);
            [DllImport(NetDll)] public static extern bool SWNet_SetDeviceOneParam(byte devAdr, byte addr, byte value);
            [DllImport(NetDll)] public static extern bool SWNet_InventoryG2(byte devAdr, byte[] buffer, out ushort totalLen, out ushort cardNum);
            [DllImport(NetDll)] public static extern bool SWNet_ClearTagBuf();
            [DllImport(NetDll)] public static extern byte SWNet_GetTagBuf(byte[] buf, out int length, out int tagNumber);
            [DllImport(NetDll)] public static extern bool SWNet_RelayOn(byte devAdr);
            [DllImport(NetDll)] public static extern bool SWNet_RelayOff(byte devAdr);
        }

        public static class Com
        {
            [DllImport(ComDll, CallingConvention = CallingConvention.StdCall)]
            public static extern bool SWCom_OpenDevice([MarshalAs(UnmanagedType.LPStr)] string com, int baud);
            [DllImport(ComDll)] public static extern bool SWCom_CloseDevice();
            [DllImport(ComDll)] public static extern bool SWCom_GetDeviceSystemInfo(byte devAdr, byte[] sysInfo);
            [DllImport(ComDll)] public static extern bool SWCom_ReadDeviceOneParam(byte devAdr, byte addr, byte[] value);
            [DllImport(ComDll)] public static extern bool SWCom_SetDeviceOneParam(byte devAdr, byte addr, byte value);
            [DllImport(ComDll)] public static extern bool SWCom_InventoryG2(byte devAdr, byte[] buffer, out ushort totalLen, out ushort cardNum);
            [DllImport(ComDll)] public static extern bool SWCom_ClearTagBuf();
            [DllImport(ComDll)] public static extern byte SWCom_GetTagBuf(byte[] buf, out int length, out int tagNumber);
            [DllImport(ComDll)] public static extern bool SWCom_RelayOn(byte devAdr);
            [DllImport(ComDll)] public static extern bool SWCom_RelayOff(byte devAdr);
        }

        /// <summary>
        /// Parse vendor tag buffer (see SDK Form1 button7/timer1): per tag
        /// [packLen][type][ant][id...][rssi] (+6 timestamp bytes if type &amp; 0x80).
        /// Returns EPC hex strings without spaces.
        /// Tolerant: accepts slight framing variations and also scans the raw
        /// buffer as ASCII hex (covers ActiveMode raw-push seen in Hercules).
        /// Layout: packLen = bytes after len byte = type(1)+ant(1)+id(N)+rssi(1)[+ts(6)].
        /// So N = packLen-3 (no TS) or packLen-9 (with TS). Short test tags
        /// (e.g. 00000309, 4 bytes) are kept: minimum 8 hex chars.
        /// </summary>
        public static System.Collections.Generic.List<string> ParseTagBuffer(byte[] buffer, int tagCount)
        {
            var epcs = new System.Collections.Generic.List<string>();
            int pos = 0;
            for (int t = 0; t < tagCount; t++)
            {
                if (pos >= buffer.Length) break;
                int packLen = buffer[pos];
                if (packLen <= 0) break;
                // Original check used >= which dropped a tag ending exactly at the buffer end.
                if (pos + packLen >= buffer.Length && pos + packLen + 1 > buffer.Length) break;
                if (pos + 2 >= buffer.Length) break;
                bool hasTs = (buffer[pos + 1] & 0x80) == 0x80;
                // FIX: was packLen-7/-1 then -3 (one byte short, dropped last EPC byte).
                int idBytes = hasTs ? packLen - 9 : packLen - 3;
                // id bytes: from +3: skip type(1)+ant(1), stop before rssi(1)[+ts(6)]
                int idStart = pos + 3;
                if (idBytes > 0 && idStart + idBytes <= buffer.Length)
                {
                    var sb = new System.Text.StringBuilder(idBytes * 2);
                    for (int i = 0; i < idBytes; i++)
                        sb.Append(buffer[idStart + i].ToString("X2"));
                    var epc = sb.ToString();
                    if (epc.Length >= 8) epcs.Add(epc);
                }
                pos += packLen + 1;
                if (pos <= 0) break; // guard against infinite loop
            }
            // Fallback: if SDK framing parse yields nothing (e.g. ActiveMode raw
            // push where buffer is plain ASCII hex), extract hex strings directly.
            if (epcs.Count == 0 && tagCount > 0)
            {
                foreach (var e in ExtractEpcsFromText(System.Text.Encoding.ASCII.GetString(buffer, 0, System.Math.Min(buffer.Length, 64000))))
                    if (!epcs.Contains(e)) epcs.Add(e);
            }
            return epcs;
        }

        /// <summary>
        /// Hercules-equivalent parser: pull EPC candidates (8-64 hex chars) out of
        /// any text/binary stream. Handles ASCII hex lines, space/comma separated,
        /// "EPC:..." prefixes and CRLF framing used by ActiveMode serial push.
        /// Minimum 8 chars so short test tags (e.g. 00000309) are not dropped.
        /// </summary>
        public static System.Collections.Generic.List<string> ExtractEpcsFromText(string text)
        {
            var out_ = new System.Collections.Generic.List<string>();
            if (string.IsNullOrEmpty(text)) return out_;
            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            // Longest-first so a 24-char EPC inside a longer line is captured whole.
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"[0-9A-Fa-f]{8,64}"))
            {
                var v = m.Value.Trim().ToUpperInvariant().Replace(" ", "");
                if (v.Length < 8) continue;
                // Keep even lengths only (whole bytes); skip odd fragments.
                if ((v.Length & 1) == 1) continue;
                if (seen.Add(v)) out_.Add(v);
            }
            return out_;
        }
    }
}
