// Fusion inward entry + stock register (mirrors fusion_link_v3_ui.html tab t2).
// Ticket/bay/count captured at the gate; APW, best-before, GRN and coconut
// condition are calculated. fusion_inward (+ fusion_suppliers for names) only.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Services;

using ProWeighSuitePlatforms.Models;
namespace ProWeighSuitePlatforms.ViewModels
{
    public sealed class InwardStockViewModel : ViewModels.BaseViewModel
    {
        public string Title => "Inward";

        private readonly AuditService _audit = new();
        private readonly string _username;

        public ObservableCollection<string> SupplierNames { get; } = new();
        public ObservableCollection<string> MaterialNames { get; } = new();
        public ObservableCollection<string> BayNames { get; } = new();
        public ObservableCollection<FusionInwardRow> Rows { get; } = new();

        // Supplier-master parity: search + paging over the inward register.
        public ObservableCollection<FusionInwardRow> PagedRows { get; } = new();
        public ObservableCollection<PageNumberItem> PageNumbers { get; } = new();

        private string _filterText = string.Empty;
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    CurrentPage = 1;
                    UpdatePagedData();
                }
            }
        }

        private int _pageSize = 10;
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (SetProperty(ref _pageSize, value))
                {
                    CurrentPage = 1;
                    UpdatePagedData();
                }
            }
        }

        private int _currentPage = 1;
        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    UpdatePagedData();
                    RaisePropertyChanged(nameof(TotalPages));
                    RaisePropertyChanged(nameof(PageInfo));
                }
            }
        }

        public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)FilteredRows.Count / Math.Max(1, PageSize)));

        private int _totalRecords;
        public int TotalRecords
        {
            get => _totalRecords;
            private set => SetProperty(ref _totalRecords, value);
        }

        public string PageInfo => $"Page {CurrentPage} of {TotalPages}  ({TotalRecords} records)";

        public string ShowingText
        {
            get
            {
                if (TotalRecords == 0) return "Showing 0 to 0 of 0 entries";
                var from = (CurrentPage - 1) * PageSize + 1;
                var to = Math.Min(CurrentPage * PageSize, TotalRecords);
                return $"Showing {from} to {to} of {TotalRecords} entries";
            }
        }

        private List<FusionInwardRow> FilteredRows
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FilterText))
                    return Rows.ToList();
                var search = FilterText.Trim();
                return Rows.Where(r => RowMatches(r, search)).ToList();
            }
        }

        private static bool RowMatches(FusionInwardRow r, string search)
        {
            return (r.GrnNo?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.SupplierName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.MaterialName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.BayId?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.TicketNo?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.Condition?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        private int _nextGrn = 1;
        private int _nearExpiryDays = FusionSettingsDefaults.NearExpiryDays;
        private int _expiredBelowDays = FusionSettingsDefaults.ExpiredBelowDays;
        private List<FusionSupplier> _suppliers = new();
        private readonly Dictionary<string, int> _materialShelf = new(StringComparer.OrdinalIgnoreCase);

        private string _ticketNo = string.Empty;
        public string TicketNo { get => _ticketNo; set { if (SetProperty(ref _ticketNo, value)) ValidateTicketNo(); } }
        private DateTime? _inwardDate = DateTime.Today;
        public DateTime? InwardDate { get => _inwardDate; set { if (SetProperty(ref _inwardDate, value)) { ValidateInwardDate(); Recalc(); } } }
        private string _supplierName = string.Empty;
        public string SupplierName
        {
            get => _supplierName;
            set
            {
                if (SetProperty(ref _supplierName, value))
                {
                    ValidateSupplierName();
                    Recalc();
                }
            }
        }
        private string _bayId = string.Empty;
        public string BayId { get => _bayId; set { if (SetProperty(ref _bayId, value)) ValidateBayId(); } }
        private string _materialName = string.Empty;
        public string MaterialName
        {
            get => _materialName;
            set
            {
                if (SetProperty(ref _materialName, value))
                {
                    // Shelf life comes from the materials master (shelf_life column).
                    if (!string.IsNullOrWhiteSpace(value) && _materialShelf.TryGetValue(value.Trim(), out var shelf) && shelf > 0)
                        ShelfLifeText = shelf.ToString();
                    ValidateMaterialName();
                    Recalc();
                }
            }
        }
        private string _shelfLifeText = string.Empty;
        public string ShelfLifeText { get => _shelfLifeText; set { if (SetProperty(ref _shelfLifeText, value)) Recalc(); } }
        private string _weightKgText = string.Empty;
        public string WeightKgText { get => _weightKgText; set { if (SetProperty(ref _weightKgText, value)) { ValidateWeightKgText(); Recalc(); } } }
        private string _countText = string.Empty;
        public string CountText { get => _countText; set { if (SetProperty(ref _countText, value)) { ValidateCountText(); Recalc(); } } }

        private string? _ticketNoError;
        public string? TicketNoError { get => _ticketNoError; private set => SetProperty(ref _ticketNoError, value); }
        private string? _inwardDateError;
        public string? InwardDateError { get => _inwardDateError; private set => SetProperty(ref _inwardDateError, value); }
        private string? _supplierNameError;
        public string? SupplierNameError { get => _supplierNameError; private set => SetProperty(ref _supplierNameError, value); }
        private string? _bayIdError;
        public string? BayIdError { get => _bayIdError; private set => SetProperty(ref _bayIdError, value); }
        private string? _materialNameError;
        public string? MaterialNameError { get => _materialNameError; private set => SetProperty(ref _materialNameError, value); }
        private string? _weightKgTextError;
        public string? WeightKgTextError { get => _weightKgTextError; private set => SetProperty(ref _weightKgTextError, value); }
        private string? _countTextError;
        public string? CountTextError { get => _countTextError; private set => SetProperty(ref _countTextError, value); }

        public bool HasFormErrors => !string.IsNullOrEmpty(TicketNoError)
            || !string.IsNullOrEmpty(InwardDateError)
            || !string.IsNullOrEmpty(SupplierNameError)
            || !string.IsNullOrEmpty(BayIdError)
            || !string.IsNullOrEmpty(MaterialNameError)
            || !string.IsNullOrEmpty(WeightKgTextError)
            || !string.IsNullOrEmpty(CountTextError);

        // Edit state: Edit fills the entry form above and the submit button flips to "Update inward".
        // GRN is the PK, so it stays locked while editing.
        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            private set
            {
                if (SetProperty(ref _isEditing, value))
                {
                    RaisePropertyChanged(nameof(FormTitle));
                    RaisePropertyChanged(nameof(SubmitButtonText));
                }
            }
        }

        private string _editingGrnNo = string.Empty;
        public string EditingGrnNo { get => _editingGrnNo; private set => SetProperty(ref _editingGrnNo, value); }

        private bool _isFormExpanded = true;
        public bool IsFormExpanded
        {
            get => _isFormExpanded;
            set => SetProperty(ref _isFormExpanded, value);
        }

        public string FormTitle => IsEditing
            ? $"Edit inward entry ({EditingGrnNo})"
            : "New inward entry (from main-gate weighbridge ticket)";
        public string SubmitButtonText => IsEditing ? "Update inward" : "Save inward";

        private string _apwText = string.Empty;
        public string ApwText { get => _apwText; private set => SetProperty(ref _apwText, value); }
        private string _bestBeforeText = string.Empty;
        public string BestBeforeText { get => _bestBeforeText; private set => SetProperty(ref _bestBeforeText, value); }
        private string _nextGrnText = string.Empty;
        public string NextGrnText { get => _nextGrnText; private set => SetProperty(ref _nextGrnText, value); }
        private string _conditionText = string.Empty;
        public string ConditionText { get => _conditionText; private set => SetProperty(ref _conditionText, value); }

        private string _kpiStock = "-";
        public string KpiStock { get => _kpiStock; private set => SetProperty(ref _kpiStock, value); }
        private string _kpiNuts = "-";
        public string KpiNuts { get => _kpiNuts; private set => SetProperty(ref _kpiNuts, value); }
        private string _kpiUsable = "-";
        public string KpiUsable { get => _kpiUsable; private set => SetProperty(ref _kpiUsable, value); }
        private string _kpiExpired = "-";
        public string KpiExpired { get => _kpiExpired; private set => SetProperty(ref _kpiExpired, value); }

        private string _statusText = string.Empty;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        // Legend labels reflecting the active Fusion Settings thresholds.
        public string LegendFreshText => $"Fresh (> {_nearExpiryDays} days)";
        public string LegendNearText => $"Near Expiry ({_expiredBelowDays}–{_nearExpiryDays} days)";
        public string LegendExpiredText => $"Expired (< {_expiredBelowDays} days)";

        public ICommand RefreshCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ToggleFormCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand GoToPageCommand { get; }

        public InwardStockViewModel(string username = "System")
        {
            _username = string.IsNullOrWhiteSpace(username) ? "System" : username;
            RefreshCommand = new ViewModels.RelayCommand(async _ => await LoadAsync());
            SaveCommand = new ViewModels.RelayCommand(async _ => await AddOrUpdateAsync());
            ToggleFormCommand = new ViewModels.RelayCommand(_ => IsFormExpanded = !IsFormExpanded);
            CancelEditCommand = new ViewModels.RelayCommand(_ => CancelEdit());
            PrevPageCommand = new ViewModels.RelayCommand(_ => { if (CurrentPage > 1) CurrentPage--; }, _ => CurrentPage > 1);
            NextPageCommand = new ViewModels.RelayCommand(_ => { if (CurrentPage < TotalPages) CurrentPage++; }, _ => CurrentPage < TotalPages);
            GoToPageCommand = new ViewModels.RelayCommand(param =>
            {
                if (param is int page && page >= 1 && page <= Math.Max(1, TotalPages) && page != CurrentPage)
                    CurrentPage = page;
            });
            _ = LoadAsync();
        }

        internal static FusionInwardRow ToRow(FusionInward g,
            int nearExpiryDays = FusionSettingsDefaults.NearExpiryDays,
            int expiredBelowDays = FusionSettingsDefaults.ExpiredBelowDays)
        {
            string date = !string.IsNullOrEmpty(g.InwardDate) ? g.InwardDate
                : (g.ReceivedAt.Length >= 10 ? g.ReceivedAt[..10] : DateTime.Today.ToString("yyyy-MM-dd"));
            double apw = g.CoconutCount > 0 ? g.WeightKg / g.CoconutCount : 0;
            var bb = FusionConditions.BestBefore(date, g.ShelfLifeDays);
            int left = (bb.Date - DateTime.Today).Days;
            double q = g.SampleKg + g.SpoilKg + g.WaterLossKg + g.BrokenKg + g.RejectionKg;
            double avail = g.WeightKg - q;
            double availNuts = (g.WeightKg > 0 && g.CoconutCount > 0)
                ? Math.Round(avail * g.CoconutCount / g.WeightKg) - g.ConsumedNuts : 0;
            return new FusionInwardRow
            {
                GrnNo = g.GrnNo,
                InwardDate = date,
                TicketNo = string.IsNullOrEmpty(g.TicketNo) ? "-" : g.TicketNo,
                SupplierName = g.SupplierName,
                BayId = g.BayId,
                MaterialName = g.MaterialName,
                ShelfLifeDays = g.ShelfLifeDays,
                BestBefore = bb.ToString("yyyy-MM-dd"),
                DaysLeft = left,
                WeightKg = g.WeightKg,
                CoconutCount = g.CoconutCount,
                ApwKg = apw,
                BalanceKg = avail - g.ConsumedKg,
                BalanceNuts = availNuts,
                Condition = FusionConditions.ForStock(availNuts, left, nearExpiryDays, expiredBelowDays),
            };
        }

        private void Recalc()
        {
            double.TryParse(WeightKgText, out var w);
            double.TryParse(CountText, out var c);
            int.TryParse(ShelfLifeText, out var sl);
            if (sl <= 0) sl = 0;
            ApwText = (w > 0 && c > 0) ? (w / c).ToString("F3") : string.Empty;
            var d = InwardDate ?? DateTime.Today;
            var bb = d.AddDays(sl);
            BestBeforeText = bb.ToString("yyyy-MM-dd");
            NextGrnText = IsEditing ? EditingGrnNo : $"GRN-{_nextGrn}";
            int left = (bb.Date - DateTime.Today).Days;
            ConditionText = FusionConditions.ForPreview(left, _nearExpiryDays, _expiredBelowDays);
        }

        internal static string? ValidateTicketNo(string? ticket)
        {
            var v = (ticket ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(v))
                return "Ticket no is required.";
            if (v.Length > 30)
                return "Ticket no must not exceed 30 characters.";
            return null;
        }

        internal static string? ValidateInwardDate(DateTime? date)
        {
            if (!date.HasValue)
                return "Inward date is required.";
            if (date.Value.Date > DateTime.Today)
                return "Inward date cannot be in the future.";
            return null;
        }

        internal static string? ValidateSupplierName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Supplier is required.";
            return null;
        }

        internal static string? ValidateMaterialName(string? name, bool required)
        {
            if (!required)
                return null;
            if (string.IsNullOrWhiteSpace(name))
                return "Material is required.";
            return null;
        }

        internal static string? ValidateBayId(string? bay)
        {
            var v = (bay ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(v))
                return "Bay is required.";
            if (v.Length > 50)
                return "Bay must not exceed 50 characters.";
            return null;
        }

        internal static bool TryParsePositive(string? text, out double value)
        {
            value = 0;
            var v = (text ?? string.Empty).Trim();
            if (!double.TryParse(v, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
                && !double.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
                return false;
            return value > 0;
        }

        internal static string? ValidateWeight(string? text)
        {
            var v = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(v))
                return "Weight is required.";
            if (!TryParsePositive(v, out var w))
                return "Weight must be a positive number.";
            if (w > 1000000)
                return "Weight must not exceed 1000000 kg.";
            return null;
        }

        internal static bool TryParseCount(string? text, out long count)
        {
            count = 0;
            var v = (text ?? string.Empty).Trim();
            if (!long.TryParse(v, NumberStyles.Number, CultureInfo.CurrentCulture, out count)
                && !long.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out count))
                return false;
            return count > 0;
        }

        internal static string? ValidateCount(string? text)
        {
            var v = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(v))
                return "Coconut count is required.";
            if (!TryParseCount(v, out var c))
                return "Coconut count must be a whole number greater than 0.";
            if (c > 100000000)
                return "Coconut count must not exceed 100000000.";
            return null;
        }

        private void ValidateTicketNo() => TicketNoError = ValidateTicketNo(TicketNo);
        private void ValidateInwardDate() => InwardDateError = ValidateInwardDate(InwardDate);

        private void ValidateSupplierName()
        {
            SupplierNameError = ValidateSupplierName(SupplierName);
            if (SupplierNameError == null && SupplierNames.Count > 0
                && !SupplierNames.Any(n => string.Equals(n, (SupplierName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)))
                SupplierNameError = "Select an active supplier.";
        }

        private void ValidateMaterialName() => MaterialNameError = ValidateMaterialName(MaterialName, MaterialNames.Count > 0);
        private void ValidateBayId()
        {
            BayIdError = ValidateBayId(BayId);
            if (BayIdError == null && BayNames.Count > 0
                && !BayNames.Any(n => string.Equals(n, (BayId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)))
                BayIdError = "Select an active bay.";
        }
        private void ValidateWeightKgText() => WeightKgTextError = ValidateWeight(WeightKgText);
        private void ValidateCountText() => CountTextError = ValidateCount(CountText);

        private void ValidateForm()
        {
            ValidateTicketNo();
            ValidateInwardDate();
            ValidateSupplierName();
            ValidateMaterialName();
            ValidateBayId();
            ValidateWeightKgText();
            ValidateCountText();
        }

        private string? FirstFormError() =>
            !string.IsNullOrEmpty(TicketNoError) ? TicketNoError
            : !string.IsNullOrEmpty(InwardDateError) ? InwardDateError
            : !string.IsNullOrEmpty(SupplierNameError) ? SupplierNameError
            : !string.IsNullOrEmpty(MaterialNameError) ? MaterialNameError
            : !string.IsNullOrEmpty(BayIdError) ? BayIdError
            : !string.IsNullOrEmpty(WeightKgTextError) ? WeightKgTextError
            : !string.IsNullOrEmpty(CountTextError) ? CountTextError
            : null;

        public async Task LoadAsync()
        {
            try
            {
                await new FusionDatabaseInit(AppConfigurationManager.GetDatabasePath()).EnsureCreatedAsync();
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                (_nearExpiryDays, _expiredBelowDays) = await repo.GetConditionThresholdsAsync();
                _suppliers = await repo.GetSuppliersAsync();
                var active = _suppliers.Where(s => s.IsActive).Select(s => s.Name).ToList();
                SupplierNames.Clear();
                foreach (var n in active) SupplierNames.Add(n);
                if (!string.IsNullOrEmpty(SupplierName) && !active.Contains(SupplierName))
                    SupplierName = string.Empty;
                if (string.IsNullOrEmpty(SupplierName) && active.Count > 0)
                    SupplierName = active[0];

                // Bay dropdown shows active bays only (same rule as suppliers).
                var bays = await repo.GetBaysAsync();
                var activeBays = bays
                    .Where(b => b.IsActive && !string.IsNullOrWhiteSpace(b.BayId))
                    .Select(b => b.BayId.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                BayNames.Clear();
                foreach (var n in activeBays) BayNames.Add(n);

                var materials = await repo.GetActiveMaterialsAsync();
                _materialShelf.Clear();
                foreach (var m in materials) _materialShelf[m.Name] = m.ShelfLife;
                var names = materials.Select(m => m.Name).ToList();
                MaterialNames.Clear();
                foreach (var n in names) MaterialNames.Add(n);
                if (!string.IsNullOrEmpty(MaterialName) && !_materialShelf.ContainsKey(MaterialName))
                    MaterialName = string.Empty;
                if (string.IsNullOrEmpty(MaterialName) && names.Count > 0)
                {
                    // Default to first material so shelf life auto-fills from master.
                    _materialName = names[0];
                    RaisePropertyChanged(nameof(MaterialName));
                    if (_materialShelf.TryGetValue(names[0], out var shelf) && shelf > 0)
                        _shelfLifeText = shelf.ToString();
                    RaisePropertyChanged(nameof(ShelfLifeText));
                }
                else if (!string.IsNullOrEmpty(MaterialName)
                    && _materialShelf.TryGetValue(MaterialName, out var cur) && cur > 0
                    && (!int.TryParse(ShelfLifeText, out var existing) || existing <= 0))
                {
                    ShelfLifeText = cur.ToString();
                }

                _nextGrn = await repo.GetInwardSequenceAsync();

                var inwardList = await repo.GetInwardAsync(2000);
                var rows = inwardList.Select(g => ToRow(g, _nearExpiryDays, _expiredBelowDays)).ToList();
                Rows.Clear();
                foreach (var r in rows) Rows.Add(r);

                if (CurrentPage > Math.Max(1, (int)Math.Ceiling((double)Rows.Count / Math.Max(1, PageSize))))
                    _currentPage = 1;
                UpdatePagedData();

                KpiStock = $"{rows.Sum(r => r.BalanceKg):N0} kg";
                KpiNuts = $"{rows.Sum(r => r.BalanceNuts):N0}";
                KpiUsable = $"{rows.Where(r => r.Condition != "Expired").Sum(r => r.BalanceNuts):N0}";
                KpiExpired = $"{rows.Where(r => r.Condition == "Expired").Sum(r => r.BalanceNuts):N0}";
                StatusText = $"{rows.Count} inward record(s). < {_expiredBelowDays} Expired · {_expiredBelowDays}–{_nearExpiryDays} Near Expiry · > {_nearExpiryDays} Fresh.";
                RaisePropertyChanged(nameof(LegendFreshText));
                RaisePropertyChanged(nameof(LegendNearText));
                RaisePropertyChanged(nameof(LegendExpiredText));
                Recalc();
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        public async Task SaveAsync()
        {
            ValidateForm();
            if (HasFormErrors)
            {
                StatusText = FirstFormError() ?? "Please fix validation errors before saving.";
                return;
            }
            var supplier = (SupplierName ?? string.Empty).Trim();
            var material = (MaterialName ?? string.Empty).Trim();
            var bay = (BayId ?? string.Empty).Trim();
            TryParsePositive(WeightKgText, out var w);
            TryParseCount(CountText, out var c);
            // Shelf life always comes from the materials master when a material is chosen.
            int sl;
            if (!string.IsNullOrWhiteSpace(MaterialName) && _materialShelf.TryGetValue(MaterialName.Trim(), out var masterShelf) && masterShelf > 0)
                sl = masterShelf;
            else if (!int.TryParse(ShelfLifeText, out sl) || sl <= 0) sl = 0;
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var ticket = (TicketNo ?? string.Empty).Trim();
                if (await repo.InwardTicketExistsAsync(ticket))
                {
                    TicketNoError = $"Ticket no {ticket} is already used by another inward entry.";
                    StatusText = TicketNoError;
                    return;
                }
                var grn = await repo.GetNextInwardGrnAsync();
                var d = (InwardDate ?? DateTime.Today).ToString("yyyy-MM-dd");
                // supplier_id references the shared suppliers table (integer PK);
                // resolve it from the chosen supplier name (mirrors the fusion
                // supplier into the shared table when needed) instead of 0.
                var supplierId = await repo.ResolveSupplierIdByNameAsync(supplier);
                var inward = new FusionInward
                {
                    GrnNo = grn,
                    SupplierId = supplierId,
                    SupplierName = supplier,
                    TicketNo = (TicketNo ?? string.Empty).Trim(),
                    InwardDate = d,
                    ReceivedAt = d,
                    BayId = bay,
                    MaterialName = material,
                    ShelfLifeDays = sl,
                    WeightKg = w,
                    CoconutCount = c,
                };
                await repo.AddInwardFullAsync(inward);
                await _audit.LogFusionEventAsync(_username, "Fusion_Inward_Created",
                    "FusionInward", grn,
                    $"Inward {grn} created: supplier {supplier}, material {material}, bay {bay}, " +
                    $"ticket {inward.TicketNo}, {w:0.###} kg / {c:0} nuts, shelf life {sl} day(s).",
                    AuditCategory.Transaction, AuditSeverity.Info,
                    null, $"GRN={grn}; Supplier={supplier}; Material={material}; Bay={bay}; WeightKg={w}; Nuts={c}; ShelfLifeDays={sl}");
                StatusText = $"{grn} saved to {supplier}'s account, bay {bay}, APW {(w / c):F3}.";
                TicketNo = BayId = WeightKgText = CountText = string.Empty;
                // Clearing fires live validation — reset errors so a fresh form shows no red messages.
                TicketNoError = BayIdError = WeightKgTextError = CountTextError = null;
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Create Fusion inward", ex);
            }
        }

        public async Task AddOrUpdateAsync()
        {
            if (IsEditing)
                await SaveEditAsync();
            else
                await SaveAsync();
        }

        public void BeginEdit(FusionInwardRow row)
        {
            if (row == null) return;
            EditingGrnNo = row.GrnNo ?? string.Empty;
            TicketNo = row.TicketNo == "-" ? string.Empty : (row.TicketNo ?? string.Empty);
            if (DateTime.TryParse(row.InwardDate, out var d))
                InwardDate = d;
            else
                InwardDate = DateTime.Today;
            SupplierName = row.SupplierName ?? string.Empty;
            MaterialName = row.MaterialName ?? string.Empty;
            // Keep the stored shelf life (master may have changed since entry).
            ShelfLifeText = row.ShelfLifeDays.ToString();
            BayId = row.BayId ?? string.Empty;
            WeightKgText = row.WeightKg.ToString(CultureInfo.InvariantCulture);
            CountText = row.CoconutCount.ToString("F0", CultureInfo.InvariantCulture);
            TicketNoError = InwardDateError = SupplierNameError = MaterialNameError
                = BayIdError = WeightKgTextError = CountTextError = null;
            IsEditing = true;
            IsFormExpanded = true;
            Recalc();
            StatusText = $"Editing {EditingGrnNo}. Update the details above and click Update inward.";
        }

        public void CancelEdit()
        {
            IsEditing = false;
            EditingGrnNo = string.Empty;
            TicketNo = BayId = WeightKgText = CountText = string.Empty;
            TicketNoError = InwardDateError = SupplierNameError = MaterialNameError
                = BayIdError = WeightKgTextError = CountTextError = null;
            Recalc();
        }

        public async Task SaveEditAsync()
        {
            ValidateForm();
            if (HasFormErrors)
            {
                StatusText = FirstFormError() ?? "Please fix validation errors before updating.";
                return;
            }
            var supplier = (SupplierName ?? string.Empty).Trim();
            var material = (MaterialName ?? string.Empty).Trim();
            var bay = (BayId ?? string.Empty).Trim();
            TryParsePositive(WeightKgText, out var w);
            TryParseCount(CountText, out var c);
            int sl;
            if (!string.IsNullOrWhiteSpace(material) && _materialShelf.TryGetValue(material, out var masterShelf) && masterShelf > 0)
                sl = masterShelf;
            else if (!int.TryParse(ShelfLifeText, out sl) || sl <= 0) sl = 0;
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var ticket = (TicketNo ?? string.Empty).Trim();
                if (await repo.InwardTicketExistsAsync(ticket, EditingGrnNo))
                {
                    TicketNoError = $"Ticket no {ticket} is already used by another inward entry.";
                    StatusText = TicketNoError;
                    return;
                }
                var d = (InwardDate ?? DateTime.Today).ToString("yyyy-MM-dd");
                // Resolve the shared suppliers.id from the name (never reset the
                // column back to 0 on edit).
                var supplierId = await repo.ResolveSupplierIdByNameAsync(supplier);
                var old = await repo.GetInwardByGrnAsync(EditingGrnNo);
                var inward = new FusionInward
                {
                    GrnNo = EditingGrnNo,
                    SupplierId = supplierId,
                    TicketNo = ticket,
                    InwardDate = d,
                    ReceivedAt = d,
                    SupplierName = supplier,
                    BayId = bay,
                    MaterialName = material,
                    ShelfLifeDays = sl,
                    WeightKg = w,
                    CoconutCount = c,
                };
                await repo.UpdateInwardFullAsync(inward);
                await _audit.LogFusionEventAsync(_username, "Fusion_Inward_Updated",
                    "FusionInward", EditingGrnNo,
                    $"Inward {EditingGrnNo} updated: supplier {supplier}, material {material}, bay {bay}, " +
                    $"ticket {ticket}, {w:0.###} kg / {c:0} nuts, shelf life {sl} day(s).",
                    AuditCategory.Transaction, AuditSeverity.Info,
                    old is null ? null : $"Supplier={old.SupplierName}; Material={old.MaterialName}; Bay={old.BayId}; WeightKg={old.WeightKg}; Nuts={old.CoconutCount}; ShelfLifeDays={old.ShelfLifeDays}",
                    $"Supplier={supplier}; Material={material}; Bay={bay}; WeightKg={w}; Nuts={c}; ShelfLifeDays={sl}");
                StatusText = $"Updated {EditingGrnNo}.";
                CancelEdit();
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Update Fusion inward", ex);
            }
        }

        private void UpdatePagedData()
        {
            var filtered = FilteredRows;
            TotalRecords = filtered.Count;
            if (CurrentPage < 1) _currentPage = 1;
            var pages = Math.Max(1, (int)Math.Ceiling((double)filtered.Count / Math.Max(1, PageSize)));
            if (_currentPage > pages) _currentPage = pages;
            var paged = filtered
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();
            PagedRows.Clear();
            foreach (var r in paged)
                PagedRows.Add(r);

            RefreshPageNumbers();
            RaisePropertyChanged(nameof(CurrentPage));
            RaisePropertyChanged(nameof(TotalPages));
            RaisePropertyChanged(nameof(PageInfo));
            RaisePropertyChanged(nameof(ShowingText));
        }

        private void RefreshPageNumbers()
        {
            PageNumbers.Clear();
            var total = Math.Max(1, TotalPages);
            var start = Math.Max(1, Math.Min(CurrentPage - 2, total - 4));
            var end = Math.Min(total, start + 4);
            start = Math.Max(1, end - 4);
            for (var i = start; i <= end; i++)
                PageNumbers.Add(new PageNumberItem { Number = i, IsCurrent = i == CurrentPage });
        }

        public sealed class PageNumberItem
        {
            public int Number { get; set; }
            public bool IsCurrent { get; set; }
        }
    }
}
