// Fusion tub master over 50 RFID tubs (mirrors fusion_link_v3_ui.html tab t5).
// Replacement rule + colour/status/search filters; cycles editable inline;
// block/unblock and mark-replaced actions. fusion_tubs only.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Views;

using ProWeighSuitePlatforms.Models;
namespace ProWeighSuitePlatforms.ViewModels
{
    public sealed class TubMasterRow : INotifyPropertyChanged
    {
        public int Index { get; set; }
        public FusionTub Tub { get; set; } = new();
        public string Replacement { get; set; } = "OK";
        public double LifePct { get; set; }
        public int CyclesLeft { get; set; }
        public int AgeingDays { get; set; }
        public string AgeingDisplay { get; set; } = string.Empty;
        public string AssignedDisplay => string.IsNullOrWhiteSpace(Tub?.AssignedTo) ? "–" : Tub.AssignedTo;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed class TubMasterViewModel : ViewModels.BaseViewModel
    {
        public string Title => "Tub master";

        private readonly AuditService _audit = new();
        private readonly string _username;

        private List<FusionTub> _all = new();
        public ObservableCollection<TubMasterRow> Rows { get; } = new();

        // Supplier-style paging over the filtered list.
        private List<TubMasterRow> _filtered = new();
        public ObservableCollection<TubMasterRow> PagedRows { get; } = new();
        public ObservableCollection<PageNumberItem> PageNumbers { get; } = new();

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
                }
            }
        }

        public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)_filtered.Count / Math.Max(1, PageSize)));

        private int _totalRecords;
        public int TotalRecords
        {
            get => _totalRecords;
            private set => SetProperty(ref _totalRecords, value);
        }

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

        public List<string> ColourOptions { get; } = new() { "All", "Orange", "Blue" };
        public List<string> StatusOptions { get; } = new() { "All", "Available", "In use", "Blocked" };
        public List<string> ColourCreateOptions { get; } = new() { "Orange", "Blue" };

        // Create-tub form (mirrors supplier add bar).
        // Untouched by default: no auto-fill for Tub no / EPC, no validation errors
        // shown until the user edits a field or clicks Add/Update.
        private bool _isResettingAddForm;
        private string _newTubNo = string.Empty;
        public string NewTubNo { get => _newTubNo; set { if (SetProperty(ref _newTubNo, value) && !_isResettingAddForm) ValidateNewTubNo(); } }
        private string _newEpc = string.Empty;
        public string NewEpc { get => _newEpc; set { if (SetProperty(ref _newEpc, value) && !_isResettingAddForm) ValidateNewEpc(); } }
        private string _newColour = "Orange";
        public string NewColour { get => _newColour; set { if (SetProperty(ref _newColour, value) && !_isResettingAddForm) ValidateNewColour(); } }
        private string _newCapacity = "50";
        public string NewCapacity { get => _newCapacity; set { if (SetProperty(ref _newCapacity, value) && !_isResettingAddForm) ValidateNewCapacity(); } }
        private string _newBay = string.Empty;
        public string NewBay { get => _newBay; set { if (SetProperty(ref _newBay, value) && !_isResettingAddForm) ValidateNewBay(); } }

        private void ResetAddForm()
        {
            // Programmatic reset must NOT trigger validation: the form stays
            // untouched with no errors visible until user interaction / submit.
            _isResettingAddForm = true;
            try
            {
                _newTubNo = string.Empty;
                RaisePropertyChanged(nameof(NewTubNo));
                _newEpc = string.Empty;
                RaisePropertyChanged(nameof(NewEpc));
                _newColour = "Orange";
                RaisePropertyChanged(nameof(NewColour));
                _newCapacity = "50";
                RaisePropertyChanged(nameof(NewCapacity));
                _newBay = string.Empty;
                RaisePropertyChanged(nameof(NewBay));
                NewTubNoError = NewEpcError = NewColourError = NewCapacityError = NewBayError = null;
            }
            finally
            {
                _isResettingAddForm = false;
            }
        }

        private void FillAddFormForEdit(string tubNo, string epc, string colour, string capacity, string bay)
        {
            // Same as reset: fill without live validation, then clear errors so
            // the edit form opens untouched. Errors appear on edit / submit.
            _isResettingAddForm = true;
            try
            {
                _newTubNo = tubNo ?? string.Empty;
                RaisePropertyChanged(nameof(NewTubNo));
                _newEpc = epc ?? string.Empty;
                RaisePropertyChanged(nameof(NewEpc));
                _newColour = colour ?? "Orange";
                RaisePropertyChanged(nameof(NewColour));
                _newCapacity = capacity ?? string.Empty;
                RaisePropertyChanged(nameof(NewCapacity));
                _newBay = bay ?? string.Empty;
                RaisePropertyChanged(nameof(NewBay));
                NewTubNoError = NewEpcError = NewColourError = NewCapacityError = NewBayError = null;
            }
            finally
            {
                _isResettingAddForm = false;
            }
        }

        private string? _newTubNoError;
        public string? NewTubNoError { get => _newTubNoError; private set => SetProperty(ref _newTubNoError, value); }
        private string? _newEpcError;
        public string? NewEpcError { get => _newEpcError; private set => SetProperty(ref _newEpcError, value); }
        private string? _newColourError;
        public string? NewColourError { get => _newColourError; private set => SetProperty(ref _newColourError, value); }
        private string? _newCapacityError;
        public string? NewCapacityError { get => _newCapacityError; private set => SetProperty(ref _newCapacityError, value); }
        private string? _newBayError;
        public string? NewBayError { get => _newBayError; private set => SetProperty(ref _newBayError, value); }

        private string _replaceAt = "2000";
        public string ReplaceAt { get => _replaceAt; set { if (SetProperty(ref _replaceAt, value)) { ValidateReplaceAt(); RefreshView(); } } }
        private string _warnPercent = "80";
        public string WarnPercent { get => _warnPercent; set { if (SetProperty(ref _warnPercent, value)) { ValidateWarnPercent(); RefreshView(); } } }
        private bool _replaceRuleLoaded;
        private string _colourFilter = "All";
        public string ColourFilter { get => _colourFilter; set { if (SetProperty(ref _colourFilter, value)) { CurrentPage = 1; RefreshView(); } } }
        private string _statusFilter = "All";
        public string StatusFilter { get => _statusFilter; set { if (SetProperty(ref _statusFilter, value)) { CurrentPage = 1; RefreshView(); } } }
        private string _searchText = string.Empty;
        public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) { CurrentPage = 1; RefreshView(); } } }

        // Row checkboxes: header "select all" applies to the filtered list (all pages).
        private bool _isAllSelected;
        private bool _syncingSelection;

        public bool IsAllSelected
        {
            get => _isAllSelected;
            set
            {
                if (_syncingSelection || _isAllSelected == value) return;
                _syncingSelection = true;
                try
                {
                    foreach (var r in _filtered) r.IsSelected = value;
                }
                finally
                {
                    _syncingSelection = false;
                }
                _isAllSelected = _filtered.Count > 0 && value;
                RaisePropertyChanged(nameof(IsAllSelected));
                RaiseSelectionProperties();
            }
        }

        public int SelectedTubCount => _filtered.Count(r => r.IsSelected);
        public bool HasSelectedTubs => SelectedTubCount > 0;

        private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TubMasterRow.IsSelected)) return;
            UpdateSelectionState();
        }

        private void UpdateSelectionState()
        {
            if (_syncingSelection) return;
            _isAllSelected = _filtered.Count > 0 && _filtered.All(r => r.IsSelected);
            RaisePropertyChanged(nameof(IsAllSelected));
            RaiseSelectionProperties();
        }

        private void RaiseSelectionProperties()
        {
            RaisePropertyChanged(nameof(SelectedTubCount));
            RaisePropertyChanged(nameof(HasSelectedTubs));
        }

        private string _kpiTotal = "-";
        public string KpiTotal { get => _kpiTotal; private set => SetProperty(ref _kpiTotal, value); }
        private string _kpiColours = "-";
        public string KpiColours { get => _kpiColours; private set => SetProperty(ref _kpiColours, value); }
        private string _kpiInUse = "-";
        public string KpiInUse { get => _kpiInUse; private set => SetProperty(ref _kpiInUse, value); }
        private string _kpiReplace = "-";
        public string KpiReplace { get => _kpiReplace; private set => SetProperty(ref _kpiReplace, value); }
        private string _kpiHighest = "-";
        public string KpiHighest { get => _kpiHighest; private set => SetProperty(ref _kpiHighest, value); }

        private string _statusText = string.Empty;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public void ShowStatus(string message)
        {
            StatusText = message ?? string.Empty;
        }

        // Edit state: Edit fills the Add form above (NewTubNo / NewEpc /
        // NewColour / NewCapacity / NewBay) and the submit button flips to "Update Tub".
        // Tub no is the PK referenced by lots/transactions/events, so it stays locked while editing.
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
                    RaisePropertyChanged(nameof(IsTubNoLocked));
                }
            }
        }

        private string _editingOriginalId = string.Empty;
        public string EditingOriginalId { get => _editingOriginalId; private set => SetProperty(ref _editingOriginalId, value); }

        public string FormTitle => IsEditing ? $"Edit tub {EditingOriginalId}" : "Add tub";
        public string SubmitButtonText => IsEditing ? "Update Tub" : "Add tub";
        public bool IsTubNoLocked => IsEditing;

        private bool _isFormExpanded = true;
        public bool IsFormExpanded
        {
            get => _isFormExpanded;
            set => SetProperty(ref _isFormExpanded, value);
        }

        private string? _replaceAtError;
        public string? ReplaceAtError { get => _replaceAtError; private set => SetProperty(ref _replaceAtError, value); }
        private string? _warnPercentError;
        public string? WarnPercentError { get => _warnPercentError; private set => SetProperty(ref _warnPercentError, value); }

        public bool HasAddFormErrors => !string.IsNullOrEmpty(NewTubNoError)
            || !string.IsNullOrEmpty(NewEpcError)
            || !string.IsNullOrEmpty(NewColourError)
            || !string.IsNullOrEmpty(NewCapacityError)
            || !string.IsNullOrEmpty(NewBayError);

        public ICommand RefreshCommand { get; }
        public ICommand BlockCommand { get; }
        public ICommand ReplaceCommand { get; }
        public ICommand StepUpCommand { get; }
        public ICommand StepDownCommand { get; }
        public ICommand AddTubCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand ToggleFormCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand GoToPageCommand { get; }
        public ICommand ImportCsvCommand { get; }
        public ICommand DownloadTemplateCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ReleaseSelectedTubsCommand { get; }

        public TubMasterViewModel(string username = "System")
        {
            _username = string.IsNullOrWhiteSpace(username) ? "System" : username;
            RefreshCommand = new ViewModels.RelayCommand(async _ => await LoadAsync());
            BlockCommand = new ViewModels.RelayCommand(async p =>
            {
                if (p is TubMasterRow r) await SetBlockedAsync(r, !r.Tub.IsBlocked);
            });
            ReplaceCommand = new ViewModels.RelayCommand(async p =>
            {
                if (p is TubMasterRow r) await MarkReplacedAsync(r);
            });
            StepUpCommand = new ViewModels.RelayCommand(async p =>
            {
                if (p is TubMasterRow r) await StepCyclesAsync(r, +1);
            });
            StepDownCommand = new ViewModels.RelayCommand(async p =>
            {
                if (p is TubMasterRow r) await StepCyclesAsync(r, -1);
            });
            AddTubCommand = new ViewModels.RelayCommand(async _ => await AddOrUpdateTubAsync());
            CancelEditCommand = new ViewModels.RelayCommand(_ => CancelEdit());
            ToggleFormCommand = new ViewModels.RelayCommand(_ => IsFormExpanded = !IsFormExpanded);
            ImportCsvCommand = new ViewModels.RelayCommand(async _ => await ImportCsvAsync());
            DownloadTemplateCommand = new ViewModels.RelayCommand(_ => DownloadTemplate());
            ExportCsvCommand = new ViewModels.RelayCommand(async _ => await ExportCsvAsync());
            ReleaseSelectedTubsCommand = new ViewModels.RelayCommand(async _ => await ReleaseSelectedTubsAsync());
            PrevPageCommand = new ViewModels.RelayCommand(_ => { if (CurrentPage > 1) CurrentPage--; }, _ => CurrentPage > 1);
            NextPageCommand = new ViewModels.RelayCommand(_ => { if (CurrentPage < TotalPages) CurrentPage++; }, _ => CurrentPage < TotalPages);
            GoToPageCommand = new ViewModels.RelayCommand(param =>
            {
                if (param is int page && page >= 1 && page <= Math.Max(1, TotalPages) && page != CurrentPage)
                    CurrentPage = page;
            });
            ValidateReplaceAt();
            ValidateWarnPercent();
            _ = LoadAsync();
        }

        private int ReplaceLimit => int.TryParse(ReplaceAt, out var v) && v > 0 ? v : 2000;
        private double WarnRatio
        {
            get
            {
                if (double.TryParse(WarnPercent, out var v) && v > 0) return v / 100.0;
                return 0.8;
            }
        }

        internal string ReplacementFor(int cycles)
        {
            double p = (double)cycles / ReplaceLimit;
            if (p >= 1) return "Replace now";
            if (p >= WarnRatio) return "Plan replacement";
            return "OK";
        }

        public async Task LoadAsync()
        {
            try
            {
                await new FusionDatabaseInit(AppConfigurationManager.GetDatabasePath()).EnsureCreatedAsync();
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                _all = await repo.GetTubsAsync();
                if (!_replaceRuleLoaded)
                {
                    // Persisted default from Fusion settings; afterwards the panel is a session override.
                    _replaceRuleLoaded = true;
                    var (replaceAt, warn) = await repo.GetTubReplacementSettingsAsync();
                    _replaceAt = replaceAt.ToString();
                    RaisePropertyChanged(nameof(ReplaceAt));
                    _warnPercent = warn.ToString(CultureInfo.InvariantCulture);
                    RaisePropertyChanged(nameof(WarnPercent));
                    ValidateReplaceAt();
                    ValidateWarnPercent();
                }
                RefreshView();
                // Keep the add form untouched on page open / refresh: do NOT
                // auto-fill Tub no (previous SuggestNextTubNo could produce a
                // single digit like "5" which instantly fails the min-2-chars
                // rule and shows a validation error before any user input).
                // Leave NewTubNo empty until the user types or submits.
                StatusText = $"{_all.Count} tub(s). Cycles are editable; blocked tubs stay out of batch pools.";
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        private string SuggestNextTubNo()
        {
            int max = 0;
            foreach (var t in _all)
            {
                var id = (t.TubId ?? string.Empty).Trim();
                // Numeric-only tub nos; still tolerate legacy ids like "Tub_12" by trailing digits.
                var digits = new string(id.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
                if (int.TryParse(digits, out var n) && n > max) max = n;
            }
            return (max + 1).ToString();
        }

        public async Task AddTubAsync()
        {
            ValidateAddForm();
            if (HasAddFormErrors)
            {
                StatusText = FirstAddFormError() ?? "Please fix validation errors before adding tub.";
                return;
            }
            var tubNo = (NewTubNo ?? string.Empty).Trim();
            var epc = (NewEpc ?? string.Empty).Trim();
            var colour = (NewColour ?? string.Empty).Trim();
            var bay = (NewBay ?? string.Empty).Trim();
            if (_all.Any(t => string.Equals(t.TubId, tubNo, StringComparison.OrdinalIgnoreCase)))
            {
                NewTubNoError = $"Tub {tubNo} already exists.";
                StatusText = NewTubNoError;
                return;
            }
            if (_all.Any(t => string.Equals(t.Epc, epc, StringComparison.OrdinalIgnoreCase)))
            {
                NewEpcError = $"EPC {epc} already exists.";
                StatusText = NewEpcError;
                return;
            }
            if (!TryParseCapacity(NewCapacity, out var cap))
            {
                ValidateNewCapacity();
                StatusText = NewCapacityError ?? "Enter a valid capacity.";
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.AddTubAsync(tubNo, epc, colour, cap, bay, DateTime.Today.ToString("yyyy-MM-dd"));
                await _audit.LogFusionEventAsync(_username, "Fusion_Tub_Created",
                    "FusionTub", tubNo,
                    $"Tub created: {tubNo}, EPC {epc}, colour {colour}, capacity {cap:0.###} kg, bay {bay}.",
                    AuditCategory.Material, AuditSeverity.Info,
                    null, $"TubId={tubNo}; EPC={epc}; Colour={colour}; CapacityKg={cap}; Bay={bay}");
                StatusText = $"Added tub {tubNo}.";
                ResetAddForm();
                await LoadAsync();
            }
            catch (Exception ex)
            {
                // Belt-and-braces: if a duplicate slips past the in-memory check
                // (e.g. raced or legacy data), map the DB constraint to the field.
                if (ex.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
                {
                    await LoadAsync();
                    if (ex.Message.Contains("epc", StringComparison.OrdinalIgnoreCase))
                    {
                        NewEpcError = $"EPC {epc} already exists.";
                        StatusText = NewEpcError;
                    }
                    else
                    {
                        NewTubNoError = $"Tub {tubNo} already exists.";
                        StatusText = NewTubNoError;
                    }
                    return;
                }
                StatusText = $"Add failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Add Fusion tub", ex);
            }
        }

        public async Task AddOrUpdateTubAsync()
        {
            if (IsEditing)
                await SaveEditAsync();
            else
                await AddTubAsync();
        }

        public void BeginEdit(TubMasterRow row)
        {
            if (row?.Tub == null) return;
            EditingOriginalId = row.Tub.TubId ?? string.Empty;
            FillAddFormForEdit(
                row.Tub.TubId ?? string.Empty,
                row.Tub.Epc ?? string.Empty,
                string.Equals(row.Tub.Colour, "Blue", StringComparison.OrdinalIgnoreCase) ? "Blue" : "Orange",
                row.Tub.CapacityKg.ToString(CultureInfo.InvariantCulture),
                row.Tub.BayId ?? string.Empty);
            IsEditing = true;
            IsFormExpanded = true;
            StatusText = $"Editing tub {EditingOriginalId}. Update the details above and click Update Tub.";
        }

        public void CancelEdit()
        {
            IsEditing = false;
            EditingOriginalId = string.Empty;
            ResetAddForm();
        }

        public async Task SaveEditAsync()
        {
            ValidateAddForm();
            if (HasAddFormErrors)
            {
                StatusText = FirstAddFormError() ?? "Please fix validation errors before updating.";
                return;
            }
            var epc = (NewEpc ?? string.Empty).Trim();
            var colour = (NewColour ?? string.Empty).Trim();
            var bay = (NewBay ?? string.Empty).Trim();
            if (_all.Any(t => !string.Equals(t.TubId, EditingOriginalId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.Epc, epc, StringComparison.OrdinalIgnoreCase)))
            {
                NewEpcError = $"EPC {epc} already exists.";
                StatusText = NewEpcError;
                return;
            }
            if (!TryParseCapacity(NewCapacity, out var cap))
            {
                ValidateNewCapacity();
                StatusText = NewCapacityError ?? "Enter a valid capacity.";
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var old = _all.FirstOrDefault(t => string.Equals(t.TubId, EditingOriginalId, StringComparison.OrdinalIgnoreCase));
                await repo.UpdateTubAsync(EditingOriginalId, epc, colour, cap, bay);
                await _audit.LogFusionEventAsync(_username, "Fusion_Tub_Updated",
                    "FusionTub", EditingOriginalId,
                    $"Tub updated: {EditingOriginalId}, EPC {epc}, colour {colour}, capacity {cap:0.###} kg, bay {bay}.",
                    AuditCategory.Material, AuditSeverity.Info,
                    old is null ? null : $"EPC={old.Epc}; Colour={old.Colour}; CapacityKg={old.CapacityKg}; Bay={old.BayId}",
                    $"EPC={epc}; Colour={colour}; CapacityKg={cap}; Bay={bay}");
                StatusText = $"Updated tub {EditingOriginalId}.";
                CancelEdit();
                await LoadAsync();
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                    && ex.Message.Contains("epc", StringComparison.OrdinalIgnoreCase))
                {
                    await LoadAsync();
                    NewEpcError = $"EPC {epc} already exists.";
                    StatusText = NewEpcError;
                    return;
                }
                StatusText = $"Save failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Update Fusion tub", ex);
            }
        }

        private static readonly Regex EpcRegex = new(@"^[A-Za-z0-9]+$", RegexOptions.Compiled);
        private static readonly Regex TubNoRegex = new(@"^[A-Za-z0-9_\-]+$", RegexOptions.Compiled);

        internal static string? ValidateTubNo(string? tubNo)
        {
            var v = (tubNo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v))
                return "Tub no is required.";
            if (v.Length < 2)
                return "Tub no must be at least 2 characters.";
            if (v.Length > 30)
                return "Tub no must not exceed 30 characters.";
            if (!TubNoRegex.IsMatch(v))
                return "Tub no may only contain letters, digits, _ or -.";
            return null;
        }

        internal static string? ValidateEpc(string? epc)
        {
            var v = (epc ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v))
                return "EPC ID is required.";
            if (!EpcRegex.IsMatch(v))
                return "EPC ID must contain letters or digits only.";
            if (v.Length < 2)
                return "EPC ID must be at least 2 characters.";
            if (v.Length > 32)
                return "EPC ID must not exceed 32 characters.";
            return null;
        }

        internal static string? ValidateColour(string? colour)
        {
            var v = (colour ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v))
                return "Colour is required.";
            if (!string.Equals(v, "Orange", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(v, "Blue", StringComparison.OrdinalIgnoreCase))
                return "Colour must be Orange or Blue.";
            return null;
        }

        internal static string? ValidateCapacity(string? capacity)
        {
            var v = (capacity ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v))
                return "Capacity is required.";
            if (!TryParseCapacity(v, out var cap))
                return "Capacity must be a positive number.";
            if (cap <= 0)
                return "Capacity must be greater than 0.";
            if (cap > 1000)
                return "Capacity must not exceed 1000 kg.";
            return null;
        }

        internal static string? ValidateBay(string? bay)
        {
            var v = (bay ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v))
                return null; // optional
            if (v.Length > 30)
                return "Bay must not exceed 30 characters.";
            return null;
        }

        internal static string? ValidateReplaceAt(string? replaceAt)
        {
            var v = (replaceAt ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v))
                return "Replace-at cycles is required.";
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.CurrentCulture, out var n)
                && !int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return "Replace-at must be a whole number.";
            if (n <= 0)
                return "Replace-at must be greater than 0.";
            if (n > 1000000)
                return "Replace-at must not exceed 1000000.";
            return null;
        }

        internal static string? ValidateWarnPercent(string? warnPercent)
        {
            var v = (warnPercent ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v))
                return "Warn % is required.";
            if (!double.TryParse(v, NumberStyles.Number, CultureInfo.CurrentCulture, out var d)
                && !double.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out d))
                return "Warn % must be a number.";
            if (d < 1 || d > 99)
                return "Warn % must be between 1 and 99.";
            return null;
        }

        internal static bool TryParseCapacity(string? text, out double capacity)
        {
            capacity = 0;
            var v = (text ?? string.Empty).Trim();
            if (!double.TryParse(v, NumberStyles.Number, CultureInfo.CurrentCulture, out capacity)
                && !double.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out capacity))
                return false;
            return capacity > 0 && capacity <= 1000;
        }

        private void ValidateNewTubNo()
        {
            NewTubNoError = ValidateTubNo(NewTubNo);
            var v = (NewTubNo ?? string.Empty).Trim();
            if (NewTubNoError == null
                && _all.Any(t => string.Equals(t.TubId, v, StringComparison.OrdinalIgnoreCase)
                    && !(IsEditing && string.Equals(t.TubId, EditingOriginalId, StringComparison.OrdinalIgnoreCase))))
                NewTubNoError = $"Tub {v} already exists.";
        }

        private void ValidateNewEpc()
        {
            NewEpcError = ValidateEpc(NewEpc);
            var v = (NewEpc ?? string.Empty).Trim();
            if (NewEpcError == null
                && _all.Any(t => string.Equals(t.Epc, v, StringComparison.OrdinalIgnoreCase)
                    && !(IsEditing && string.Equals(t.TubId, EditingOriginalId, StringComparison.OrdinalIgnoreCase))))
                NewEpcError = $"EPC {v} already exists.";
        }
        private void ValidateNewColour() => NewColourError = ValidateColour(NewColour);
        private void ValidateNewCapacity() => NewCapacityError = ValidateCapacity(NewCapacity);
        private void ValidateNewBay() => NewBayError = ValidateBay(NewBay);
        private void ValidateReplaceAt() => ReplaceAtError = ValidateReplaceAt(ReplaceAt);
        private void ValidateWarnPercent() => WarnPercentError = ValidateWarnPercent(WarnPercent);

        private void ValidateAddForm()
        {
            ValidateNewTubNo();
            ValidateNewEpc();
            ValidateNewColour();
            ValidateNewCapacity();
            ValidateNewBay();
        }

        private string? FirstAddFormError() =>
            !string.IsNullOrEmpty(NewTubNoError) ? NewTubNoError
            : !string.IsNullOrEmpty(NewEpcError) ? NewEpcError
            : !string.IsNullOrEmpty(NewColourError) ? NewColourError
            : !string.IsNullOrEmpty(NewCapacityError) ? NewCapacityError
            : !string.IsNullOrEmpty(NewBayError) ? NewBayError
            : null;

        public void RefreshView()
        {
            if (_all == null) return;
            var q = _searchText?.Trim().ToLowerInvariant() ?? string.Empty;
            var list = new List<TubMasterRow>();
            int idx = 0;
            foreach (var t in _all.OrderBy(t => t.TubId, StringComparer.Ordinal))
            {
                idx++;
                if (!string.Equals(_colourFilter, "All", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(t.Colour, _colourFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(_statusFilter, "All", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(t.DisplayStatus, _statusFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(q)
                    && !(t.Epc ?? string.Empty).ToLowerInvariant().Contains(q)
                    && !(t.TubId ?? string.Empty).ToLowerInvariant().Contains(q))
                    continue;
                double pct = ReplaceLimit > 0 ? Math.Min(100, (double)t.UsedCycles / ReplaceLimit * 100) : 0;
                int ageingDays = 0;
                string ageingDisplay = "-";
                if (DateTime.TryParse(t.AgeingDate, out var ad))
                {
                    ageingDays = Math.Max(0, (DateTime.Today - ad.Date).Days);
                    ageingDisplay = $"{ad:dd/MM/yyyy} · {ageingDays} d";
                }
                list.Add(new TubMasterRow
                {
                    Index = idx,
                    Tub = t,
                    Replacement = ReplacementFor(t.UsedCycles),
                    LifePct = pct,
                    CyclesLeft = Math.Max(0, ReplaceLimit - t.UsedCycles),
                    AgeingDays = ageingDays,
                    AgeingDisplay = ageingDisplay,
                });
            }
            foreach (var old in Rows) old.PropertyChanged -= Row_PropertyChanged;
            Rows.Clear();
            foreach (var r in list)
            {
                r.PropertyChanged += Row_PropertyChanged;
                Rows.Add(r);
            }
            _filtered = list;
            UpdatePagedData();
            UpdateSelectionState();

            KpiTotal = _all.Count.ToString();
            KpiColours = $"{_all.Count(t => t.Colour == "Orange")} / {_all.Count(t => t.Colour == "Blue")}";
            KpiInUse = _all.Count(t => !string.IsNullOrEmpty(t.AssignedTo)).ToString();
            KpiReplace = _all.Count(t => ReplacementFor(t.UsedCycles) != "OK").ToString();
            KpiHighest = _all.Count == 0 ? "0" : _all.Max(t => t.UsedCycles).ToString("N0");
        }

        private void UpdatePagedData()
        {
            var total = _filtered.Count;
            TotalRecords = total;
            if (CurrentPage < 1) _currentPage = 1;
            var pages = Math.Max(1, (int)Math.Ceiling((double)total / Math.Max(1, PageSize)));
            if (_currentPage > pages) _currentPage = pages;
            PagedRows.Clear();
            foreach (var r in _filtered.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
                PagedRows.Add(r);

            RefreshPageNumbers();
            RaisePropertyChanged(nameof(CurrentPage));
            RaisePropertyChanged(nameof(TotalPages));
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

        public async Task SaveCyclesAsync(TubMasterRow row)
        {
            if (row == null) return;
            if (row.Tub.UsedCycles < 0)
            {
                StatusText = $"Enter valid used cycles for {row.Tub.TubId} (must be 0 or more).";
                await LoadAsync();
                return;
            }
            if (row.Tub.UsedCycles > 1000000)
            {
                StatusText = $"Enter valid used cycles for {row.Tub.TubId} (must not exceed 1000000).";
                await LoadAsync();
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.UpdateTubCyclesAsync(row.Tub.TubId, row.Tub.UsedCycles);
                await _audit.LogFusionEventAsync(_username, "Fusion_Tub_Cycles_Updated",
                    "FusionTub", row.Tub.TubId,
                    $"Tub {row.Tub.TubId} used cycles set to {row.Tub.UsedCycles}.",
                    AuditCategory.Material, AuditSeverity.Info,
                    null, $"UsedCycles={row.Tub.UsedCycles}");
                RefreshView();
            }
            catch (Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Update Fusion tub cycles", ex);
            }
        }

        public async Task StepCyclesAsync(TubMasterRow row, int delta)
        {
            if (row == null) return;
            row.Tub.UsedCycles = Math.Max(0, row.Tub.UsedCycles + delta);
            await SaveCyclesAsync(row);
        }

        public async Task SetBlockedAsync(TubMasterRow row, bool blocked)
        {
            if (row?.Tub == null) return;
            var tubId = row.Tub.TubId ?? string.Empty;
            if (row.Tub.IsBlocked == blocked)
            {
                StatusText = blocked ? $"Tub {tubId} is already blocked." : $"Tub {tubId} is already unblocked.";
                return;
            }
            var confirm = MessageBox.Show(
                blocked
                    ? $"Are you sure you want to block tub '{tubId}'? It will stay out of batch pools."
                    : $"Are you sure you want to unblock tub '{tubId}'?",
                blocked ? "Confirm Block" : "Confirm Unblock",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                StatusText = blocked ? $"Blocking of {tubId} cancelled." : $"Unblocking of {tubId} cancelled.";
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.SetTubBlockedAsync(tubId, blocked);
                await _audit.LogFusionEventAsync(_username,
                    blocked ? "Fusion_Tub_Blocked" : "Fusion_Tub_Unblocked",
                    "FusionTub", tubId,
                    $"Tub {tubId} {(blocked ? "blocked" : "unblocked")}.",
                    AuditCategory.Material, AuditSeverity.Info,
                    $"IsBlocked={!blocked}", $"IsBlocked={blocked}");
                StatusText = blocked ? $"Blocked {tubId}." : $"Unblocked {tubId}.";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Update failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Update Fusion tub blocked state", ex);
            }
        }

        public async Task MarkReplacedAsync(TubMasterRow row)
        {
            if (row?.Tub == null) return;
            var tubId = row.Tub.TubId ?? string.Empty;
            try
            {
                if (!string.IsNullOrEmpty(row.Tub.AssignedTo))
                {
                    StatusText = $"{tubId} is in a running batch.";
                    return;
                }
                var confirm = MessageBox.Show(
                    $"Are you sure you want to mark tub '{tubId}' as replaced? Its used cycles will reset to 0.",
                    "Confirm Replace",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes)
                {
                    StatusText = $"Replacement of {tubId} cancelled.";
                    return;
                }
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                int oldCycles = row.Tub.UsedCycles;
                await repo.ReplaceTubAsync(tubId, DateTime.Today.ToString("yyyy-MM-dd"));
                await _audit.LogFusionEventAsync(_username, "Fusion_Tub_Replaced",
                    "FusionTub", tubId,
                    $"Tub {tubId} marked replaced; used cycles reset from {oldCycles} to 0.",
                    AuditCategory.Material, AuditSeverity.Warning,
                    $"UsedCycles={oldCycles}", "UsedCycles=0");
                StatusText = $"Replaced {tubId} (cycles reset).";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Replace failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Replace Fusion tub", ex);
            }
        }

        public async Task ReleaseSelectedTubsAsync()
        {
            var selected = _filtered.Where(r => r.IsSelected && r.Tub != null).ToList();
            if (selected.Count == 0)
            {
                StatusText = "Select at least one tub to release.";
                return;
            }
            var inUse = selected.Where(r => !string.IsNullOrEmpty(r.Tub.AssignedTo)).ToList();
            if (inUse.Count == 0)
            {
                StatusText = "The selected tub(s) are not in use.";
                return;
            }
            var names = string.Join(", ", inUse.Select(r => r.Tub.TubId));
            var confirm = MessageBox.Show(
                $"Release {inUse.Count} selected tub(s) from in use?\n\n{names}\n\nReleased tubs become available for assignment again.",
                "Confirm Release",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                StatusText = "Release cancelled.";
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var released = await repo.ReleaseTubsAsync(inUse.Select(r => r.Tub.TubId));
                await _audit.LogFusionEventAsync(_username, "Fusion_Tubs_Released",
                    "FusionTub", null,
                    $"Released {released} tub(s) from in use: {string.Join(", ", inUse.Select(r => r.Tub.TubId))}.",
                    AuditCategory.Material, AuditSeverity.Warning);
                StatusText = $"Released {released} tub(s) from in use.";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Release failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Release Fusion tubs", ex);
            }
        }

        // ===== CSV import / template / export (mirrors material master) =====
        // Columns: TubNo,EPC,Colour,CapacityKg,Bay
        public static string GetCsvTemplate()
        {
            return "TubNo,EPC,Colour,CapacityKg,Bay\r\n" +
                   "10,00000101,Orange,50,BAY-01\r\n" +
                   "11,00000102,Blue,50,\r\n";
        }

        private sealed record TubCsvRow(string TubNo, string Epc, string Colour, double CapacityKg, string Bay, int RowNumber);

        private void DownloadTemplate()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Save Tub Import Template",
                    Filter = "CSV Files (*.csv)|*.csv",
                    FileName = "TubImportTemplate.csv",
                    DefaultExt = "csv"
                };
                if (dialog.ShowDialog() != true) return;
                File.WriteAllText(dialog.FileName, GetCsvTemplate());
                _ = _audit.LogCsvTemplateDownloadedAsync(_username, "Tub", dialog.FileName);
                MessageBox.Show(
                    $"Template saved to:\n{dialog.FileName}\n\nFill in the columns and use 'Import' to import.\n\nNotes:\n  - TubNo: required, min 2 chars, letters/digits/_/- only, must be unique.\n  - EPC: required, digits only, min 4 digits, must be unique.\n  - Colour: Orange or Blue.\n  - CapacityKg: > 0 and <= 1000.\n  - Bay: optional, max 30 chars.",
                    "Template Downloaded",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving template: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ExportCsvAsync()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Export Tubs",
                    Filter = "CSV Files (*.csv)|*.csv",
                    FileName = $"Tubs_{DateTime.Now:yyyyMMddHHmmss}.csv",
                    DefaultExt = "csv"
                };
                if (dialog.ShowDialog() != true) return;
                var sb = new StringBuilder();
                sb.AppendLine("TubNo,EPC,Colour,CapacityKg,Bay");
                foreach (var t in _all.OrderBy(t => t.TubId, StringComparer.Ordinal))
                {
                    sb.AppendLine($"{EscapeCsv(t.TubId)},{EscapeCsv(t.Epc)},{EscapeCsv(t.Colour)},{t.CapacityKg.ToString("0.###", CultureInfo.InvariantCulture)},{EscapeCsv(t.BayId)}");
                }
                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
                await _audit.LogReportExportedAsync(_username, "Fusion Tub Master (CSV)", _all.Count);
                StatusText = $"Exported {_all.Count} tub(s) to {Path.GetFileName(dialog.FileName)}.";
                MessageBox.Show(
                    $"Exported {_all.Count} tub(s) to:\n{dialog.FileName}",
                    "Export Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText = $"Export failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Export Fusion tubs CSV", ex);
                MessageBox.Show($"Error exporting tubs: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            await Task.CompletedTask;
        }

        private async Task ImportCsvAsync()
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Select Tub CSV File",
                    Filter = "CSV Files (*.csv)|*.csv",
                    DefaultExt = "csv"
                };
                if (dialog.ShowDialog() != true) return;

                string[] lines;
                try
                {
                    lines = await File.ReadAllLinesAsync(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not read file: {ex.Message}", "Import",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (lines.Length < 2)
                {
                    MessageBox.Show("CSV file is empty or has no data rows.", "Import",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var header = lines[0].Split(',');
                if (header.Length < 4
                    || !header[0].Trim().Equals("TubNo", StringComparison.OrdinalIgnoreCase)
                    || !header[1].Trim().Equals("EPC", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Invalid CSV format. Expected header: TubNo,EPC,Colour,CapacityKg,Bay", "Import",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var errors = new List<string>();
                var rows = new List<TubCsvRow>();
                var tubNosInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var epcsInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var existingTubNos = new HashSet<string>(_all.Select(t => t.TubId ?? string.Empty), StringComparer.OrdinalIgnoreCase);
                var existingEpcs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in _all)
                    if (!string.IsNullOrWhiteSpace(t.Epc))
                        existingEpcs[t.Epc.Trim()] = t.TubId ?? string.Empty;

                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int rowNo = i + 1;
                    var cols = ParseCsvLine(line);
                    var tubNo = cols.Length > 0 ? cols[0].Trim() : string.Empty;
                    var epc = cols.Length > 1 ? cols[1].Trim() : string.Empty;
                    var colour = cols.Length > 2 ? cols[2].Trim() : "Orange";
                    if (string.IsNullOrWhiteSpace(colour)) colour = "Orange";
                    var capText = cols.Length > 3 ? cols[3].Trim() : "50";
                    if (string.IsNullOrWhiteSpace(capText)) capText = "50";
                    var bay = cols.Length > 4 ? cols[4].Trim() : string.Empty;

                    var tubErr = ValidateTubNo(tubNo);
                    if (tubErr != null) { errors.Add($"Row {rowNo}: {tubErr}"); continue; }
                    var epcErr = ValidateEpc(epc);
                    if (epcErr != null) { errors.Add($"Row {rowNo}: {epcErr}"); continue; }
                    var colErr = ValidateColour(colour);
                    if (colErr != null) { errors.Add($"Row {rowNo}: {colErr}"); continue; }
                    var capErr = ValidateCapacity(capText);
                    if (capErr != null) { errors.Add($"Row {rowNo}: {capErr}"); continue; }
                    var bayErr = ValidateBay(bay);
                    if (bayErr != null) { errors.Add($"Row {rowNo}: {bayErr}"); continue; }
                    if (!TryParseCapacity(capText, out var cap))
                    {
                        errors.Add($"Row {rowNo}: Capacity must be a positive number.");
                        continue;
                    }

                    if (!tubNosInFile.Add(tubNo))
                    {
                        errors.Add($"Row {rowNo}: Duplicate TubNo '{tubNo}' within the file.");
                        continue;
                    }
                    if (!epcsInFile.Add(epc))
                    {
                        errors.Add($"Row {rowNo}: Duplicate EPC '{epc}' within the file.");
                        continue;
                    }
                    if (existingEpcs.TryGetValue(epc, out var ownerTub)
                        && !string.Equals(ownerTub, tubNo, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"Row {rowNo}: EPC '{epc}' already belongs to tub '{ownerTub}'.");
                        continue;
                    }

                    // Normalise colour casing.
                    var normColour = string.Equals(colour, "Blue", StringComparison.OrdinalIgnoreCase) ? "Blue" : "Orange";
                    rows.Add(new TubCsvRow(tubNo, epc, normColour, cap, bay, rowNo));
                }

                if (errors.Count > 0)
                {
                    Views.ScrollableMessageBox.Show(
                        $"The following errors were found in the CSV file:\n\n{string.Join("\n", errors)}",
                        "CSV Validation Errors",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (rows.Count == 0)
                {
                    MessageBox.Show("No valid rows found in the CSV file.", "Import",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var duplicates = rows.Where(r => existingTubNos.Contains(r.TubNo)).ToList();
                var overrideTubNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (duplicates.Count > 0)
                {
                    var dupSummary = string.Join("\n", duplicates.Select(d => $"  Row {d.RowNumber}: [{d.TubNo}] EPC {d.Epc}"));
                    var choice = Views.ScrollableMessageBox.Show(
                        $"{duplicates.Count} duplicate tub no(s) already exist in the system:\n\n{dupSummary}\n\n" +
                        "Click YES to override (update) the existing records.\n" +
                        "Click NO to skip duplicates and only import new records.\n" +
                        "Click CANCEL to abort the import.",
                        "Duplicate Tubs Found",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);
                    if (choice == MessageBoxResult.Cancel) return;
                    if (choice == MessageBoxResult.Yes)
                        foreach (var d in duplicates)
                            overrideTubNos.Add(d.TubNo);
                }

                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                int inserted = 0, updated = 0, skipped = 0;
                foreach (var r in rows)
                {
                    bool exists = existingTubNos.Contains(r.TubNo);
                    try
                    {
                        if (exists)
                        {
                            if (overrideTubNos.Contains(r.TubNo))
                            {
                                await repo.UpdateTubAsync(r.TubNo, r.Epc, r.Colour, r.CapacityKg, r.Bay);
                                updated++;
                            }
                            else
                            {
                                skipped++;
                            }
                        }
                        else
                        {
                            await repo.AddTubAsync(r.TubNo, r.Epc, r.Colour, r.CapacityKg, r.Bay, DateTime.Today.ToString("yyyy-MM-dd"));
                            inserted++;
                            existingTubNos.Add(r.TubNo);
                        }
                    }
                    catch (Exception ex) when (ex.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"Row {r.RowNumber}: '{r.TubNo}' conflicts with an existing record ({ex.Message}).");
                    }
                }

                await LoadAsync();

                await _audit.LogFusionEventAsync(_username, "Fusion_Tub_Import",
                    "FusionTub", null,
                    $"Tub CSV import: {inserted} inserted, {updated} updated, {skipped} skipped, {errors.Count} error(s).",
                    AuditCategory.Material, errors.Count > 0 ? AuditSeverity.Warning : AuditSeverity.Info);

                if (errors.Count > 0)
                {
                    Views.ScrollableMessageBox.Show(
                        $"Import finished with {errors.Count} error(s):\n\n{string.Join("\n", errors)}\n\nInserted: {inserted}\nUpdated: {updated}\nSkipped: {skipped}",
                        "Import Completed With Errors",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    Views.ScrollableMessageBox.Show(
                        $"Import completed successfully!\n\n  Inserted : {inserted}\n  Updated  : {updated}\n  Skipped  : {skipped}",
                        "Import Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                StatusText = $"CSV import: {inserted} inserted, {updated} updated, {skipped} skipped.";
            }
            catch (Exception ex)
            {
                StatusText = $"Import failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Import Fusion tubs CSV", ex);
                MessageBox.Show($"Error importing CSV: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result.ToArray();
        }
    }
}
