using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Views;
using Microsoft.Win32;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class MaterialsViewModel : BaseViewModel
    {
        private readonly ICurrentUserContext _main;
        private readonly MaterialService _service;
        private readonly CategoryService _categoryService;
        private readonly VariantService _variantService;
        private readonly AuditService _auditService;

        public ObservableCollection<Material> Materials { get; } = new();
        public ObservableCollection<Material> PagedMaterials { get; } = new();
        public ObservableCollection<Category> ActiveCategories { get; } = new();
        public ObservableCollection<Variant> ActiveVariants { get; } = new();

        public string Title => IsCreating && CreateViewModel != null ? CreateViewModel.Title : "Materials";
        
        // Permission-based properties
        public bool CanCreateMaterial => HasPermission(_main.CurrentUser, Permission.CreateMaterial);
        public bool CanEditMaterial => HasPermission(_main.CurrentUser, Permission.EditMaterial);
        public bool CanDeleteMaterial => HasPermission(_main.CurrentUser, Permission.DeleteMaterial);
        public bool CanImportMaterial => HasPermission(_main.CurrentUser, Permission.ImportMaterial);
        public bool CanDownloadMaterialTemplate => HasPermission(_main.CurrentUser, Permission.DownloadMaterialTemplate);
        public bool CanExportMaterial => HasPermission(_main.CurrentUser, Permission.ExportMaterial);

        public ICommand RefreshCommand { get; }
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
        public ICommand ExportMaterialsCommand { get; }
        public ICommand CopyCodeCommand { get; }

        private string _filterText = string.Empty;
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    CurrentPage = 1;
                    ApplyFilterAndPage();
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
                    ApplyFilterAndPage();
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
                    ApplyFilterAndPage();
                    RaisePropertyChanged(nameof(PageInfo));
                    RaisePropertyChanged(nameof(TotalPages));
                    RaisePropertyChanged(nameof(ShowingText));
                }
            }
        }

        private int _totalPages = 1;
        public int TotalPages
        {
            get => _totalPages;
            private set
            {
                if (SetProperty(ref _totalPages, value))
                    RaisePropertyChanged(nameof(PageInfo));
            }
        }

        private int _totalRecords;
        public int TotalRecords
        {
            get => _totalRecords;
            private set
            {
                if (SetProperty(ref _totalRecords, value))
                    RaisePropertyChanged(nameof(PageInfo));
            }
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

        private Material? _selected;
        public Material? Selected
        {
            get => _selected;
            set => SetProperty(ref _selected, value);
        }

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

        private MaterialsCreateViewModel? _createViewModel;
        public MaterialsCreateViewModel? CreateViewModel
        {
            get => _createViewModel;
            set
            {
                if (SetProperty(ref _createViewModel, value))
                    RaisePropertyChanged(nameof(Title));
            }
        }

        public MaterialsViewModel(ICurrentUserContext main)
        {
            _main = main;
            
            // Use centralized database path from configuration so restore/import operates on same file
            var dbPath = AppConfigurationManager.GetDatabasePath();
            
            _service = new MaterialService(new MaterialRepository(dbPath));
            _categoryService = new CategoryService(new CategoryRepository(dbPath));
            _variantService = new VariantService(new VariantRepository(dbPath));
            _auditService = new AuditService();

            RefreshCommand = new RelayCommand(async _ => await LoadAsync());
            StartCreateCommand = new RelayCommand(async _ => await StartCreateAsync(), _ => CanCreateMaterial);
            StartUpdateRowCommand = new RelayCommand(async param => await StartUpdateAsync(param as Material), _ => CanEditMaterial);
            DeleteCommand = new RelayCommand(async param => await DeleteAsync(param as Material), _ => CanDeleteMaterial);
            NextPageCommand = new RelayCommand(_ => { if (CurrentPage < TotalPages) CurrentPage++; });
            PrevPageCommand = new RelayCommand(_ => { if (CurrentPage > 1) CurrentPage--; });
            FirstPageCommand = new RelayCommand(_ => { if (CurrentPage != 1) CurrentPage = 1; });
            LastPageCommand = new RelayCommand(_ => { if (CurrentPage != TotalPages) CurrentPage = TotalPages; });
            GoToPageCommand = new RelayCommand(param =>
            {
                if (param is int page && page >= 1 && page <= Math.Max(1, TotalPages) && page != CurrentPage)
                    CurrentPage = page;
            });
            ImportCsvCommand = new RelayCommand(async _ => await ImportCsvAsync(), _ => CanImportMaterial);
            DownloadTemplateCommand = new RelayCommand(_ => DownloadTemplate(), _ => CanDownloadMaterialTemplate);
            ExportMaterialsCommand = new RelayCommand(async _ => await ExportCsvAsync(), _ => CanExportMaterial);
            CopyCodeCommand = new RelayCommand(param => CopyCode(param as string ?? string.Empty));

            // Wait for app initialization to complete before loading data
            _ = EnsureInitializedAndLoadAsync();
        }

        // Helper method to get current username for audit logging
        public string GetCurrentUsername()
        {
            return _main.CurrentUser?.Username ?? "Unknown";
        }

        private async Task EnsureInitializedAndLoadAsync()
        {
            try
            {
                // Await the global initialization task (set in App.OnStartup)
                await AppInitialization.Task;
            }
            catch
            {
                // Swallow: initialization errors already handled in App
            }

            await LoadAsync();
        }

        private void CopyCode(string code)
        {
            try
            {
                if (!string.IsNullOrEmpty(code))
                {
                    Clipboard.SetText(code);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MaterialsViewModel] Failed to copy code: {ex.Message}");
            }
        }

        private async Task LoadAsync()
        {
            try
            {
                Materials.Clear();
                var list = await _service.GetAllAsync();
                foreach (var m in list)
                    Materials.Add(m);

                await LoadActiveCategories();
                await LoadActiveVariants();

                CurrentPage = 1;
                ApplyFilterAndPage();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading materials: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadActiveCategories()
        {
            await _categoryService.InitializeAsync();
            ActiveCategories.Clear();
            var categories = await _categoryService.GetAllAsync();
            foreach (var cat in categories.Where(c => c.Status))
            {
                ActiveCategories.Add(cat);
            }
        }

        private async Task LoadActiveVariants()
        {
            await _variantService.InitializeAsync();
            ActiveVariants.Clear();
            var variants = await _variantService.GetAllAsync();
            foreach (var variant in variants.Where(v => v.Active))
            {
                ActiveVariants.Add(variant);
            }
        }

        private void ApplyFilterAndPage()
        {
            var query = Materials.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(FilterText))
            {
                var search = FilterText.Trim();
                query = query.Where(m => MaterialMatches(m, search));
            }

            var total = query.Count();
            TotalRecords = total;
            TotalPages = total == 0 ? 1 : (int)System.Math.Ceiling(total / (double)PageSize);
            if (CurrentPage > TotalPages) CurrentPage = TotalPages;
            RaisePropertyChanged(nameof(PageInfo));
            RaisePropertyChanged(nameof(ShowingText));
            var page = query.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();

            PagedMaterials.Clear();
            var index = (CurrentPage - 1) * PageSize + 1;
            foreach (var m in page)
            {
                m.SerialNumber = index++;
                PagedMaterials.Add(m);
            }

            RefreshPageNumbers();
            RaisePropertyChanged(nameof(CurrentPage));
            RaisePropertyChanged(nameof(TotalPages));
            RaisePropertyChanged(nameof(PageInfo));
            RaisePropertyChanged(nameof(ShowingText));

            if (PagedMaterials.Count > 0)
            {
                Selected = PagedMaterials[0];
            }
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

        /// <summary>
        /// Case-insensitive match of the search term against every displayed column
        /// of the materials grid (S No, Product Name, Code, Category, Variant,
        /// Target/Min/Max Weight, Status). Nullable values are handled gracefully.
        /// </summary>
        private static bool MaterialMatches(Material m, string search)
        {
            return (m.ProductName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (m.ProductCode?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (m.CategoryName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (m.VariantName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (m.StatusText?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || m.Id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                || m.SerialNumber.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                || $"{m.TargetWeight:N2} kg".Contains(search, StringComparison.OrdinalIgnoreCase)
                || $"{m.MinWeight:N2} kg".Contains(search, StringComparison.OrdinalIgnoreCase)
                || $"{m.MaxWeight:N2} kg".Contains(search, StringComparison.OrdinalIgnoreCase)
                || m.ShelfLife.ToString().Contains(search, StringComparison.OrdinalIgnoreCase);
        }

        public async Task StartCreateAsync()
        {
            await LoadActiveCategories();
            await LoadActiveVariants();
            CreateViewModel = new MaterialsCreateViewModel(this, null, ActiveCategories, ActiveVariants);
            IsCreating = true;
        }

        public async Task StartUpdateAsync(Material? material)
        {
            if (material == null) return;
            Selected = material;
            await LoadActiveCategories();
            await LoadActiveVariants();
            CreateViewModel = new MaterialsCreateViewModel(this, material, ActiveCategories, ActiveVariants);
            IsCreating = true;
        }

        public void StartCreate()
        {
            CreateViewModel = new MaterialsCreateViewModel(this, null, ActiveCategories, ActiveVariants);
            IsCreating = true;
        }

        public void StartUpdate(Material? material)
        {
            if (material == null) return;
            Selected = material;
            CreateViewModel = new MaterialsCreateViewModel(this, material, ActiveCategories, ActiveVariants);
            IsCreating = true;
        }

        public async Task CancelCreate()
        {
            IsCreating = false;
            Selected = null;
            CreateViewModel = null;
            await LoadAsync();
        }

        private void DownloadTemplate()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Save Material Import Template",
                    Filter = "CSV Files (*.csv)|*.csv",
                    FileName = "MaterialImportTemplate.csv",
                    DefaultExt = "csv"
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, MaterialService.GetCsvTemplate());
                    _ = _auditService.LogCsvTemplateDownloadedAsync(GetCurrentUsername(), "Material", dialog.FileName);
                    MessageBox.Show(
                        $"Template saved to:\n{dialog.FileName}\n\n" +
                        $"Fill in the columns and use 'Import CSV' to import.\n\n" +
                        $"Notes:\n" +
                        $"  - CategoryName and VariantName must match existing records.\n" +
                        $"  - TargetWeightUOM: Kg or Gram\n" +
                        $"  - CheckWeigherRequired: Yes or No\n" +
                        $"  - IsActive: Active or Inactive\n" +
                        $"  - MinWeight / MaxWeight: only used in Absolute tolerance mode\n" +
                        $"  - ToleranceMode: Relative or Absolute",
                        "Template Downloaded",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
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
                CheckPermissionOrThrow(_main.CurrentUser, Permission.ExportMaterial);

                var materials = await _service.GetAllAsync();

                var dialog = new SaveFileDialog
                {
                    Title = "Export Materials",
                    Filter = "CSV Files (*.csv)|*.csv",
                    FileName = $"Material_{DateTime.Now:yyyyMMddHHmmss}.csv",
                    DefaultExt = "csv"
                };

                if (dialog.ShowDialog() != true) return;

                var sb = new StringBuilder();
                sb.AppendLine("ProductName,ProductCode,CategoryName,VariantName,TargetWeight,TargetWeightUOM,TMinusPercent,TPlusPercent,ShelfLife,CheckWeigherRequired,IsActive,MinWeight,MaxWeight,ToleranceMode");
                foreach (var m in materials)
                {
                    sb.AppendLine($"{EscapeCsv(m.ProductName)},{EscapeCsv(m.ProductCode)},{EscapeCsv(m.CategoryName)},{EscapeCsv(m.VariantName)},{m.TargetWeight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},{(m.TargetWeightUOM == WeightUOM.Gram ? "Gram" : "Kg")},{m.TMinusPercent.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},{m.TPlusPercent.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},{m.ShelfLife},{(m.CheckWeigherRequired ? "Yes" : "No")},{(m.IsActive ? "Active" : "Inactive")},{m.MinWeight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},{m.MaxWeight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},{EscapeCsv(m.ToleranceMode)}");
                }
                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);

                await _auditService.LogAsync(new AuditEntry
                {
                    Action = "Material_CsvExport",
                    Category = AuditCategory.Configuration,
                    Username = GetCurrentUsername(),
                    EntityType = "Material",
                    Details = $"CSV export: {materials.Count()} material{(materials.Count() == 1 ? "" : "s")} exported to {System.IO.Path.GetFileName(dialog.FileName)}.",
                    Severity = AuditSeverity.Info
                });

                MessageBox.Show(
                    $"Exported {materials.Count()} material{(materials.Count() == 1 ? "" : "s")} to:\n{dialog.FileName}",
                    "Export Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await _auditService.LogErrorAsync(GetCurrentUsername(), "Export materials CSV", ex);
                MessageBox.Show($"Error exporting materials: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }

        private async Task ImportCsvAsync()
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Select Material CSV File",
                    Filter = "CSV Files (*.csv)|*.csv",
                    DefaultExt = "csv"
                };

                if (dialog.ShowDialog() != true) return;

                var filePath = dialog.FileName;
                var username = GetCurrentUsername();

                // Require password confirmation before import
                var owner = Application.Current?.MainWindow;
                var passwordConfirmed = await PasswordPromptWindow.ConfirmPasswordAsync(owner ?? new Window(), username);
                if (!passwordConfirmed)
                {
                    await _auditService.LogSecurityViolationAsync(
                        username,
                        "Import materials CSV",
                        "Password verification failed");

                    MessageBox.Show(
                        "Password incorrect or cancelled.",
                        "Access Denied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Load all categories and variants for resolution
                var allCategories = await _categoryService.GetAllAsync();
                var allVariants = await _variantService.GetAllAsync();

                var (duplicates, allRows, errors) = await _service.ParseCsvAsync(filePath, allCategories, allVariants);

                if (errors.Count > 0)
                {
                    ScrollableMessageBox.Show(
                        $"The following errors were found in the CSV file:\n\n{string.Join("\n", errors)}",
                        "CSV Validation Errors",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (allRows.Count == 0)
                {
                    MessageBox.Show("No valid rows found in the CSV file.", "Import",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var overrideCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (duplicates.Count > 0)
                {
                    var dupSummary = string.Join("\n", duplicates.Select(d =>
                        $"  Row {d.RowNumber}: [{d.ProductCode}] {d.ProductName}"));

                    var choice = ScrollableMessageBox.Show(
                        $"{duplicates.Count} duplicate product code(s) already exist in the system:\n\n{dupSummary}\n\n" +
                        $"Click YES to override (update) the existing records.\n" +
                        $"Click NO to skip duplicates and only import new records.\n" +
                        $"Click CANCEL to abort the import.",
                        "Duplicate Materials Found",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    if (choice == MessageBoxResult.Cancel)
                        return;

                    if (choice == MessageBoxResult.Yes)
                    {
                        foreach (var d in duplicates)
                            overrideCodes.Add(d.ProductCode);
                    }
                }

                var result = await _service.ImportParsedAsync(allRows, overrideCodes);

                await _auditService.LogAsync(new AuditEntry
                {
                    Action = "Material_CsvImport",
                    Category = AuditCategory.Configuration,
                    Username = username,
                    EntityType = "Material",
                    Details = $"CSV import: {result.Inserted} inserted, {result.Updated} updated, {result.Skipped} skipped.",
                    Severity = AuditSeverity.Info
                });

                await LoadAsync();

                ScrollableMessageBox.Show(
                    $"Import completed successfully!\n\n" +
                    $"  Inserted : {result.Inserted}\n" +
                    $"  Updated  : {result.Updated}\n" +
                    $"  Skipped  : {result.Skipped}",
                    "Import Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await _auditService.LogErrorAsync(GetCurrentUsername(), "Import materials CSV", ex);
                MessageBox.Show($"Error importing CSV: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteAsync(Material? material)
        {
            if (material == null) return;

            try
            {
                CheckPermissionOrThrow(_main.CurrentUser, Permission.DeleteMaterial);

                var owner = Application.Current?.MainWindow;
                var username = _main.CurrentUser?.Username ?? string.Empty;
                var passwordConfirmed = await PasswordPromptWindow.ConfirmPasswordAsync(owner ?? new Window(), username);
                if (!passwordConfirmed)
                {
                    await _auditService.LogSecurityViolationAsync(
                        username,
                        $"Delete material {material.ProductCode}",
                        "Password verification failed");

                    MessageBox.Show(
                        "Password incorrect or cancelled.",
                        "Access Denied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show(
                    $"Are you sure you want to delete product '{material.ProductName}'?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    await _auditService.LogMaterialDeletedAsync(username, material);

                    await _service.DeleteAsync(material.Id);
                    await LoadAsync();
                    MessageBox.Show("Product deleted successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (UnauthorizedAccessException)
            {
                await _auditService.LogSecurityViolationAsync(
                    _main.CurrentUser?.Username ?? "Unknown",
                    $"Delete material {material.ProductCode}",
                    "Insufficient permissions");

                MessageBox.Show(
                    "You do not have permission to delete materials.\n\nPlease contact your administrator.",
                    "Access Denied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                await _auditService.LogErrorAsync(
                    _main.CurrentUser?.Username ?? "Unknown",
                    "Delete material",
                    ex);

                MessageBox.Show($"Error deleting product: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}