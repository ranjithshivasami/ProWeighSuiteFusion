using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
#pragma warning disable CS0414, CS0067 // Ready logic retained for compatibility but disabled per request
    /// <summary>
    /// Hybrid = Checkweigher OR Integrate PLC workflow.
    /// Discrete inputs (FC02) : scan, weighingInProcess, capture  (Addresses 0,1,2 by default).
    /// Holding registers (FC06) Address 1 Values: idleReset 0, ok 1, under 2, over 3, buzzer 4, reject 5.
    /// Spec:
    ///  - Batch started + all discrete false => Ready.
    ///  - scan=1 => write idleReset, focus MaterialCode, wait ScanIntervalMs for valid material.
    ///    Success => Scan Success. Failure/timeout/invalid => Invalid Material (still write idleReset per existing flow).
    ///  - weighingInProcess=1 => Weighing In Process (both success & rejection paths).
    ///  - capture=1 => if prior scan valid => evaluate weight => ok/under/over writes + OK/Under/Over status,
    ///                 else => Rejected + reject write. Then clear material, focus MaterialCode, wait until all discrete 0 => Ready.
    /// Holding register writes are preserved (do not change functionality).
    /// </summary>
    public sealed class HybridPlcWorkflowService : IAsyncDisposable
    {
        private readonly ConfigManager _configManager = new();
        private readonly AppSettingsService _appSettingsService = new();

        private ModbusRtuClient? _plc;
        private PlcConfig? _plcConfig;
        private AppSettings? _appSettings;

        private CancellationTokenSource? _pollCts;
        private Task? _pollTask;
        private CancellationTokenSource? _scanTimeoutCts;
        private TaskCompletionSource<string?>? _materialTcs;

        private readonly Dictionary<ushort, bool> _lastDiscrete = new();
        private volatile bool _isBatchActive;

        // Tracks whether the current scan cycle had a valid material within the window
        private bool _lastScanValid = false;
        private bool _scanPending = false;
        // Single Product mode: first scan is retained and reused for subsequent
        // captures so the operator does not need to re-scan the same product.
        // Multiple Product mode: every capture requires a fresh scan.
        // Bound from BatchWeighingViewModel.IsSingleProduct (default true).
        public bool IsSingleProductMode { get; set; } = true;
        // Last successfully scanned material code, retained for Single Product reuse.
        private string _retainedMaterialCode = string.Empty;
        // After capture we wait until all discrete inputs are 0 before raising Ready
        private bool _waitingForAllDiscreteZero = false;
        private bool _lastReadySignaled = false;
        private DateTime _lastCaptureTime = DateTime.MinValue;
        private static readonly TimeSpan CaptureStatusDwell = TimeSpan.FromMilliseconds(900); // keep OK/Under/Over visible before Ready

        private Dictionary<string, (ushort addr, ushort val)> _holdingByDefine = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<ushort, string> _discreteAddrToDefine = new();
        private Dictionary<ushort, DiscreteInputMapEntry> _discreteEntries = new();
        private Dictionary<ushort, DateTime> _discreteHighSince = new();
        private HashSet<ushort> _discreteFaulted = new();
        private bool _pausedDueToFault;

        public event EventHandler? RequestScanFocus;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<string>? DiscreteFault;
        public event EventHandler<string>? DiscreteFaultCleared;
        public event EventHandler? WeighingInProcessReady;
        // New spec events
        public event EventHandler? Scan;
        public event EventHandler? ScanSuccess;
        public event EventHandler? ScanInvalid;
        public event EventHandler? ReadyState;
        public event EventHandler<string>? CaptureProcessed; // status string OK/Under/Over/Rejected
        public event EventHandler<string>? PlcRawDataChanged;
        public string LastPlcRawData { get; private set; } = string.Empty;

        public Func<double>? GetCurrentNetWeight { get; set; }
        public Func<string>? EvaluateWeightStatus { get; set; }
        public Func<Task>? OnCaptureWeightAsync { get; set; }

        public bool IsRunning => _pollTask != null && !_pollTask.IsCompleted;

        public async Task StartAsync(CancellationToken ct = default)
        {
            _plcConfig = await _configManager.LoadPlcConfigAsync();
            _appSettings = await _appSettingsService.LoadAsync();

            _holdingByDefine = _plcConfig.HoldingRegisterMaps
                .GroupBy(m => PlcConfig.NormalizeWpfDefine(m.WpfDefine))
                .ToDictionary(g => g.Key, g => (g.First().Address, g.First().Value), StringComparer.OrdinalIgnoreCase);

            _discreteAddrToDefine = _plcConfig.DiscreteInputMaps
                .ToDictionary(m => m.Address, m => PlcConfig.NormalizeDiscreteWpfDefine(m.WpfDefine));
            _discreteEntries = _plcConfig.DiscreteInputMaps.ToDictionary(m => m.Address, m => m);
            _discreteHighSince.Clear();
            _discreteFaulted.Clear();

            _plc = new ModbusRtuClient(_plcConfig);
            await _plc.OpenAsync();

            _isBatchActive = true;
            _lastDiscrete.Clear();
            _lastScanValid = false;
            _scanPending = false;
            _retainedMaterialCode = string.Empty;
            _waitingForAllDiscreteZero = false;
            _lastReadySignaled = false;

            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;
            _pollTask = Task.Run(() => PollLoopAsync(token), token);
            StatusChanged?.Invoke(this, "Hybrid PLC polling started @100ms");

            // Ready logic removed — no initial Ready signal
        }

        public async Task StopAsync()
        {
            _isBatchActive = false;
            CancelScanTimeout(trySetCanceled: false);
            _materialTcs?.TrySetCanceled();
            _materialTcs = null;

            try { _pollCts?.Cancel(); } catch { }
            if (_pollTask != null) try { await Task.WhenAny(_pollTask, Task.Delay(800)); } catch { }
            _pollCts?.Dispose(); _pollCts = null; _pollTask = null;

            if (_plc != null)
            {
                try { await _plc.CloseAsync(); } catch { }
                _plc.Dispose(); _plc = null;
            }
            StatusChanged?.Invoke(this, "Hybrid PLC stopped");
        }

        public void NotifyMaterialScanned(string materialCode)
        {
            // Single Product: a (re-)scan at any time updates the retained product
            // so a mid-batch product change takes effect even if it arrives after
            // the scan window already reused the previous code.
            if (IsSingleProductMode && !string.IsNullOrWhiteSpace(materialCode))
                _retainedMaterialCode = materialCode.Trim();
            if (_materialTcs != null && !_materialTcs.Task.IsCompleted)
                _materialTcs.TrySetResult(materialCode);
            else if (_scanPending && _lastScanValid && IsSingleProductMode && !string.IsNullOrWhiteSpace(materialCode))
            {
                // Window already reused retained code but a new (different) code
                // arrived — treat as fresh success.
                _lastScanValid = true;
                _retainedMaterialCode = materialCode.Trim();
                ScanSuccess?.Invoke(this, EventArgs.Empty);
                StatusChanged?.Invoke(this, $"Scan Success {materialCode} (late, Single Product)");
            }
        }

        public void NotifyMaterialInvalid()
        {
            if (_materialTcs != null && !_materialTcs.Task.IsCompleted)
                _materialTcs.TrySetResult(string.Empty); // empty => invalid
        }

        private async Task PollLoopAsync(CancellationToken ct)
        {
            var addrs = _discreteAddrToDefine.Keys.OrderBy(a => a).ToArray();
            try { await Task.Delay(150, ct); } catch (OperationCanceledException) { return; }

            while (!ct.IsCancellationRequested && _isBatchActive)
            {
                var plc = _plc;
                if (plc == null || !plc.IsOpen)
                {
                    UpdatePlcRawDataSnapshot("PLC: disconnected");
                    await Task.Delay(300, ct); continue;
                }

                try
                {
                    foreach (var addr in addrs)
                    {
                        bool cur = await plc.ReadDiscreteInputAsync(addr, ct);
                        bool prev = _lastDiscrete.TryGetValue(addr, out var p) ? p : false;
                        bool rising = !prev && cur;
                        bool falling = prev && !cur;
                        _lastDiscrete[addr] = cur;

                        // Track CutOffTimeMs per DiscreteInputMap and handle resume when input resets to 0
                        if (cur && !prev)
                        {
                            _discreteHighSince[addr] = DateTime.UtcNow;
                        }
                        else if (falling)
                        {
                            bool wasFaulted = _discreteFaulted.Contains(addr);
                            _discreteHighSince.Remove(addr);
                            if (wasFaulted)
                            {
                                _discreteFaulted.Remove(addr);
                                if (_discreteEntries.TryGetValue(addr, out var faultEntry))
                                {
                                    var inputName = string.IsNullOrWhiteSpace(faultEntry.InputName) ? $"{faultEntry.WpfDefine} (DI:{addr})" : faultEntry.InputName;
                                    StatusChanged?.Invoke(this, $"RESUMED: {inputName} reset to 0 — resuming process");
                                    DiscreteFaultCleared?.Invoke(this, inputName);
                                    try { await WriteHoldingAsync("idleReset", ct); } catch { }
                                }
                                if (_discreteFaulted.Count == 0)
                                    _pausedDueToFault = false;
                            }
                            else
                            {
                                _discreteFaulted.Remove(addr);
                            }
                        }

                        // While paused due to fault, skip normal scan/capture handling but keep monitoring for reset
                        if (_pausedDueToFault)
                        {
                            if (!rising) continue;
                            // Still ignore rising while paused
                            continue;
                        }

                        if (!rising) continue;

                        var define = _discreteAddrToDefine[addr];
                        if (define == "scan") await OnScanRisingAsync(ct);
                        else if (define == "weighingInProcess") OnWeighingInProcessRising();
                        else if (define == "capture") await OnCaptureRisingAsync(ct);
                    }

                    // Check for stuck-highInputs beyond CutOffTimeMs — fault on InputName
                    if (!_pausedDueToFault)
                    {
                        foreach (var kv in _discreteHighSince.ToList())
                        {
                            var addr = kv.Key;
                            var since = kv.Value;
                            if (_discreteFaulted.Contains(addr)) continue;
                            if (!_discreteEntries.TryGetValue(addr, out var entry)) continue;
                            uint cutoff = entry.CutOffTimeMs;
                            if (cutoff == 0) continue;
                            var elapsed = (DateTime.UtcNow - since).TotalMilliseconds;
                            if (elapsed > cutoff)
                            {
                                _discreteFaulted.Add(addr);
                                _pausedDueToFault = true;
                                var inputName = string.IsNullOrWhiteSpace(entry.InputName) ? $"{entry.WpfDefine} (DI:{addr})" : entry.InputName;
                                var msg = $"ERROR: {inputName} stuck at 1 beyond {cutoff}ms (DI:{addr})";
                                StatusChanged?.Invoke(this, msg);
                                DiscreteFault?.Invoke(this, inputName);
                                try { await WriteHoldingAsync("buzzer", ct); } catch { }
                                // Halt process — stays paused until input resets to 0
                            }
                        }
                    }

                    PublishPlcRawDataSnapshot();
                    // Ready detection: all discrete inputs are 0
                    CheckReadyCondition();

                    await Task.Delay(100, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (TimeoutException) { PublishPlcRawDataSnapshot(true); try { await Task.Delay(200, ct); } catch (OperationCanceledException) { break; } }
                catch (Exception ex) { UpdatePlcRawDataSnapshot($"PLC error: {ex.Message}"); try { await Task.Delay(200, ct); } catch (OperationCanceledException) { break; } }
            }
        }

        private void PublishPlcRawDataSnapshot(bool forceErrorHint = false)
        {
            if (_discreteAddrToDefine.Count == 0)
            {
                UpdatePlcRawDataSnapshot("PLC: no discrete map");
                return;
            }
            var parts = new List<string>();
            foreach (var kv in _discreteAddrToDefine.OrderBy(k => k.Key))
            {
                var addr = kv.Key;
                var define = kv.Value;
                bool val = _lastDiscrete.TryGetValue(addr, out var v) && v;
                parts.Add($"{define}={ (val ? 1 : 0)} (DI:{addr})");
            }
            // append holding register target for visibility (last FC06 value is not polled, show config)
            var holdingHint = _holdingByDefine.Count > 0
                ? $" | HOLD Addr:{_holdingByDefine.First().Value.addr} OK/Under/Over via FC06"
                : string.Empty;
            var raw = $"PLC RX [FC02] {string.Join(" | ", parts)}{holdingHint}";
            if (forceErrorHint) raw += " (timeout)";
            UpdatePlcRawDataSnapshot(raw);
        }

        private void UpdatePlcRawDataSnapshot(string raw)
        {
            if (raw == LastPlcRawData) return;
            LastPlcRawData = raw;
            try { PlcRawDataChanged?.Invoke(this, raw); } catch { }
        }

        private void CheckReadyCondition()
        {
            // Ready status removed per request — no longer signal Ready when all discrete inputs are 0
            return;
        }

        private bool AreAllDiscreteFalse()
        {
            if (_lastDiscrete.Count == 0) return false;
            // All known discrete addrs must be false; if any true, not ready
            foreach (var kv in _lastDiscrete)
            {
                if (kv.Value) return false;
            }
            // Ensure we have entries for all configured addrs — if some never read, treat as not ready until populated
            if (_lastDiscrete.Count < _discreteAddrToDefine.Count) return false;
            return true;
        }

        private void SignalReady()
        {
            // Ready logic removed per request — keep method as no-op for compatibility
            return;
        }

        private async Task OnScanRisingAsync(CancellationToken pollCt)
        {
            if (!_isBatchActive) return;

            _scanPending = true;
            // Multiple Product: each scan=1 starts a fresh cycle, so invalidate.
            // Single Product: keep the retained valid flag so a capture arriving
            // during the window (or with no re-scan) still uses the old product.
            if (!IsSingleProductMode || string.IsNullOrWhiteSpace(_retainedMaterialCode))
                _lastScanValid = false;
            _lastReadySignaled = false;
            Scan?.Invoke(this, EventArgs.Empty);

            // 1. focus MaterialCode
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                await dispatcher.InvokeAsync(() => RequestScanFocus?.Invoke(this, EventArgs.Empty));
            else
                RequestScanFocus?.Invoke(this, EventArgs.Empty);
            StatusChanged?.Invoke(this, "SCAN=1 -> focus MaterialCode");

            // 2. write idleReset per existing flow (do not change holding register functionality)
            try { await WriteHoldingAsync("idleReset", pollCt); } catch (Exception ex) { StatusChanged?.Invoke(this, $"idleReset write failed: {ex.Message}"); }

            // 3. start non-blocking scan window
            CancelScanTimeout(trySetCanceled: false);
            _scanTimeoutCts = new CancellationTokenSource();
            var timeoutCts = _scanTimeoutCts;
            _materialTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            int scanMs = _appSettings?.ScanIntervalMs > 0 ? _appSettings.ScanIntervalMs : 1000;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(scanMs, timeoutCts.Token);
                    // Timeout — no valid material within window
                    if (_scanPending && !_materialTcs.Task.IsCompleted)
                    {
                        _materialTcs.TrySetResult(null);
                    }
                    // If TCS completed with null/empty => timeout case
                    if (_materialTcs.Task.IsCompleted)
                    {
                        var result = _materialTcs.Task.Result;
                        if (string.IsNullOrEmpty(result))
                        {
                            await OnScanFailedAsync(pollCt);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Success path cancelled the timeout — handled below via continuation
                }
            }, pollCt);

            // Also handle immediate success completion without blocking poll: attach continuation
            _ = _materialTcs.Task.ContinueWith(async t =>
            {
                if (t.IsCanceled) return;
                var result = t.Result;
                if (!string.IsNullOrEmpty(result))
                {
                    await OnScanSuccessAsync(result, pollCt);
                }
                else if (t.IsCompleted && result != null && result == string.Empty)
                {
                    // Explicit invalid notified
                    await OnScanFailedAsync(pollCt);
                }
            }, TaskScheduler.Default);
        }

        private Task OnScanSuccessAsync(string code, CancellationToken ct)
        {
            if (!_scanPending) return Task.CompletedTask;
            CancelScanTimeout(trySetCanceled: false);
            _scanPending = false;
            _lastScanValid = true;
            // Retain for Single Product reuse across captures.
            if (!string.IsNullOrWhiteSpace(code))
                _retainedMaterialCode = code.Trim();
            ScanSuccess?.Invoke(this, EventArgs.Empty);
            StatusChanged?.Invoke(this, $"Scan Success {code}");
            return Task.CompletedTask;
        }

        private async Task OnScanFailedAsync(CancellationToken ct)
        {
            if (!_scanPending) return;
            // Single Product: no new barcode within the window -> reuse the
            // previously scanned product instead of failing. First cycle with
            // no retained code still fails as Invalid Material.
            if (IsSingleProductMode && !string.IsNullOrWhiteSpace(_retainedMaterialCode))
            {
                _scanPending = false;
                _lastScanValid = true;
                ScanSuccess?.Invoke(this, EventArgs.Empty);
                StatusChanged?.Invoke(this, $"Scan reused retained product {_retainedMaterialCode} (Single Product, no re-scan)");
                return;
            }
            _scanPending = false;
            _lastScanValid = false;
            // Per spec, send idleReset on invalid (already sent at scan rising, but ensure again per existing flow)
            try { await WriteHoldingAsync("idleReset", ct); } catch { }
            ScanInvalid?.Invoke(this, EventArgs.Empty);
            StatusChanged?.Invoke(this, "Invalid Material -> idleReset");
        }

        private void OnWeighingInProcessRising()
        {
            // Both valid and invalid (rejection) paths show Weighing In Process
            WeighingInProcessReady?.Invoke(this, EventArgs.Empty);
            StatusChanged?.Invoke(this, "WEIGHING_IN_PROCESS=1 -> Weighing In Process");
        }

        private async Task OnCaptureRisingAsync(CancellationToken ct)
        {
            // Single Product: a capture arriving without a fresh scan reuses the
            // retained product (operator scanned once for the whole batch).
            bool scanValid = _lastScanValid ||
                             (IsSingleProductMode && !string.IsNullOrWhiteSpace(_retainedMaterialCode));
            double net = GetCurrentNetWeight?.Invoke() ?? 0;
            string status;
            if (!scanValid)
            {
                status = "Rejected";
                StatusChanged?.Invoke(this, $"CAPTURE=1 (invalid scan) -> net={net:0.000} => Rejected");
                await ExecuteVerificationWritesAsync(status, ct);
                CaptureProcessed?.Invoke(this, status);
                if (OnCaptureWeightAsync != null)
                {
                    // Rejection path still notifies but ViewModel will handle as Rejected and not save as OK transaction
                    await Application.Current.Dispatcher.InvokeAsync(async () => await OnCaptureWeightAsync());
                }
            }
            else
            {
                status = EvaluateWeightStatus?.Invoke() ?? "Accepted";
                StatusChanged?.Invoke(this, $"CAPTURE=1 -> net={net:0.000} status={status}");
                await ExecuteVerificationWritesAsync(status, ct);
                CaptureProcessed?.Invoke(this, status);
                if (OnCaptureWeightAsync != null)
                    await Application.Current.Dispatcher.InvokeAsync(async () => await OnCaptureWeightAsync());
            }

            // After capture, clear pending scan and wait for all discrete 0 => Ready
            _scanPending = false;
            _waitingForAllDiscreteZero = true;
            _lastReadySignaled = false;
            _lastCaptureTime = DateTime.UtcNow;
            if (scanValid)
            {
                // Keep the valid flag for Single Product so the next capture
                // without a re-scan still succeeds; require a fresh scan for
                // Multiple Product.
                _lastScanValid = IsSingleProductMode;
            }
        }

        private async Task ExecuteVerificationWritesAsync(string status, CancellationToken ct)
        {
            switch (status)
            {
                case "Accepted":
                    await WriteHoldingAsync("ok", ct);
                    break;
                case "Under Weight":
                    await WriteHoldingAsync("under", ct);
                    break;
                case "Over Weight":
                    await WriteHoldingAsync("over", ct);
                    break;
                case "Rejected":
                    await WriteHoldingAsync("reject", ct);
                    break;
                default:
                    await WriteHoldingAsync("reject", ct);
                    break;
            }
        }

        private async Task WriteHoldingAsync(string define, CancellationToken ct)
        {
            var key = PlcConfig.NormalizeWpfDefine(define);
            if (!_holdingByDefine.TryGetValue(key, out var map))
                throw new InvalidOperationException($"HoldingRegisterMaps missing WpfDefine '{define}' (normalized '{key}') — check plc-config.json");
            await _plc!.WriteSingleRegisterAsync(map.addr, map.val, ct);
            StatusChanged?.Invoke(this, $"FC06 Write Addr {map.addr} Val {map.val} ({key})");
        }

        private void CancelScanTimeout(bool trySetCanceled)
        {
            try { _scanTimeoutCts?.Cancel(); } catch { }
            _scanTimeoutCts?.Dispose();
            _scanTimeoutCts = null;
            if (trySetCanceled) _materialTcs?.TrySetCanceled();
        }

        public async ValueTask DisposeAsync() => await StopAsync();
    }
#pragma warning restore CS0414, CS0067
}
