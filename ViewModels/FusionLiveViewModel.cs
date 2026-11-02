// Fusion live batch run (mirrors the Fusion Link live view tab).
// Batch pills -> status banner + TAT -> stepper -> run buttons -> KPI cards ->
// lots table -> supplier cards -> recent transactions -> add-a-lot.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Navigation;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Services.Scanners;

using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Views;
namespace ProWeighSuitePlatforms.ViewModels
{
    public sealed class LiveLotRow
    {
        public FusionPlanLot Lot { get; set; } = new();
        public string Status { get; set; } = "Running"; // Running / Paused / Completed
        public double ProgressPct { get; set; }
        public int AssignedCount { get; set; }
        public string DonePlanText { get; set; } = string.Empty;
        public string NutsText { get; set; } = string.Empty;
        public string KgText { get; set; } = string.Empty;
        public string GrnBayText { get; set; } = string.Empty;
    }

    public sealed class LiveSupplierCard
    {
        public string Supplier { get; set; } = string.Empty;
        public string Lots { get; set; } = string.Empty;
        public string Colour { get; set; } = string.Empty;
        public string ProgressText { get; set; } = string.Empty;
        public double DoneNuts { get; set; }
        public double PlanNuts { get; set; }
        public double DoneKg { get; set; }
        public int AssignedCount { get; set; }
        public double BalanceNuts { get; set; }
        public double ProgressPct { get; set; }
    }

    public sealed class LiveTxRow
    {
        public string Time { get; set; } = string.Empty;
        public string Tub { get; set; } = string.Empty;
        public string Epc { get; set; } = string.Empty;
        public int Cycles { get; set; }
        public string Supplier { get; set; } = string.Empty;
        public string Grn { get; set; } = string.Empty;
        public string Bay { get; set; } = string.Empty;
        public string Lot { get; set; } = string.Empty;
        public double Weight { get; set; }
        public int Nuts { get; set; }
        public string Colour { get; set; } = string.Empty;
    }

    public sealed class LiveBatchPill
    {
        public string BatchNo { get; set; } = string.Empty;
        public string Display { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }

    public sealed class LiveStepItem
    {
        public string Title { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public bool IsDone { get; set; }
        public bool IsCurrent { get; set; }
    }

    public sealed class FusionLiveViewModel : ViewModels.BaseViewModel
    {
        public string Title => "Live view";

        private static readonly Random _rand = new();
        private static readonly string[] StepOrder =
            { "Batch Created", "Lot Created", "Running", "Paused", "Resumed", "Batch Completed" };

        public List<string> OperatorNames { get; } = new() { "Ravi", "Karthik", "admin" };
        private string _operatorName = "Ravi";
        public string OperatorName { get => _operatorName; set => SetProperty(ref _operatorName, value); }

        public ObservableCollection<LiveBatchPill> Pills { get; } = new();
        public ObservableCollection<LiveLotRow> Lots { get; } = new();
        public ObservableCollection<LiveSupplierCard> SupplierCards { get; } = new();
        public ObservableCollection<LiveTxRow> TxRows { get; } = new();
        public ObservableCollection<LiveStepItem> Steps { get; } = new();
        public ObservableCollection<FusionInwardRow> CandidateLots { get; } = new();

        private FusionBatch? _batch;
        public string SelectedBatchNo { get; private set; } = string.Empty;

        private string _bannerText = "No batch yet.";
        public string BannerText { get => _bannerText; private set => SetProperty(ref _bannerText, value); }
        private string _bannerSub = "Create one in the Batch creation tab.";
        public string BannerSub { get => _bannerSub; private set => SetProperty(ref _bannerSub, value); }
        private string _batchStatus = string.Empty;
        public string BatchStatus { get => _batchStatus; private set => SetProperty(ref _batchStatus, value); }
        public bool IsRunning => string.Equals(_batchStatus, "running", StringComparison.OrdinalIgnoreCase);
        public bool IsCompleted => string.Equals(_batchStatus, "completed", StringComparison.OrdinalIgnoreCase);

        private string _pillsNote = string.Empty;
        public string PillsNote { get => _pillsNote; private set => SetProperty(ref _pillsNote, value); }

        private string _lotsTitle = "Lots";
        public string LotsTitle { get => _lotsTitle; private set => SetProperty(ref _lotsTitle, value); }

        private string _kpiWeight = "-";
        public string KpiWeight { get => _kpiWeight; private set => SetProperty(ref _kpiWeight, value); }
        private string _kpiWeightSub = string.Empty;
        public string KpiWeightSub { get => _kpiWeightSub; private set => SetProperty(ref _kpiWeightSub, value); }
        private string _kpiNuts = "-";
        public string KpiNuts { get => _kpiNuts; private set => SetProperty(ref _kpiNuts, value); }
        private string _kpiNutsSub = string.Empty;
        public string KpiNutsSub { get => _kpiNutsSub; private set => SetProperty(ref _kpiNutsSub, value); }
        private string _kpiTubs = "-";
        public string KpiTubs { get => _kpiTubs; private set => SetProperty(ref _kpiTubs, value); }
        private string _kpiTubsSub = string.Empty;
        public string KpiTubsSub { get => _kpiTubsSub; private set => SetProperty(ref _kpiTubsSub, value); }
        private string _kpiProgress = "-";
        public string KpiProgress { get => _kpiProgress; private set => SetProperty(ref _kpiProgress, value); }
        private double _kpiProgressPct;
        public double KpiProgressPct { get => _kpiProgressPct; private set => SetProperty(ref _kpiProgressPct, value); }
        private string _kpiTat = "-";
        public string KpiTat { get => _kpiTat; private set => SetProperty(ref _kpiTat, value); }
        private string _kpiTatSub = string.Empty;
        public string KpiTatSub { get => _kpiTatSub; private set => SetProperty(ref _kpiTatSub, value); }

        private bool _addLotVisible;
        public bool AddLotVisible { get => _addLotVisible; private set => SetProperty(ref _addLotVisible, value); }
        private string _addLotHead = string.Empty;
        public string AddLotHead { get => _addLotHead; private set => SetProperty(ref _addLotHead, value); }
        private string _addLotNote = string.Empty;
        public string AddLotNote { get => _addLotNote; private set => SetProperty(ref _addLotNote, value); }
        private string? _selectedCandidateGrn;
        public string? SelectedCandidateGrn
        {
            get => _selectedCandidateGrn;
            set => SetProperty(ref _selectedCandidateGrn, value);
        }
        private string _newLotTubs = string.Empty;
        public string NewLotTubs { get => _newLotTubs; set => SetProperty(ref _newLotTubs, value); }

        private bool _isAutoRunning;
        public bool IsAutoRunning
        {
            get => _isAutoRunning;
            private set
            {
                if (SetProperty(ref _isAutoRunning, value))
                {
                    RaisePropertyChanged(nameof(AutoRunButtonText));
                    RaisePropertyChanged(nameof(IsEpcReadOnly));
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }
        public string AutoRunButtonText => IsAutoRunning ? "Stop auto run" : "Auto run";
        // Auto-run ON => EPC box read-only for typing (scanner still fills it).
        public bool IsEpcReadOnly => IsAutoRunning;
        public string PauseResumeButtonText => string.Equals(_batchStatus, "paused", StringComparison.OrdinalIgnoreCase)
            ? "Resume batch" : "Pause batch";

        private string _statusText = string.Empty;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        // Manual EPC scan (Weigh next tub button flow): operator scans/types the tub
        // EPC, it is validated live, and Weigh next tub captures the checkweigher
        // weight for THAT tub. Empty = today's auto-pick. EPC only — Tub IDs rejected.
        // RFID scans auto-fill this box when a batch is running; the value is also
        // kept in _pendingScannedEpc (internal memory) for the auto-run flow.
        private readonly List<FusionTub> _tubsCache = new();
        private string _manualEpcText = string.Empty;
        public string ManualEpcText
        {
            get => _manualEpcText;
            set
            {
                if (SetProperty(ref _manualEpcText, value))
                {
                    _pendingScannedEpc = (_manualEpcText ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(_pendingScannedEpc))
                        _pendingScannedAt = DateTime.Now;
                    ValidateManualEpc();
                }
            }
        }

        // Internal memory of the last scanned EPC (RFID or typed). Cleared on capture.
        private string _pendingScannedEpc = string.Empty;
        private DateTime _pendingScannedAt = DateTime.MinValue;
        private string _lastScannerEpc = string.Empty;
        private DateTime _lastScannerAt = DateTime.MinValue;
        private string? _manualEpcError;
        public string? ManualEpcError { get => _manualEpcError; private set => SetProperty(ref _manualEpcError, value); }
        private string _manualEpcTubInfo = string.Empty;
        public string ManualEpcTubInfo { get => _manualEpcTubInfo; private set => SetProperty(ref _manualEpcTubInfo, value); }

        // Latest checkweigher push slot (continuous serial/TCP/ID510 modes).
        // Consume-once: a pushed value is recorded at most one time.
        private readonly object _weightSlotLock = new();
        private double _weightSlot;
        private DateTime _weightSlotAt = DateTime.MinValue;
        private DateTime _weightConsumedAt = DateTime.MinValue;

        private void ValidateManualEpc()
        {
            var v = (_manualEpcText ?? string.Empty).Trim();
            ManualEpcTubInfo = string.Empty;
            if (string.IsNullOrEmpty(v))
            {
                ManualEpcError = null;
                return;
            }
            ManualEpcError = TubMasterViewModel.ValidateEpc(v);
            if (ManualEpcError != null) return;
            var t = FindTubByEpc(_tubsCache, v);
            if (t == null)
            {
                ManualEpcError = $"EPC {v} not found in the tub master.";
                return;
            }
            if (t.IsBlocked)
            {
                ManualEpcError = $"{t.TubId} is blocked.";
                return;
            }
            if (t.UsedCycles >= 2000)
            {
                ManualEpcError = $"{t.TubId} has reached its cycle limit and needs replacement.";
                return;
            }
            if (!string.IsNullOrEmpty(t.AssignedTo)
                && !string.Equals(t.AssignedTo, SelectedBatchNo, StringComparison.OrdinalIgnoreCase))
            {
                ManualEpcError = $"{t.TubId} is already assigned to {t.AssignedTo}.";
                return;
            }
            ManualEpcError = null;
            ManualEpcTubInfo = $"{t.TubId} · {t.Colour} · {t.UsedCycles:N0} cycles";
        }

        internal static FusionTub? FindTubByEpc(IEnumerable<FusionTub> tubs, string val)
        {
            // EPC only — never fall back to TubId. Leading-zero-insensitive like batch scan.
            string raw = (val ?? string.Empty).Trim();
            string q = raw.TrimStart('0');
            return tubs.FirstOrDefault(t => string.Equals(t.Epc, raw, StringComparison.OrdinalIgnoreCase)
                || (q.Length > 0 && string.Equals((t.Epc ?? string.Empty).TrimStart('0'), q, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool TubFitsLot(FusionTub t, FusionPlanLot l) =>
            string.Equals(l.Colour, "Mixed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(l.Colour, "Virtual", StringComparison.OrdinalIgnoreCase)
            || string.Equals(l.Colour, "-", StringComparison.Ordinal)
            || string.Equals(t.Colour, l.Colour, StringComparison.OrdinalIgnoreCase);

        // Zero-return gate: Fusion setting (zero_return true/false) takes precedence;
        // when Fusion is off/unset, fall back to the active scale's ZeroReturn flag.
        // When armed, the next weigh is blocked until the scale is back near zero
        // (tub removed). Band = % of the last expected tub weight around zero.
        private bool _waitingForZero;
        public bool WaitingForZero
        {
            get => _waitingForZero;
            private set
            {
                if (SetProperty(ref _waitingForZero, value))
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
        private double _zeroRefWeight = 25;
        private double _zeroTolMinus = 20;
        private double _zeroTolPlus = 20;

        private bool IsWithinZeroBand(double w) =>
            w >= -(_zeroRefWeight * _zeroTolMinus / 100.0)
            && w <= (_zeroRefWeight * _zeroTolPlus / 100.0);

        internal static bool ParseZeroReturn(string? raw)
        {
            var t = (raw ?? string.Empty).Trim().ToLowerInvariant();
            return t == "1" || t == "true" || t == "yes" || t == "on";
        }

        private async Task<bool> GetFusionZeroReturnAsync()
        {
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var raw = await repo.GetSettingAsync(FusionSettingsKeys.ZeroReturn, "0");
                return ParseZeroReturn(raw);
            }
            catch { return false; }
        }

        private async Task ArmZeroReturnAsync(double referenceWeight)
        {
            try
            {
                // Fusion-level switch wins: True => always arm (clear/0 required).
                if (await GetFusionZeroReturnAsync())
                {
                    _zeroRefWeight = referenceWeight > 0 ? referenceWeight : 1.0;
                    _zeroTolMinus = 20;
                    _zeroTolPlus = 20;
                    try
                    {
                        var scales = await new ScaleSettingsRepository(AppConfigurationManager.GetDatabasePath()).GetAllAsync();
                        var s = scales.FirstOrDefault(x => x.IsActive) ?? scales.FirstOrDefault();
                        if (s != null)
                        {
                            if (s.ZeroReturnToleranceMinus > 0) _zeroTolMinus = s.ZeroReturnToleranceMinus;
                            if (s.ZeroReturnTolerancePlus > 0) _zeroTolPlus = s.ZeroReturnTolerancePlus;
                        }
                    }
                    catch { }
                    WaitingForZero = true;
                    return;
                }
                var scales2 = await new ScaleSettingsRepository(AppConfigurationManager.GetDatabasePath()).GetAllAsync();
                var s2 = scales2.FirstOrDefault(x => x.IsActive) ?? scales2.FirstOrDefault();
                if (s2 == null || !s2.ZeroReturn) { WaitingForZero = false; return; }
                _zeroRefWeight = referenceWeight > 0 ? referenceWeight : 1.0;
                _zeroTolMinus = s2.ZeroReturnToleranceMinus > 0 ? s2.ZeroReturnToleranceMinus : 20;
                _zeroTolPlus = s2.ZeroReturnTolerancePlus > 0 ? s2.ZeroReturnTolerancePlus : 20;
                WaitingForZero = true;
            }
            catch { WaitingForZero = false; }
        }

        private string _scaleStatusText = "Scale: –";
        public string ScaleStatusText { get => _scaleStatusText; private set => SetProperty(ref _scaleStatusText, value); }

        private string _liveScaleText = "–";
        public string LiveScaleText { get => _liveScaleText; private set => SetProperty(ref _liveScaleText, value); }

        private bool _isOverLimit;
        public bool IsOverLimit { get => _isOverLimit; private set => SetProperty(ref _isOverLimit, value); }

        // Cached AppSettings scale limits (MaxWeight capacity, resolution, presence).
        private double _scaleMaxKg;
        private double _scaleResolutionKg;
        private double _presenceKg = 0.05;

        private async Task RefreshScaleLimitsAsync()
        {
            try
            {
                var app = await new AppSettingsService().LoadAsync();
                _scaleMaxKg = (double)app.MaxWeight;
                _scaleResolutionKg = (double)app.ScaleResolution;
                _presenceKg = app.PresenceThreshold;
            }
            catch { }
        }

        private string? CheckScaleLimits(double w)
        {
            if (_scaleMaxKg > 0 && w > _scaleMaxKg)
                return $"Weight {w:F2} kg exceeds scale limit {_scaleMaxKg:F3} kg — not weighed. Remove load.";
            if (_presenceKg > 0 && w < _presenceKg)
                return $"Weight {w:F3} kg below presence threshold {_presenceKg:F3} kg — not weighed.";
            if (_scaleResolutionKg > 0 && w < _scaleResolutionKg)
                return $"Weight {w:F3} kg below scale resolution {_scaleResolutionKg:F3} kg — not weighed.";
            return null;
        }

        private static ScaleReaderService? _scale;
        private bool _scaleSubscribed;
        private bool _scannerSubscribed;
        private DateTime _lastLivePush = DateTime.MinValue;

        private void EnsureScaleSubscribed()
        {
            if (_scaleSubscribed) return;
            try
            {
                _scale ??= ScaleReaderService.GetShared(new ScaleSettingsRepository(AppConfigurationManager.GetDatabasePath()));
                _scale.WeightChanged -= OnLiveWeightChanged;
                _scale.WeightChanged += OnLiveWeightChanged;
                _scaleSubscribed = true;
                // Keep the read loop running while Live view is active so continuous
                // checkweigher pushes keep arriving. When startup/save left the port
                // closed, open the saved connection now instead of showing no weight.
                if (_scale.IsPolling) _scale.StartPolling();
                else _ = ScaleConnectionService.EnsureConnectedAsync();
            }
            catch { }
        }

        private void EnsureScannerSubscribed()
        {
            if (_scannerSubscribed) return;
            try
            {
                var svc = ScannerServiceLocator.Current;
                if (svc == null) return;
                svc.BarcodeScanned -= OnScannerBarcodeScanned;
                svc.BarcodeScanned += OnScannerBarcodeScanned;
                _scannerSubscribed = true;
            }
            catch { }
        }

        public void DetachScanner()
        {
            try
            {
                var svc = ScannerServiceLocator.Current;
                if (svc != null && _scannerSubscribed)
                    svc.BarcodeScanned -= OnScannerBarcodeScanned;
            }
            catch { }
            _scannerSubscribed = false;
        }

        // True while the Live view is in the visual tree. While auto-run is
        // active the scale + scanner feeds stay attached even after the view
        // unloads (navigating to another menu such as Bay status), so RFID
        // scans and weight changes keep being captured exactly like Display
        // mode. Feeds are constructed in LoadAsync, so start as attached.
        private bool _viewAttached = true;

        public void OnViewLoaded() => _viewAttached = true;

        public void OnViewUnloaded()
        {
            _viewAttached = false;
            // Keep the feeds while auto-run is active so captures continue from
            // other menus; otherwise release them until the view returns.
            if (!IsAutoRunning)
            {
                DetachScale();
                DetachScanner();
            }
        }

        private void OnScannerBarcodeScanned(object? sender, ScannerBarcodeEventArgs? e)
        {
            try
            {
                if (e == null) return;
                string label = e.Label ?? string.Empty;
                string raw = e.RawHex ?? string.Empty;
                string epc = (label.Trim().Length > 0 ? label : raw).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(epc)) return;
                var now = DateTime.Now;
                if (string.Equals(epc, _lastScannerEpc, StringComparison.OrdinalIgnoreCase)
                    && (now - _lastScannerAt).TotalMilliseconds < 500)
                    return; // transport-level double-fire guard (provider already dedups)
                _lastScannerEpc = epc;
                _lastScannerAt = now;
                var disp = Application.Current?.Dispatcher;
                if (disp != null && !disp.CheckAccess())
                    disp.InvokeAsync(() => HandleScannedEpc(epc));
                else
                    HandleScannedEpc(epc);
            }
            catch { }
        }

        private void HandleScannedEpc(string epc)
        {
            if (_batch == null || !IsRunning)
            {
                StatusText = "No running batch — scan ignored.";
                return;
            }
            // Auto-fill EPC box + internal memory; validation runs via setter.
            ManualEpcText = epc;
            _pendingScannedEpc = epc;
            _pendingScannedAt = DateTime.Now;
            if (ManualEpcError != null)
            {
                StatusText = ManualEpcError;
                return;
            }
            StatusText = $"Scanned {epc} — {(IsAutoRunning ? "waiting for weight…" : "press Weigh next tub.")}";
            if (IsAutoRunning)
                _ = TryAutoCaptureAsync();
        }

        private bool HasFreshWeight()
        {
            try
            {
                if (_scale != null && _scale.IsSicsMode) return true; // queried on demand
                lock (_weightSlotLock)
                {
                    return _weightSlotAt > _weightConsumedAt
                        && (DateTime.Now - _weightSlotAt).TotalSeconds <= 120
                        && _weightSlot > 0;
                }
            }
            catch { return false; }
        }

        private async Task TryAutoCaptureAsync()
        {
            if (!IsAutoRunning || _ticking || _batch == null || !IsRunning || _waitingForZero) return;
            if (string.IsNullOrWhiteSpace(_pendingScannedEpc)) return;
            if (ManualEpcError != null) return;
            if (!HasFreshWeight()) return; // weight push will retry via OnLiveWeightChanged
            await WeighNextTubAsync();
        }

        private void OnLiveWeightChanged(object? sender, double w)
        {
            // Throttle UI pushes; the polling loop can fire rapidly.
            var now = DateTime.Now;
            lock (_weightSlotLock) { _weightSlot = w; _weightSlotAt = now; }
            if (_waitingForZero && IsWithinZeroBand(w))
            {
                var dsp = Application.Current?.Dispatcher;
                if (dsp != null && !dsp.CheckAccess())
                    dsp.InvokeAsync(() => WaitingForZero = false);
                else
                    WaitingForZero = false;
            }
            bool over = _scaleMaxKg > 0 && w > _scaleMaxKg;
            var disp2 = Application.Current?.Dispatcher;
            if (disp2 != null && !disp2.CheckAccess())
            {
                disp2.InvokeAsync(() =>
                {
                    IsOverLimit = over;
                    if (IsAutoRunning && !string.IsNullOrWhiteSpace(_pendingScannedEpc) && !_waitingForZero)
                        _ = TryAutoCaptureAsync();
                });
            }
            else
            {
                IsOverLimit = over;
                if (IsAutoRunning && !string.IsNullOrWhiteSpace(_pendingScannedEpc) && !_waitingForZero)
                    _ = TryAutoCaptureAsync();
            }
            if ((now - _lastLivePush).TotalMilliseconds < 250) return;
            _lastLivePush = now;
            string text = $"{w:N2} kg";
            var disp = Application.Current?.Dispatcher;
            if (disp != null && !disp.CheckAccess())
                disp.InvokeAsync(() => LiveScaleText = text);
            else
                LiveScaleText = text;
        }

        public void DetachScale()
        {
            if (_scale != null && _scaleSubscribed)
            {
                _scale.WeightChanged -= OnLiveWeightChanged;
                _scaleSubscribed = false;
            }
        }

        // PLC-driven auto run: the capture signal triggers weighing, then capture + refresh.
        // Created and started when the PLC auto-run listener is wired up (currently
        // the auto-run path is scale/scanner-driven, so this stays null).
        private FusionPlcRunService? _plcRun = null;
        private bool _ticking;
        public ICommand RefreshCommand { get; }
        public ICommand SelectBatchCommand { get; }
        public ICommand WeighNextTubCommand { get; }
        public ICommand ToggleAutoRunCommand { get; }
        public ICommand PauseResumeBatchCommand { get; }
        public ICommand CompleteBatchCommand { get; }
        public ICommand NewBatchCommand { get; }
        public ICommand PauseLotCommand { get; }
        public ICommand CompleteLotCommand { get; }
        public ICommand AddLotCommand { get; }
        public ICommand DisplayModeCommand { get; }

        public FusionLiveViewModel()
        {
            RefreshCommand = new ViewModels.RelayCommand(async _ => await LoadAsync());
            SelectBatchCommand = new ViewModels.RelayCommand(async p =>
            {
                if (p is string no && !string.IsNullOrWhiteSpace(no)) await LoadAsync(no);
                else if (p is LiveBatchPill pill) await LoadAsync(pill.BatchNo);
            });
            WeighNextTubCommand = new ViewModels.RelayCommand(async _ => { await WeighNextTubAsync(); }, _ => IsRunning && !WaitingForZero && !IsAutoRunning);
            ToggleAutoRunCommand = new ViewModels.RelayCommand(async _ => await ToggleAutoRunAsync(), _ => IsRunning && !IsCompleted && _batch != null);
            PauseResumeBatchCommand = new ViewModels.RelayCommand(async _ => await PauseResumeBatchAsync(), _ => !IsCompleted && _batch != null);
            CompleteBatchCommand = new ViewModels.RelayCommand(async _ => await CompleteBatchAsync(), _ => !IsCompleted && _batch != null);
            NewBatchCommand = new ViewModels.RelayCommand(_ => FusionNavigation.RequestShowModule("BatchLots"));
            PauseLotCommand = new ViewModels.RelayCommand(async p =>
            {
                if (p is LiveLotRow r) await PauseLotAsync(r);
            });
            CompleteLotCommand = new ViewModels.RelayCommand(async p =>
            {
                if (p is LiveLotRow r) await CompleteLotAsync(r);
            });
            AddLotCommand = new ViewModels.RelayCommand(async _ => await AddLotAsync());
            DisplayModeCommand = new ViewModels.RelayCommand(_ => ShowDisplayMode());
            _ = LoadAsync();
        }

        public void ShowDisplayMode()
        {
            if (string.IsNullOrWhiteSpace(SelectedBatchNo)) return;
            foreach (Window window in Application.Current.Windows)
            {
                if (window is FusionLiveDisplayWindow dw && string.Equals(dw.BatchNo, SelectedBatchNo, StringComparison.OrdinalIgnoreCase))
                {
                    window.Activate();
                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;
                    return;
                }
            }
            var display = new FusionLiveDisplayWindow(SelectedBatchNo);
            display.Show();
        }

        public void StopAutoRun() => _ = StopAutoRunAsync();

        private async Task StopAutoRunAsync()
        {
            IsAutoRunning = false;
            // Navigation was locked while the run was active; release it now so
            // the operator can move to other menus again.
            NavigationLock.Unlock();
            // If the run ended while the Live view is not visible (e.g. the batch
            // auto-completed from another menu), release the feeds until the
            // view is shown again.
            if (!_viewAttached)
            {
                DetachScale();
                DetachScanner();
            }
            if (_plcRun != null)
            {
                try { await _plcRun.StopAsync(); }
                catch { }
            }
        }

        private async Task ToggleAutoRunAsync()
        {
            if (IsAutoRunning)
            {
                await StopAutoRunAsync();
                StatusText = "Auto run stopped — manual weigh enabled.";
                return;
            }
            if (_batch == null || !IsRunning || IsCompleted)
            {
                StatusText = "Start a running batch before auto run.";
                return;
            }
            EnsureScaleSubscribed();
            EnsureScannerSubscribed();
            await RefreshScaleLimitsAsync();
            IsAutoRunning = true;
            // While auto-run is active the operator must stay on the Live view;
            // the shell shows NavigationLockedMessage for any navigation away
            // until the batch is paused or completed. ForceRelease on logout /
            // exit stops the run so the lock can never strand the workspace.
            NavigationLock.Lock(FusionRoutes.LiveView, FusionNavigation.NavigationLockedMessage, forceRelease: StopAutoRun);
            StatusText = "Auto run started — scan EPC then place tub; capture is automatic (Weigh button read-only).";
        }

        private async void OnPlcWeighReceived(object? sender, EventArgs e)
        {
            // PLC capture edge: capture the check-weigher weight, record, refresh.
            bool ok = await WeighNextTubAsync();
            if (_plcRun != null)
                await _plcRun.AcknowledgeAsync(ok);
        }

        private void OnPlcStatusChanged(object? sender, string e) => StatusText = e;
        private void OnPlcFaulted(object? sender, string e) => StatusText = e;

        public async Task LoadAsync(string? batchNo = null)
        {
            try
            {
                await new FusionDatabaseInit(AppConfigurationManager.GetDatabasePath()).EnsureCreatedAsync();
                EnsureScaleSubscribed();
                EnsureScannerSubscribed();
                await RefreshScaleLimitsAsync();
                var pending = FusionNavigation.TakePendingBatchNo();
                var want = batchNo ?? pending ?? SelectedBatchNo;
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var batches = await repo.GetBatchesAsync();
                if (!string.IsNullOrWhiteSpace(want) && batches.Any(b => string.Equals(b.BatchNo, want, StringComparison.OrdinalIgnoreCase)))
                    SelectedBatchNo = want;
                else if (!batches.Any(b => string.Equals(b.BatchNo, SelectedBatchNo, StringComparison.OrdinalIgnoreCase)))
                    SelectedBatchNo = batches.FirstOrDefault(b => !string.Equals(b.Status, "completed", StringComparison.OrdinalIgnoreCase))?.BatchNo
                        ?? batches.FirstOrDefault()?.BatchNo ?? string.Empty;
                await RefreshBatchAsync();
                // Mirror batch weighing: with ZeroReturn on, the first weigh of a
                // running batch also waits for the scale to read near zero.
                if (IsRunning)
                {
                    double ref0 = (_batch?.TargetApwKg ?? 0) * Math.Max(1, _batch?.NutsPerTub ?? 50);
                    await ArmZeroReturnAsync(ref0 > 0 ? ref0 : 25);
                }
                else WaitingForZero = false;
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        public async Task RefreshBatchAsync()
        {
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var batches = await repo.GetBatchesAsync();
                var tubs = await repo.GetTubsAsync();
                _tubsCache.Clear();
                _tubsCache.AddRange(tubs);
                ValidateManualEpc();

                Pills.Clear();
                foreach (var b in batches)
                    Pills.Add(new LiveBatchPill
                    {
                        BatchNo = b.BatchNo,
                        Display = $"{b.BatchNo} · {b.Status}",
                        Status = b.Status ?? string.Empty,
                        IsSelected = string.Equals(b.BatchNo, SelectedBatchNo, StringComparison.OrdinalIgnoreCase),
                    });
                int activeCount = batches.Count(b => !string.Equals(b.Status, "completed", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(b.Status, "Closed", StringComparison.OrdinalIgnoreCase));
                int freeTubs = tubs.Count(t => !t.IsBlocked && string.IsNullOrEmpty(t.AssignedTo) && t.UsedCycles < 2000);
                PillsNote = batches.Count == 0
                    ? "No batch yet. Create one in the Batch creation tab."
                    : $"{activeCount} active batch(es) · {freeTubs} free tubs in the pool";

                _batch = batches.FirstOrDefault(b => string.Equals(b.BatchNo, SelectedBatchNo, StringComparison.OrdinalIgnoreCase));
                if (_batch == null)
                {
                    BannerText = "No batch yet.";
                    BannerSub = "Create one in the Batch creation tab.";
                    BatchStatus = string.Empty;
                    LotsTitle = "Lots";
                    Lots.Clear(); SupplierCards.Clear(); TxRows.Clear(); Steps.Clear(); CandidateLots.Clear();
                    AddLotVisible = false;
                    KpiWeight = KpiNuts = KpiTubs = KpiProgress = KpiTat = "-";
                    KpiWeightSub = KpiNutsSub = KpiTubsSub = KpiTatSub = string.Empty;
                    KpiProgressPct = 0;
                    RaisePropertyChanged(nameof(IsRunning));
                    RaisePropertyChanged(nameof(IsCompleted));
                    RaisePropertyChanged(nameof(PauseResumeButtonText));
                    StatusText = PillsNote;
                    return;
                }

                var lots = await repo.GetPlanLotsAsync(_batch.BatchNo);
                var events = await repo.GetBatchEventsAsync(_batch.BatchNo);
                var tx = await repo.GetTransactionsAsync(_batch.BatchNo, 10);
                var (near, expiredBelow) = await repo.GetConditionThresholdsAsync();
                var inward = await repo.GetInwardAsync(2000);
                var stock = inward.Select(g => InwardStockViewModel.ToRow(g, near, expiredBelow)).ToList();

                BatchStatus = _batch.Status ?? string.Empty;
                var (tot, pz) = TatFor(_batch);
                var net = tot - pz;
                if (IsCompleted)
                {
                    int doneN = lots.Sum(l => l.DoneTubs) * Math.Max(1, _batch.NutsPerTub);
                    BannerText = $"{_batch.BatchNo} · COMPLETED · {doneN:N0} coconuts, {lots.Sum(l => l.DoneKg):N0} kg weighed · tubs released";
                    BannerSub = $"Turn around time {FDur(tot)} · running {FDur(net)} · paused {FDur(pz)} ({_batch.PauseCount} pause(s))";
                }
                else
                {
                    BannerText = $"{_batch.BatchNo} · {BatchStatus.ToUpperInvariant()}";
                    BannerSub = $"Turn around time {FDur(tot)} · running {FDur(net)} · paused {FDur(pz)} ({_batch.PauseCount} pause(s))";
                }

                Steps.Clear();
                var last = events.Count == 0 ? string.Empty : events[events.Count - 1].Event;
                foreach (var s in StepOrder)
                {
                    var hits = events.Where(e => string.Equals(e.Event, s, StringComparison.OrdinalIgnoreCase)).ToList();
                    Steps.Add(new LiveStepItem
                    {
                        Title = s,
                        Time = hits.Count == 0 ? string.Empty : hits[hits.Count - 1].At,
                        IsDone = hits.Count > 0,
                        IsCurrent = string.Equals(s, last, StringComparison.OrdinalIgnoreCase),
                    });
                }

                int np = Math.Max(1, _batch.NutsPerTub);
                int doneTubs = lots.Sum(l => l.DoneTubs);
                double doneKg = lots.Sum(l => l.DoneKg);
                int targetTubs = _batch.TargetTubs > 0 ? _batch.TargetTubs : lots.Sum(l => l.Tubs);
                int targetNuts = _batch.TargetNuts > 0 ? _batch.TargetNuts : lots.Sum(l => l.Nuts);
                double pc = targetTubs > 0 ? Math.Min(100, (double)doneTubs / targetTubs * 100) : 0;

                KpiWeight = $"{doneKg:N0} kg";
                KpiWeightSub = $"target {_batch.TargetKg:N0} kg";
                KpiNuts = $"{(doneTubs * np):N0}";
                KpiNutsSub = $"of {targetNuts:N0}";
                KpiTubs = $"{doneTubs:N0}";
                KpiTubsSub = $"of {targetTubs:N0} · lots planned {lots.Sum(l => l.Tubs):N0}";
                KpiProgress = $"{pc:F1}%";
                KpiProgressPct = pc;
                KpiTat = FDur(tot);
                KpiTatSub = $"running {FDur(net)}";

                Lots.Clear();
                LotsTitle = $"Lots in {_batch.BatchNo} (all lots count towards the batch target)";
                foreach (var l in lots)
                {
                    var assigned = AssignedFor(tubs, _batch.BatchNo, l.Colour).Count;
                    Lots.Add(new LiveLotRow
                    {
                        Lot = l,
                        Status = l.IsCompleted || l.DoneTubs >= l.Tubs ? "Completed" : l.IsPaused ? "Paused" : "Running",
                        ProgressPct = l.Tubs > 0 ? Math.Min(100, (double)l.DoneTubs / l.Tubs * 100) : 0,
                        AssignedCount = assigned,
                        DonePlanText = $"{l.DoneTubs:N0} / {l.Tubs:N0}",
                        NutsText = $"{(l.DoneTubs * np):N0}",
                        KgText = $"{l.DoneKg:N0}",
                        GrnBayText = $"{l.GrnNo} · {l.BayId}",
                    });
                }

                SupplierCards.Clear();
                foreach (var s in lots.Select(l => l.SupplierName).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var sl = lots.Where(l => string.Equals(l.SupplierName, s, StringComparison.OrdinalIgnoreCase)).ToList();
                    int plan = sl.Sum(l => l.Tubs) * np;
                    int done = sl.Sum(l => l.DoneTubs) * np;
                    SupplierCards.Add(new LiveSupplierCard
                    {
                        Supplier = s,
                        Lots = string.Join(", ", sl.Select(l => l.LotNo)),
                        Colour = sl[0].Colour,
                        DoneNuts = done,
                        PlanNuts = plan,
                        DoneKg = sl.Sum(l => l.DoneKg),
                        AssignedCount = AssignedFor(tubs, _batch.BatchNo, sl[0].Colour).Count,
                        BalanceNuts = stock.Where(r => string.Equals(r.SupplierName, s, StringComparison.OrdinalIgnoreCase)).Sum(r => Math.Max(0, r.BalanceNuts)),
                        ProgressPct = plan > 0 ? Math.Min(100, (double)done / plan * 100) : 0,
                        ProgressText = $"{done:N0} of {plan:N0} nuts · {sl.Sum(l => l.DoneKg):N0} kg",
                    });
                }

                TxRows.Clear();
                foreach (var t in tx)
                    TxRows.Add(new LiveTxRow
                    {
                        Time = ShortTime(t.RecordedAt),
                        Tub = t.TubId,
                        Epc = t.Epc,
                        Cycles = t.TubCycles,
                        Supplier = t.SupplierName,
                        Grn = t.GrnNo,
                        Bay = t.BayId,
                        Lot = t.LotNo,
                        Weight = t.WeightKg,
                        Nuts = t.Nuts,
                        Colour = ColourOf(tubs, t.TubId),
                    });

                // Add-a-lot panel (only while running and target not covered).
                int remaining = targetTubs - lots.Sum(l => l.Tubs);
                var candidates = stock
                    .Where(r => r.DaysLeft >= 0 && r.BalanceNuts >= np && !string.IsNullOrWhiteSpace(r.GrnNo))
                    .OrderBy(r => r.BestBefore, StringComparer.Ordinal)
                    .ThenBy(r => r.InwardDate, StringComparer.Ordinal)
                    .ToList();
                CandidateLots.Clear();
                foreach (var c in candidates) CandidateLots.Add(c);
                if (!IsRunning || remaining <= 0 || candidates.Count == 0)
                {
                    AddLotVisible = false;
                    AddLotHead = remaining <= 0 && IsRunning
                        ? $"Add a lot to {_batch.BatchNo} · the lots already cover the batch target."
                        : string.Empty;
                    AddLotNote = string.Empty;
                }
                else
                {
                    AddLotVisible = true;
                    var first = candidates[0];
                    int n0 = Math.Min(remaining, (int)Math.Floor(first.BalanceNuts / np));
                    AddLotHead = $"Add a lot to {_batch.BatchNo} · remaining to allocate: {remaining:N0} tubs ({(remaining * np):N0} coconuts) · {freeTubs} free tubs in the pool";
                    AddLotNote = string.Empty;
                    if (string.IsNullOrWhiteSpace(SelectedCandidateGrn) || !candidates.Any(c => string.Equals(c.GrnNo, SelectedCandidateGrn, StringComparison.OrdinalIgnoreCase)))
                        SelectedCandidateGrn = first.GrnNo;
                    NewLotTubs = Math.Max(1, n0).ToString();
                }

                RaisePropertyChanged(nameof(IsRunning));
                RaisePropertyChanged(nameof(IsCompleted));
                RaisePropertyChanged(nameof(PauseResumeButtonText));
                if (string.IsNullOrEmpty(StatusText) || StatusText.StartsWith("Weigh failed", StringComparison.Ordinal))
                    StatusText = $"{_batch.BatchNo} · {doneTubs:N0}/{targetTubs:N0} tubs weighed.";
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        private static List<FusionTub> AssignedFor(List<FusionTub> tubs, string batchNo, string colour)
        {
            return tubs.Where(t => string.Equals(t.AssignedTo, batchNo, StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(colour, "Mixed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(colour, "Virtual", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(colour, "-", StringComparison.Ordinal)
                        || string.Equals(t.Colour, colour, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        private static string ColourOf(List<FusionTub> tubs, string tubId)
        {
            return tubs.FirstOrDefault(t => string.Equals(t.TubId, tubId, StringComparison.OrdinalIgnoreCase))?.Colour ?? string.Empty;
        }

        private static string ShortTime(string recordedAt)
        {
            if (string.IsNullOrWhiteSpace(recordedAt)) return string.Empty;
            var parts = recordedAt.Split(' ');
            return parts.Length > 1 ? parts[1] : recordedAt;
        }

        internal static (TimeSpan tot, TimeSpan pz) TatFor(FusionBatch b)
        {
            if (!DateTime.TryParse(b.StartedAt, out var start)) start = DateTime.Now;
            DateTime end = DateTime.Now;
            if (!string.IsNullOrWhiteSpace(b.CompletedAt) && DateTime.TryParse(b.CompletedAt, out var c)) end = c;
            var tot = end - start;
            if (tot < TimeSpan.Zero) tot = TimeSpan.Zero;
            var pz = TimeSpan.FromMilliseconds(Math.Max(0, b.PausedMs));
            if (!string.IsNullOrWhiteSpace(b.PauseStartedAt) && DateTime.TryParse(b.PauseStartedAt, out var ps))
                pz += end - ps;
            if (pz < TimeSpan.Zero) pz = TimeSpan.Zero;
            return (tot, pz);
        }

        internal static string FDur(TimeSpan t)
        {
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m {t.Seconds}s";
            return $"{Math.Max(0, (int)t.TotalSeconds)}s";
        }

        public async Task<bool> WeighNextTubAsync()
        {
            if (_ticking) return false;
            if (_batch == null || !IsRunning) return false;
            if (_waitingForZero)
            {
                _scale ??= ScaleReaderService.GetShared(new ScaleSettingsRepository(AppConfigurationManager.GetDatabasePath()));
                if (_scale.IsSicsMode)
                {
                    // SICS has no stream: this press is a zero-confirm read, never a weigh.
                    _ticking = true;
                    try
                    {
                        double? z = await _scale.RequestWeightAsync();
                        if (z.HasValue && IsWithinZeroBand(z.Value))
                        {
                            WaitingForZero = false;
                            StatusText = "Zero confirmed — place the tub and press Weigh next tub.";
                        }
                        else
                            StatusText = "Waiting for zero — remove the tub from the scale.";
                        return false;
                    }
                    finally { _ticking = false; }
                }
                StatusText = "Waiting for zero — remove the tub from the scale.";
                return false;
            }
            _ticking = true;
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var manualEpc = (_manualEpcText ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(manualEpc))
                    return await WeighManualEpcAsync(repo, manualEpc);
                var lots = await repo.GetPlanLotsAsync(_batch.BatchNo);
                var open = lots.Where(l => !l.IsCompleted && l.DoneTubs < l.Tubs && !l.IsPaused).ToList();
                if (open.Count == 0)
                {
                    if (lots.Count > 0 && lots.All(l => l.IsCompleted || l.DoneTubs >= l.Tubs))
                        await CompleteBatchInternalAsync(repo, auto: true);
                    else
                        StatusText = "All open lots are paused.";
                    await RefreshBatchAsync();
                    return false;
                }
                int total = open.Sum(l => l.Tubs - l.DoneTubs);
                int r;
                lock (_rand) { r = _rand.Next(Math.Max(1, total)); }
                var lot = open[0];
                foreach (var l in open) { r -= l.Tubs - l.DoneTubs; if (r < 0) { lot = l; break; } }

                var tubs = await repo.GetTubsAsync();
                var pool = AssignedFor(tubs, _batch.BatchNo, lot.Colour).Where(t => !t.IsBlocked).OrderBy(t => t.UsedCycles).ToList();
                if (pool.Count == 0)
                {
                    pool = tubs.Where(t => string.IsNullOrEmpty(t.AssignedTo) && !t.IsBlocked && t.UsedCycles < 2000
                            && (string.Equals(lot.Colour, "Mixed", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(lot.Colour, "Virtual", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(lot.Colour, "-", StringComparison.Ordinal)
                                || string.Equals(t.Colour, lot.Colour, StringComparison.OrdinalIgnoreCase)))
                        .OrderBy(t => t.UsedCycles).Take(4).ToList();
                    if (pool.Count == 0)
                    {
                        StatusText = $"No free tubs for lot {lot.LotNo}.";
                        return false;
                    }
                    await repo.SetTubsAssignedAsync(pool.Select(t => t.TubId), _batch.BatchNo);
                }
                var tub = pool[0];
                return await WeighTubAsync(repo, lot, tub);
            }
            catch (Exception ex)
            {
                StatusText = $"Weigh failed: {ex.Message}";
                return false;
            }
            finally
            {
                _ticking = false;
            }
        }

        private async Task<bool> WeighManualEpcAsync(FusionRepository repo, string epc)
        {
            // Manual EPC scan: re-validate against fresh data; any failure blocks
            // with an error and ZERO writes.
            var tubs = await repo.GetTubsAsync();
            _tubsCache.Clear();
            _tubsCache.AddRange(tubs);
            ValidateManualEpc();
            if (ManualEpcError != null)
            {
                StatusText = ManualEpcError;
                return false;
            }
            var tub = FindTubByEpc(tubs, epc);
            if (tub == null)
            {
                StatusText = $"EPC {epc} not found in the tub master.";
                return false;
            }
            if (string.IsNullOrEmpty(tub.AssignedTo))
                await repo.SetTubsAssignedAsync(new[] { tub.TubId }, _batch!.BatchNo);
            var lots = await repo.GetPlanLotsAsync(_batch!.BatchNo);
            var lot = lots.Where(l => !l.IsCompleted && l.DoneTubs < l.Tubs && !l.IsPaused && TubFitsLot(tub, l))
                .OrderByDescending(l => l.Tubs - l.DoneTubs)
                .FirstOrDefault();
            if (lot == null)
            {
                StatusText = $"No open lot accepts {tub.TubId} (colour {tub.Colour}).";
                return false;
            }
            bool ok = await WeighTubAsync(repo, lot, tub);
            if (ok)
            {
                ManualEpcText = string.Empty;
                _pendingScannedEpc = string.Empty;
            }
            return ok;
        }

        private async Task<bool> WeighTubAsync(FusionRepository repo, FusionPlanLot lot, FusionTub tub)
        {
            int np = Math.Max(1, _batch!.NutsPerTub);
            // GRN-level stock guard: block a weigh that would drive the GRN balance negative.
            if (!string.IsNullOrWhiteSpace(lot.GrnNo))
            {
                var g = await repo.GetInwardByGrnAsync(lot.GrnNo);
                if (g != null)
                {
                    var row = InwardStockViewModel.ToRow(g);
                    if (row.BalanceNuts < np || row.BalanceKg <= 0)
                    {
                        StatusText = $"GRN {lot.GrnNo} balance too low for another tub — not weighed.";
                        return false;
                    }
                }
            }
            double expected = lot.Tubs > 0 ? lot.WeightKg / lot.Tubs : _batch.TargetApwKg * np;
            if (expected <= 0) expected = _batch.TargetApwKg * np;
            if (expected <= 0) expected = 25;
            // Checkweigher single value only (mode-switched) — never simulate.
            // No fresh value = no weighing.
            double? single = await TryTakeCheckweigherWeightAsync();
            if (!single.HasValue || single.Value <= 0)
            {
                ScaleStatusText = "Scale: no weight";
                StatusText = $"No checkweigher weight for {tub.TubId} — not weighed. Check the scale.";
                return false;
            }
            double w = Math.Round(single.Value, 2);
            // AppSettings scale limits: capacity / resolution / presence gate.
            string? limitError = CheckScaleLimits(w);
            if (limitError != null)
            {
                bool overflow = _scaleMaxKg > 0 && w > _scaleMaxKg;
                ScaleStatusText = overflow ? "Scale: overflow" : "Scale: no product";
                IsOverLimit = overflow;
                StatusText = limitError;
                if (overflow)
                {
                    // Force unload before retry: weight must be cleared/0.
                    _zeroRefWeight = w > 0 ? w : 1.0;
                    WaitingForZero = true;
                    lock (_weightSlotLock) { _weightSlot = 0; }
                }
                return false;
            }
            IsOverLimit = false;
            ScaleStatusText = "Scale: live weight";
            double dev = (w - expected) / expected * 100;
            // supplier_id references the shared suppliers table (integer PK).
            // Fusion carries supplier_name text only, so resolve the id here
            // (mirrors the fusion supplier into the shared table when needed).
            int supplierId = await repo.ResolveSupplierIdByNameAsync(lot.SupplierName);
            await repo.RecordLiveWeighAsync(new FusionTransaction
            {
                Id = Guid.NewGuid().ToString("N"),
                BatchNo = _batch.BatchNo,
                TubId = tub.TubId,
                SupplierId = supplierId,
                WeightKg = w,
                Epc = tub.Epc,
                TubCycles = tub.UsedCycles + 1,
                SupplierName = lot.SupplierName,
                GrnNo = lot.GrnNo,
                BayId = lot.BayId,
                LotNo = lot.LotNo,
                Nuts = np,
                OperatorName = OperatorName,
            }, lot.DoneTubs + 1, lot.DoneKg + w);
            string? warn = Math.Abs(dev) > 1.45
                ? $"Check weigher: {tub.TubId} weight {w:F2} kg deviates {dev:F1}% from expected {expected:F2} kg."
                : null;
            string? grnNote = string.IsNullOrWhiteSpace(lot.GrnNo)
                ? $"No GRN on lot {lot.LotNo} — stock consumption skipped."
                : null;
            if ((lot.DoneTubs + 1) >= lot.Tubs)
            {
                await repo.SetLotCompletedAsync(_batch.BatchNo, lot.LotNo, true);
                await repo.AddBatchEventAsync(_batch.BatchNo, "Lot Completed");
            }
            var fresh = await repo.GetPlanLotsAsync(_batch.BatchNo);
            if (fresh.Count > 0 && fresh.All(l => l.IsCompleted || l.DoneTubs >= l.Tubs))
                await CompleteBatchInternalAsync(repo, auto: true);
            await RefreshBatchAsync();
            if (warn != null) StatusText = warn;
            else if (grnNote != null) StatusText = grnNote;
            await ArmZeroReturnAsync(expected);
            return true;
        }

        private async Task<double?> TryTakeCheckweigherWeightAsync()
        {
            try
            {
                // Shared instance owned by batch weighing; never stop the connection from here.
                _scale ??= ScaleReaderService.GetShared(new ScaleSettingsRepository(AppConfigurationManager.GetDatabasePath()));
                if (_scale.IsSicsMode)
                {
                    // SICS: one-shot inquiry — the scale's single reply is the tub weight.
                    return await _scale.RequestWeightAsync();
                }
                // Continuous (serial/TCP/ID510): consume the latest pushed value once.
                // A value newer than the last consumption and not older than 120 s.
                lock (_weightSlotLock)
                {
                    if (_weightSlotAt > _weightConsumedAt && (DateTime.Now - _weightSlotAt).TotalSeconds <= 120)
                    {
                        _weightConsumedAt = _weightSlotAt;
                        return _weightSlot > 0 ? _weightSlot : null;
                    }
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        public async Task PauseResumeBatchAsync()
        {
            if (_batch == null || IsCompleted) return;
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                if (IsRunning)
                {
                    if (IsAutoRunning) await StopAutoRunAsync();
                    await repo.PauseBatchAsync(_batch.BatchNo);
                    await repo.AddBatchEventAsync(_batch.BatchNo, "Paused");
                    StatusText = $"Paused {_batch.BatchNo}.";
                }
                else
                {
                    await repo.ResumeBatchAsync(_batch.BatchNo);
                    await repo.AddBatchEventAsync(_batch.BatchNo, "Resumed");
                    StatusText = $"Resumed {_batch.BatchNo}.";
                }
                await RefreshBatchAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Update failed: {ex.Message}";
            }
        }

        public async Task CompleteBatchAsync()
        {
            if (_batch == null || IsCompleted) return;
            var confirm = MessageBox.Show(
                $"Are you sure you want to complete batch '{_batch.BatchNo}'? Assigned tubs will be released.",
                "Confirm Complete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                StatusText = $"Completion of {_batch.BatchNo} cancelled.";
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await CompleteBatchInternalAsync(repo, auto: false);
                await RefreshBatchAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Complete failed: {ex.Message}";
            }
        }

        private async Task CompleteBatchInternalAsync(FusionRepository repo, bool auto)
        {
            if (_batch == null) return;
            await repo.CompleteBatchRunAsync(_batch.BatchNo);
            await repo.AddBatchEventAsync(_batch.BatchNo, "Batch Completed");
            await repo.ClearBatchAssignmentsAsync(_batch.BatchNo);
            StopAutoRun();
            StatusText = auto ? $"Batch {_batch.BatchNo} completed automatically." : $"Batch {_batch.BatchNo} completed.";
        }

        public async Task PauseLotAsync(LiveLotRow row)
        {
            if (row?.Lot == null || _batch == null || IsCompleted) return;
            if (row.Status == "Completed") return;
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                bool paused = !row.Lot.IsPaused;
                await repo.SetLotPausedAsync(_batch.BatchNo, row.Lot.LotNo, paused);
                StatusText = paused ? $"Paused {row.Lot.LotNo}." : $"Resumed {row.Lot.LotNo}.";
                await RefreshBatchAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Update failed: {ex.Message}";
            }
        }

        public async Task CompleteLotAsync(LiveLotRow row)
        {
            if (row?.Lot == null || _batch == null || IsCompleted) return;
            if (row.Status == "Completed") return;
            var confirm = MessageBox.Show(
                $"Are you sure you want to complete lot '{row.Lot.LotNo}' manually?",
                "Confirm Complete Lot",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                StatusText = $"Completion of {row.Lot.LotNo} cancelled.";
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.SetLotCompletedAsync(_batch.BatchNo, row.Lot.LotNo, true);
                await repo.AddBatchEventAsync(_batch.BatchNo, "Lot Completed");
                StatusText = $"Completed {row.Lot.LotNo}.";
                var fresh = await repo.GetPlanLotsAsync(_batch.BatchNo);
                if (fresh.Count > 0 && fresh.All(l => l.IsCompleted || l.DoneTubs >= l.Tubs))
                    await CompleteBatchInternalAsync(repo, auto: true);
                await RefreshBatchAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Update failed: {ex.Message}";
            }
        }

        public async Task AddLotAsync()
        {
            if (_batch == null || !IsRunning) return;
            if (string.IsNullOrWhiteSpace(SelectedCandidateGrn))
            {
                AddLotNote = "Select a stock lot first.";
                return;
            }
            if (!int.TryParse((NewLotTubs ?? string.Empty).Trim(), out var tubs) || tubs <= 0)
            {
                AddLotNote = "Enter tubs as a whole number greater than 0.";
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var (near, expiredBelow) = await repo.GetConditionThresholdsAsync();
                var inward = await repo.GetInwardAsync(2000);
                var cand = inward.Select(g => InwardStockViewModel.ToRow(g, near, expiredBelow))
                    .FirstOrDefault(r => string.Equals(r.GrnNo, SelectedCandidateGrn, StringComparison.OrdinalIgnoreCase));
                if (cand == null)
                {
                    AddLotNote = "Selected stock lot is no longer available.";
                    return;
                }
                int np = Math.Max(1, _batch.NutsPerTub);
                var existing = await repo.GetPlanLotsAsync(_batch.BatchNo);
                var sups = existing.Select(l => l.SupplierName)
                    .Concat(new[] { cand.SupplierName })
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                bool virt = string.Equals(_batch.TubMode, "virtual", StringComparison.OrdinalIgnoreCase);
                string colour = virt ? "Virtual"
                    : sups.Count == 1 ? "Mixed"
                    : sups.FindIndex(s => string.Equals(s, cand.SupplierName, StringComparison.OrdinalIgnoreCase)) == 1 ? "Blue"
                    : "Orange";
                double apw = cand.CoconutCount > 0 ? cand.WeightKg / cand.CoconutCount : _batch.TargetApwKg;
                var allTubs = await repo.GetTubsAsync();
                var free = allTubs.Where(t => string.IsNullOrEmpty(t.AssignedTo) && !t.IsBlocked && t.UsedCycles < 2000
                        && (colour == "Mixed" || colour == "Virtual" || string.Equals(t.Colour, colour, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(t => t.UsedCycles)
                    .Take(Math.Max(2, tubs))
                    .Select(t => t.TubId).ToList();
                var lot = new FusionPlanLot
                {
                    LotNo = $"LOT-{existing.Count + 1}",
                    BatchNo = _batch.BatchNo,
                    SupplierName = cand.SupplierName,
                    GrnNo = cand.GrnNo,
                    BayId = cand.BayId,
                    BestBefore = cand.BestBefore,
                    DaysLeft = cand.DaysLeft,
                    Tubs = tubs,
                    Nuts = tubs * np,
                    WeightKg = tubs * np * apw,
                    Colour = colour,
                    TubsInPool = free.Count,
                    CyclesPerTub = free.Count > 0 ? (double)tubs / free.Count : 0,
                };
                await repo.InsertPlanLotAsync(lot);
                await repo.SetTubsAssignedAsync(free, _batch.BatchNo);
                await repo.AddBatchEventAsync(_batch.BatchNo, "Lot Created");
                StatusText = $"Added {lot.LotNo} ({cand.SupplierName}, {tubs} tubs).";
                await RefreshBatchAsync();
            }
            catch (Exception ex)
            {
                AddLotNote = $"Add failed: {ex.Message}";
            }
        }
    }
}
