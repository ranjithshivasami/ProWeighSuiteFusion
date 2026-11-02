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
    public class VariantViewModel : BaseViewModel
    {
        private readonly ICurrentUserContext _main;
        private readonly VariantService _variantService;
        private readonly AuditService _auditService;

        public string Title => IsCreating && CreateViewModel != null ? CreateViewModel.Title : "Variant Master";

        public bool CanCreateVariant => HasPermission(_main.CurrentUser, Permission.CreateVariant);
        public bool CanEditVariant => HasPermission(_main.CurrentUser, Permission.EditVariant);
        public bool CanDeleteVariant => HasPermission(_main.CurrentUser, Permission.DeleteVariant);
        public bool CanImportVariant => HasPermission(_main.CurrentUser, Permission.ImportVariant);
        public bool CanDownloadVariantTemplate => HasPermission(_main.CurrentUser, Permission.DownloadVariantTemplate);
        public bool CanExportVariant => HasPermission(_main.CurrentUser, Permission.ExportVariant);

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

        private VariantCreateViewModel? _createViewModel;
        public VariantCreateViewModel? CreateViewModel
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
        public ICommand ExportVariantsCommand { get; }

        public ObservableCollection<Variant> PagedVariants { get; } = new();

        private Variant? _selected;
        public Variant? Selected
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
                    RaisePropertyChanged(nameof(ShowingText));
                }
            }
        }

        public int TotalPages
        {
            get
            {
                var count = FilteredVariants.Count;
                if (count == 0) return 1;
                return (int)Math.Ceiling((double)count / PageSize);
            }
        }

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

        private ObservableCollection<Variant> _allVariants = new();
        private ObservableCollection<Variant> FilteredVariants
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FilterText))
                    return _allVariants;

                var search = FilterText.Trim();
                var filtered = _allVariants.Where(v => VariantMatches(v, search));

                return new ObservableCollection<Variant>(filtered);
            }
        }

        /// <summary>
        /// Case-insensitive match of the search term against every displayed column
        /// of the variant grid (S No, Name, Description, Active, Created Date).
        /// Nullable values are handled gracefully.
        /// </summary>
        private static bool VariantMatches(Variant variant, string search)
        {
            return (variant.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (variant.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (variant.ActiveText?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || variant.CreatedAt.ToString("g").Contains(search, StringComparison.OrdinalIgnoreCase)
                || variant.Id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                || variant.SerialNumber.ToString().Contains(search, StringComparison.OrdinalIgnoreCase);
        }

        public VariantViewModel(ICurrentUserContext main)
        {
            _main = main;
            _auditService = new AuditService();

            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuitePlatforms", "proweighsuiteplatform.db");
            _variantService = new VariantService(new VariantRepository(dbPath));

            StartCreateCommand = new RelayCommand(_ => StartCreate(), _ => CanCreateVariant);
            StartUpdateRowCommand = new RelayCommand(param => StartUpdate(param as Variant), _ => CanEditVariant);
            DeleteCommand = new RelayCommand(async param => await DeleteVariantAsync(param as Variant), _ => CanDeleteVariant);
            PrevPageCommand = new RelayCommand(_ => PrevPage(), _ => CurrentPage > 1);
            NextPageCommand = new RelayCommand(_ => NextPage(), _ => CurrentPage < TotalPages);
            FirstPageCommand = new RelayCommand(_ => { if (CurrentPage != 1) CurrentPage = 1; });
            LastPageCommand = new RelayCommand(_ => { if (CurrentPage != TotalPages) CurrentPage = TotalPages; });
            GoToPageCommand = new RelayCommand(param =>
            {
                if (param is int page && page >= 1 && page <= Math.Max(1, TotalPages) && page != CurrentPage)
                    CurrentPage = page;
            });
            ImportCsvCommand = new RelayCommand(async _ => await ImportCsvAsync(), _ => CanImportVariant);
            DownloadTemplateCommand = new RelayCommand(_ => DownloadTemplate(), _ => CanDownloadVariantTemplate);
            ExportVariantsCommand = new RelayCommand(async _ => await ExportCsvAsync(), _ => CanExportVariant);

            _ = LoadVariantsAsync();
        }

        public string GetCurrentUsername()
        {
            return _main.CurrentUser?.Username ?? "Unknown";
        }

        public void StartCreate()
        {
            CreateViewModel = new VariantCreateViewModel(this, null);
            IsCreating = true;
        }

        public void StartUpdate(Variant? variant)
        {
            if (variant == null) return;
            Selected = variant;
            CreateViewModel = new VariantCreateViewModel(this, variant);
            IsCreating = true;
        }

        public async Task CancelCreate()
        {
            IsCreating = false;
            Selected = null;
            CreateViewModel = null;
            await LoadVariantsAsync();
        }

        private async Task LoadVariantsAsync()
        {
            try
            {
                var variants = await _variantService.GetAllAsync();

                _allVariants.Clear();
                foreach (var variant in variants)
                {
                    _allVariants.Add(variant);
                }

                UpdatePagedData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading variants: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdatePagedData()
        {
            var filtered = FilteredVariants;
            TotalRecords = filtered.Count;
            if (CurrentPage < 1) _currentPage = 1;
            var pages = Math.Max(1, (int)Math.Ceiling((double)filtered.Count / Math.Max(1, PageSize)));
            if (_currentPage > pages) _currentPage = pages;
            var paged = filtered
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            PagedVariants.Clear();
            int serialNumber = (CurrentPage - 1) * PageSize + 1;
            foreach (var variant in paged)
            {
                variant.SerialNumber = serialNumber++;
                PagedVariants.Add(variant);
            }

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

        private void PrevPage()
        {
            if (CurrentPage > 1)
                CurrentPage--;
        }

        private void NextPage()
        {
            if (CurrentPage < TotalPages)
                CurrentPage++;
        }

        private void FirstPage()
        {
            CurrentPage = 1;
        }

        private void LastPage()
        {
            CurrentPage = TotalPages;
        }

        private void DownloadTemplate()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Download Variant Template",
                    Filter = "CSV Files (*.csv)|*.csv",
                    FileName = "Variant_Template.csv",
                    DefaultExt = "csv"
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, VariantService.GetCsvTemplate());
                    MessageBox.Show(
                        $"Sample template saved to:\n{dialog.FileName}\n\nFill in the Name, Description, and Active (Active/Inactive) columns and then use 'Import' to import.",
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
                CheckPermissionOrThrow(_main.CurrentUser, Permission.ExportVariant);

                var variants = await _variantService.GetAllAsync();

                var dialog = new SaveFileDialog
                {
                    Title = "Export Variants",
                    Filter = "CSV Files (*.csv)|*.csv",
                    FileName = $"Variant_{DateTime.Now:yyyyMMddHHmmss}.csv",
                    DefaultExt = "csv"
                };

                if (dialog.ShowDialog() != true) return;

                var sb = new StringBuilder();
                sb.AppendLine("Name,Description,Active");
                foreach (var variant in variants)
                {
                    sb.AppendLine($"{EscapeCsv(variant.Name)},{EscapeCsv(variant.Description)},{variant.ActiveText}");
                }
                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);

                await _auditService.LogAsync(new AuditEntry
                {
                    Action = "Variant_CsvExport",
                    Category = AuditCategory.Configuration,
                    Username = GetCurrentUsername(),
                    EntityType = "Variant",
                    Details = $"CSV export: {variants.Count} variant{(variants.Count == 1 ? "" : "s")} exported to {System.IO.Path.GetFileName(dialog.FileName)}.",
                    Severity = AuditSeverity.Info
                });

                MessageBox.Show(
                    $"Exported {variants.Count} variant{(variants.Count == 1 ? "" : "s")} to:\n{dialog.FileName}",
                    "Export Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await _auditService.LogErrorAsync(GetCurrentUsername(), "Export variants CSV", ex);
                MessageBox.Show($"Error exporting variants: {ex.Message}", "Error",
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
                    Title = "Select Variant CSV File",
                    Filter = "CSV Files (*.csv)|*.csv",
                    DefaultExt = "csv"
                };

                if (dialog.ShowDialog() != true) return;

                var filePath = dialog.FileName;
                var username = GetCurrentUsername();

                var owner = Application.Current?.MainWindow;
                var passwordConfirmed = await PasswordPromptWindow.ConfirmPasswordAsync(owner ?? new Window(), username);
                if (!passwordConfirmed)
                {
                    await _auditService.LogSecurityViolationAsync(
                        username,
                        "Import variants CSV",
                        "Password verification failed");

                    MessageBox.Show(
                        "Password incorrect or cancelled.",
                        "Access Denied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var (duplicates, allRows, errors) = await _variantService.ParseCsvAsync(filePath);

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

                var overrideNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (duplicates.Count > 0)
                {
                    var dupSummary = string.Join("\n", duplicates.Select(d =>
                        $"  Row {d.RowNumber}: {d.Name}"));

                    var choice = ScrollableMessageBox.Show(
                        $"{duplicates.Count} duplicate variant name(s) already exist in the system:\n\n{dupSummary}\n\n" +
                        $"Click YES to override (update) the existing records.\n" +
                        $"Click NO to skip duplicates and only import new records.\n" +
                        $"Click CANCEL to abort the import.",
                        "Duplicate Variants Found",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    if (choice == MessageBoxResult.Cancel)
                        return;

                    if (choice == MessageBoxResult.Yes)
                    {
                        foreach (var d in duplicates)
                            overrideNames.Add(d.Name);
                    }
                }

                var result = await _variantService.ImportParsedAsync(allRows, overrideNames);

                await _auditService.LogAsync(new AuditEntry
                {
                    Action = "Variant_CsvImport",
                    Category = AuditCategory.Configuration,
                    Username = username,
                    EntityType = "Variant",
                    Details = $"CSV import: {result.Inserted} inserted, {result.Updated} updated, {result.Skipped} skipped.",
                    Severity = AuditSeverity.Info
                });

                await LoadVariantsAsync();

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
                await _auditService.LogErrorAsync(GetCurrentUsername(), "Import variants CSV", ex);
                MessageBox.Show($"Error importing CSV: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteVariantAsync(Variant? variant)
        {
            if (variant == null) return;

            try
            {
                CheckPermissionOrThrow(_main.CurrentUser, Permission.DeleteVariant);

                var owner = Application.Current?.MainWindow;
                var username = GetCurrentUsername();
                var passwordConfirmed = await PasswordPromptWindow.ConfirmPasswordAsync(owner ?? new Window(), username);
                if (!passwordConfirmed)
                {
                    await _auditService.LogSecurityViolationAsync(
                        username,
                        $"Delete variant {variant.Name}",
                        "Password verification failed");

                    MessageBox.Show(
                        "Password incorrect or cancelled.",
                        "Access Denied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show(
                    $"Are you sure you want to delete variant '{variant.Name}'?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await _variantService.DeleteAsync(variant.Id);

                    await _auditService.LogAsync(new AuditEntry
                    {
                        Action = "Variant_Deleted",
                        Category = AuditCategory.Configuration,
                        Username = username,
                        EntityType = "Variant",
                        EntityId = variant.Id.ToString(),
                        Details = $"Variant deleted: {variant.Name}",
                        OldValue = $"Name={variant.Name}, Active={variant.Active}",
                        Severity = AuditSeverity.Warning
                    });

                    await LoadVariantsAsync();
                    MessageBox.Show("Variant deleted successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (UnauthorizedAccessException)
            {
                await _auditService.LogSecurityViolationAsync(
                    GetCurrentUsername(),
                    $"Delete variant {variant.Name}",
                    "Insufficient permissions");

                MessageBox.Show(
                    "You do not have permission to delete variants.\n\nPlease contact your administrator.",
                    "Access Denied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                await _auditService.LogErrorAsync(GetCurrentUsername(), "Delete variant", ex);

                MessageBox.Show($"Error deleting variant: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
