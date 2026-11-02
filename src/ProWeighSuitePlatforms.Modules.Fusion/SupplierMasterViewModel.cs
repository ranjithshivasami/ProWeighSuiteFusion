// Fusion coconut supplier master (mirrors fusion_link_v3_ui.html tab t1).
// fusion_suppliers + fusion_inward only. Suppliers are deactivated, never deleted.
// Available weight = WB weight minus sample/spoil/water/broken/rejection.
// Balance nuts = round(available x WB count / WB weight) minus consumed nuts.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data.Fusion;

namespace ProWeighSuitePlatforms.Modules.Fusion
{
    public sealed class SupplierMasterViewModel : ViewModels.BaseViewModel
    {
        public string Title => "Supplier master";

        public ObservableCollection<FusionSupplier> Suppliers { get; } = new();
        public ObservableCollection<FusionSupplierStockRow> Rows { get; } = new();

        // Category-grid parity: search + paging over Suppliers (created fields only).
        public ObservableCollection<FusionSupplier> PagedSuppliers { get; } = new();
        public ObservableCollection<PageNumberItem> PageNumbers { get; } = new();

        private FusionSupplier? _selected;
        public FusionSupplier? Selected
        {
            get => _selected;
            set => SetProperty(ref _selected, value);
        }

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

        public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)FilteredSuppliers.Count / Math.Max(1, PageSize)));

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

        private List<FusionSupplier> FilteredSuppliers
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FilterText))
                    return Suppliers.ToList();
                var search = FilterText.Trim();
                return Suppliers.Where(s => SupplierMatches(s, search)).ToList();
            }
        }

        private static bool SupplierMatches(FusionSupplier s, string search)
        {
            return (s.Id?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.Location?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.Phone?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.StatusText?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        private string _newName = string.Empty;
        public string NewName
        {
            get => _newName;
            set
            {
                if (SetProperty(ref _newName, value))
                    ValidateNewName();
            }
        }
        private string _newLocation = string.Empty;
        public string NewLocation
        {
            get => _newLocation;
            set
            {
                if (SetProperty(ref _newLocation, value))
                    ValidateNewLocation();
            }
        }
        private string _newPhone = string.Empty;
        public string NewPhone
        {
            get => _newPhone;
            set
            {
                if (SetProperty(ref _newPhone, value))
                    ValidateNewPhone();
            }
        }

        private string? _newNameError;
        public string? NewNameError { get => _newNameError; private set => SetProperty(ref _newNameError, value); }
        private string? _newLocationError;
        public string? NewLocationError { get => _newLocationError; private set => SetProperty(ref _newLocationError, value); }
        private string? _newPhoneError;
        public string? NewPhoneError { get => _newPhoneError; private set => SetProperty(ref _newPhoneError, value); }

        private string _kpiSuppliers = "-";
        public string KpiSuppliers { get => _kpiSuppliers; private set => SetProperty(ref _kpiSuppliers, value); }
        private string _kpiGrns = "-";
        public string KpiGrns { get => _kpiGrns; private set => SetProperty(ref _kpiGrns, value); }
        private string _kpiWbWeight = "-";
        public string KpiWbWeight { get => _kpiWbWeight; private set => SetProperty(ref _kpiWbWeight, value); }
        private string _kpiDeductions = "-";
        public string KpiDeductions { get => _kpiDeductions; private set => SetProperty(ref _kpiDeductions, value); }
        private string _kpiAvailable = "-";
        public string KpiAvailable { get => _kpiAvailable; private set => SetProperty(ref _kpiAvailable, value); }

        private string _statusText = string.Empty;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        // Edit state: Edit fills the Add form above (NewName / NewLocation /
        // NewPhone) and the submit button flips to "Update Supplier".
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

        private string _editingSupplierId = string.Empty;
        public string EditingSupplierId { get => _editingSupplierId; private set => SetProperty(ref _editingSupplierId, value); }

        public string FormTitle => IsEditing ? "Edit supplier" : "Add supplier";
        public string SubmitButtonText => IsEditing ? "Update Supplier" : "Add supplier";

        private bool _isFormExpanded = true;
        public bool IsFormExpanded
        {
            get => _isFormExpanded;
            set => SetProperty(ref _isFormExpanded, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand AddSupplierCommand { get; }
        public ICommand ToggleActiveCommand { get; }
        public ICommand ToggleFormCommand { get; }
        public ICommand BeginEditCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand GoToPageCommand { get; }

        public SupplierMasterViewModel()
        {
            RefreshCommand = new ViewModels.RelayCommand(async _ => await LoadAsync());
            AddSupplierCommand = new ViewModels.RelayCommand(async _ => await AddOrUpdateSupplierAsync());
            ToggleFormCommand = new ViewModels.RelayCommand(_ => IsFormExpanded = !IsFormExpanded);
            ToggleActiveCommand = new ViewModels.RelayCommand(async p =>
            {
                if (p is FusionSupplier s) await ToggleActiveAsync(s);
                else if (p is FusionSupplierStockRow r) await ToggleActiveAsync(r);
            });
            BeginEditCommand = new ViewModels.RelayCommand(p =>
            {
                if (p is FusionSupplier s) BeginEdit(s);
                else if (p is FusionSupplierStockRow r)
                {
                    var sup = Suppliers.FirstOrDefault(x => x.Id == r.SupplierId);
                    if (sup != null) BeginEdit(sup);
                }
            });
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

        public async Task LoadAsync()
        {
            try
            {
                await new FusionDatabaseInit(AppConfigurationManager.GetDatabasePath()).EnsureCreatedAsync();
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var suppliers = await repo.GetSuppliersAsync();
                var inward = await repo.GetInwardAsync(2000);

                Suppliers.Clear();
                foreach (var s in suppliers) Suppliers.Add(s);

                if (CurrentPage > Math.Max(1, (int)Math.Ceiling((double)Suppliers.Count / Math.Max(1, PageSize))))
                    _currentPage = 1;
                UpdatePagedData();

                var (nearDays, expiredBelow) = await repo.GetConditionThresholdsAsync();
                var rows = BuildRows(suppliers, inward, nearDays, expiredBelow);
                Rows.Clear();
                foreach (var r in rows) Rows.Add(r);

                var withGrn = rows.Where(r => r.HasGrn).ToList();
                KpiSuppliers = $"{suppliers.Count(s => s.IsActive)} active";
                KpiGrns = withGrn.Count.ToString();
                KpiWbWeight = $"{withGrn.Sum(r => r.WbWeightKg):N0} kg";
                KpiDeductions = $"{withGrn.Sum(r => r.SampleKg + r.SpoilKg + r.WaterLossKg + r.BrokenKg + r.RejectionKg):N0} kg";
                KpiAvailable = $"{withGrn.Sum(r => r.AvailableKg):N0} kg";
                StatusText = $"{suppliers.Count} supplier(s), {withGrn.Count} GRN row(s). Deductions auto-update available weight and balance nuts.";
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        internal static List<FusionSupplierStockRow> BuildRows(List<FusionSupplier> suppliers, List<FusionInward> inward,
            int nearExpiryDays = FusionSettingsDefaults.NearExpiryDays,
            int expiredBelowDays = FusionSettingsDefaults.ExpiredBelowDays)
        {
            var rows = new List<FusionSupplierStockRow>();
            foreach (var s in suppliers)
            {
                var grns = inward.Where(g => string.Equals(g.SupplierName, s.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (grns.Count == 0)
                {
                    rows.Add(new FusionSupplierStockRow
                    {
                        SupplierId = s.Id,
                        SupplierName = s.Name,
                        Location = s.Location,
                        Phone = s.Phone,
                        ShelfLifeDays = 0,
                        Condition = "No stock",
                        SupplierActive = s.IsActive,
                        HasGrn = false,
                    });
                    continue;
                }
                foreach (var g in grns)
                {
                    double apw = g.CoconutCount > 0 ? g.WeightKg / g.CoconutCount : 0;
                    double avail = g.WeightKg - (g.SampleKg + g.SpoilKg + g.WaterLossKg + g.BrokenKg + g.RejectionKg);
                    double availNuts = (g.WeightKg > 0 && g.CoconutCount > 0)
                        ? Math.Round(avail * g.CoconutCount / g.WeightKg) - g.ConsumedNuts : 0;
                    var bb = FusionConditions.BestBefore(
                        string.IsNullOrEmpty(g.InwardDate) ? g.ReceivedAt[..Math.Min(10, g.ReceivedAt.Length)] : g.InwardDate,
                        g.ShelfLifeDays);
                    int left = (bb.Date - DateTime.Today).Days;
                    rows.Add(new FusionSupplierStockRow
                    {
                        SupplierId = s.Id,
                        GrnNo = g.GrnNo,
                        SupplierName = s.Name,
                        Location = s.Location,
                        Phone = s.Phone,
                        WbWeightKg = g.WeightKg,
                        WbCount = g.CoconutCount,
                        ApwKg = apw,
                        SampleKg = g.SampleKg,
                        SpoilKg = g.SpoilKg,
                        WaterLossKg = g.WaterLossKg,
                        BrokenKg = g.BrokenKg,
                        RejectionKg = g.RejectionKg,
                        ShelfLifeDays = g.ShelfLifeDays,
                        AvailableKg = avail,
                        BalanceNuts = availNuts,
                        Condition = FusionConditions.ForStock(availNuts, left, nearExpiryDays, expiredBelowDays),
                        SupplierActive = s.IsActive,
                        HasGrn = true,
                    });
                }
            }
            return rows;
        }

        public async Task AddOrUpdateSupplierAsync()
        {
            if (IsEditing)
                await SaveEditAsync();
            else
                await AddSupplierAsync();
        }

        public async Task AddSupplierAsync()
        {
            ValidateNewName();
            ValidateNewLocation();
            ValidateNewPhone();
            if (!string.IsNullOrEmpty(NewNameError)
                || !string.IsNullOrEmpty(NewLocationError)
                || !string.IsNullOrEmpty(NewPhoneError))
            {
                StatusText = !string.IsNullOrEmpty(NewNameError)
                    ? NewNameError
                    : !string.IsNullOrEmpty(NewLocationError)
                        ? NewLocationError
                        : NewPhoneError ?? "Please fix validation errors before adding supplier.";
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.AddSupplierAsync(NewName.Trim(), NewLocation?.Trim() ?? string.Empty,
                    NewPhone?.Trim() ?? string.Empty);
                StatusText = $"Added supplier {NewName.Trim()}.";
                _newName = _newLocation = _newPhone = string.Empty;
                RaisePropertyChanged(nameof(NewName));
                RaisePropertyChanged(nameof(NewLocation));
                RaisePropertyChanged(nameof(NewPhone));
                NewNameError = NewLocationError = NewPhoneError = null;
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Add failed: {ex.Message}";
            }
        }

        private void ValidateNewName()
        {
            var name = (NewName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                NewNameError = "Supplier name is required.";
            else if (name.Length < 3)
                NewNameError = "Supplier name must be at least 3 characters.";
            else if (name.Length > 50)
                NewNameError = "Supplier name must not exceed 50 characters.";
            else
                NewNameError = null;
        }

        private void ValidateNewLocation()
        {
            var location = (NewLocation ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(location))
                NewLocationError = "Location is required.";
            else if (location.Length < 3)
                NewLocationError = "Location must be at least 3 characters.";
            else if (location.Length > 50)
                NewLocationError = "Location must not exceed 50 characters.";
            else
                NewLocationError = null;
        }

        // WhatsApp numbers follow E.164: optional leading '+', country code,
        // 7-15 digits total. Spaces, hyphens and parentheses are ignored.
        // The field is optional; blank values are accepted.
        internal static readonly Regex WhatsAppNumberRegex =
            new(@"^\+?[1-9]\d{6,14}$", RegexOptions.Compiled);

        internal static string? ValidateWhatsAppNumber(string? phone)
        {
            var v = (phone ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v))
                return null;
            var normalized = Regex.Replace(v, @"[\s\-().]", string.Empty);
            return WhatsAppNumberRegex.IsMatch(normalized)
                ? null
                : "Enter a valid WhatsApp number with country code (e.g. +919876543210).";
        }

        private void ValidateNewPhone()
        {
            NewPhoneError = ValidateWhatsAppNumber(NewPhone);
        }

        public void BeginEdit(FusionSupplier supplier)
        {
            if (supplier == null) return;
            EditingSupplierId = supplier.Id;
            _newName = supplier.Name ?? string.Empty;
            RaisePropertyChanged(nameof(NewName));
            _newLocation = supplier.Location ?? string.Empty;
            RaisePropertyChanged(nameof(NewLocation));
            _newPhone = supplier.Phone ?? string.Empty;
            RaisePropertyChanged(nameof(NewPhone));
            NewNameError = NewLocationError = NewPhoneError = null;
            IsEditing = true;
            IsFormExpanded = true;
            StatusText = $"Editing {supplier.Name}. Update the details above and click Update Supplier.";
        }

        public void CancelEdit()
        {
            IsEditing = false;
            EditingSupplierId = string.Empty;
            _newName = _newLocation = _newPhone = string.Empty;
            RaisePropertyChanged(nameof(NewName));
            RaisePropertyChanged(nameof(NewLocation));
            RaisePropertyChanged(nameof(NewPhone));
            NewNameError = NewLocationError = NewPhoneError = null;
        }

        public async Task SaveEditAsync()
        {
            ValidateNewName();
            ValidateNewLocation();
            ValidateNewPhone();
            if (!string.IsNullOrEmpty(NewNameError)
                || !string.IsNullOrEmpty(NewLocationError)
                || !string.IsNullOrEmpty(NewPhoneError))
            {
                StatusText = !string.IsNullOrEmpty(NewNameError)
                    ? NewNameError
                    : !string.IsNullOrEmpty(NewLocationError)
                        ? NewLocationError
                        : NewPhoneError ?? "Please fix validation errors before updating.";
                return;
            }

            var name = (NewName ?? string.Empty).Trim();
            if (Suppliers.Any(s => !string.Equals(s.Id, EditingSupplierId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                NewNameError = $"Supplier {name} already exists.";
                StatusText = NewNameError;
                return;
            }

            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.UpdateSupplierAsync(EditingSupplierId, name,
                    (NewLocation ?? string.Empty).Trim(), (NewPhone ?? string.Empty).Trim());
                StatusText = $"Updated supplier {name}.";
                CancelEdit();
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
            }
        }

        public void ShowStatus(string message)
        {
            StatusText = message ?? string.Empty;
        }

        public async Task SetActiveAsync(FusionSupplier supplier, bool active)
        {
            if (supplier == null) return;
            if (supplier.IsActive == active)
            {
                StatusText = active ? $"{supplier.Name} is already active." : $"{supplier.Name} is already inactive.";
                return;
            }
            var confirm = MessageBox.Show(
                active
                    ? $"Are you sure you want to activate supplier '{supplier.Name}'?"
                    : $"Are you sure you want to deactivate supplier '{supplier.Name}'? Its history will be kept.",
                active ? "Confirm Activate" : "Confirm Deactivate",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                StatusText = active ? $"Activation of {supplier.Name} cancelled." : $"Deactivation of {supplier.Name} cancelled.";
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.SetSupplierActiveAsync(supplier.Id, active);
                StatusText = active ? $"Activated {supplier.Name}." : $"Deactivated {supplier.Name} (history kept).";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Update failed: {ex.Message}";
            }
        }

        public async Task SetActiveAsync(FusionSupplierStockRow row, bool active)
        {
            if (row == null) return;
            try
            {
                var current = Suppliers.FirstOrDefault(s => s.Id == row.SupplierId);
                bool isActive = current?.IsActive ?? row.SupplierActive;
                if (isActive == active)
                {
                    StatusText = active ? $"{row.SupplierName} is already active." : $"{row.SupplierName} is already inactive.";
                    return;
                }
                var confirm = MessageBox.Show(
                    active
                        ? $"Are you sure you want to activate supplier '{row.SupplierName}'?"
                        : $"Are you sure you want to deactivate supplier '{row.SupplierName}'? Its history will be kept.",
                    active ? "Confirm Activate" : "Confirm Deactivate",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes)
                {
                    StatusText = active ? $"Activation of {row.SupplierName} cancelled." : $"Deactivation of {row.SupplierName} cancelled.";
                    return;
                }
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.SetSupplierActiveAsync(row.SupplierId, active);
                StatusText = active ? $"Activated {row.SupplierName}." : $"Deactivated {row.SupplierName} (history kept).";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Update failed: {ex.Message}";
            }
        }

        public async Task ToggleActiveAsync(FusionSupplier supplier)
        {
            if (supplier == null) return;
            await SetActiveAsync(supplier, !supplier.IsActive);
        }

        public async Task ToggleActiveAsync(FusionSupplierStockRow row)
        {
            if (row == null) return;
            var current = Suppliers.FirstOrDefault(s => s.Id == row.SupplierId);
            bool active = current?.IsActive ?? row.SupplierActive;
            await SetActiveAsync(row, !active);
        }

        private void UpdatePagedData()
        {
            var filtered = FilteredSuppliers;
            TotalRecords = filtered.Count;
            if (CurrentPage < 1) _currentPage = 1;
            var pages = Math.Max(1, (int)Math.Ceiling((double)filtered.Count / Math.Max(1, PageSize)));
            if (_currentPage > pages) _currentPage = pages;
            var paged = filtered
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();
            PagedSuppliers.Clear();
            foreach (var s in paged)
                PagedSuppliers.Add(s);

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
