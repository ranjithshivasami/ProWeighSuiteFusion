// Bay status over coconut store bays (mirrors the Bay status reference screen).
// Occupancy derives from fusion_inward balance nuts grouped by bay;
// capacities live in fusion_bays and are editable inline.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Services;

using ProWeighSuitePlatforms.Models;
namespace ProWeighSuitePlatforms.ViewModels
{
    public sealed class BayStatusRow
    {
        public string BayId { get; set; } = string.Empty;
        public double MaxCapacityNuts { get; set; }
        public string CapacityText { get; set; } = string.Empty;
        public double OccupiedNuts { get; set; }
        public double OccupiedPct { get; set; }
        public double AvailableNuts { get; set; }
        public string Status { get; set; } = "Available";
        public string GrnsDisplay { get; set; } = "–";
        public double FreshNuts { get; set; }
        public double NearNuts { get; set; }
        public double ExpiredNuts { get; set; }
        public bool IsActive { get; set; } = true;
        public string ActiveText => IsActive ? "Active" : "Inactive";
    }

    public sealed class BayStatusViewModel : ViewModels.BaseViewModel
    {
        private readonly AuditService _audit = new();
        private readonly string _username;
        public string Title => "Bay status";

        // Bay at or above this occupancy % counts as nearly full (full = 100%+).
        private const double NearlyFullPct = 90;
        // Default capacity assigned when a bay first appears in inward.
        private const double DefaultCapacityNuts = 250000;

        public ObservableCollection<BayStatusRow> Rows { get; } = new();

        private string _newBayId = string.Empty;
        public string NewBayId
        {
            get => _newBayId;
            set
            {
                if (SetProperty(ref _newBayId, value))
                    ValidateNewBayId();
            }
        }
        private string _newCapacityText = string.Empty;
        public string NewCapacityText
        {
            get => _newCapacityText;
            set
            {
                if (SetProperty(ref _newCapacityText, value))
                    ValidateNewCapacity();
            }
        }

        private string? _newBayIdError;
        public string? NewBayIdError { get => _newBayIdError; private set => SetProperty(ref _newBayIdError, value); }
        private string? _newCapacityError;
        public string? NewCapacityError { get => _newCapacityError; private set => SetProperty(ref _newCapacityError, value); }

        private string _kpiBays = "-";
        public string KpiBays { get => _kpiBays; private set => SetProperty(ref _kpiBays, value); }
        private string _kpiMaxCapacity = "-";
        public string KpiMaxCapacity { get => _kpiMaxCapacity; private set => SetProperty(ref _kpiMaxCapacity, value); }
        private string _kpiOccupied = "-";
        public string KpiOccupied { get => _kpiOccupied; private set => SetProperty(ref _kpiOccupied, value); }
        private string _kpiAvailable = "-";
        public string KpiAvailable { get => _kpiAvailable; private set => SetProperty(ref _kpiAvailable, value); }
        private string _kpiCongested = "-";
        public string KpiCongested { get => _kpiCongested; private set => SetProperty(ref _kpiCongested, value); }

        private string _statusText = string.Empty;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public void ShowStatus(string message)
        {
            StatusText = message ?? string.Empty;
        }

        // Edit state: Edit fills the Add form above (NewBayId / NewCapacityText)
        // and the submit button flips to "Update Bay". BayId is the PK, so a
        // rename moves fusion_inward history via FusionRepository.RenameBayAsync.
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

        private string _editingOriginalId = string.Empty;
        public string EditingOriginalId { get => _editingOriginalId; private set => SetProperty(ref _editingOriginalId, value); }

        public string FormTitle => IsEditing ? $"Edit bay {EditingOriginalId}" : "Add bay";
        public string SubmitButtonText => IsEditing ? "Update Bay" : "Add bay";

        private bool _isFormExpanded = true;
        public bool IsFormExpanded
        {
            get => _isFormExpanded;
            set => SetProperty(ref _isFormExpanded, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand AddBayCommand { get; }
        public ICommand ToggleActiveCommand { get; }
        public ICommand ToggleFormCommand { get; }
        public ICommand BeginEditCommand { get; }
        public ICommand CancelEditCommand { get; }

        public BayStatusViewModel(string username = "System")
        {
            _username = string.IsNullOrWhiteSpace(username) ? "System" : username;
            RefreshCommand = new ViewModels.RelayCommand(async _ => await LoadAsync());
            AddBayCommand = new ViewModels.RelayCommand(async _ => await AddOrUpdateBayAsync());
            ToggleFormCommand = new ViewModels.RelayCommand(_ => IsFormExpanded = !IsFormExpanded);
            ToggleActiveCommand = new ViewModels.RelayCommand(async p =>
            {
                if (p is BayStatusRow r) await ToggleActiveAsync(r);
            });
            BeginEditCommand = new ViewModels.RelayCommand(p =>
            {
                if (p is BayStatusRow r) BeginEdit(r);
            });
            CancelEditCommand = new ViewModels.RelayCommand(_ => CancelEdit());
            _ = LoadAsync();
        }

        internal static string StatusFor(double occupiedPct, double capacity)
        {
            if (capacity <= 0) return "No capacity";
            if (occupiedPct >= 100) return "Full";
            if (occupiedPct >= NearlyFullPct) return "Nearly full";
            return "Available";
        }

        public async Task LoadAsync()
        {
            try
            {
                await new FusionDatabaseInit(AppConfigurationManager.GetDatabasePath()).EnsureCreatedAsync();
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var (near, expiredBelow) = await repo.GetConditionThresholdsAsync();

                var inward = await repo.GetInwardAsync(2000);
                var stock = inward.Select(g => InwardStockViewModel.ToRow(g, near, expiredBelow)).ToList();
                await repo.EnsureBaysExistAsync(
                    stock.Select(r => r.BayId).Where(b => !string.IsNullOrWhiteSpace(b)),
                    DefaultCapacityNuts);
                var bays = await repo.GetBaysAsync();

                var byBay = stock.GroupBy(r => r.BayId.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
                var rows = new List<BayStatusRow>();
                foreach (var b in bays.OrderBy(b => b.BayId, StringComparer.OrdinalIgnoreCase))
                {
                    byBay.TryGetValue(b.BayId, out var list);
                    list ??= new List<FusionInwardRow>();
                    double occupied = list.Sum(r => Math.Max(0, r.BalanceNuts));
                    double pct = b.MaxCapacityNuts > 0 ? occupied / b.MaxCapacityNuts * 100 : 0;
                    var grns = list.Select(r => $"{r.GrnNo} {r.SupplierName}".Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s) && s != "-")
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    rows.Add(new BayStatusRow
                    {
                        BayId = b.BayId,
                        MaxCapacityNuts = b.MaxCapacityNuts,
                        CapacityText = b.MaxCapacityNuts.ToString("N0"),
                        OccupiedNuts = occupied,
                        OccupiedPct = pct,
                        AvailableNuts = Math.Max(0, b.MaxCapacityNuts - occupied),
                        Status = b.IsActive ? StatusFor(pct, b.MaxCapacityNuts) : "Inactive",
                        GrnsDisplay = grns.Count == 0 ? "–" : string.Join(", ", grns),
                        FreshNuts = list.Where(r => r.Condition == "Fresh").Sum(r => Math.Max(0, r.BalanceNuts)),
                        NearNuts = list.Where(r => r.Condition == "Near expiry").Sum(r => Math.Max(0, r.BalanceNuts)),
                        ExpiredNuts = list.Where(r => r.Condition == "Expired").Sum(r => Math.Max(0, r.BalanceNuts)),
                        IsActive = b.IsActive,
                    });
                }
                Rows.Clear();
                foreach (var r in rows) Rows.Add(r);

                double max = rows.Sum(r => r.MaxCapacityNuts);
                double occ = rows.Sum(r => r.OccupiedNuts);
                double avail = rows.Sum(r => r.AvailableNuts);
                double occPct = max > 0 ? occ / max * 100 : 0;
                int activeCount = rows.Count(r => r.IsActive);
                KpiBays = $"{activeCount} active";
                KpiMaxCapacity = $"{max:N0} nuts";
                KpiOccupied = $"{occ:N0} nuts · {occPct:N0}%";
                KpiAvailable = $"{avail:N0} nuts";
                KpiCongested = rows.Count(r => r.IsActive && (r.Status == "Nearly full" || r.Status == "Full")).ToString();
                StatusText = $"{rows.Count} bay(s), {activeCount} active. Nearly full at {NearlyFullPct:N0}% occupancy.";
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        public async Task AddOrUpdateBayAsync()
        {
            if (IsEditing)
                await SaveEditAsync();
            else
                await AddBayAsync();
        }

        public async Task AddBayAsync()
        {
            ValidateNewBayId();
            ValidateNewCapacity();
            if (!string.IsNullOrEmpty(NewBayIdError) || !string.IsNullOrEmpty(NewCapacityError))
            {
                StatusText = !string.IsNullOrEmpty(NewBayIdError)
                    ? NewBayIdError
                    : NewCapacityError ?? "Please fix validation errors before adding bay.";
                return;
            }

            var bayId = (NewBayId ?? string.Empty).Trim();
            var capText = (NewCapacityText ?? string.Empty).Trim();
            if (!double.TryParse(capText, NumberStyles.Number, CultureInfo.CurrentCulture, out var cap) || cap <= 0)
            {
                // Fallback for invariant separators (e.g. pasted "250,000").
                if (!double.TryParse(capText, NumberStyles.Number, CultureInfo.InvariantCulture, out cap) || cap <= 0)
                {
                    NewCapacityError = "Max capacity must be greater than 0.";
                    StatusText = NewCapacityError;
                    return;
                }
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var existing = await repo.GetBaysAsync();
                if (existing.Any(b => string.Equals(b.BayId, bayId, StringComparison.OrdinalIgnoreCase)))
                {
                    NewBayIdError = $"Bay {bayId} already exists. Use Edit to update it.";
                    StatusText = NewBayIdError;
                    return;
                }
                await repo.UpsertBayAsync(bayId, cap);
                await _audit.LogFusionEventAsync(_username, "Fusion_Bay_Created",
                    "FusionBay", bayId,
                    $"Bay {bayId} created with {cap:N0} nuts capacity.",
                    AuditCategory.Material, AuditSeverity.Info,
                    null, $"BayId={bayId}; MaxCapacityNuts={cap}");
                StatusText = $"Added bay {bayId} with {cap:N0} nuts capacity.";
                _newBayId = string.Empty;
                RaisePropertyChanged(nameof(NewBayId));
                _newCapacityText = string.Empty;
                RaisePropertyChanged(nameof(NewCapacityText));
                NewBayIdError = null;
                NewCapacityError = null;
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Add failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Add Fusion bay", ex);
            }
        }

        private void ValidateNewBayId()
        {
            var bayId = (NewBayId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(bayId))
                NewBayIdError = "Bay is required.";
            else if (bayId.Length < 3)
                NewBayIdError = "Bay must be at least 3 characters.";
            else if (bayId.Length > 50)
                NewBayIdError = "Bay must not exceed 50 characters.";
            else
                NewBayIdError = null;
        }

        private void ValidateNewCapacity()
        {
            var text = (NewCapacityText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                NewCapacityError = "Max capacity is required.";
                return;
            }
            if (!double.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var cap)
                && !double.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out cap))
            {
                NewCapacityError = "Max capacity must be a positive number.";
                return;
            }
            if (cap <= 0)
                NewCapacityError = "Max capacity must be greater than 0.";
            else
                NewCapacityError = null;
        }

        public async Task SaveCapacityAsync(BayStatusRow row, string text)
        {
            if (row == null) return;
            if (!double.TryParse((text ?? string.Empty).Trim(), out var cap) || cap < 0)
            {
                StatusText = $"Enter a valid capacity for {row.BayId}.";
                await LoadAsync();
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.UpsertBayAsync(row.BayId, cap);
                await _audit.LogFusionEventAsync(_username, "Fusion_Bay_Capacity_Updated",
                    "FusionBay", row.BayId,
                    $"Bay {row.BayId} capacity updated to {cap:N0} nuts.",
                    AuditCategory.Material, AuditSeverity.Info,
                    $"MaxCapacityNuts={row.MaxCapacityNuts}", $"MaxCapacityNuts={cap}");
                StatusText = $"Updated {row.BayId} capacity to {cap:N0} nuts.";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Update Fusion bay capacity", ex);
            }
        }

        public async Task ToggleActiveAsync(BayStatusRow row)
        {
            if (row == null) return;
            bool activating = !row.IsActive;
            var confirm = MessageBox.Show(
                activating
                    ? $"Are you sure you want to activate bay '{row.BayId}'?"
                    : $"Are you sure you want to deactivate bay '{row.BayId}'? Its history will be kept.",
                activating ? "Confirm Activate" : "Confirm Deactivate",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                StatusText = activating ? $"Activation of {row.BayId} cancelled." : $"Deactivation of {row.BayId} cancelled.";
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.SetBayActiveAsync(row.BayId, activating);
                await _audit.LogFusionEventAsync(_username,
                    activating ? "Fusion_Bay_Activated" : "Fusion_Bay_Deactivated",
                    "FusionBay", row.BayId,
                    $"Bay {row.BayId} {(activating ? "activated" : "deactivated")} (history kept).",
                    AuditCategory.Material, AuditSeverity.Info,
                    $"IsActive={!activating}", $"IsActive={activating}");
                StatusText = activating
                    ? $"Activated {row.BayId}."
                    : $"Deactivated {row.BayId} (history kept).";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Update failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Update Fusion bay status", ex);
            }
        }

        public void BeginEdit(BayStatusRow row)
        {
            if (row == null) return;
            EditingOriginalId = row.BayId;
            _newBayId = row.BayId;
            RaisePropertyChanged(nameof(NewBayId));
            _newCapacityText = row.MaxCapacityNuts.ToString("F0", CultureInfo.InvariantCulture);
            RaisePropertyChanged(nameof(NewCapacityText));
            NewBayIdError = null;
            NewCapacityError = null;
            IsEditing = true;
            IsFormExpanded = true;
            StatusText = $"Editing {row.BayId}. Update the details above and click Update Bay.";
        }

        public void CancelEdit()
        {
            IsEditing = false;
            EditingOriginalId = string.Empty;
            _newBayId = string.Empty;
            RaisePropertyChanged(nameof(NewBayId));
            _newCapacityText = DefaultCapacityNuts.ToString("F0", CultureInfo.InvariantCulture);
            RaisePropertyChanged(nameof(NewCapacityText));
            NewBayIdError = null;
            NewCapacityError = null;
        }

        public async Task SaveEditAsync()
        {
            ValidateNewBayId();
            ValidateNewCapacity();
            if (!string.IsNullOrEmpty(NewBayIdError) || !string.IsNullOrEmpty(NewCapacityError))
            {
                StatusText = !string.IsNullOrEmpty(NewBayIdError)
                    ? NewBayIdError
                    : NewCapacityError ?? "Please fix validation errors before updating.";
                return;
            }

            var newId = (NewBayId ?? string.Empty).Trim();
            var capText = (NewCapacityText ?? string.Empty).Trim();
            if (!double.TryParse(capText, NumberStyles.Number, CultureInfo.CurrentCulture, out var cap) || cap <= 0)
            {
                // Fallback for invariant separators (e.g. pasted "250,000").
                if (!double.TryParse(capText, NumberStyles.Number, CultureInfo.InvariantCulture, out cap) || cap <= 0)
                {
                    NewCapacityError = "Max capacity must be greater than 0.";
                    StatusText = NewCapacityError;
                    return;
                }
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.RenameBayAsync(EditingOriginalId, newId, cap);
                bool renamed = !string.Equals(EditingOriginalId, newId, StringComparison.OrdinalIgnoreCase);
                await _audit.LogFusionEventAsync(_username,
                    renamed ? "Fusion_Bay_Renamed" : "Fusion_Bay_Updated",
                    "FusionBay", newId,
                    renamed
                        ? $"Bay {EditingOriginalId} renamed to {newId} with {cap:N0} nuts capacity."
                        : $"Bay {newId} updated to {cap:N0} nuts capacity.",
                    AuditCategory.Material, AuditSeverity.Info,
                    $"BayId={EditingOriginalId}", $"BayId={newId}; MaxCapacityNuts={cap}");
                StatusText = string.Equals(EditingOriginalId, newId, StringComparison.OrdinalIgnoreCase)
                    ? $"Updated {newId} capacity to {cap:N0} nuts."
                    : $"Renamed {EditingOriginalId} to {newId} with {cap:N0} nuts capacity.";
                CancelEdit();
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Update Fusion bay", ex);
            }
        }
    }
}
