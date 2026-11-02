using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services.Scanners
{
    /// <summary>
    /// SRK-SYMD6L via official SW_SDK_V3.2 X64 (SWNetApi.dll LAN / SWComApi.dll serial).
    /// Continuous mode = ActiveMode (ClearTagBuf + GetTagBuf poll 150ms).
    /// Triggered/AnswerMode = InventoryG2 single poll per TriggerAsync + 300ms poll.
    /// EPC is raised as ScannerBarcodeEventArgs.Label (upper hex) with dedup + prefix filter.
    ///
    /// Hercules parity (UHF Reader Manual V1.9, no SDK): the reader speaks a binary
    /// frame protocol – Host sends 53 57 (SW) frames, reader answers/pushes 43 54
    /// (CT) frames. In ActiveMode (default, bWorkMode=1) it spontaneously pushes
    /// CMD_ACTIVE_DATA (0x45) frames which is exactly what Hercules displays.
    /// When the SDK DLL path stays silent (missing DLL/VC++, wrong COM/baud, port
    /// busy, protocol mismatch) this provider falls back to speaking that frame
    /// protocol directly over SerialPort / TCP – no SDK needed.
    /// </summary>
    public sealed class SrkSymd6lProvider : IScannerProvider
    {
        public string Brand => "Srk";
        public bool IsConnected => _connected;

        public event EventHandler<ScannerBarcodeEventArgs>? BarcodeScanned;
        public event EventHandler<DiscoveredScanner>? ScannerAttached;
        public event EventHandler<DiscoveredScanner>? ScannerDetached;

        /// <summary>Last failure reason for UI status (port busy = close Hercules, etc.).</summary>
        public string LastError { get; private set; } = string.Empty;

        private readonly AppSettingsService _settingsService = new();
        private CancellationTokenSource? _cts;
        private Task? _loop;
        private bool _connected;
        private bool _isLan = true;
        private bool _continuous = true;
        private bool _sdkOpen;
        private bool _rawActive;
        private ScannerSettings _cfg = new();
        private ScannerSettings? _override;
        private readonly Dictionary<string, DateTime> _lastSeen = new();
        private readonly object _lock = new();
        private string _endpoint = string.Empty;

        // Raw (SDK-free, Hercules-equivalent) handles.
        private SerialPort? _rawPort;
        private readonly object _rawWriteLock = new();
        private TcpClient? _rawTcp;
        private NetworkStream? _rawStream;
        private Task? _rawTcpLoop;
        private CancellationTokenSource? _rawTcpCts;
        private readonly List<byte> _rx = new();
        private readonly object _rxLock = new();
        private readonly StringBuilder _textLine = new();
        private int _sdkEmptyPolls;
        private DateTime _lastRawAnswerPoll;

        /// <summary>
        /// ViewModel/test path: use live on-screen values instead of the saved
        /// file. Call before StartAsync so Test works without Save first.
        /// </summary>
        public void ConfigureOverride(ScannerSettings? cfg) => _override = cfg;

        public async Task StartAsync(CancellationToken ct = default)
        {
            await StopAsync().ConfigureAwait(false);
            try
            {
                if (_override != null)
                {
                    _cfg = _override;
                }
                else
                {
                    var s = await _settingsService.LoadAsync().ConfigureAwait(false);
                    _cfg = s.Scanner ?? new ScannerSettings();
                }
            }
            catch { _cfg = new ScannerSettings(); }

            _isLan = string.Equals(ScannerSettings.NormalizeConnection(_cfg.ConnectionType), "LAN", StringComparison.OrdinalIgnoreCase);
            _continuous = !string.Equals(_cfg.InventoryMode, "Triggered", StringComparison.OrdinalIgnoreCase);
            _endpoint = _isLan ? $"{_cfg.IpAddress}:{_cfg.TcpPort}" : $"{_cfg.ComPort}@{_cfg.BaudRate}";
            LastError = string.Empty;
            _sdkEmptyPolls = 0;

            if (!OpenDevice())
            {
                System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] SDK OpenDevice failed ({_endpoint}): {LastError}. Trying raw protocol fallback (no SDK).");
                if (TryStartRawFallback(out var rawErr))
                {
                    _connected = true;
                    LastError = string.Empty;
                    try { ScannerAttached?.Invoke(this, new DiscoveredScanner { ScannerId = 1, Model = "SRK-SYMD6L", Serial = _endpoint + " (raw)", Type = _isLan ? "LAN-RAW" : _cfg.ConnectionType + "-RAW" }); } catch { }
                    _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    _loop = Task.Run(() => PollLoopAsync(_cts.Token), CancellationToken.None);
                    return;
                }
                System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] Raw fallback also failed: {rawErr}. Check IP:Port/COM, power. Close Hercules if it holds the COM port.");
                // The raw fallback reason is the real blocker (unreachable IP, busy
                // COM port). Do not leave the earlier "SDK DLL missing" text, which
                // only says the SDK path was unavailable.
                if (!string.IsNullOrWhiteSpace(rawErr)) LastError = rawErr!;
                return;
            }

            // Optional: apply RF power 0..30 (param 0x05) – ignore failure.
            try
            {
                var p = (byte)Math.Clamp(_cfg.PowerDbm, 0, 30);
                if (_isLan) SrkNativeApi.Net.SWNet_SetDeviceOneParam(SrkNativeApi.DevAddr, 0x05, p);
                else SrkNativeApi.Com.SWCom_SetDeviceOneParam(SrkNativeApi.DevAddr, 0x05, p);
            }
            catch { }

            // Verify device responds (serial number read).
            try
            {
                var info = new byte[64];
                bool ok = _isLan
                    ? SrkNativeApi.Net.SWNet_GetDeviceSystemInfo(SrkNativeApi.DevAddr, info)
                    : SrkNativeApi.Com.SWCom_GetDeviceSystemInfo(SrkNativeApi.DevAddr, info);
                System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] GetDeviceSystemInfo ok={ok} endpoint={_endpoint}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] GetDeviceSystemInfo threw: {ex.Message}");
            }

            if (_continuous)
            {
                try
                {
                    if (_isLan) SrkNativeApi.Net.SWNet_ClearTagBuf();
                    else SrkNativeApi.Com.SWCom_ClearTagBuf();
                }
                catch { }
            }

            _connected = true;
            try { ScannerAttached?.Invoke(this, new DiscoveredScanner { ScannerId = 1, Model = "SRK-SYMD6L", Serial = _endpoint, Type = _isLan ? "LAN" : _cfg.ConnectionType }); } catch { }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _loop = Task.Run(() => PollLoopAsync(_cts.Token), CancellationToken.None);
        }

        public async Task StopAsync()
        {
            try { _cts?.Cancel(); } catch { }
            if (_loop != null)
            {
                try { await Task.WhenAny(_loop, Task.Delay(800)).ConfigureAwait(false); } catch { }
                _loop = null;
            }
            try { _rawTcpCts?.Cancel(); } catch { }
            if (_rawTcpLoop != null)
            {
                try { await Task.WhenAny(_rawTcpLoop, Task.Delay(800)).ConfigureAwait(false); } catch { }
                _rawTcpLoop = null;
            }
            try { _rawTcpCts?.Dispose(); } catch { } _rawTcpCts = null;
            // Ask reader to stop pushing before dropping the line (ActiveMode).
            try { RawSend(SrkProtocol.StopRead()); } catch { }
            try
            {
                if (_rawPort != null)
                {
                    try { _rawPort.DataReceived -= RawPort_DataReceived; } catch { }
                    try { if (_rawPort.IsOpen) _rawPort.Close(); } catch { }
                    try { _rawPort.Dispose(); } catch { }
                    _rawPort = null;
                }
            }
            catch { }
            try
            {
                if (_rawStream != null) { try { _rawStream.Close(); } catch { } _rawStream = null; }
                if (_rawTcp != null) { try { _rawTcp.Close(); } catch { } _rawTcp = null; }
            }
            catch { }
            _rawActive = false;
            lock (_rxLock) { _rx.Clear(); _textLine.Clear(); }
            try
            {
                if (_sdkOpen)
                {
                    if (_isLan) SrkNativeApi.Net.SWNet_CloseDevice();
                    else SrkNativeApi.Com.SWCom_CloseDevice();
                }
            }
            catch { }
            finally { _sdkOpen = false; }
            _cts?.Dispose(); _cts = null;
            if (_connected)
            {
                _connected = false;
                try { ScannerDetached?.Invoke(this, new DiscoveredScanner { ScannerId = 1, Model = "SRK-SYMD6L" }); } catch { }
            }
            lock (_lock) _lastSeen.Clear();
        }

        public Task<IReadOnlyList<DiscoveredScanner>> GetDiscoveredAsync(CancellationToken ct = default)
        {
            // Native DLL has no broadcast discovery – probe SDK open + GetDeviceSystemInfo,
            // then fall back to a real protocol ping (READ_SYSTEM_PARAM) without SDK.
            return Task.Run<IReadOnlyList<DiscoveredScanner>>(() =>
            {
                ScannerSettings cfg;
                if (_override != null) cfg = _override;
                else
                {
                    try { cfg = _settingsService.LoadAsync().GetAwaiter().GetResult().Scanner ?? new ScannerSettings(); }
                    catch { cfg = new ScannerSettings(); }
                }
                bool lan = string.Equals(ScannerSettings.NormalizeConnection(cfg.ConnectionType), "LAN", StringComparison.OrdinalIgnoreCase);
                string ep = lan ? $"{cfg.IpAddress}:{cfg.TcpPort}" : $"{cfg.ComPort}@{cfg.BaudRate}";
                try
                {
                    bool opened = lan
                        ? SrkNativeApi.Net.SWNet_OpenDevice(cfg.IpAddress?.Trim() ?? "", (UInt16)(cfg.TcpPort > 0 ? cfg.TcpPort : 60000))
                        : SrkNativeApi.Com.SWCom_OpenDevice(cfg.ComPort?.Trim() ?? "COM1", cfg.BaudRate > 0 ? cfg.BaudRate : 115200);
                    if (!opened)
                    {
                        var hint = ProbeRawReachability(cfg, lan);
                        LastError = hint;
                        System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] Discover SDK open failed ({ep}): {hint}");
                        if (!string.IsNullOrEmpty(hint) && hint.StartsWith("RAW-OK", StringComparison.OrdinalIgnoreCase))
                            return new List<DiscoveredScanner>
                            {
                                new() { ScannerId = 1, Model = "SRK-SYMD6L", Serial = $"{ep} ({hint})", Type = lan ? "LAN-RAW" : cfg.ConnectionType + "-RAW" }
                            };
                        return Array.Empty<DiscoveredScanner>();
                    }
                    var info = new byte[64];
                    _ = lan ? SrkNativeApi.Net.SWNet_GetDeviceSystemInfo(0xFF, info)
                            : SrkNativeApi.Com.SWCom_GetDeviceSystemInfo(0xFF, info);
                    try { if (lan) SrkNativeApi.Net.SWNet_CloseDevice(); else SrkNativeApi.Com.SWCom_CloseDevice(); } catch { }
                    var sn = lan || info.Length >= 9 ? BitConverter.ToString(info, 2, Math.Min(7, info.Length - 2)).Replace("-", "") : ep;
                    return new List<DiscoveredScanner>
                    {
                        new() { ScannerId = 1, Model = "SRK-SYMD6L", Serial = $"{ep} SN:{sn}", Type = lan ? "LAN" : cfg.ConnectionType }
                    };
                }
                catch (DllNotFoundException)
                {
                    var hint = ProbeRawReachability(cfg, lan);
                    System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] SDK DLL not found – raw probe: {hint}");
                    LastError = "SDK DLL not found (Drivers/SRK); " + hint;
                    if (hint.StartsWith("RAW-OK", StringComparison.OrdinalIgnoreCase))
                        return new List<DiscoveredScanner>
                        {
                            new() { ScannerId = 1, Model = "SRK-SYMD6L", Serial = $"{ep} ({hint})", Type = lan ? "LAN-RAW" : cfg.ConnectionType + "-RAW" }
                        };
                    return Array.Empty<DiscoveredScanner>();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] Discover failed: {ex.Message}");
                    LastError = ex.Message;
                    return Array.Empty<DiscoveredScanner>();
                }
            }, ct);
        }

        public Task TriggerAsync(int scannerId = 0, CancellationToken ct = default)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    if (_rawActive)
                    {
                        // Answer/Trigger work mode over raw link: send one inventory.
                        RawSend(SrkProtocol.Inventory());
                        return;
                    }
                    var buf = new byte[64000];
                    if (_sdkOpen || !_rawActive)
                    {
                        if (_isLan)
                        {
                            if (SrkNativeApi.Net.SWNet_InventoryG2(SrkNativeApi.DevAddr, buf, out _, out ushort n) && n > 0)
                                foreach (var e in SrkNativeApi.ParseTagBuffer(buf, n)) EmitEpc(e);
                        }
                        else
                        {
                            if (SrkNativeApi.Com.SWCom_InventoryG2(SrkNativeApi.DevAddr, buf, out _, out ushort n) && n > 0)
                                foreach (var e in SrkNativeApi.ParseTagBuffer(buf, n)) EmitEpc(e);
                        }
                    }
                }
                catch { }
            }, ct);
            return Task.CompletedTask;
        }

        public Task ReleaseTriggerAsync(int scannerId = 0, CancellationToken ct = default) => Task.CompletedTask;

        public Task EnableAsync(int scannerId = 0, CancellationToken ct = default)
        {
            _continuous = true;
            try
            {
                if (_rawActive) RawSend(SrkProtocol.StartRead());
                if (_sdkOpen)
                {
                    if (_isLan) SrkNativeApi.Net.SWNet_ClearTagBuf();
                    else SrkNativeApi.Com.SWCom_ClearTagBuf();
                }
            }
            catch { }
            return Task.CompletedTask;
        }

        public Task DisableAsync(int scannerId = 0, CancellationToken ct = default)
        {
            _continuous = false;
            try { if (_rawActive) RawSend(SrkProtocol.StopRead()); } catch { }
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

        private bool OpenDevice()
        {
            try
            {
                bool ok;
                if (_isLan)
                {
                    var ip = (_cfg.IpAddress ?? "192.168.1.250").Trim();
                    var port = _cfg.TcpPort > 0 ? _cfg.TcpPort : 60000;
                    ok = SrkNativeApi.Net.SWNet_OpenDevice(ip, (UInt16)port);
                    if (!ok) LastError = $"Cannot open {ip}:{port} via SDK. Check ping, static IP, reader power, firewall.";
                }
                else
                {
                    var com = string.IsNullOrWhiteSpace(_cfg.ComPort) ? "COM1" : _cfg.ComPort.Trim();
                    var baud = _cfg.BaudRate > 0 ? _cfg.BaudRate : 115200;
                    ok = SrkNativeApi.Com.SWCom_OpenDevice(com, baud);
                    if (!ok) LastError = $"Cannot open {com}@{baud} via SDK. Close Hercules (port busy?), check Device Manager COM number and baud.";
                }
                _sdkOpen = ok;
                return ok;
            }
            catch (DllNotFoundException ex)
            {
                LastError = $"SDK DLL missing ({ex.Message}). Using raw no-SDK protocol.";
                System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] DLL missing: {ex.Message}");
                _sdkOpen = false;
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] OpenDevice threw: {ex.Message}");
                _sdkOpen = false;
                return false;
            }
        }

        private void MarkDisconnected(string reason)
        {
            LastError = reason;
            if (!_connected) return;
            _connected = false;
            System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] Disconnected: {reason}");
            try { ScannerDetached?.Invoke(this, new DiscoveredScanner { ScannerId = 1, Model = "SRK-SYMD6L", Serial = _endpoint }); } catch { }
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            int delay = _continuous ? 150 : 400;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_rawActive)
                    {
                        // Detect a cable/network drop so the UI stops reporting Connected.
                        if (_rawPort != null && !_rawPort.IsOpen)
                        {
                            MarkDisconnected("Serial scanner disconnected.");
                            break;
                        }
                        if (_rawTcp != null && !_rawTcp.Connected)
                        {
                            MarkDisconnected("TCP scanner disconnected.");
                            break;
                        }
                        // ActiveMode pushes 0x45 spontaneously (parsed in FeedRawBytes).
                        // AnswerMode needs a periodic inventory poll on the raw link.
                        if (!_continuous && (DateTime.UtcNow - _lastRawAnswerPoll).TotalMilliseconds >= 400)
                        {
                            _lastRawAnswerPoll = DateTime.UtcNow;
                            try { RawSend(SrkProtocol.Inventory()); } catch { }
                        }
                        try { await Task.Delay(150, token).ConfigureAwait(false); } catch { break; }
                        continue;
                    }
                    var buf = new byte[64000];
                    if (_continuous)
                    {
                        // ActiveMode SDK path. Accept ANY ret with n>0: some firmware
                        // returns 0/1 instead of 2 while still delivering tags.
                        bool got = false;
                        if (_isLan)
                        {
                            byte ret = 0xFF;
                            int n = 0;
                            try { ret = SrkNativeApi.Net.SWNet_GetTagBuf(buf, out _, out n); } catch { }
                            if (n > 0)
                            {
                                got = true;
                                foreach (var e in SrkNativeApi.ParseTagBuffer(buf, n)) EmitEpc(e);
                            }
                            else if (ret != 2 && ret != 0)
                                System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] LAN GetTagBuf ret={ret} n={n}");
                        }
                        else
                        {
                            byte ret = 0xFF;
                            int n = 0;
                            try { ret = SrkNativeApi.Com.SWCom_GetTagBuf(buf, out _, out n); } catch { }
                            if (n > 0)
                            {
                                got = true;
                                foreach (var e in SrkNativeApi.ParseTagBuffer(buf, n)) EmitEpc(e);
                            }
                            else if (ret != 2 && ret != 0)
                                System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] COM GetTagBuf ret={ret} n={n}");
                        }
                        // Auto-switch: SDK open but silent for ~5s on serial while the
                        // reader is known-good in Hercules (raw push) -> go raw.
                        if (!_isLan && !_rawActive)
                        {
                            _sdkEmptyPolls = got ? 0 : _sdkEmptyPolls + 1;
                            if (_sdkEmptyPolls >= 33) // ~5s at 150ms
                            {
                                System.Diagnostics.Debug.WriteLine("[SrkSymd6l] SDK silent for 5s, switching to raw no-SDK listener.");
                                if (TryStartRawFallbackAfterSdk(out var err))
                                    System.Diagnostics.Debug.WriteLine("[SrkSymd6l] Raw fallback active after SDK silence.");
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] Raw fallback after silence failed: {err}");
                                    _sdkEmptyPolls = 0;
                                }
                            }
                        }
                        else if (got) _sdkEmptyPolls = 0;
                    }
                    else
                    {
                        try
                        {
                            if (_isLan)
                            {
                                if (_sdkOpen && SrkNativeApi.Net.SWNet_InventoryG2(SrkNativeApi.DevAddr, buf, out _, out ushort n1) && n1 > 0)
                                    foreach (var e in SrkNativeApi.ParseTagBuffer(buf, n1)) EmitEpc(e);
                            }
                            else
                            {
                                if (_sdkOpen && SrkNativeApi.Com.SWCom_InventoryG2(SrkNativeApi.DevAddr, buf, out _, out ushort n2) && n2 > 0)
                                    foreach (var e in SrkNativeApi.ParseTagBuffer(buf, n2)) EmitEpc(e);
                            }
                        }
                        catch { }
                    }
                }
                catch (DllNotFoundException)
                {
                    MarkDisconnected("Scanner SDK not found.");
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] Poll error: {ex.Message}");
                }
                try { await Task.Delay(delay, token).ConfigureAwait(false); } catch { break; }
            }
        }

        #region Raw no-SDK protocol (Manual V1.9 frames, Hercules parity)

        private bool TryStartRawFallback(out string? error)
        {
            error = null;
            lock (_rxLock) _rx.Clear();
            try
            {
                if (_isLan)
                {
                    var ip = (_cfg.IpAddress ?? "192.168.1.250").Trim();
                    var port = _cfg.TcpPort > 0 ? _cfg.TcpPort : 60000;
                    var client = new TcpClient();
                    var ar = client.BeginConnect(ip, port, null, null);
                    bool success = ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3));
                    if (!success || !client.Connected)
                    {
                        try { client.Close(); } catch { }
                        error = $"RAW-FAIL cannot connect {ip}:{port} (ping reader, check power/subnet).";
                        return false;
                    }
                    try { client.EndConnect(ar); } catch (Exception ex) { error = "RAW-FAIL " + ex.Message; try { client.Close(); } catch { } return false; }
                    _rawTcp = client;
                    _rawStream = client.GetStream();
                    _rawActive = true;
                    if (_continuous) RawSend(SrkProtocol.StartRead());
                    else RawSend(SrkProtocol.SetWorkMode(0x00)); // Answer
                    _rawTcpCts = new CancellationTokenSource();
                    _rawTcpLoop = Task.Run(() => RawTcpReadLoopAsync(_rawTcpCts.Token));
                    System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] Raw TCP no-SDK connected {_endpoint} mode={(_continuous ? "Active" : "Answer")}");
                    return true;
                }
                else
                {
                    var com = string.IsNullOrWhiteSpace(_cfg.ComPort) ? "COM1" : _cfg.ComPort.Trim();
                    var baud = _cfg.BaudRate > 0 ? _cfg.BaudRate : 115200;
                    var available = SerialPort.GetPortNames();
                    var sp = new SerialPort(com, baud, Parity.None, 8, StopBits.One)
                    {
                        Handshake = Handshake.None,
                        ReadTimeout = 500,
                        WriteTimeout = 1000,
                        DtrEnable = true,
                        RtsEnable = true
                    };
                    try { sp.Open(); }
                    catch (UnauthorizedAccessException)
                    {
                        error = $"Port {com} busy – close Hercules / vendor demo and retry. Available: {string.Join(",", available)}";
                        try { sp.Dispose(); } catch { }
                        return false;
                    }
                    catch (Exception ex)
                    {
                        error = $"Cannot open {com}@{baud}: {ex.Message}. Available: {string.Join(",", available)}";
                        try { sp.Dispose(); } catch { }
                        return false;
                    }
                    sp.DataReceived += RawPort_DataReceived;
                    _rawPort = sp;
                    _rawActive = true;
                    // Drain anything already streaming, then (re)start Active reading.
                    try { DrainSerialToRx(sp); } catch { }
                    if (_continuous) RawSend(SrkProtocol.StartRead());
                    else RawSend(SrkProtocol.SetWorkMode(0x00));
                    _lastRawAnswerPoll = DateTime.UtcNow;
                    System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] Raw serial no-SDK open {_endpoint} mode={(_continuous ? "Active" : "Answer")} (Hercules mode)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = "RAW-FAIL " + ex.Message;
                return false;
            }
        }

        private bool TryStartRawFallbackAfterSdk(out string? error)
        {
            try
            {
                if (_isLan) { try { SrkNativeApi.Net.SWNet_CloseDevice(); } catch { } }
                else { try { SrkNativeApi.Com.SWCom_CloseDevice(); } catch { } }
            }
            catch { }
            _sdkOpen = false;
            return TryStartRawFallback(out error);
        }

        private void RawSend(byte[] frame)
        {
            if (frame == null || frame.Length == 0) return;
            lock (_rawWriteLock)
            {
                try { _rawPort?.Write(frame, 0, frame.Length); } catch { }
                try { _rawStream?.Write(frame, 0, frame.Length); } catch { }
            }
        }

        private void DrainSerialToRx(SerialPort sp)
        {
            try
            {
                int n = sp.BytesToRead;
                while (n > 0)
                {
                    var tmp = new byte[Math.Min(n, 4096)];
                    int r = sp.Read(tmp, 0, tmp.Length);
                    if (r > 0) FeedRawBytes(tmp, r);
                    n = sp.BytesToRead;
                }
            }
            catch { }
        }

        private void RawPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                var sp = (SerialPort)sender;
                int n = sp.BytesToRead;
                while (n > 0)
                {
                    var tmp = new byte[Math.Min(n, 4096)];
                    int r;
                    try { r = sp.Read(tmp, 0, tmp.Length); } catch { break; }
                    if (r <= 0) break;
                    FeedRawBytes(tmp, r);
                    n = sp.BytesToRead;
                }
            }
            catch { }
        }

        private async Task RawTcpReadLoopAsync(CancellationToken token)
        {
            var buf = new byte[8192];
            string? reason = null;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int read;
                    try
                    {
                        if (_rawStream == null) { reason = "Scanner TCP stream closed."; break; }
                        read = await _rawStream.ReadAsync(buf, 0, buf.Length, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { reason = $"Scanner TCP read error: {ex.Message}"; break; }
                    if (read <= 0) { reason = "Scanner closed the TCP connection."; break; }
                    FeedRawBytes(buf, read);
                }
            }
            catch { }
            finally
            {
                if (!token.IsCancellationRequested && reason != null)
                    MarkDisconnected(reason);
            }
        }

        /// <summary>Binary CT-frame parser (Manual §3.2) + custom STX-protocol fallback.</summary>
        private void FeedRawBytes(byte[] chunk, int count)
        {
            lock (_rxLock)
            {
                for (int i = 0; i < count; i++) _rx.Add(chunk[i]);
                if (_rx.Count > 131072) _rx.RemoveRange(0, _rx.Count - 131072);
                // Also try ASCII custom-protocol extraction on printable chunks.
                try
                {
                    string ascii = Encoding.ASCII.GetString(chunk, 0, count);
                    foreach (var e in SrkNativeApi.ExtractEpcsFromText(ascii))
                        EmitEpc(e);
                }
                catch { }
                // Plain-text simulators (e.g. RFID_ScanerSimulator on TCP) send an
                // arbitrary ID per line. Accept those short/alphanumeric IDs too;
                // binary reader frames are parsed below.
                if (IsPrintableText(chunk, count))
                {
                    try
                    {
                        _textLine.Append(Encoding.ASCII.GetString(chunk, 0, count));
                        if (_textLine.Length > 8192) _textLine.Remove(0, _textLine.Length - 4096);
                        ProcessTextLines();
                    }
                    catch { }
                }
                else if (_textLine.Length > 0)
                {
                    _textLine.Clear();
                }
                // Parse complete CT frames.
                for (;;)
                {
                    if (_rx.Count < 4) break;
                    // Custom STX protocol: 0x02 ... 0x0D 0x0A 0x03
                    if (_rx[0] == 0x02)
                    {
                        int end = -1;
                        for (int i = 1; i + 2 < _rx.Count; i++)
                        {
                            if (_rx[i] == 0x0D && _rx[i + 1] == 0x0A && _rx[i + 2] == 0x03) { end = i + 3; break; }
                        }
                        if (end < 0) break; // wait for more
                        try
                        {
                            string id = Encoding.ASCII.GetString(_rx.GetRange(1, end - 4).ToArray()).Trim();
                            foreach (var e in SrkNativeApi.ExtractEpcsFromText(id)) EmitEpc(e);
                        }
                        catch { }
                        _rx.RemoveRange(0, end);
                        continue;
                    }
                    if (!(_rx[0] == 0x43 && _rx[1] == 0x54))
                    {
                        // Resync: drop until next plausible head.
                        int next = -1;
                        for (int i = 1; i < _rx.Count; i++)
                        {
                            if (_rx[i] == 0x43 && i + 1 < _rx.Count && _rx[i + 1] == 0x54) { next = i; break; }
                            if (_rx[i] == 0x02) { next = i; break; }
                        }
                        if (next < 0) { _rx.Clear(); break; }
                        _rx.RemoveRange(0, next);
                        continue;
                    }
                    if (_rx.Count < 4) break;
                    int len = (_rx[2] << 8) | _rx[3]; // Addr+Cmd+Status+Data+Check
                    if (len < 4 || len > 1027) { _rx.RemoveAt(0); continue; }
                    int total = 4 + len;
                    if (_rx.Count < total) break; // wait for more
                    var frame = _rx.GetRange(0, total).ToArray();
                    _rx.RemoveRange(0, total);
                    HandleCtFrame(frame);
                }
            }
        }

        private static bool IsPrintableText(byte[] chunk, int count)
        {
            for (int i = 0; i < count; i++)
            {
                byte b = chunk[i];
                if (b != (byte)'\r' && b != (byte)'\n' && b != (byte)'\t' && (b < 0x20 || b > 0x7E))
                    return false;
            }
            return count > 0;
        }

        private void ProcessTextLines()
        {
            for (;;)
            {
                int end = -1;
                for (int i = 0; i < _textLine.Length; i++)
                {
                    char c = _textLine[i];
                    if (c == '\r' || c == '\n') { end = i; break; }
                }
                if (end < 0) break;
                string line = _textLine.ToString(0, end).Trim();
                _textLine.Remove(0, end + 1);
                HandlePlainTextLine(line);
            }
        }

        private void HandlePlainTextLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            // Optional label used by some simulators: "EPC: 304", "TAG=ABC".
            string body = line.Trim().Trim('\0', '"', '\'');
            foreach (var label in new[] { "RFID", "EPC", "TAG", "CODE", "ID" })
            {
                if (!body.StartsWith(label, StringComparison.OrdinalIgnoreCase)) continue;
                string rest = body.Substring(label.Length).TrimStart();
                if (rest.Length > 0 && (rest[0] == ':' || rest[0] == '='))
                    body = rest.Substring(1).Trim();
                break;
            }
            if (body.Length == 0) return;
            // Long hex EPCs are handled by the existing text extraction.
            var hexes = SrkNativeApi.ExtractEpcsFromText(body);
            if (hexes.Count > 0)
            {
                foreach (var hex in hexes) EmitEpc(hex);
                return;
            }
            // A plain-text ID is the whole line (simulator sends one ID per message).
            if (TryNormalizePlainTextId(body, out var id))
                EmitTextEpc(id);
        }

        private static bool TryNormalizePlainTextId(string value, out string id)
        {
            id = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (id.Length < 1 || id.Length > 64) return false;
            foreach (char c in id)
            {
                if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                    return false;
            }
            return true;
        }

        private void HandleCtFrame(byte[] frame)
        {
            // frame: 43 54 LenHi LenLo Addr Cmd Status Data.. Check
            try
            {
                if (frame.Length < 8) return;
                if (!SrkProtocol.VerifyCheck(frame)) return;
                byte addr = frame[4];
                byte cmd = frame[5];
                byte status = frame[6];
                _ = addr;
                if (cmd == 0xFF) return; // heartbeat, ignore
                if (status != 0x01) return; // failed / no-tag
                if (cmd == 0x45 || cmd == 0x01)
                {
                    // Data = SN(7) + TagNum(1 or 2) + tags[len,type,ant,id,rssi]
                    int dataLen = frame.Length - 8; // excl head/len/addr/cmd/status/check
                    if (dataLen < 8) return;
                    var data = new byte[dataLen];
                    Array.Copy(frame, 7, data, 0, dataLen);
                    foreach (var epc in SrkProtocol.ExtractEpcsFromTagData(data))
                        EmitEpc(epc);
                }
                // else: sysinfo/params responses – ignore (discovery uses SDK path).
            }
            catch { }
        }

        private string ProbeRawReachability(ScannerSettings cfg, bool lan)
        {
            // Real protocol ping: READ_SYSTEM_PARAM, expect CT 00 10 reply.
            try
            {
                if (lan)
                {
                    try
                    {
                        using var c = new TcpClient();
                        var ar = c.BeginConnect(cfg.IpAddress?.Trim() ?? "", cfg.TcpPort > 0 ? cfg.TcpPort : 60000, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2)) || !c.Connected)
                            return "TCP unreachable – ping IP, check power/subnet/port";
                        c.EndConnect(ar);
                        var s = c.GetStream();
                        s.ReadTimeout = 1500; s.WriteTimeout = 1500;
                        var req = SrkProtocol.ReadSystemParam();
                        s.Write(req, 0, req.Length);
                        var resp = new byte[256];
                        int r = s.Read(resp, 0, resp.Length);
                        if (r >= 8 && resp[0] == 0x43 && resp[1] == 0x54)
                            return "RAW-OK TCP reader answers without SDK";
                        return "TCP reachable but no reader reply – check reader TCP mode/RJ45 transport";
                    }
                    catch (Exception ex) { return "TCP probe: " + ex.Message; }
                }
                var ports = SerialPort.GetPortNames();
                var want = (cfg.ComPort ?? "COM1").Trim();
                bool exists = false;
                foreach (var p in ports) if (string.Equals(p, want, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                if (!exists) return $"Port {want} not found. Available: {string.Join(",", ports)}";
                // Port exists – protocol ping (must not fight Hercules for the port).
                SerialPort? sp = null;
                try
                {
                    sp = new SerialPort(want, cfg.BaudRate > 0 ? cfg.BaudRate : 115200, Parity.None, 8, StopBits.One)
                    {
                        Handshake = Handshake.None, ReadTimeout = 1500, WriteTimeout = 1500
                    };
                    sp.Open();
                    sp.DiscardInBuffer();
                    var req = SrkProtocol.ReadSystemParam();
                    sp.Write(req, 0, req.Length);
                    var buf = new byte[256];
                    int read = 0;
                    try { read = sp.Read(buf, 0, buf.Length); } catch (TimeoutException) { read = 0; }
                    if (read >= 8 && buf[0] == 0x43 && buf[1] == 0x54)
                        return $"RAW-OK {want} reader answers without SDK";
                    if (read > 0)
                        return $"RAW-OK {want} exists, {read} byte(s) heard (close Hercules if busy, check baud 115200)";
                    return $"{want} exists but reader silent – check baud (115200?), power, RS232 transport, close Hercules";
                }
                catch (UnauthorizedAccessException)
                {
                    return $"Port {want} busy – close Hercules / vendor demo and retry";
                }
                catch (Exception ex) { return $"Probe {want}: {ex.Message}"; }
                finally { try { if (sp != null) { if (sp.IsOpen) sp.Close(); sp.Dispose(); } } catch { } }
            }
            catch (Exception ex) { return "RAW probe: " + ex.Message; }
        }

        #endregion

        private void EmitEpc(string epc) => EmitEpcCore(epc, plainTextId: false);

        private void EmitTextEpc(string epc) => EmitEpcCore(epc, plainTextId: true);

        private void EmitEpcCore(string epc, bool plainTextId)
        {
            if (string.IsNullOrWhiteSpace(epc)) return;
            epc = epc.Trim().ToUpperInvariant().Replace(" ", "");
            if (plainTextId)
            {
                // Simulator/plain-text IDs (Tub Master allows letters+digits, 2-32).
                if (epc.Length < 1 || epc.Length > 64) return;
                foreach (char c in epc)
                {
                    if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z'))) return;
                }
            }
            else
            {
                if (epc.Length < 8) return;
                if ((epc.Length & 1) == 1) return;
                foreach (char c in epc)
                {
                    if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'))) return;
                }
            }
            var prefix = _cfg.EpcFilterPrefix?.Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(prefix) && !epc.StartsWith(prefix, StringComparison.Ordinal)) return;
            var dedup = _cfg.DedupMs > 0 ? _cfg.DedupMs : 2000;
            lock (_lock)
            {
                if (_lastSeen.TryGetValue(epc, out var last) && (DateTime.UtcNow - last).TotalMilliseconds < dedup)
                    return;
                _lastSeen[epc] = DateTime.UtcNow;
            }
            var filter = _cfg.ScannerIdFilter?.Trim().ToUpperInvariant() ?? string.Empty;
            if (!string.IsNullOrEmpty(filter) && !epc.Contains(filter, StringComparison.Ordinal)) return;
            try
            {
                BarcodeScanned?.Invoke(this, new ScannerBarcodeEventArgs(epc, epc, 0, "SRK-SYMD6L", _endpoint, 1, Brand));
                System.Diagnostics.Debug.WriteLine($"[SrkSymd6l] EPC {epc}");
            }
            catch { }
        }
    }

    /// <summary>
    /// UHF Reader Manual V1.9 frame codec. Host→reader head 0x53 0x57 ("SW"),
    /// reader→host head 0x43 0x54 ("CT"). Length (2B big-endian) counts all bytes
    /// after the Length field (Addr+Cmd[+Status]+Data+Check). Check = (-sum)&amp;0xFF
    /// over every byte from Head through last Data byte (verified against the
    /// manual's worked examples, e.g. 53 57 00 03 FF 10 → 0x44).
    /// </summary>
    internal static class SrkProtocol
    {
        public const byte Broadcast = 0xFF;

        public static byte CheckOf(byte[] withoutCheck)
        {
            int sum = 0;
            foreach (var b in withoutCheck) sum += b;
            return (byte)((-sum) & 0xFF);
        }

        public static bool VerifyCheck(byte[] frame)
        {
            if (frame == null || frame.Length < 2) return false;
            int sum = 0;
            for (int i = 0; i + 1 < frame.Length; i++) sum += frame[i];
            return (byte)((-sum) & 0xFF) == frame[frame.Length - 1];
        }

        private static byte[] Build(byte addr, byte cmd, byte[]? data = null)
        {
            data ??= Array.Empty<byte>();
            int len = 1 + 1 + data.Length + 1; // Addr+Cmd+Data+Check
            var pre = new byte[2 + 2 + 1 + 1 + data.Length];
            pre[0] = 0x53; pre[1] = 0x57;
            pre[2] = (byte)((len >> 8) & 0xFF); pre[3] = (byte)(len & 0xFF);
            pre[4] = addr; pre[5] = cmd;
            if (data.Length > 0) Array.Copy(data, 0, pre, 6, data.Length);
            byte chk = CheckOf(pre);
            var full = new byte[pre.Length + 1];
            Array.Copy(pre, full, pre.Length);
            full[full.Length - 1] = chk;
            return full;
        }

        public static byte[] ReadSystemParam() => Build(Broadcast, 0x10);
        public static byte[] Inventory() => Build(Broadcast, 0x01);
        public static byte[] StartRead() => Build(Broadcast, 0x41);
        public static byte[] StopRead() => Build(Broadcast, 0x40);
        public static byte[] ConfirmActive() => Build(Broadcast, 0x45);
        public static byte[] SetWorkMode(byte mode) => Build(Broadcast, 0x24, new[] { (byte)0x02, mode });
        public static byte[] SetPower(byte dbm) => Build(Broadcast, 0x24, new[] { (byte)0x05, dbm });

        /// <summary>
        /// Tag-data payload = SN(7) + TagNum(1 or 2 bytes) + N × [len,type,ant,id,rssi].
        /// Tries 1-byte then 2-byte count; returns EPC hex strings.
        /// </summary>
        public static List<string> ExtractEpcsFromTagData(byte[] data)
        {
            var out_ = new List<string>();
            if (data == null || data.Length < 8) return out_;
            // Layout per manual: 7-byte SN first.
            if (TryParseWithCount(data, 7, 1, out_)) return out_;
            out_.Clear();
            if (TryParseWithCount(data, 7, 2, out_)) return out_;
            // Fallback: scan for len-prefixed tags anywhere in payload.
            out_.Clear();
            ScanTags(data, 0, out_);
            return out_;
        }

        private static bool TryParseWithCount(byte[] data, int snLen, int countLen, List<string> out_)
        {
            if (data.Length < snLen + countLen) return false;
            int tagNum = countLen == 1 ? data[snLen] : (data[snLen] << 8) | data[snLen + 1];
            if (tagNum <= 0 || tagNum > 64) return false;
            int pos = snLen + countLen;
            int start = out_.Count;
            for (int t = 0; t < tagNum; t++)
            {
                if (pos >= data.Length) { out_.RemoveRange(start, out_.Count - start); return false; }
                int L = data[pos];
                if (L <= 3 || pos + 1 + L > data.Length) { out_.RemoveRange(start, out_.Count - start); return false; }
                int idLen = L - 3; // excl type/ant/rssi
                if (idLen <= 0) { out_.RemoveRange(start, out_.Count - start); return false; }
                var sb = new StringBuilder(idLen * 2);
                for (int i = 0; i < idLen; i++) sb.Append(data[pos + 1 + 2 + i].ToString("X2"));
                string epc = sb.ToString();
                if (epc.Length >= 8) out_.Add(epc);
                pos += 1 + L;
            }
            return true;
        }

        private static void ScanTags(byte[] data, int from, List<string> out_)
        {
            for (int pos = from; pos + 4 < data.Length; pos++)
            {
                int L = data[pos];
                if (L < 7 || L > 32) continue;
                if (pos + 1 + L > data.Length) continue;
                int idLen = L - 3;
                var sb = new StringBuilder(idLen * 2);
                for (int i = 0; i < idLen; i++) sb.Append(data[pos + 1 + 2 + i].ToString("X2"));
                string epc = sb.ToString();
                if (epc.Length >= 8) { out_.Add(epc); pos += L; }
            }
        }
    }
}
