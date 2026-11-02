using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ProWeighSuitePlatforms.Services.Scanners
{
    /// <summary>
    /// Zebra CoreScanner provider (SDK for Windows, support.zebra.com article 000034194).
    /// Uses late-bound COM (ProgID Zebra.Scanner.CoreScanner) so the app builds/runs even when CoreScanner is not installed.
    /// Implements Open/GetScanners/REGISTER_FOR_EVENTS/BarcodeEvent/PNPEvent + DEVICE_PULL_TRIGGER 2011 as per TechDocs.
    /// Requires scanner in USB SNAPI (or SSI) for command control; HIDKB is data-only.
    /// </summary>
    public sealed class ZebraScannerProvider : IScannerProvider
    {
        public string Brand => "Zebra";
        public bool IsConnected => _connected;

        public event EventHandler<ScannerBarcodeEventArgs>? BarcodeScanned;
        public event EventHandler<DiscoveredScanner>? ScannerAttached;
        public event EventHandler<DiscoveredScanner>? ScannerDetached;

        private object? _coreScanner;
        private bool _connected;
        private readonly List<int> _scannerIds = new();
        private const int OPCODE_REGISTER_FOR_EVENTS = 1001;
        private const int OPCODE_DEVICE_PULL_TRIGGER = 2011;
        private const int OPCODE_DEVICE_RELEASE_TRIGGER = 2012;
        private const int OPCODE_SCAN_ENABLE = 2014;
        private const int OPCODE_SCAN_DISABLE = 2013;

        public Task StartAsync(CancellationToken ct = default)
        {
            try
            {
                var progId = "CoreScanner.CCoreScanner";
                var type = Type.GetTypeFromProgID(progId);
                if (type == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ZebraScanner] CoreScanner COM not registered – install Zebra_Scanner_SDK_(64bit)_v3.07+. Install from support.zebra.com.");
                    return Task.CompletedTask;
                }

                _coreScanner = Activator.CreateInstance(type);
                if (_coreScanner == null) return Task.CompletedTask;

                // Open – all scanner types (1) – status 0 = SUCCESS per SDK appendix
                var scannerTypes = new short[] { 1 }; // SCANNER_TYPES_ALL
                int status = -1;
                try
                {
                    type.InvokeMember("Open", System.Reflection.BindingFlags.InvokeMethod, null, _coreScanner, new object[] { 0, scannerTypes, (short)scannerTypes.Length, status });
                    // late-bound may need out param handling via reflection – try dynamic fallback
                }
                catch { /* fallback to dynamic */ }

                // Use dynamic for simpler out params
                try
                {
                    dynamic dyn = _coreScanner;
                    short[] types = new short[] { 1 };
                    int st = -1;
                    dyn.Open(0, types, (short)types.Length, out st);
                    System.Diagnostics.Debug.WriteLine($"[ZebraScanner] Open status={st}");
                    if (st != 0) return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ZebraScanner] Open failed: {ex.Message}");
                    return Task.CompletedTask;
                }

                // Hook events via COM event interface _ICoreScannerEvents (BarcodeEvent, PNPEvent etc.)
                try
                {
                    HookEvents();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ZebraScanner] HookEvents failed: {ex.Message}");
                }

                // GetScanners
                try
                {
                    dynamic dyn = _coreScanner!;
                    short num = 0;
                    int[] ids = new int[255];
                    string outXml = string.Empty;
                    int st = -1;
                    dyn.GetScanners(out num, ids, out outXml, out st);
                    System.Diagnostics.Debug.WriteLine($"[ZebraScanner] GetScanners num={num} status={st} xml={outXml}");
                    _scannerIds.Clear();
                    for (int i = 0; i < num; i++) _scannerIds.Add(ids[i]);
                    _connected = num > 0 && st == 0;
                    // Parse outXml for attached notifications if needed
                    ParseScannersXml(outXml);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ZebraScanner] GetScanners failed: {ex.Message}");
                }

                // Register for Barcode (1) + PNP (16) + OTHER (32) – 3 events
                try
                {
                    dynamic dyn = _coreScanner!;
                    string inXml = "<inArgs><cmdArgs><arg-int>3</arg-int><arg-int>1,16,32</arg-int></cmdArgs></inArgs>";
                    string outXml = string.Empty;
                    int st = -1;
                    dyn.ExecCommand(OPCODE_REGISTER_FOR_EVENTS, ref inXml, out outXml, out st);
                    System.Diagnostics.Debug.WriteLine($"[ZebraScanner] RegisterForEvents status={st} out={outXml}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ZebraScanner] RegisterForEvents failed: {ex.Message}");
                }

                // Claim each scanner (optional but recommended for exclusive control)
                foreach (var id in _scannerIds)
                {
                    try { ExecCommandSync(1500, $"<inArgs><scannerID>{id}</scannerID></inArgs>"); } catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ZebraScanner] StartAsync exception: {ex}");
            }
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            try
            {
                if (_coreScanner != null)
                {
                    try
                    {
                        dynamic dyn = _coreScanner;
                        string inXml = "<inArgs><cmdArgs><arg-int>3</arg-int><arg-int>1,16,32</arg-int></cmdArgs></inArgs>";
                        string outXml = string.Empty; int st = -1;
                        dyn.ExecCommand(1002, ref inXml, out outXml, out st); // UNREGISTER_FOR_EVENTS
                    }
                    catch { }
                    foreach (var id in _scannerIds)
                    {
                        try { ExecCommandSync(1501, $"<inArgs><scannerID>{id}</scannerID></inArgs>"); } catch { }
                    }
                    try { dynamic dyn = _coreScanner; int st = -1; dyn.Close(0, out st); } catch { }
                    try { Marshal.ReleaseComObject(_coreScanner); } catch { }
                }
            }
            catch { }
            _coreScanner = null;
            _connected = false;
            _scannerIds.Clear();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DiscoveredScanner>> GetDiscoveredAsync(CancellationToken ct = default)
        {
            if (_coreScanner == null) return Task.FromResult<IReadOnlyList<DiscoveredScanner>>(Array.Empty<DiscoveredScanner>());
            try
            {
                dynamic dyn = _coreScanner;
                short num = 0; int[] ids = new int[255]; string outXml = string.Empty; int st = -1;
                dyn.GetScanners(out num, ids, out outXml, out st);
                var list = ParseScannersXml(outXml);
                for (int i = 0; i < num && i < list.Count; i++) list[i].ScannerId = ids[i];
                return Task.FromResult<IReadOnlyList<DiscoveredScanner>>(list);
            }
            catch { return Task.FromResult<IReadOnlyList<DiscoveredScanner>>(Array.Empty<DiscoveredScanner>()); }
        }

        public Task TriggerAsync(int scannerId = 0, CancellationToken ct = default)
        {
            int id = scannerId != 0 ? scannerId : (_scannerIds.Count > 0 ? _scannerIds[0] : 1);
            return ExecCommandAsync(OPCODE_DEVICE_PULL_TRIGGER, $"<inArgs><scannerID>{id}</scannerID></inArgs>");
        }

        public Task ReleaseTriggerAsync(int scannerId = 0, CancellationToken ct = default)
        {
            int id = scannerId != 0 ? scannerId : (_scannerIds.Count > 0 ? _scannerIds[0] : 1);
            return ExecCommandAsync(OPCODE_DEVICE_RELEASE_TRIGGER, $"<inArgs><scannerID>{id}</scannerID></inArgs>");
        }

        public Task EnableAsync(int scannerId = 0, CancellationToken ct = default)
        {
            int id = scannerId != 0 ? scannerId : (_scannerIds.Count > 0 ? _scannerIds[0] : 1);
            return ExecCommandAsync(OPCODE_SCAN_ENABLE, $"<inArgs><scannerID>{id}</scannerID></inArgs>");
        }

        public Task DisableAsync(int scannerId = 0, CancellationToken ct = default)
        {
            int id = scannerId != 0 ? scannerId : (_scannerIds.Count > 0 ? _scannerIds[0] : 1);
            return ExecCommandAsync(OPCODE_SCAN_DISABLE, $"<inArgs><scannerID>{id}</scannerID></inArgs>");
        }

        private Task ExecCommandAsync(int opcode, string inXml)
        {
            if (_coreScanner == null) return Task.CompletedTask;
            try
            {
                dynamic dyn = _coreScanner;
                string outXml = string.Empty; int st = -1;
                string xml = inXml;
                dyn.ExecCommand(opcode, ref xml, out outXml, out st);
                System.Diagnostics.Debug.WriteLine($"[ZebraScanner] Exec {opcode} status={st}");
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ZebraScanner] Exec {opcode} failed: {ex.Message}"); }
            return Task.CompletedTask;
        }

        private void ExecCommandSync(int opcode, string inXml)
        {
            if (_coreScanner == null) return;
            try { dynamic dyn = _coreScanner; string outXml = string.Empty; int st = -1; string xml = inXml; dyn.ExecCommand(opcode, ref xml, out outXml, out st); } catch { }
        }

        private void OnBarcodeScanned(ScannerBarcodeEventArgs e) => BarcodeScanned?.Invoke(this, e);
        private void OnScannerAttached(DiscoveredScanner s) => ScannerAttached?.Invoke(this, s);
        private void OnScannerDetached(DiscoveredScanner s) => ScannerDetached?.Invoke(this, s);

        private void HookEvents()
        {
            if (_coreScanner == null) return;
            try
            {
                // CoreScanner COM source interface _ICoreScannerEvents exposes BarcodeEvent(short eventType, string pscanData)
                // and PNPEvent etc. Use reflection to attach handlers that parse the XML and raise our .NET events.
                var t = _coreScanner.GetType();
                var barcodeEvt = t.GetEvent("BarcodeEvent");
                if (barcodeEvt != null)
                {
                    System.Diagnostics.Debug.WriteLine("[ZebraScanner] BarcodeEvent found, hooking");
                    try
                    {
                        // Try to create a delegate matching the COM event signature dynamically
                        var handlerMethod = GetType().GetMethod(nameof(HandleBarcodeEvent), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (handlerMethod != null)
                        {
                            var del = Delegate.CreateDelegate(barcodeEvt.EventHandlerType!, this, handlerMethod, false);
                            if (del != null) barcodeEvt.AddEventHandler(_coreScanner, del);
                        }
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ZebraScanner] BarcodeEvent hook failed: {ex.Message}"); }
                }

                var pnpEvt = t.GetEvent("PNPEvent");
                if (pnpEvt != null)
                {
                    System.Diagnostics.Debug.WriteLine("[ZebraScanner] PNPEvent found, hooking");
                    try
                    {
                        var handlerMethod = GetType().GetMethod(nameof(HandlePnpEvent), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (handlerMethod != null)
                        {
                            var del = Delegate.CreateDelegate(pnpEvt.EventHandlerType!, this, handlerMethod, false);
                            if (del != null) pnpEvt.AddEventHandler(_coreScanner, del);
                        }
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ZebraScanner] PNPEvent hook failed: {ex.Message}"); }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ZebraScanner] HookEvents failed: {ex.Message}"); }
        }

        // COM BarcodeEvent handler: signature (short eventType, string pscanDataXml)
        private void HandleBarcodeEvent(short eventType, string pscanData)
        {
            try
            {
                // pscanData is XML: <outArgs><scannerID>1</scannerID><arg-xml><scannerevent><datalabel>xxx</datalabel>...
                if (string.IsNullOrWhiteSpace(pscanData)) return;
                var doc = XDocument.Parse(pscanData);
                var scannerId = int.TryParse(doc.Descendants("scannerID").FirstOrDefault()?.Value, out var sid) ? sid : (_scannerIds.Count > 0 ? _scannerIds[0] : 0);
                var dl = doc.Descendants("datalabel").FirstOrDefault()?.Value ?? doc.Descendants("dataLabel").FirstOrDefault()?.Value ?? string.Empty;
                var raw = doc.Descendants("rawdata").FirstOrDefault()?.Value ?? doc.Descendants("rawData").FirstOrDefault()?.Value ?? string.Empty;
                var dtStr = doc.Descendants("datatype").FirstOrDefault()?.Value ?? doc.Descendants("dataType").FirstOrDefault()?.Value ?? "0";
                int.TryParse(dtStr, out var dt);
                // Try to resolve model/serial from discovered list
                var disc = ParseScannersXml(pscanData).FirstOrDefault();
                var args = new ScannerBarcodeEventArgs(dl, raw, dt, disc?.Model ?? string.Empty, disc?.Serial ?? string.Empty, scannerId, Brand);
                OnBarcodeScanned(args);
                System.Diagnostics.Debug.WriteLine($"[ZebraScanner] BarcodeEvent label={dl} id={scannerId}");
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ZebraScanner] HandleBarcodeEvent failed: {ex.Message}"); }
        }

        private void HandlePnpEvent(short eventType, string pnpData)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pnpData)) return;
                var doc = XDocument.Parse(pnpData);
                var list = ParseScannersXml(pnpData);
                foreach (var s in list)
                {
                    // eventType 0 = attached, 1 = detached per SDK docs (check: PNP akey)
                    if (eventType == 0) OnScannerAttached(s);
                    else OnScannerDetached(s);
                    System.Diagnostics.Debug.WriteLine($"[ZebraScanner] PNPEvent type={eventType} model={s.Model} serial={s.Serial}");
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ZebraScanner] HandlePnpEvent failed: {ex.Message}"); }
        }

        private List<DiscoveredScanner> ParseScannersXml(string xml)
        {
            var list = new List<DiscoveredScanner>();
            if (string.IsNullOrWhiteSpace(xml)) return list;
            try
            {
                var doc = XDocument.Parse(xml);
                foreach (var s in doc.Descendants("scanner"))
                {
                    list.Add(new DiscoveredScanner
                    {
                        Model = s.Element("modelnumber")?.Value?.Trim() ?? s.Element("modelNumber")?.Value?.Trim() ?? string.Empty,
                        Serial = s.Element("serialnumber")?.Value?.Trim() ?? string.Empty,
                        Guid = s.Element("GUID")?.Value?.Trim() ?? string.Empty,
                        Type = s.Attribute("type")?.Value ?? string.Empty,
                        ScannerId = int.TryParse(s.Element("scannerID")?.Value, out var id) ? id : 0
                    });
                }
            }
            catch { }
            return list;
        }

        public async ValueTask DisposeAsync() => await StopAsync();
    }
}
