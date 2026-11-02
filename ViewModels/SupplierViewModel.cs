using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Security;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Views;
using Microsoft.Win32;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class SupplierViewModel : BaseViewModel
    {
        private readonly ICurrentUserContext _main;
        private readonly SupplierService _supplierService;
        private readonly AuditService _auditService;

        public string Title => IsCreating && CreateViewModel != null ? CreateViewModel.Title : "Supplier Master";

        public bool CanCreateSupplier => HasPermission(_main.CurrentUser, Permission.CreateSupplier);
        public bool CanEditSupplier => HasPermission(_main.CurrentUser, Permission.EditSupplier);
        public bool CanDeleteSupplier => HasPermission(_main.CurrentUser, Permission.DeleteSupplier);
        public bool CanImportSupplier => HasPermission(_main.CurrentUser, Permission.ImportSupplier);
        public bool CanDownloadSupplierTemplate => HasPermission(_main.CurrentUser, Permission.DownloadSupplierTemplate);
        public bool CanExportSupplier => HasPermission(_main.CurrentUser, Permission.ExportSupplier);

        private bool _isCreating;
        public bool IsCreating
        {
            get => _isCreating;
            set
            {
                if (SetProperty(ref _isCreating, value))
                    RaisePropertyChanged(nameof(Title));
            }
        }

        private SupplierCreateViewModel? _createViewModel;
        public SupplierCreateViewModel? CreateViewModel
        {
            get => _createViewModel;
            set
            {
                if (SetProperty(ref _createViewModel, value))
                    RaisePropertyChanged(nameof(Title));
            }
        }

        public ICommand StartCreateCommand { get; }
        public ICommand StartUpdateRowCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand LastPageCommand { get; }
        public ICommand GoToPageCommand { get; }
        public ObservableCollection<PageNumberItem> PageNumbers { get; } = new();
        public ICommand ImportCsvCommand { get; }
        public ICommand DownloadTemplateCommand { get; }
        public ICommand ExportSuppliersCommand { get; }

        public ObservableCollection<Supplier> PagedSuppliers { get; } = new();

        private Supplier? _selected;
        public Supplier? Selected
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

        public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)FilteredSuppliers.Count / PageSize));

        private int _totalRecords;
        public int TotalRecords
        {
            get => _totalRecords;
            private set => SetProperty(ref _totalRecords, value);
        }

        public string PageInfo => $"Page {CurrentPage} of {TotalPages}  ({TotalRecords} records)";

        // Basic-Datatables style footer: "Showing 1 to 10 of 14 entries"
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

        private ObservableCollection<Supplier> _allSuppliers = new();
        private ObservableCollection<Supplier> FilteredSuppliers
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FilterText))
                    return _allSuppliers;
                var search = FilterText.Trim();
                return new ObservableCollection<Supplier>(_allSuppliers.Where(s => SupplierMatches(s, search)));
            }
        }

        private static bool SupplierMatches(Supplier s, string search)
        {
            return (s.SupplierCode?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.SupplierName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.DisplayName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.SupplierType?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.BusinessCategory?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.ContactPerson?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.Phone?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.Mobile?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.TaxId?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.StatusText?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        public SupplierViewModel(ICurrentUserContext main)
        {
            _main = main;
            _auditService = new AuditService();
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuitePlatforms", "proweighsuiteplatform.db");
            _supplierService = new SupplierService(new SupplierRepository(dbPath));

            StartCreateCommand = new RelayCommand(_ => StartCreate(), _ => CanCreateSupplier);
            StartUpdateRowCommand = new RelayCommand(param => StartUpdate(param as Supplier), _ => CanEditSupplier);
            DeleteCommand = new RelayCommand(async param => await DeleteSupplierAsync(param as Supplier), _ => CanDeleteSupplier);
            PrevPageCommand = new RelayCommand(_ => { if (CurrentPage > 1) CurrentPage--; }, _ => CurrentPage > 1);
            NextPageCommand = new RelayCommand(_ => { if (CurrentPage < TotalPages) CurrentPage++; }, _ => CurrentPage < TotalPages);
            FirstPageCommand = new RelayCommand(_ => { if (CurrentPage != 1) CurrentPage = 1; });
            LastPageCommand = new RelayCommand(_ => { if (CurrentPage != TotalPages) CurrentPage = TotalPages; });
            GoToPageCommand = new RelayCommand(param =>
            {
                if (param is int page && page >= 1 && page <= Math.Max(1, TotalPages) && page != CurrentPage)
                    CurrentPage = page;
            });
            ImportCsvCommand = new RelayCommand(async _ => await ImportCsvAsync(), _ => CanImportSupplier);
            DownloadTemplateCommand = new RelayCommand(_ => DownloadTemplate(), _ => CanDownloadSupplierTemplate);
            ExportSuppliersCommand = new RelayCommand(async _ => await ExportCsvAsync(), _ => CanExportSupplier);

            _ = LoadSuppliersAsync();
        }

        public string GetCurrentUsername() => _main.CurrentUser?.Username ?? "Unknown";

        public void StartCreate()
        {
            CreateViewModel = new SupplierCreateViewModel(this, null);
            IsCreating = true;
        }

        public void StartUpdate(Supplier? supplier)
        {
            if (supplier == null) return;
            Selected = supplier;
            CreateViewModel = new SupplierCreateViewModel(this, supplier);
            IsCreating = true;
        }

        public async Task CancelCreate()
        {
            IsCreating = false;
            Selected = null;
            CreateViewModel = null;
            await LoadSuppliersAsync();
        }

        internal SupplierService Service => _supplierService;

        private async Task LoadSuppliersAsync()
        {
            try
            {
                var list = await _supplierService.GetAllAsync();
                _allSuppliers.Clear();
                foreach (var s in list)
                    _allSuppliers.Add(s);
                if (CurrentPage > TotalPages) _currentPage = TotalPages;
                if (_currentPage < 1) _currentPage = 1;
                UpdatePagedData();
                RaisePropertyChanged(nameof(TotalPages));
                RaisePropertyChanged(nameof(PageInfo));
                RaisePropertyChanged(nameof(ShowingText));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading suppliers: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            // Sliding window of max 5 page buttons (Basic Datatables style: Previous 1 2 Next)
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

        private async Task DeleteSupplierAsync(Supplier? supplier)
        {
            if (supplier == null) return;
            try
            {
                CheckPermissionOrThrow(_main.CurrentUser, Permission.DeleteSupplier);
                var owner = Application.Current?.MainWindow;
                var username = GetCurrentUsername();
                var confirmed = await PasswordPromptWindow.ConfirmPasswordAsync(owner ?? new Window(), username);
                if (!confirmed)
                {
                    await _auditService.LogSecurityViolationAsync(username, $"Delete supplier {supplier.SupplierCode}", "Password verification failed");
                    MessageBox.Show("Password incorrect or cancelled.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var result = MessageBox.Show($"Are you sure you want to delete supplier '{supplier.SupplierCode} - {supplier.SupplierName}'?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    await _supplierService.DeleteAsync(supplier.Id);
                    await _auditService.LogAsync(new AuditEntry
                    {
                        Action = "Supplier_Deleted",
                        Category = AuditCategory.Configuration,
                        Username = username,
                        EntityType = "Supplier",
                        EntityId = supplier.Id.ToString(),
                        Details = $"Supplier deleted: {supplier.SupplierCode} - {supplier.SupplierName}",
                        OldValue = $"Code={supplier.SupplierCode}, Name={supplier.SupplierName}",
                        Severity = AuditSeverity.Warning
                    });
                    await LoadSuppliersAsync();
                    MessageBox.Show("Supplier deleted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("You do not have permission to delete suppliers.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                await _auditService.LogErrorAsync(GetCurrentUsername(), "Delete supplier", ex);
                MessageBox.Show($"Error deleting supplier: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DownloadTemplate()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Download Supplier Template",
                    Filter = "CSV Files (*.csv)|*.csv",
                    FileName = "Supplier_Template.csv",
                    DefaultExt = "csv"
                };
                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, SupplierService.GetCsvTemplate());
                    MessageBox.Show($"Sample template saved to:\n{dialog.FileName}", "Template Downloaded",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving template: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ExportCsvAsync()
        {
            try
            {
                CheckPermissionOrThrow(_main.CurrentUser, Permission.ExportSupplier);
                var list = await _supplierService.GetAllAsync();
                var dialog = new SaveFileDialog
                {
                    Title = "Export Suppliers",
                    Filter = "CSV Files (*.csv)|*.csv",
                    FileName = $"Supplier_{DateTime.Now:yyyyMMddHHmmss}.csv",
                    DefaultExt = "csv"
                };
                if (dialog.ShowDialog() != true) return;
                var sb = new StringBuilder();
                sb.AppendLine("SupplierCode,SupplierName,DisplayName,SupplierType,BusinessCategory,Status,LegalEntityType,RegistrationNumber,TaxId,Website,ContactPerson,Designation,Phone,Mobile,Email");
                foreach (var s in list)
                    sb.AppendLine($"{E(s.SupplierCode)},{E(s.SupplierName)},{E(s.DisplayName)},{E(s.SupplierType)},{E(s.BusinessCategory)},{s.StatusText},{E(s.LegalEntityType)},{E(s.RegistrationNumber)},{E(s.TaxId)},{E(s.Website)},{E(s.ContactPerson)},{E(s.Designation)},{E(s.Phone)},{E(s.Mobile)},{E(s.Email)}");
                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
                await _auditService.LogAsync(new AuditEntry
                {
                    Action = "Supplier_CsvExport",
                    Category = AuditCategory.Configuration,
                    Username = GetCurrentUsername(),
                    EntityType = "Supplier",
                    Details = $"CSV export: {list.Count} suppliers exported.",
                    Severity = AuditSeverity.Info
                });
                MessageBox.Show($"Exported {list.Count} suppliers to:\n{dialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting suppliers: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string E(string? v)
        {
            if (string.IsNullOrEmpty(v)) return string.Empty;
            if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }

        private async Task ImportCsvAsync()
        {
            try
            {
                var dialog = new OpenFileDialog { Title = "Select Supplier CSV File", Filter = "CSV Files (*.csv)|*.csv", DefaultExt = "csv" };
                if (dialog.ShowDialog() != true) return;
                var username = GetCurrentUsername();
                var owner = Application.Current?.MainWindow;
                if (!await PasswordPromptWindow.ConfirmPasswordAsync(owner ?? new Window(), username))
                {
                    MessageBox.Show("Password incorrect or cancelled.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var (rows, errors) = await _supplierService.ParseCsvAsync(dialog.FileName);
                if (errors.Count > 0)
                {
                    ScrollableMessageBox.Show("Errors found:\n\n" + string.Join("\n", errors), "CSV Validation Errors", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (rows.Count == 0)
                {
                    MessageBox.Show("No valid rows found.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var choice = MessageBox.Show($"{rows.Count} supplier(s) found. Overwrite existing codes?\n\nYES = update existing\nNO = skip duplicates\nCANCEL = abort",
                    "Import Suppliers", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (choice == MessageBoxResult.Cancel) return;
                var result = await _supplierService.ImportParsedAsync(rows, choice == MessageBoxResult.Yes);
                await _auditService.LogAsync(new AuditEntry
                {
                    Action = "Supplier_CsvImport",
                    Category = AuditCategory.Configuration,
                    Username = username,
                    EntityType = "Supplier",
                    Details = $"CSV import: {result.Inserted} inserted, {result.Updated} updated, {result.Skipped} skipped.",
                    Severity = AuditSeverity.Info
                });
                await LoadSuppliersAsync();
                MessageBox.Show($"Import complete!\nInserted: {result.Inserted}\nUpdated: {result.Updated}\nSkipped: {result.Skipped}",
                    "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing CSV: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
