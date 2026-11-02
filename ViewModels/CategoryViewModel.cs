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
    public class CategoryViewModel : BaseViewModel
    {
        private readonly ICurrentUserContext _main;
        private readonly CategoryService _categoryService;
        private readonly AuditService _auditService;

        public string Title => IsCreating && CreateViewModel != null ? CreateViewModel.Title : "Category Master";
        
        // Permission-based properties
        public bool CanCreateCategory => HasPermission(_main.CurrentUser, Permission.CreateCategory);
        public bool CanEditCategory => HasPermission(_main.CurrentUser, Permission.EditCategory);
        public bool CanDeleteCategory => HasPermission(_main.CurrentUser, Permission.DeleteCategory);
        public bool CanImportCategory => HasPermission(_main.CurrentUser, Permission.ImportCategory);
        public bool CanDownloadCategoryTemplate => HasPermission(_main.CurrentUser, Permission.DownloadCategoryTemplate);
        public bool CanExportCategory => HasPermission(_main.CurrentUser, Permission.ExportCategory);

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

        private CategoryCreateViewModel? _createViewModel;
        public CategoryCreateViewModel? CreateViewModel
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
        public ICommand ExportCategoriesCommand { get; }

        public ObservableCollection<Category> PagedCategories { get; } = new();

        private Category? _selected;
        public Category? Selected
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

        public int TotalPages => (int)Math.Ceiling((double)FilteredCategories.Count / PageSize);

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

        private ObservableCollection<Category> _allCategories = new();
        private ObservableCollection<Category> FilteredCategories
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FilterText))
                    return _allCategories;

                var search = FilterText.Trim();
                var filtered = _allCategories.Where(c => CategoryMatches(c, search));

                return new ObservableCollection<Category>(filtered);
            }
        }

        /// <summary>
        /// Case-insensitive match of the search term against every displayed column
        /// of the category grid (S No, Name, Description, Status, Created Date).
        /// Nullable values are handled gracefully.
        /// </summary>
        private static bool CategoryMatches(Category category, string search)
        {
            return (category.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (category.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (category.StatusText?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || category.CreatedAt.ToString("g").Contains(search, StringComparison.OrdinalIgnoreCase)
                || category.Id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                || category.SerialNumber.ToString().Contains(search, StringComparison.OrdinalIgnoreCase);
        }

        public CategoryViewModel(ICurrentUserContext main)
        {
            _main = main;
            _auditService = new AuditService();

            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuitePlatforms", "proweighsuiteplatform.db");
            _categoryService = new CategoryService(new CategoryRepository(dbPath));

            StartCreateCommand = new RelayCommand(_ => StartCreate(), _ => CanCreateCategory);
            StartUpdateRowCommand = new RelayCommand(param => StartUpdate(param as Category), _ => CanEditCategory);
            DeleteCommand = new RelayCommand(async param => await DeleteCategoryAsync(param as Category), _ => CanDeleteCategory);
            PrevPageCommand = new RelayCommand(_ => PrevPage(), _ => CurrentPage > 1);
            NextPageCommand = new RelayCommand(_ => NextPage(), _ => CurrentPage < TotalPages);
            FirstPageCommand = new RelayCommand(_ => { if (CurrentPage != 1) CurrentPage = 1; });
            LastPageCommand = new RelayCommand(_ => { if (CurrentPage != TotalPages) CurrentPage = TotalPages; });
            GoToPageCommand = new RelayCommand(param =>
            {
                if (param is int page && page >= 1 && page <= Math.Max(1, TotalPages) && page != CurrentPage)
                    CurrentPage = page;
            });
            ImportCsvCommand = new RelayCommand(async _ => await ImportCsvAsync(), _ => CanImportCategory);
            DownloadTemplateCommand = new RelayCommand(_ => DownloadTemplate(), _ => CanDownloadCategoryTemplate);
            ExportCategoriesCommand = new RelayCommand(async _ => await ExportCsvAsync(), _ => CanExportCategory);

            _ = LoadCategoriesAsync();
        }

        // Helper method to get current username for audit logging
        public string GetCurrentUsername()
        {
            return _main.CurrentUser?.Username ?? "Unknown";
        }

        public void StartCreate()
        {
            CreateViewModel = new CategoryCreateViewModel(this, null);
            IsCreating = true;
        }

        public void StartUpdate(Category? category)
        {
            if (category == null) return;
            Selected = category;
            CreateViewModel = new CategoryCreateViewModel(this, category);
            IsCreating = true;
        }

        public async Task CancelCreate()
        {
            IsCreating = false;
            Selected = null;
            CreateViewModel = null;
            await LoadCategoriesAsync();
        }

        private async Task LoadCategoriesAsync()
        {
            try
            {
                var categories = await _categoryService.GetAllAsync();

                _allCategories.Clear();
                foreach (var category in categories)
                {
                    _allCategories.Add(category);
                }

                UpdatePagedData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading categories: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdatePagedData()
        {
            var filtered = FilteredCategories;
            TotalRecords = filtered.Count;
            if (CurrentPage < 1) _currentPage = 1;
            var pages = Math.Max(1, (int)Math.Ceiling((double)filtered.Count / Math.Max(1, PageSize)));
            if (_currentPage > pages) _currentPage = pages;
            var paged = filtered
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            PagedCategories.Clear();
            foreach (var category in paged)
            {
                PagedCategories.Add(category);
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

        private async Task DeleteCategoryAsync(Category? category)
        {
            if (category == null) return;

            try
            {
                CheckPermissionOrThrow(_main.CurrentUser, Permission.DeleteCategory);

                var owner = Application.Current?.MainWindow;
                var username = GetCurrentUsername();
                var passwordConfirmed = await PasswordPromptWindow.ConfirmPasswordAsync(owner ?? new Window(), username);
                if (!passwordConfirmed)
                {
                    await _auditService.LogSecurityViolationAsync(
                        username,
                        $"Delete category {category.Name}",
                        "Password verification failed");
                    
                    MessageBox.Show(
                        "Password incorrect or cancelled.",
                        "Access Denied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show(
                    $"Are you sure you want to delete category '{category.Name}'?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await _categoryService.DeleteAsync(category.Id);
                    
                    await _auditService.LogAsync(new AuditEntry
                    {
                        Action = "Category_Deleted",
                        Category = AuditCategory.Configuration,
                        Username = username,
                        EntityType = "Category",
                        EntityId = category.Id.ToString(),
                        Details = $"Category deleted: {category.Name}",
                        OldValue = $"Name={category.Name}, Status={category.Status}",
                        Severity = AuditSeverity.Warning
                    });
                    
                    await LoadCategoriesAsync();
                    MessageBox.Show("Category deleted successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (UnauthorizedAccessException)
            {
                await _auditService.LogSecurityViolationAsync(
                    GetCurrentUsername(),
                    $"Delete category {category.Name}",
                    "Insufficient permissions");
                
                MessageBox.Show(
                    "You do not have permission to delete categories.\n\nPlease contact your administrator.",
                    "Access Denied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                await _auditService.LogErrorAsync(GetCurrentUsername(), "Delete category", ex);
                
                MessageBox.Show($"Error deleting category: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DownloadTemplate()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Download Category Template",
                    Filter = "CSV Files (*.csv)|*.csv",
                    FileName = "Category_Template.csv",
                    DefaultExt = "csv"
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, CategoryService.GetCsvTemplate());
                    MessageBox.Show(
                        $"Sample template saved to:\n{dialog.FileName}\n\nFill in the Name, Description, and Status (Active/Inactive) columns and then use 'Import' to import.",
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
                CheckPermissionOrThrow(_main.CurrentUser, Permission.ExportCategory);

                var categories = await _categoryService.GetAllAsync();

                var dialog = new SaveFileDialog
                {
                    Title = "Export Categories",
                    Filter = "CSV Files (*.csv)|*.csv",
                    FileName = $"Category_{DateTime.Now:yyyyMMddHHmmss}.csv",
                    DefaultExt = "csv"
                };

                if (dialog.ShowDialog() != true) return;

                var sb = new StringBuilder();
                sb.AppendLine("Name,Description,Status");
                foreach (var category in categories)
                {
                    sb.AppendLine($"{EscapeCsv(category.Name)},{EscapeCsv(category.Description)},{category.StatusText}");
                }
                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);

                await _auditService.LogAsync(new AuditEntry
                {
                    Action = "Category_CsvExport",
                    Category = AuditCategory.Configuration,
                    Username = GetCurrentUsername(),
                    EntityType = "Category",
                    Details = $"CSV export: {categories.Count} categor{(categories.Count == 1 ? "y" : "ies")} exported to {System.IO.Path.GetFileName(dialog.FileName)}.",
                    Severity = AuditSeverity.Info
                });

                MessageBox.Show(
                    $"Exported {categories.Count} categor{(categories.Count == 1 ? "y" : "ies")} to:\n{dialog.FileName}",
                    "Export Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await _auditService.LogErrorAsync(GetCurrentUsername(), "Export categories CSV", ex);
                MessageBox.Show($"Error exporting categories: {ex.Message}", "Error",
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
                    Title = "Select Category CSV File",
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
                        "Import categories CSV",
                        "Password verification failed");

                    MessageBox.Show(
                        "Password incorrect or cancelled.",
                        "Access Denied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var (duplicates, allRows, errors) = await _categoryService.ParseCsvAsync(filePath);

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
                    // Build duplicate summary
                    var dupSummary = string.Join("\n", duplicates.Select(d =>
                        $"  Row {d.RowNumber}: {d.Name}"));

                    var choice = ScrollableMessageBox.Show(
                        $"{duplicates.Count} duplicate category name(s) already exist in the system:\n\n{dupSummary}\n\n" +
                        $"Click YES to override (update) the existing records.\n" +
                        $"Click NO to skip duplicates and only import new records.\n" +
                        $"Click CANCEL to abort the import.",
                        "Duplicate Categories Found",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    if (choice == MessageBoxResult.Cancel)
                        return;

                    if (choice == MessageBoxResult.Yes)
                    {
                        foreach (var d in duplicates)
                            overrideNames.Add(d.Name);
                    }
                    // No = leave overrideNames empty so duplicates are skipped
                }

                var result = await _categoryService.ImportParsedAsync(allRows, overrideNames);

                await _auditService.LogAsync(new AuditEntry
                {
                    Action = "Category_CsvImport",
                    Category = AuditCategory.Configuration,
                    Username = GetCurrentUsername(),
                    EntityType = "Category",
                    Details = $"CSV import: {result.Inserted} inserted, {result.Updated} updated, {result.Skipped} skipped.",
                    Severity = AuditSeverity.Info
                });

                await LoadCategoriesAsync();

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
                await _auditService.LogErrorAsync(GetCurrentUsername(), "Import categories CSV", ex);
                MessageBox.Show($"Error importing CSV: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
