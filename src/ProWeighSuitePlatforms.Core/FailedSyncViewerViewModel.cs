using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Platform.Weighing.Contracts;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Views;

namespace ProWeighSuitePlatforms.ViewModels
{
    /// <summary>
    /// ViewModel for viewing and managing failed transaction syncs - now loads from weighing_transactions (is_synced=0)
    /// </summary>
    public class FailedSyncViewerViewModel : BaseViewModel
    {
        private readonly ITransactionRetryQueue _queue;

        public string Title => "Failed Syncs";

        // Date filters - same as RecentTransactionsView
        private DateTime? _fromDate;
        public DateTime? FromDate
        {
            get => _fromDate;
            set => SetProperty(ref _fromDate, value);
        }

        private DateTime? _toDate;
        public DateTime? ToDate
        {
            get => _toDate;
            set => SetProperty(ref _toDate, value);
        }

        // Pagination - same as RecentTransactionsView
        private int _pageSize = 20;
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (SetProperty(ref _pageSize, value))
                {
                    CurrentPage = 1;
                    _ = ApplyFilterAsync();
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
                    RaisePropertyChanged(nameof(PageInfo));
            }
        }

        private int _totalPages = 1;
        public int TotalPages
        {
            get => _totalPages;
            set
            {
                if (SetProperty(ref _totalPages, value))
                    RaisePropertyChanged(nameof(PageInfo));
            }
        }

        private int _totalRecords;
        public int TotalRecords
        {
            get => _totalRecords;
            set
            {
                if (SetProperty(ref _totalRecords, value))
                    RaisePropertyChanged(nameof(PageInfo));
            }
        }

        public string PageInfo => $"Page {CurrentPage} of {TotalPages}  ({TotalRecords} records)";

        // Backing store for all filtered unsynced transactions
        private List<FailedTransactionViewModel> _allFiltered = new();

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    _searchDebounceTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                    _searchDebounceTimer?.Change(300, System.Threading.Timeout.Infinite);
                }
            }
        }
        private System.Threading.Timer? _searchDebounceTimer;

        public FailedSyncViewerViewModel(ITransactionRetryQueue queue)
        {
            _queue = queue;
            // Default filter empty -> load ALL failed sync data
            FromDate = null;
            ToDate = null;

            _searchDebounceTimer = new System.Threading.Timer(_ =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    CurrentPage = 1;
                    ApplyPage();
                    RaisePropertyChanged(nameof(PageInfo));
                });
            });

            // Commands - pagination + filter + existing actions
            RefreshCommand = new RelayCommand(async _ => await ApplyFilterAsync());
            ApplyFilterCommand = new RelayCommand(async _ => { CurrentPage = 1; await ApplyFilterAsync(); });
            ClearFilterCommand = new RelayCommand(async _ => { FromDate = null; ToDate = null; SearchText = string.Empty; CurrentPage = 1; await ApplyFilterAsync(); });
            PrevPageCommand = new RelayCommand(_ => { if (CurrentPage > 1) { CurrentPage--; ApplyPage(); } });
            NextPageCommand = new RelayCommand(_ => { if (CurrentPage < TotalPages) { CurrentPage++; ApplyPage(); } });
            FirstPageCommand = new RelayCommand(_ => { if (CurrentPage != 1) { CurrentPage = 1; ApplyPage(); } });
            LastPageCommand = new RelayCommand(_ => { if (CurrentPage != TotalPages) { CurrentPage = TotalPages; ApplyPage(); } });

            DeleteSelectedCommand = new RelayCommand(async _ => await DeleteSelectedAsync(), _ => SelectedItem != null);
            ViewDetailsCommand = new RelayCommand(param => ViewDetails(param as FailedTransactionViewModel), _ => true);
            ClearAllCommand = new RelayCommand(async _ => await ClearAllAsync(), _ => _allFiltered.Count > 0);
            RetryAllCommand = new RelayCommand(async _ => await RetryAllAsync(), _ => _allFiltered.Count > 0);
            RetrySelectedCommand = new RelayCommand(async _ => await RetrySelectedAsync(), _ => FailedItems.Any(x => x.IsSelected));
            ToggleSelectAllCommand = new RelayCommand(param => { IsSelectAll = param is bool b ? b : !IsSelectAll; });
            CopyTransIdCommand = new RelayCommand(param => CopyToClipboard(param as FailedTransactionViewModel, "TransId"));
            CopyBatchNumberCommand = new RelayCommand(param => CopyToClipboard(param as FailedTransactionViewModel, "BatchNumber"));
            CopyMaterialCodeCommand = new RelayCommand(param => CopyToClipboard(param as FailedTransactionViewModel, "MaterialCode"));

            // Initial load from transaction table
            _ = ApplyFilterAsync();
        }

        public ObservableCollection<FailedTransactionViewModel> FailedItems { get; } = new();

        private FailedTransactionViewModel? _selectedItem;
        public FailedTransactionViewModel? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        private string _statusMessage = "Loading...";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private bool _isSelectAll;
        public bool IsSelectAll
        {
            get => _isSelectAll;
            set
            {
                if (SetProperty(ref _isSelectAll, value))
                {
                    SelectAll(value);
                    RaisePropertyChanged(nameof(SelectedCount));
                }
            }
        }

        public int SelectedCount => FailedItems.Count(x => x.IsSelected);

        private void SelectAll(bool isSelected)
        {
            foreach (var item in FailedItems)
                item.IsSelected = isSelected;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        private void OnItemIsSelectedChanged()
        {
            // Update header checkbox state without triggering SelectAll loop
            var allSelected = FailedItems.Count > 0 && FailedItems.All(x => x.IsSelected);
            if (_isSelectAll != allSelected)
            {
                _isSelectAll = allSelected;
                RaisePropertyChanged(nameof(IsSelectAll));
            }
            RaisePropertyChanged(nameof(SelectedCount));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        // Commands
        public ICommand RefreshCommand { get; }
        public ICommand ApplyFilterCommand { get; }
        public ICommand ClearFilterCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand LastPageCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand ViewDetailsCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand RetryAllCommand { get; }
        public ICommand RetrySelectedCommand { get; }
        public ICommand ToggleSelectAllCommand { get; }
        public ICommand CopyTransIdCommand { get; }
        public ICommand CopyBatchNumberCommand { get; }
        public ICommand CopyMaterialCodeCommand { get; }

        // Kept for backward compat (DailyResetService)
        private Task LoadFailedItemsAsync() => ApplyFilterAsync();

        private async Task ApplyFilterAsync(bool forceReload = false)
        {
            if (IsLoading && !forceReload) return;
            IsLoading = true;
            StatusMessage = "Loading failed transactions (is_synced=0)...";
            // Allow spinner to render before heavy DB work
            await Task.Delay(50);
            try
            {
                var dbPath = AppConfigurationManager.GetDatabasePath();
                var repo = new WeighingRepository(dbPath);
                var failed = await repo.GetTransactionsByFilterAsync(FromDate, ToDate, false);

                // Resolve API url once
                string apiUrl;
                try
                {
                    var syncSvc = new Services.SyncSettingsService();
                    var settings = await syncSvc.LoadAsync();
                    apiUrl = !string.IsNullOrWhiteSpace(settings?.ApiBaseUrl) ? settings.ApiBaseUrl! : "Not configured";
                }
                catch { apiUrl = "Not configured"; }

                _allFiltered = new List<FailedTransactionViewModel>();
                foreach (var t in failed.OrderByDescending(x => x.WeighedDateValue))
                {
                    try
                    {
                        var json = JsonSerializer.Serialize(t, new JsonSerializerOptions { WriteIndented = true });
                        var item = new FailedTransactionViewModel
                        {
                            Timestamp        = t.WeighedDateValue != DateTime.MinValue ? t.WeighedDateValue : DateTime.Now,
                            TransactionId    = t.Id,
                            AttemptCount     = 0,
                            EnqueuedAt       = t.WeighedDateValue != DateTime.MinValue ? t.WeighedDateValue : DateTime.Now,
                            LastErrorMessage = !string.IsNullOrWhiteSpace(t.ServerResponse) ? t.ServerResponse! : "Not synced - pending retry (is_synced=0)",
                            AllUrls          = new List<string> { apiUrl },
                            FailedUrls       = new List<string> { apiUrl },
                            SucceededUrls    = new List<string>(),
                            ApiUrl           = apiUrl,
                            Note             = t.Status ?? "",
                            BatchNumber  = t.BatchNumber ?? "",
                            MaterialCode = t.MaterialCode ?? "",
                            MaterialName = t.MaterialName ?? "",
                            ActualWeight = t.ActualWeight,
                            TargetWeight = t.TargetWeight,
                            WeighedBy    = t.WeighedBy ?? "",
                            WeighedDate  = t.WeighedDateValue != DateTime.MinValue ? t.WeighedDateValue : DateTime.Now,
                            CustomerName = t.CustomerName ?? "",
                            Location     = t.Location ?? "",
                            FullJson     = json
                        };
                        _allFiltered.Add(item);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FailedSyncViewer] Error mapping transaction {t.Id}: {ex.Message}");
                    }
                }

                TotalRecords = _allFiltered.Count;
                TotalPages = TotalRecords == 0 ? 1 : (int)Math.Ceiling(TotalRecords / (double)PageSize);
                if (CurrentPage > TotalPages) CurrentPage = TotalPages;

                ApplyPage();

                var fromStr = FromDate?.ToString("dd-MM-yyyy") ?? "-";
                var toStr = ToDate?.ToString("dd-MM-yyyy") ?? "-";
                StatusMessage = _allFiltered.Count == 0
                    ? $"No failed transactions from {fromStr} to {toStr} (all synced)"
                    : $"Loaded {TotalRecords} failed transaction(s) from {fromStr} to {toStr} (is_synced=0)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Error loading failed transactions from DB: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private IEnumerable<FailedTransactionViewModel> GetSearchFiltered()
        {
            var search = SearchText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(search))
                return _allFiltered;

            var lower = search.ToLowerInvariant();
            return _allFiltered.Where(x =>
                (x.TransactionId?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.BatchNumber?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.MaterialCode?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.MaterialName?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.WeighedBy?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.ApiUrl?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.ErrorNote?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.TimestampFormatted?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                x.ActualWeight.ToString("F3").Contains(lower) ||
                x.AttemptCount.ToString().Contains(lower));
        }

        private void ApplyPage()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                // Unhook old items
                foreach (var old in FailedItems)
                    old.PropertyChanged -= Item_PropertyChanged;

                FailedItems.Clear();
                var filtered = GetSearchFiltered().ToList();
                TotalRecords = filtered.Count;
                TotalPages = TotalRecords == 0 ? 1 : (int)Math.Ceiling(TotalRecords / (double)PageSize);
                if (CurrentPage > TotalPages) CurrentPage = TotalPages;

                var page = filtered.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();
                int slNo = (CurrentPage - 1) * PageSize + 1;
                foreach (var item in page)
                {
                    item.SlNo = slNo++;
                    item.PropertyChanged += Item_PropertyChanged;
                    FailedItems.Add(item);
                }
                // Reset header checkbox
                _isSelectAll = FailedItems.Count > 0 && FailedItems.All(x => x.IsSelected);
                RaisePropertyChanged(nameof(IsSelectAll));
                RaisePropertyChanged(nameof(SelectedCount));
                RaisePropertyChanged(nameof(PageInfo));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            });
        }

        private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FailedTransactionViewModel.IsSelected))
                OnItemIsSelectedChanged();
        }

        private async Task DeleteSelectedAsync()
        {
            if (SelectedItem == null) return;

            try
            {
                var result = MessageBox.Show(
                    $"Delete this failed transaction from weighing_transactions?\n\n" +
                    $"ID: {SelectedItem.TransactionId}\n" +
                    $"Batch: {SelectedItem.BatchNumber}\n" +
                    $"Material: {SelectedItem.MaterialCode} - {SelectedItem.MaterialName}\n\n" +
                    $"This will permanently delete the transaction row (is_synced=0).\nThis cannot be undone.",
                    "Confirm Delete from DB",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    var dbPath = AppConfigurationManager.GetDatabasePath();
                    var repo = new WeighingRepository(dbPath);
                    await repo.DeleteTransactionAsync(SelectedItem.TransactionId);
                    _allFiltered.RemoveAll(x => x.TransactionId == SelectedItem.TransactionId);
                    TotalRecords = _allFiltered.Count;
                    TotalPages = TotalRecords == 0 ? 1 : (int)Math.Ceiling(TotalRecords / (double)PageSize);
                    if (CurrentPage > TotalPages) CurrentPage = TotalPages;
                    ApplyPage();
                    StatusMessage = $"Deleted transaction {SelectedItem.TransactionId} from weighing_transactions";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error deleting: {ex.Message}";
                MessageBox.Show($"Error deleting transaction: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ClearAllAsync()
        {
            try
            {
                var result = MessageBox.Show(
                    $"Delete ALL {FailedItems.Count} failed (unsynced) transactions from weighing_transactions?\n\n" +
                    $"This will permanently delete all rows where is_synced=0.\n" +
                    $"This cannot be undone.",
                    "Confirm Clear All Unsynced",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    // Use date-filtered clear: deleting only what is currently loaded (filtered)
                    // To keep consistent with transaction table request, delete all unsynced
                    var dbPath = AppConfigurationManager.GetDatabasePath();
                    var repo = new WeighingRepository(dbPath);
                    int deleted;
                    if (FromDate.HasValue || ToDate.HasValue)
                    {
                        // Delete filtered items one by one to respect date filter
                        deleted = 0;
                        foreach (var it in _allFiltered.ToList())
                        {
                            await repo.DeleteTransactionAsync(it.TransactionId);
                            deleted++;
                        }
                    }
                    else
                    {
                        deleted = await repo.DeleteAllUnsyncedAsync();
                    }
                    _allFiltered.Clear();
                    FailedItems.Clear();
                    TotalRecords = 0;
                    TotalPages = 1;
                    CurrentPage = 1;
                    RaisePropertyChanged(nameof(PageInfo));
                    StatusMessage = $"Deleted {deleted} unsynced transaction(s) from DB";
                    
                    MessageBox.Show(
                        $"All {deleted} failed (unsynced) transactions have been deleted from weighing_transactions.",
                        "Cleared",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error clearing: {ex.Message}";
                MessageBox.Show($"Error clearing failed transactions: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RetryAllAsync()
        {
            try
            {
                if (FailedItems.Count == 0)
                {
                    MessageBox.Show(
                        "No failed transactions to retry.",
                        "Nothing to Retry",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var result = MessageBox.Show(
                    $"Retry ALL {_allFiltered.Count} failed transactions?\n\n" +
                    $"This will re-enqueue them to the sync queue.\n" +
                    $"They will be automatically synced when internet is available.\n\n" +
                    $"Do you want to continue?",
                    "Confirm Retry All",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                IsLoading = true;
                StatusMessage = $"Retrying {_allFiltered.Count} transactions...";
                await Task.Delay(80);

                var liteQueueService = _queue;

                int successCount = 0;
                int failCount = 0;

                // Re-enqueue each unsynced transaction from DB (all filtered, not just current page)
                foreach (var item in _allFiltered.ToList())
                {
                    try
                    {
                        var dbPath2 = AppConfigurationManager.GetDatabasePath();
                        var repo2 = new WeighingRepository(dbPath2);
                        var transaction = await repo2.GetTransactionByIdAsync(item.TransactionId);
                        if (transaction != null)
                        {
                            liteQueueService.EnqueueTransaction(transaction);
                            successCount++;
                        }
                        else
                        {
                            failCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error re-enqueuing transaction {item.TransactionId}: {ex.Message}");
                        failCount++;
                    }
                }

                if (successCount > 0)
                    liteQueueService.AdjustFailureCountForRetry(successCount);

                // Reload the failed list under spinner (forceReload allows re-entry while IsLoading=true)
                StatusMessage = successCount > 0
                    ? $"Re-enqueued {successCount} transaction(s), reloading..."
                    : "Failed to re-enqueue transactions, reloading...";
                await ApplyFilterAsync(forceReload: true);

                // Hide spinner before showing modal result dialog, so spinner is not hidden behind MessageBox
                IsLoading = false;

                // On success, is_synced will flip to 1 when sync succeeds; just reload the failed list
                if (successCount > 0 && failCount == 0)
                {
                    StatusMessage = $"Successfully re-enqueued all {successCount} transactions for retry";
                    MessageBox.Show(
                        $"Successfully re-enqueued {successCount} transactions from weighing_transactions (is_synced=0)!\n\n" +
                        $"They will be automatically synced when internet is available.\n" +
                        $"Monitor progress in the Queue Monitor window.",
                        "Retry Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else if (successCount > 0)
                {
                    StatusMessage = $"Retried {successCount} transactions, {failCount} failed";
                    MessageBox.Show(
                        $"Re-enqueued {successCount} transactions successfully.\n" +
                        $"{failCount} transactions could not be retried.\n\n" +
                        $"Remaining failed transactions are still in weighing_transactions.",
                        "Partial Success",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    StatusMessage = "Failed to retry transactions";
                    MessageBox.Show(
                        "Failed to re-enqueue transactions.\n\n" +
                        "Please check the logs for more details.",
                        "Retry Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Error in RetryAllAsync: {ex}");
                IsLoading = false;
                MessageBox.Show(
                    $"Error retrying transactions:\n\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RetrySelectedAsync()
        {
            try
            {
                var selected = FailedItems.Where(x => x.IsSelected).ToList();
                // also include selected items that may be on other pages but in _allFiltered
                // if user checked items then changed page, those items remain IsSelected in _allFiltered
                var allSelected = _allFiltered.Where(x => x.IsSelected).ToList();
                // Prefer the broader set (paged + off-page) - use allSelected if it has more
                var toRetry = allSelected.Count > selected.Count ? allSelected : selected;

                if (toRetry.Count == 0)
                {
                    MessageBox.Show("No rows selected.\n\nCheck the boxes for the transactions you want to retry.",
                        "Nothing Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var result = MessageBox.Show(
                    $"Retry {toRetry.Count} selected transaction(s)?\n\n" +
                    $"They will be moved to the Queue (LiteQueue) for automatic sync when internet is available.\n\nDo you want to continue?",
                    "Confirm Retry Selected",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                IsLoading = true;
                StatusMessage = $"Retrying {toRetry.Count} selected transaction(s)...";
                await Task.Delay(80);

                var liteQueueService = _queue;

                int successCount = 0;
                int failCount = 0;

                foreach (var item in toRetry.ToList())
                {
                    try
                    {
                        var dbPath2 = AppConfigurationManager.GetDatabasePath();
                        var repo2 = new WeighingRepository(dbPath2);
                        var transaction = await repo2.GetTransactionByIdAsync(item.TransactionId);
                        if (transaction != null)
                        {
                            liteQueueService.EnqueueTransaction(transaction);
                            successCount++;
                        }
                        else failCount++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error re-enqueuing selected {item.TransactionId}: {ex.Message}");
                        failCount++;
                    }
                }

                if (successCount > 0)
                    liteQueueService.AdjustFailureCountForRetry(successCount);

                StatusMessage = successCount > 0
                    ? $"Re-enqueued {successCount} selected transaction(s), reloading..."
                    : "Failed to re-enqueue selected transactions, reloading...";
                await ApplyFilterAsync(forceReload: true);

                IsLoading = false;

                if (successCount > 0 && failCount == 0)
                {
                    StatusMessage = $"Successfully re-enqueued {successCount} selected transaction(s) to Queue";
                    MessageBox.Show($"Successfully re-enqueued {successCount} transaction(s) to the Queue!\n\nThey will be synced automatically when internet is available.",
                        "Retry Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (successCount > 0)
                {
                    StatusMessage = $"Retried {successCount}, {failCount} failed";
                    MessageBox.Show($"Re-enqueued {successCount} successfully.\n{failCount} could not be retried.",
                        "Partial Success", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    StatusMessage = "Failed to retry selected transactions";
                    MessageBox.Show("Failed to re-enqueue selected transactions.", "Retry Failed",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Error in RetrySelectedAsync: {ex}");
                IsLoading = false;
                MessageBox.Show($"Error retrying selected:\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async void ViewDetails(FailedTransactionViewModel? param = null)
        {
            var target = param ?? SelectedItem;
            if (target == null) return;

            IsLoading = true;
            StatusMessage = "Loading details...";
            try
            {
                // Yield to UI thread so the loading spinner becomes visible before blocking dialog
                await Task.Delay(50);
                var detailsText = FormatTransactionDetails(target);

                var detailsWindow = new Window
                {
                    Title = $"Transaction Details - ID: {target.TransactionId}",
                    Width = 800,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Owner = Application.Current.MainWindow,
                    Content = new System.Windows.Controls.ScrollViewer
                    {
                        Content = new System.Windows.Controls.TextBox
                        {
                            Text = detailsText,
                            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                            FontSize = 12,
                            IsReadOnly = true,
                            TextWrapping = System.Windows.TextWrapping.Wrap,
                            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                            Padding = new Thickness(15)
                        }
                    }
                };
                detailsWindow.ShowDialog();
                StatusMessage = $"Showing details for {target.TransactionId}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error viewing details: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"Error viewing details: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async void CopyToClipboard(FailedTransactionViewModel? row, string field)
        {
            if (row == null) row = SelectedItem;
            if (row == null) return;
            IsLoading = true;
            StatusMessage = $"Copying {field}...";
            try
            {
                // Brief yield so spinner is rendered before clipboard access
                await Task.Delay(80);
                string value = field switch
                {
                    "TransId" => row.TransactionId,
                    "BatchNumber" => row.BatchNumber,
                    "MaterialCode" => row.MaterialCode,
                    _ => ""
                };
                if (!string.IsNullOrWhiteSpace(value))
                {
                    Clipboard.SetText(value);
                    StatusMessage = $"{field} copied: {value}";
                }
                else
                {
                    StatusMessage = $"{field} is empty - nothing to copy";
                }
                // Keep spinner visible briefly for user feedback
                await Task.Delay(300);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Copy failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"Copy failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static List<string> ReadStringArray(JsonElement root, string propertyName)
        {
            var list = new List<string>();
            if (root.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
            }
            return list;
        }

        private string FormatTransactionDetails(FailedTransactionViewModel item)
        {
            var endpointSection = new System.Text.StringBuilder();
            if (item.AllUrls.Count > 0)
            {
                foreach (var url in item.AllUrls)
                {
                    var failed    = item.FailedUrls.Contains(url);
                    var statusTag = failed ? "[FAILED]" : "[OK]";
                    endpointSection.AppendLine($"  {statusTag} {url}");
                }
            }
            else
            {
                endpointSection.AppendLine($"  {item.ApiUrl}");
            }

            return $@"=== FAILED TRANSACTION DETAILS ===

Transaction ID : {item.TransactionId}
Failed At      : {item.TimestampFormatted}
Enqueued At    : {item.EnqueuedAt:yyyy-MM-dd HH:mm:ss}
Attempt Count  : {item.AttemptCount}

BATCH INFORMATION:
------------------
Batch Number: {item.BatchNumber}

MATERIAL INFORMATION:
---------------------
Material Code : {item.MaterialCode}
Material Name : {item.MaterialName}
Target Weight : {item.TargetWeight:F3} kg
Actual Weight : {item.ActualWeight:F3} kg

TRANSACTION DETAILS:
--------------------
Weighed By  : {item.WeighedBy}
Weighed Date: {item.WeighedDate:yyyy-MM-dd HH:mm:ss}
Customer    : {item.CustomerName}
Location    : {item.Location}

API ENDPOINTS ([OK] = received data  [FAILED] = unreachable):
--------------------------------------------------------------
{endpointSection.ToString().TrimEnd()}

ERROR INFORMATION:
------------------
{item.LastErrorMessage}

NOTE:
-----
{item.Note}

=== RAW JSON DATA ===

{item.FullJson}";
        }
    }

    /// <summary>
    /// View model for a single failed transaction
    /// </summary>
    public class FailedTransactionViewModel : BaseViewModel
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public int SlNo { get; set; }
        public DateTime Timestamp { get; set; }
        public string TransactionId { get; set; } = string.Empty;
        public int AttemptCount { get; set; }
        public DateTime EnqueuedAt { get; set; }
        public string LastErrorMessage { get; set; } = string.Empty;

        // All configured endpoints at time of failure
        public List<string> AllUrls { get; set; } = new();
        // Endpoints that could not be reached
        public List<string> FailedUrls { get; set; } = new();
        // Endpoints that received the data successfully
        public List<string> SucceededUrls { get; set; } = new();

        /// <summary>Human-readable summary of all configured endpoints.</summary>
        public string ApiUrl { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;

        // Transaction details
        public string BatchNumber { get; set; } = string.Empty;
        public string MaterialCode { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public double ActualWeight { get; set; }
        public double TargetWeight { get; set; }
        public string WeighedBy { get; set; } = string.Empty;
        public DateTime WeighedDate { get; set; }
        public string? CustomerName { get; set; }
        public string? Location { get; set; }
        public string FullJson { get; set; } = string.Empty;

        public string TimestampFormatted => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

        /// <summary>Shows failed URLs, or all URLs if none specifically recorded.</summary>
        public string FailedUrlsSummary => FailedUrls.Count > 0
            ? string.Join(", ", FailedUrls)
            : (string.IsNullOrWhiteSpace(ApiUrl) ? "Unknown" : ApiUrl);

        /// <summary>Compact status: e.g. "1 OK / 1 FAILED" for list column display.</summary>
        public string EndpointStatus
        {
            get
            {
                if (AllUrls.Count == 0)
                    return FailedUrlsSummary;
                var ok   = SucceededUrls.Count;
                var fail = FailedUrls.Count;
                return ok > 0 && fail > 0
                    ? $"{ok} OK / {fail} FAILED"
                    : fail > 0 ? $"{fail} FAILED" : $"{ok} OK";
            }
        }

        public string ErrorNote => string.IsNullOrEmpty(Note)
            ? LastErrorMessage
            : $"{LastErrorMessage} | {Note}";
    }
}
