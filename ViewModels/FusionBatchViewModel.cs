// Fusion batch creation + FEFO lot planning (mirrors fusion_link_v3_ui.html tab t3).
// Targets -> live FEFO pick list -> colour/virtual tub assignment -> 6 checks ->
// create batch (persists fusion_batches + fusion_plan_lots). fusion_* only.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Services;

using ProWeighSuitePlatforms.Navigation;
using ProWeighSuitePlatforms.Models;
namespace ProWeighSuitePlatforms.ViewModels
{
    public sealed class BatchCheck
    {
        public bool Passed { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
    }

    public sealed class AssignGroup : ViewModels.BaseViewModel
    {
        public string Supplier { get; set; } = string.Empty;
        public string Colour { get; set; } = string.Empty;
        private string _scanText = string.Empty;
        public string ScanText { get => _scanText; set => SetProperty(ref _scanText, value); }
        private string _assignedDisplay = string.Empty;
        public string AssignedDisplay { get => _assignedDisplay; set => SetProperty(ref _assignedDisplay, value); }
        private string _summary = string.Empty;
        public string Summary { get => _summary; set => SetProperty(ref _summary, value); }
    }

    internal sealed class StockSlice
    {
        public string GrnNo = string.Empty;
        public string Supplier = string.Empty;
        public string Bay = string.Empty;
        public string Date = string.Empty;
        public string BestBefore = string.Empty;
        public int DaysLeft;
        public double Apw;
        public double AvailNuts;
        public double WeightKg;
        public double CoconutCount;
    }

    public sealed class FusionBatchViewModel : ViewModels.BaseViewModel
    {
        public string Title => "Batch creation and lots";

        private readonly AuditService _audit = new();
        private readonly string _username;

        public List<string> SupplierModes { get; } = new() { "Auto FEFO (any suppliers)", "Single supplier" };
        public List<string> TubModes { get; } = new() { "Colour based (orange / blue)", "Virtual (scan tub EPC)" };
        public List<string> LotModes { get; } = new() { "Auto FEFO, all lots now", "Auto FEFO, first lot only", "Supplier wise, add lots manually" };
        public ObservableCollection<string> SupplierNames { get; } = new();

        private List<FusionSupplier> _suppliers = new();
        private List<FusionInward> _inward = new();
        private List<FusionTub> _tubs = new();
        private readonly Dictionary<string, List<string>> _asg = new(StringComparer.OrdinalIgnoreCase);
        private string _assignNote = string.Empty;

        // ---- targets ----
        private DateTime? _batchDate = DateTime.Today;
        public DateTime? BatchDate
        {
            get => _batchDate;
            set { if (SetProperty(ref _batchDate, value)) { _ = RefreshBatchNoAsync(); Replan(); } }
        }
        private string _batchNo = string.Empty;
        public string BatchNo { get => _batchNo; private set => SetProperty(ref _batchNo, value); }
        private string _supplierMode = "Auto FEFO (any suppliers)";
        public string SupplierMode { get => _supplierMode; set { if (SetProperty(ref _supplierMode, value)) { RaisePropertyChanged(nameof(IsSingle)); Replan(); } } }
        public bool IsSingle => SupplierMode == "Single supplier";
        private string _singleSupplier = string.Empty;
        public string SingleSupplier { get => _singleSupplier; set { if (SetProperty(ref _singleSupplier, value)) Replan(); } }
        private string _tubMode = "Colour based (orange / blue)";
        public string TubMode { get => _tubMode; set { if (SetProperty(ref _tubMode, value)) Replan(); } }
        public bool IsVirtual => TubMode.StartsWith("Virtual", StringComparison.Ordinal);
        private string _lotMode = "Auto FEFO, all lots now";
        public string LotMode { get => _lotMode; set { if (SetProperty(ref _lotMode, value)) Replan(); } }
        public bool IsManual => LotMode.StartsWith("Supplier wise", StringComparison.Ordinal);
        private string _targetWeight = string.Empty;
        public string TargetWeight { get => _targetWeight; set { if (SetProperty(ref _targetWeight, value)) Replan(); } }
        private string _targetApw = "0.5";
        public string TargetApw { get => _targetApw; set { if (SetProperty(ref _targetApw, value)) Replan(); } }
        private string _nutsPerTub = "50";
        public string NutsPerTub { get => _nutsPerTub; set { if (SetProperty(ref _nutsPerTub, value)) Replan(); } }
        private string _targetHours = "16";
        public string TargetHours { get => _targetHours; set { if (SetProperty(ref _targetHours, value)) Replan(); } }
        private string _loopMin = "10";
        public string LoopMin { get => _loopMin; set { if (SetProperty(ref _loopMin, value)) Replan(); } }

        private string _orangePool = "-";
        public string OrangePool { get => _orangePool; private set => SetProperty(ref _orangePool, value); }
        private string _bluePool = "-";
        public string BluePool { get => _bluePool; private set => SetProperty(ref _bluePool, value); }

        // ---- plan outputs ----
        public ObservableCollection<FusionPlanLot> PlanLots { get; } = new();
        public ObservableCollection<AssignGroup> AssignGroups { get; } = new();
        public ObservableCollection<BatchCheck> Checks { get; } = new();
        private string _planNotice = string.Empty;
        public string PlanNotice { get => _planNotice; private set => SetProperty(ref _planNotice, value); }

        private string _kpiCount = "-"; public string KpiCount { get => _kpiCount; private set => SetProperty(ref _kpiCount, value); }
        private string _kpiTubs = "-"; public string KpiTubs { get => _kpiTubs; private set => SetProperty(ref _kpiTubs, value); }
        private string _kpiTph = "-"; public string KpiTph { get => _kpiTph; private set => SetProperty(ref _kpiTph, value); }
        private string _kpiTpm = "-"; public string KpiTpm { get => _kpiTpm; private set => SetProperty(ref _kpiTpm, value); }
        private string _kpiInterval = "-"; public string KpiInterval { get => _kpiInterval; private set => SetProperty(ref _kpiInterval, value); }
        private string _kpiPlanned = "-"; public string KpiPlanned { get => _kpiPlanned; private set => SetProperty(ref _kpiPlanned, value); }

        // ---- saved ----
        public ObservableCollection<FusionBatch> Batches { get; } = new();
        public ObservableCollection<FusionPlanLot> SavedLots { get; } = new();
        private FusionBatch? _selectedBatch;
        public FusionBatch? SelectedBatch
        {
            get => _selectedBatch;
            set { if (SetProperty(ref _selectedBatch, value)) _ = LoadSavedLotsAsync(); }
        }

        private bool _canCreate;
        public bool CanCreate { get => _canCreate; private set => SetProperty(ref _canCreate, value); }

        private string _statusText = string.Empty;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        private bool _isFormExpanded = true;
        public bool IsFormExpanded
        {
            get => _isFormExpanded;
            set => SetProperty(ref _isFormExpanded, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand CreateBatchCommand { get; }
        public ICommand CloseBatchCommand { get; }
        public ICommand AutoAssignCommand { get; }
        public ICommand ClearAssignCommand { get; }
        public ICommand ScanTubCommand { get; }
        public ICommand ToggleFormCommand { get; }

        public FusionBatchViewModel(string username = "System")
        {
            _username = string.IsNullOrWhiteSpace(username) ? "System" : username;
            RefreshCommand = new ViewModels.RelayCommand(async _ => await LoadAsync());
            CreateBatchCommand = new ViewModels.RelayCommand(async _ => await CreateBatchAsync(), _ => CanCreate || IsManual);
            CloseBatchCommand = new ViewModels.RelayCommand(async _ => await CloseBatchAsync());
            AutoAssignCommand = new ViewModels.RelayCommand(p => { if (p is AssignGroup g) AutoAssign(g); });
            ClearAssignCommand = new ViewModels.RelayCommand(p => { if (p is AssignGroup g) { _asg[g.Supplier] = new List<string>(); _assignNote = string.Empty; Replan(); } });
            ScanTubCommand = new ViewModels.RelayCommand(p => { if (p is AssignGroup g) ScanTub(g); });
            ToggleFormCommand = new ViewModels.RelayCommand(_ => IsFormExpanded = !IsFormExpanded);
            _ = LoadAsync();
        }

        private static double Num(string s, double dflt)
            => double.TryParse(s, out var v) && v > 0 ? v : dflt;
        private static int NumInt(string s, int dflt)
            => int.TryParse(s, out var v) && v > 0 ? v : dflt;

        private bool Eligible(FusionTub t) => !t.IsBlocked && string.IsNullOrEmpty(t.AssignedTo) && t.UsedCycles < 2000;

        private int _expiredBelowDays = FusionSettingsDefaults.ExpiredBelowDays;

        private List<StockSlice> UsableSlices(bool single, string singleName, int nutsPerTub)
        {
            var list = new List<StockSlice>();
            foreach (var g in _inward)
            {
                if (single && !string.Equals(g.SupplierName, singleName, StringComparison.OrdinalIgnoreCase))
                    continue;
                string date = !string.IsNullOrEmpty(g.InwardDate) ? g.InwardDate
                    : (g.ReceivedAt.Length >= 10 ? g.ReceivedAt[..10] : DateTime.Today.ToString("yyyy-MM-dd"));
                var bb = FusionConditions.BestBefore(date, g.ShelfLifeDays);
                int left = (bb.Date - DateTime.Today).Days;
                if (left < _expiredBelowDays) continue;
                double q = g.SampleKg + g.SpoilKg + g.WaterLossKg + g.BrokenKg + g.RejectionKg;
                double avail = g.WeightKg - q;
                double availNuts = (g.WeightKg > 0 && g.CoconutCount > 0)
                    ? Math.Round(avail * g.CoconutCount / g.WeightKg) - g.ConsumedNuts : 0;
                if (availNuts < nutsPerTub) continue;
                list.Add(new StockSlice
                {
                    GrnNo = g.GrnNo,
                    Supplier = g.SupplierName,
                    Bay = g.BayId,
                    Date = date,
                    BestBefore = bb.ToString("yyyy-MM-dd"),
                    DaysLeft = left,
                    Apw = g.CoconutCount > 0 ? g.WeightKg / g.CoconutCount : 0,
                    AvailNuts = availNuts,
                    WeightKg = g.WeightKg,
                    CoconutCount = g.CoconutCount,
                });
            }
            list.Sort((a, b) => a.DaysLeft != b.DaysLeft ? a.DaysLeft.CompareTo(b.DaysLeft)
                : string.Compare(a.Date, b.Date, StringComparison.Ordinal));
            return list;
        }

        private void Replan()
        {
            if (_inward == null || _tubs == null) return;
            try
            {
                double tw = Num(TargetWeight, 100000);
                double ta = Num(TargetApw, 0.5);
                int np = NumInt(NutsPerTub, 50);
                double th = Num(TargetHours, 16);
                double lp = Num(LoopMin, 10);
                bool virt = IsVirtual;
                bool single = IsSingle;

                int po = _tubs.Count(t => t.Colour == "Orange" && Eligible(t));
                int pb = _tubs.Count(t => t.Colour == "Blue" && Eligible(t));
                OrangePool = po.ToString();
                BluePool = pb.ToString();

                int count = (int)Math.Round(tw / ta);
                int tubsNeeded = (int)Math.Ceiling((double)count / np);
                double tpm = th > 0 ? tubsNeeded / th / 60 : 0;

                KpiCount = $"{count:N0} nuts";
                KpiTubs = $"{tubsNeeded:N0}";
                KpiTph = th > 0 ? $"{(tubsNeeded / th):N0}" : "-";
                KpiTpm = tpm.ToString("F2");
                KpiInterval = tubsNeeded > 0 ? $"{(th * 3600 / tubsNeeded):F1} s/tub" : "-";

                PlanLots.Clear();
                AssignGroups.Clear();
                Checks.Clear();
                PlanNotice = string.Empty;

                if (IsManual)
                {
                    KpiPlanned = "-";
                    PlanNotice = "Supplier wise mode: create the batch, then add each lot in the Live view after the batch is created.";
                    Checks.Add(new BatchCheck { Passed = true, Title = "Target", Detail = $"{tubsNeeded:N0} tub loads" });
                    CanCreate = true;
                    return;
                }

                var c = UsableSlices(single, SingleSupplier, np);
                int rem = tubsNeeded;
                var lots = new List<FusionPlanLot>();
                foreach (var x in c)
                {
                    int pt = Math.Min((int)Math.Floor(x.AvailNuts / np), rem);
                    if (pt <= 0) continue;
                    lots.Add(new FusionPlanLot
                    {
                        LotNo = $"LOT-{lots.Count + 1}",
                        SupplierName = x.Supplier,
                        GrnNo = x.GrnNo,
                        BayId = x.Bay,
                        BestBefore = x.BestBefore,
                        DaysLeft = x.DaysLeft,
                        Tubs = pt,
                        Nuts = pt * np,
                        WeightKg = pt * np * x.Apw,
                    });
                    rem -= pt;
                    if (rem <= 0) break;
                }
                if (LotMode.StartsWith("Auto FEFO, first", StringComparison.Ordinal) && lots.Count > 1)
                    lots.RemoveRange(1, lots.Count - 1);

                var sps = lots.Select(l => l.SupplierName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                bool one = sps.Count == 1, two = sps.Count == 2;
                foreach (var l in lots)
                {
                    int k = sps.FindIndex(s => string.Equals(s, l.SupplierName, StringComparison.OrdinalIgnoreCase));
                    l.Colour = virt ? "Virtual" : one ? "Mixed" : two ? (k == 1 ? "Blue" : "Orange") : "-";
                }
                var poolOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var shareOf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                int totalTubs = lots.Sum(l => l.Tubs);
                foreach (var s in sps)
                {
                    int t = lots.Where(l => string.Equals(l.SupplierName, s, StringComparison.OrdinalIgnoreCase)).Sum(l => l.Tubs);
                    int k = sps.FindIndex(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase));
                    int pool = virt ? (_asg.TryGetValue(s, out var al) ? al.Count : 0)
                        : one ? po + pb : two ? (k == 1 ? pb : po) : 0;
                    poolOf[s] = pool;
                    shareOf[s] = totalTubs > 0 ? (double)t / totalTubs : 0;
                }
                foreach (var l in lots)
                {
                    l.TubsInPool = poolOf[l.SupplierName];
                    l.CyclesPerTub = poolOf[l.SupplierName] > 0
                        ? (double)lots.Where(x => string.Equals(x.SupplierName, l.SupplierName, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Tubs) / poolOf[l.SupplierName] : 0;
                }

                double mx = double.MaxValue;
                foreach (var s in sps)
                {
                    if (poolOf[s] > 0 && shareOf[s] > 0 && tpm > 0)
                        mx = Math.Min(mx, poolOf[s] / (tpm * shareOf[s]));
                }

                foreach (var l in lots) PlanLots.Add(l);
                KpiPlanned = $"{lots.Sum(l => l.WeightKg):N0} kg";

                // Tub assignment cards.
                int freeAv = _tubs.Count(Eligible);
                var assignInfo = new List<string>();
                if (freeAv < _tubs.Count) assignInfo.Add($"Free tubs: {freeAv} of {_tubs.Count}");
                if (!string.IsNullOrEmpty(_assignNote)) assignInfo.Add(_assignNote);
                foreach (var s in sps)
                {
                    var lot = lots.First(l => string.Equals(l.SupplierName, s, StringComparison.OrdinalIgnoreCase));
                    var g = new AssignGroup { Supplier = s, Colour = lot.Colour };
                    if (!virt)
                    {
                        g.Summary = lot.Colour == "Mixed" ? $"Colour based: all available tubs · {poolOf[s]} tubs available"
                            : $"Colour based: {lot.Colour} tubs · {poolOf[s]} tubs available";
                    }
                    else
                    {
                        if (!_asg.TryGetValue(s, out var al)) { al = new List<string>(); _asg[s] = al; }
                        int rec = one ? freeAv : Math.Max(2, (int)Math.Round(shareOf[s] * freeAv));
                        g.Summary = $"Assigned {al.Count} · recommended {rec}";
                        g.AssignedDisplay = al.Count == 0 ? "No tubs yet" : string.Join(", ", al.Select(e =>
                        {
                            var t = _tubs.FirstOrDefault(x => x.Epc == e);
                            return t != null ? t.TubId : e;
                        }));
                    }
                    AssignGroups.Add(g);
                }
                if (assignInfo.Count > 0) PlanNotice = string.Join(" · ", assignInfo);

                int nr = _tubs.Count(t => Eligible(t) && t.UsedCycles >= 1600);
                double planned = lots.Sum(l => l.WeightKg);
                Checks.Add(new BatchCheck { Passed = totalTubs >= tubsNeeded, Title = "Stock covers the target", Detail = $"Planned {totalTubs * np:N0} of {count:N0} nuts" });
                Checks.Add(new BatchCheck { Passed = sps.Count <= 2 || virt, Title = "Supplier limit", Detail = virt ? "Virtual assignment allows any number of suppliers" : $"{sps.Count} supplier(s); orange and blue cover only 2" });
                Checks.Add(new BatchCheck { Passed = sps.Count > 0 && sps.All(s => poolOf[s] > 0), Title = "Every supplier has tubs", Detail = virt ? "Scan or auto-assign tubs for each supplier" : "Colour pools have available tubs" });
                Checks.Add(new BatchCheck { Passed = tubsNeeded <= 0 || (mx != double.MaxValue && mx >= lp), Title = "Each supplier's tubs can keep up with the line", Detail = $"Max tub loop {(mx == double.MaxValue ? "-" : mx.ToString("F1"))} min; your loop time {lp} min" });
                Checks.Add(new BatchCheck { Passed = nr == 0, Title = "Tub life", Detail = $"Tubs near or past replacement in this pool: {nr}" });
                Checks.Add(new BatchCheck { Passed = tw > 0 && Math.Abs(planned - tw) / tw <= 0.03, Title = "Planned weight within 3% of target", Detail = $"{planned:N0} kg vs {tw:N0} kg" });

                CanCreate = lots.Count > 0 && sps.All(s => poolOf[s] > 0);
            }
            catch
            {
                CanCreate = false;
            }
        }

        private FusionTub? FindTub(string val)
        {
            string q = val.Trim().ToLowerInvariant().TrimStart('0');
            return _tubs.FirstOrDefault(t =>
                string.Equals(t.Epc?.TrimStart('0'), q, StringComparison.OrdinalIgnoreCase)
                || string.Equals(t.TubId, val.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private void ScanTub(AssignGroup g)
        {
            var val = g.ScanText?.Trim() ?? string.Empty;
            g.ScanText = string.Empty;
            if (string.IsNullOrEmpty(val)) return;
            var t = FindTub(val);
            if (t == null) { _assignNote = $"Tub {val} not found in the tub master."; Replan(); return; }
            if (t.IsBlocked) { _assignNote = $"{t.TubId} is blocked."; Replan(); return; }
            if (t.UsedCycles >= 2000) { _assignNote = $"{t.TubId} has reached its cycle limit and needs replacement."; Replan(); return; }
            var other = _asg.FirstOrDefault(kv => kv.Value.Contains(t.Epc));
            if (!string.IsNullOrEmpty(other.Key) && !string.Equals(other.Key, g.Supplier, StringComparison.OrdinalIgnoreCase))
            { _assignNote = $"{t.TubId} is already assigned to {other.Key}."; Replan(); return; }
            if (!_asg.TryGetValue(g.Supplier, out var list)) { list = new List<string>(); _asg[g.Supplier] = list; }
            if (!list.Contains(t.Epc)) list.Add(t.Epc);
            _assignNote = string.Empty;
            Replan();
        }

        private void AutoAssign(AssignGroup g)
        {
            var used = _asg.Values.SelectMany(x => x).ToHashSet();
            var free = _tubs.Where(t => Eligible(t) && !used.Contains(t.Epc)).OrderBy(t => t.UsedCycles).ToList();
            if (!_asg.TryGetValue(g.Supplier, out var list)) { list = new List<string>(); _asg[g.Supplier] = list; }
            int freeAv = _tubs.Count(Eligible);
            int need = Math.Max(0, Math.Max(2, (int)Math.Round(freeAv * 0.5)) - list.Count);
            if (free.Count == 0) { _assignNote = "No free tub to assign."; Replan(); return; }
            foreach (var t in free.Take(need)) list.Add(t.Epc);
            _assignNote = string.Empty;
            Replan();
        }

        public async Task LoadAsync()
        {
            try
            {
                await new FusionDatabaseInit(AppConfigurationManager.GetDatabasePath()).EnsureCreatedAsync();
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                _expiredBelowDays = (await repo.GetConditionThresholdsAsync()).ExpiredBelowDays;
                _suppliers = await repo.GetSuppliersAsync();
                _inward = await repo.GetInwardAsync(2000);
                _tubs = await repo.GetTubsAsync();

                var active = _suppliers.Where(s => s.IsActive).Select(s => s.Name).ToList();
                SupplierNames.Clear();
                foreach (var n in active) SupplierNames.Add(n);
                if (string.IsNullOrEmpty(SingleSupplier) && active.Count > 0)
                    SingleSupplier = active[0];

                var batches = await repo.GetBatchesAsync();
                Batches.Clear();
                foreach (var b in batches) Batches.Add(b);

                await RefreshBatchNoAsync();
                Replan();
                await LoadSavedLotsAsync();
                StatusText = $"{batches.Count} saved batch(es). Orange = first supplier, blue = second (FEFO).";
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        private async Task RefreshBatchNoAsync()
        {
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                string part = (_batchDate ?? DateTime.Today).ToString("yyyyMMdd");
                int seq = await repo.GetBatchSequenceAsync(part);
                BatchNo = $"BATCH-{part}-{seq:D3}";
            }
            catch { BatchNo = $"BATCH-{(_batchDate ?? DateTime.Today):yyyyMMdd}-001"; }
        }

        private async Task LoadSavedLotsAsync()
        {
            SavedLots.Clear();
            if (SelectedBatch == null) return;
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                foreach (var l in await repo.GetPlanLotsAsync(SelectedBatch.BatchNo)) SavedLots.Add(l);
            }
            catch { }
        }

        public async Task CreateBatchAsync()
        {
            if (string.IsNullOrWhiteSpace(BatchNo)) return;
            try
            {
                double.TryParse(TargetWeight, out var tw);
                double.TryParse(TargetApw, out var ta);
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var batch = new FusionBatch
                {
                    BatchNo = BatchNo.Trim(),
                    TargetKg = tw,
                    BatchDate = (_batchDate ?? DateTime.Today).ToString("yyyy-MM-dd"),
                    SupplierMode = IsSingle ? "single" : "auto",
                    SupplierName = IsSingle ? SingleSupplier : string.Empty,
                    TubMode = IsVirtual ? "virtual" : "colour",
                    LotMode = IsManual ? "manual" : (LotMode.StartsWith("Auto FEFO, first", StringComparison.Ordinal) ? "first" : "all"),
                    TargetApwKg = ta,
                    NutsPerTub = NumInt(NutsPerTub, 50),
                    TargetHours = Num(TargetHours, 16),
                    TubLoopMin = Num(LoopMin, 10),
                };
                var lots = PlanLots.Select(l => { l.BatchNo = batch.BatchNo; return l; }).ToList();
                batch.TargetTubs = lots.Sum(l => l.Tubs);
                batch.TargetNuts = lots.Sum(l => l.Nuts);
                await repo.CreateBatchFullAsync(batch);
                await repo.SavePlanLotsAsync(batch.BatchNo, lots);
                await repo.AddBatchEventAsync(batch.BatchNo, "Batch Created");
                foreach (var l in lots)
                    await repo.AddBatchEventAsync(batch.BatchNo, "Lot Created");
                await repo.AddBatchEventAsync(batch.BatchNo, "Running");
                int assigned = await AssignTubsToBatchAsync(repo, batch.BatchNo, lots);
                await _audit.LogFusionEventAsync(_username, "Fusion_Batch_Created",
                    "FusionBatch", batch.BatchNo,
                    $"Batch {batch.BatchNo} created: target {batch.TargetKg:0.###} kg, {lots.Count} lot(s), " +
                    $"{batch.TargetTubs} target tub(s), {assigned} tub(s) assigned; supplier mode {batch.SupplierMode}, " +
                    $"tub mode {batch.TubMode}, lot mode {batch.LotMode}.");
                // Create-and-start needs a live scale: auto-connect when not already reading.
                bool scaleReady = await ScaleConnectionService.EnsureConnectedAsync();
                StatusText = scaleReady
                    ? $"Created batch {batch.BatchNo} with {lots.Count} lot(s). Scale connected."
                    : $"Created batch {batch.BatchNo} with {lots.Count} lot(s). Scale not connected.";
                _asg.Clear();
                _assignNote = string.Empty;
                await LoadAsync();
                // Create-and-start: switch to the live view with the new batch selected.
                FusionNavigation.RequestShowModule("LiveView", batch.BatchNo);
            }
            catch (Exception ex)
            {
                StatusText = $"Create failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Create Fusion batch", ex);
            }
        }

        // Hold tubs for the running batch (persisted assigned_to; Eligible() excludes them elsewhere).
        private async Task<int> AssignTubsToBatchAsync(FusionRepository repo, string batchNo, List<FusionPlanLot> lots)
        {
            var toAssign = new List<string>();
            if (IsVirtual)
            {
                foreach (var epc in _asg.Values.SelectMany(x => x))
                {
                    var t = _tubs.FirstOrDefault(x => string.Equals(x.Epc, epc, StringComparison.OrdinalIgnoreCase));
                    if (t != null && Eligible(t)) toAssign.Add(t.TubId);
                }
            }
            else
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in lots.Select(l => l.SupplierName).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var colour = lots.First(l => string.Equals(l.SupplierName, s, StringComparison.OrdinalIgnoreCase)).Colour;
                    int planned = lots.Where(l => string.Equals(l.SupplierName, s, StringComparison.OrdinalIgnoreCase)).Sum(l => l.Tubs);
                    var pool = _tubs
                        .Where(t => Eligible(t) && !used.Contains(t.TubId)
                            && (string.Equals(colour, "Mixed", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(t.Colour, colour, StringComparison.OrdinalIgnoreCase)))
                        .OrderBy(t => t.UsedCycles)
                        .Select(t => t.TubId)
                        .Take(Math.Max(0, Math.Max(2, planned)))
                        .ToList();
                    foreach (var id in pool) used.Add(id);
                    toAssign.AddRange(pool);
                }
            }
            await repo.SetTubsAssignedAsync(toAssign, batchNo);
            return toAssign.Count;
        }

        public async Task CloseBatchAsync()
        {
            if (SelectedBatch == null)
            {
                StatusText = "Select a saved batch first.";
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.CloseBatchAsync(SelectedBatch.BatchNo);
                await _audit.LogFusionEventAsync(_username, "Fusion_Batch_Closed",
                    "FusionBatch", SelectedBatch.BatchNo,
                    $"Batch {SelectedBatch.BatchNo} closed.");
                StatusText = $"Closed batch {SelectedBatch.BatchNo}.";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Close failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Close Fusion batch", ex);
            }
        }
    }
}
