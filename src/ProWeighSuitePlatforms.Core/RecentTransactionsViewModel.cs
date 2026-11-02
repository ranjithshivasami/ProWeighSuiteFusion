using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProWeighSuitePlatforms.Platform.Weighing.Contracts;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Helpers;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class RecentTransactionsViewModel : BaseViewModel
    {
        private readonly ITransactionRetryQueue _retryQueue;

        public string Title => "Recent Transactions";

        // Filter properties
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

        private string _selectedSyncFilter = "All";
        public string SelectedSyncFilter
        {
            get => _selectedSyncFilter;
            set => SetProperty(ref _selectedSyncFilter, value);
        }

        public ObservableCollection<string> SyncFilterOptions { get; } = new()
        {
            "All", "Synced", "Not Synced"
        };

        // Pagination
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
                {
                    RaisePropertyChanged(nameof(PageInfo));
                }
            }
        }

        private int _totalPages = 1;
        public int TotalPages
        {
            get => _totalPages;
            set
            {
                if (SetProperty(ref _totalPages, value))
                {
                    RaisePropertyChanged(nameof(PageInfo));
                }
            }
        }

        private int _totalRecords;
        public int TotalRecords
        {
            get => _totalRecords;
            set
            {
                if (SetProperty(ref _totalRecords, value))
                {
                    RaisePropertyChanged(nameof(PageInfo));
                }
            }
        }

        public string PageInfo => $"Page {CurrentPage} of {TotalPages}  ({TotalRecords} records)";

        // Status
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

        // Data
        private List<WeighingTransaction> _allFiltered = new();
        public ObservableCollection<RecentTransactionRowViewModel> PagedTransactions { get; } = new();

        // Commands
        public ICommand ApplyFilterCommand { get; }
        public ICommand ClearFilterCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand LastPageCommand { get; }
        public ICommand ViewTransactionCommand { get; }
        public ICommand ViewServerResponseCommand { get; }
        public ICommand SyncTransactionCommand { get; }
        public ICommand CopyTransactionIdCommand { get; }
        public ICommand CopyBatchNumberCommand { get; }
        public ICommand CopyMaterialCodeCommand { get; }

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

        public RecentTransactionsViewModel(ITransactionRetryQueue retryQueue)
        {
            _retryQueue = retryQueue;

            _searchDebounceTimer = new System.Threading.Timer(_ =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    CurrentPage = 1;
                    ApplyPage();
                });
            });

            // Default to today's date
            var today = DateTime.Now.Date;
            FromDate = today;
            ToDate = today;

            ApplyFilterCommand = new RelayCommand(async _ => { CurrentPage = 1; await ApplyFilterAsync(); });
            ClearFilterCommand = new RelayCommand(async _ => { FromDate = DateTime.Now.Date; ToDate = DateTime.Now.Date; SelectedSyncFilter = "All"; SearchText = string.Empty; CurrentPage = 1; await ApplyFilterAsync(); });
            RefreshCommand = new RelayCommand(async _ => await ApplyFilterAsync());
            PrevPageCommand = new RelayCommand(_ => { if (CurrentPage > 1) { CurrentPage--; ApplyPage(); } });
            NextPageCommand = new RelayCommand(_ => { if (CurrentPage < TotalPages) { CurrentPage++; ApplyPage(); } });
            FirstPageCommand = new RelayCommand(_ => { if (CurrentPage != 1) { CurrentPage = 1; ApplyPage(); } });
            LastPageCommand = new RelayCommand(_ => { if (CurrentPage != TotalPages) { CurrentPage = TotalPages; ApplyPage(); } });
            ViewTransactionCommand = new RelayCommand(param => ViewTransaction(param as RecentTransactionRowViewModel));
            ViewServerResponseCommand = new RelayCommand(param => ViewServerResponse(param as RecentTransactionRowViewModel));
            SyncTransactionCommand = new RelayCommand(async param => await SyncTransactionAsync(param as RecentTransactionRowViewModel));
            CopyTransactionIdCommand = new RelayCommand(param => CopyToClipboard(param as RecentTransactionRowViewModel, "Id"));
            CopyBatchNumberCommand = new RelayCommand(param => CopyToClipboard(param as RecentTransactionRowViewModel, "BatchNumber"));
            CopyMaterialCodeCommand = new RelayCommand(param => CopyToClipboard(param as RecentTransactionRowViewModel, "MaterialCode"));

            _ = ApplyFilterAsync();
        }

        private async Task ApplyFilterAsync()
        {
            if (IsLoading) return;
            IsLoading = true;
            StatusMessage = "Loading transactions...";

            try
            {
                bool? syncFilter = SelectedSyncFilter switch
                {
                    "Synced" => true,
                    "Not Synced" => false,
                    _ => null
                };

                var dbPath = AppConfigurationManager.GetDatabasePath();
                var repo = new WeighingRepository(dbPath);
                _allFiltered = await repo.GetTransactionsByFilterAsync(FromDate, ToDate, syncFilter);

                TotalRecords = _allFiltered.Count;
                TotalPages = TotalRecords == 0 ? 1 : (int)Math.Ceiling(TotalRecords / (double)PageSize);
                if (CurrentPage > TotalPages) CurrentPage = TotalPages;

                ApplyPage();

                var fromStr = FromDate?.ToString("dd-MM-yyyy") ?? "-";
                var toStr = ToDate?.ToString("dd-MM-yyyy") ?? "-";
                var syncStr = SelectedSyncFilter != "All" ? $" | {SelectedSyncFilter}" : "";
                StatusMessage = $"Loaded {TotalRecords} transaction(s) from {fromStr} to {toStr}{syncStr}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private IEnumerable<WeighingTransaction> GetSearchFiltered()
        {
            var search = SearchText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(search))
                return _allFiltered;
            var lower = search.ToLowerInvariant();
            return _allFiltered.Where(t =>
                (t.Id?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.BatchNumber?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.MaterialCode?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.MaterialName?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.Category?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.Variant?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.WeighedBy?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (t.WeighedDate?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                t.ActualWeight.ToString("F3").Contains(lower) ||
                (t.IsSynced ? "synced" : "not synced").Contains(lower));
        }

        private void ApplyPage()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                PagedTransactions.Clear();

                var filtered = GetSearchFiltered().ToList();
                // Update paging info based on search
                TotalRecords = filtered.Count;
                TotalPages = TotalRecords == 0 ? 1 : (int)Math.Ceiling(TotalRecords / (double)PageSize);
                if (CurrentPage > TotalPages) CurrentPage = TotalPages;

                var page = filtered
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToList();

                int slNo = (CurrentPage - 1) * PageSize + 1;
                foreach (var t in page)
                {
                    PagedTransactions.Add(new RecentTransactionRowViewModel
                    {
                        SlNo = slNo++,
                        Id = t.Id,
                        BatchNumber = t.BatchNumber,
                        MaterialCode = t.MaterialCode,
                        MaterialName = t.MaterialName,
                        Category = t.Category,
                        Variant = t.Variant,
                        ActualWeight = t.ActualWeight,
                        WeighedBy = t.WeighedBy,
                        WeighedDate = t.WeighedDate,
                        IsSynced = t.IsSynced
                    });
                }

                RaisePropertyChanged(nameof(PageInfo));
            });
        }

        private void ViewTransaction(RecentTransactionRowViewModel? row)
        {
            if (row == null) return;

            Task.Run(async () =>
            {
                try
                {
                    var dbPath = AppConfigurationManager.GetDatabasePath();
                    var repo = new WeighingRepository(dbPath);
                    var t = await repo.GetTransactionByIdAsync(row.Id);
                    if (t == null) return;

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        var details =
                            $"=== Transaction Details ===\n\n" +
                            $"Transaction UUID: {t.Id.ToUpperInvariant()}\n" +
                            $"Batch Number: {t.BatchNumber}\n" +
                            $"Material Code: {t.MaterialCode}\n" +
                            $"Material Name: {t.MaterialName}\n" +
                            $"Target Weight: {t.TargetWeight:F3} kg\n" +
                            $"Achieved Weight: {t.ActualWeight:F3} kg\n" +
                            $"Deviation: {t.Deviation:F3}\n" +
                            $"Tare Weight: {t.TareWeight:F3} kg\n" +
                            $"Status: {t.Status}\n" +
                            $"Weighed By: {t.WeighedBy}\n" +
                            $"Weighed Date & Time: {t.WeighedDate}\n" +
                            $"Customer: {t.CustomerName}\n" +
                            $"Location: {t.Location}\n" +
                            $"Equipment: {t.EquipmentId}\n" +
                            $"Synced: {(t.IsSynced ? "Yes" : "No")}";

                        var window = new Window
                        {
                            Title = $"Transaction {t.Id} - Details",
                            Width = 500,
                            Height = 480,
                            WindowStartupLocation = WindowStartupLocation.CenterScreen,
                            Content = new System.Windows.Controls.ScrollViewer
                            {
                                Content = new System.Windows.Controls.TextBox
                                {
                                    Text = details,
                                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                                    FontSize = 12,
                                    IsReadOnly = true,
                                    TextWrapping = TextWrapping.Wrap,
                                    Padding = new Thickness(15)
                                }
                            }
                        };
                        window.ShowDialog();
                    });
                }
                catch (Exception ex)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Error viewing transaction: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });
        }

        private void ViewServerResponse(RecentTransactionRowViewModel? row)
        {
            if (row == null) return;

            Task.Run(async () =>
            {
                try
                {
                    var dbPath = AppConfigurationManager.GetDatabasePath();
                    var repo = new WeighingRepository(dbPath);
                    var t = await repo.GetTransactionByIdAsync(row.Id);
                    if (t == null) return;

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        var title = $"Server Response - {row.Id.ToUpperInvariant()}";

                        if (string.IsNullOrWhiteSpace(t.ServerResponse))
                        {
                            var window = new Window
                            {
                                Title = title,
                                Width = 520,
                                Height = 260,
                                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                                ResizeMode = ResizeMode.CanResize,
                                Content = new StackPanel
                                {
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    VerticalAlignment = VerticalAlignment.Center,
                                    Children =
                                    {
                                        new TextBlock
                                        {
                                            Text = "No response available",
                                            FontSize = 16,
                                            FontWeight = FontWeights.SemiBold,
                                            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                                            HorizontalAlignment = HorizontalAlignment.Center
                                        },
                                        new TextBlock
                                        {
                                            Text = "This transaction has not received a response from the server yet.",
                                            FontSize = 12,
                                            Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                                            Margin = new Thickness(0, 6, 0, 0),
                                            HorizontalAlignment = HorizontalAlignment.Center,
                                            TextWrapping = TextWrapping.Wrap
                                        }
                                    }
                                }
                            };
                            window.ShowDialog();
                            return;
                        }

                        var displayText = JsonSyntaxHighlighter.PrettyPrint(t.ServerResponse);

                        var richTextBox = new RichTextBox
                        {
                            IsReadOnly = true,
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 13,
                            Background = Brushes.White,
                            BorderThickness = new Thickness(0),
                            Padding = new Thickness(12),
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                        };

                        JsonSyntaxHighlighter.Highlight(richTextBox.Document, displayText);
                        richTextBox.Document.PageWidth = 100000; // keep long JSON lines on one line (horizontal scroll)

                        var responseWindow = new Window
                        {
                            Title = title,
                            Width = 640,
                            Height = 460,
                            WindowStartupLocation = WindowStartupLocation.CenterScreen,
                            Content = richTextBox
                        };
                        responseWindow.ShowDialog();
                    });
                }
                catch (Exception ex)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Error viewing server response: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });
        }

        private void CopyToClipboard(RecentTransactionRowViewModel? row, string fieldName)
        {
            if (row == null) return;

            try
            {
                string valueToCopy = fieldName switch
                {
                    "Id" => row.Id.ToUpperInvariant(),
                    "BatchNumber" => row.BatchNumber,
                    "MaterialCode" => row.MaterialCode,
                    _ => string.Empty
                };

                string displayName = fieldName switch
                {
                    "Id" => "Transaction UUID",
                    "BatchNumber" => "Batch Number",
                    "MaterialCode" => "Material Code",
                    _ => fieldName
                };

                if (!string.IsNullOrEmpty(valueToCopy))
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        Clipboard.SetText(valueToCopy);
                        StatusMessage = $"{displayName} copied to clipboard: {valueToCopy}";
                    });
                }
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error copying to clipboard: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private async Task SyncTransactionAsync(RecentTransactionRowViewModel? row)
        {
            if (row == null) return;

            // Gate: sync must be enabled in App Settings before any manual sync.
            if (!ProWeighSuitePlatforms.Services.SyncGate.Enabled)
            {
                MessageBox.Show(
                    "Sync is currently disabled.\n\n" +
                    "Enable \"Enable Sync Setting\" under Configuration > App Settings to allow syncing.",
                    "Sync Disabled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Gate: primary API URL must be configured before any sync is allowed
                var syncSettingsService = new Services.SyncSettingsService();
                var syncSettings = await syncSettingsService.LoadAsync();
                if (string.IsNullOrWhiteSpace(syncSettings?.ApiBaseUrl))
                {
                    MessageBox.Show(
                        "Sync is not configured.\n\n" +
                        "Please go to Settings > Sync Settings and enter the Primary API Base URL before syncing.",
                        "Sync Not Configured",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var liteQueueService = _retryQueue;

                var dbPath = AppConfigurationManager.GetDatabasePath();
                var repo = new WeighingRepository(dbPath);
                var transaction = await repo.GetTransactionByIdAsync(row.Id);
                if (transaction == null)
                {
                    MessageBox.Show("Transaction not found in database.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                bool isResync = row.IsSynced;

                if (isResync)
                {
                    var confirm = MessageBox.Show(
                        $"Transaction {row.IdUpperCase} has already been synced.\n\n" +
                        "Re-sync will push it to the API again.\n" +
                        "Use this only if the data was lost on the server side.\n\n" +
                        "Continue?",
                        "Re-sync Confirmation",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.Yes) return;

                    // Mark as unsynced in DB so the queue worker re-marks it synced on success
                    await repo.UpdateTransactionSyncStatusAsync(row.Id, false);
                    row.IsSynced = false;
                }

                // Adjust failure count if this was a previously failed transaction
                bool wasPreviouslyFailed = liteQueueService.IsTransactionFailed(row.Id);
                liteQueueService.EnqueueTransaction(transaction);
                if (wasPreviouslyFailed)
                    liteQueueService.AdjustFailureCountForRetry(1);

                MessageBox.Show(
                    $"Transaction {row.IdUpperCase} has been enqueued for {(isResync ? "re-sync" : "sync")}.\n" +
                    "It will be processed by the background sync worker.",
                    isResync ? "Re-sync Queued" : "Sync Queued",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error syncing transaction: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class RecentTransactionRowViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public int SlNo { get; set; }
        public string Id { get; set; } = string.Empty;
        public string IdUpperCase => Id.ToUpperInvariant();
        public string BatchNumber { get; set; } = string.Empty;
        public string MaterialCode { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Variant { get; set; } = string.Empty;
        public double ActualWeight { get; set; }
        public string Unit { get; } = "kg";
        public string WeighedBy { get; set; } = string.Empty;
        public string WeighedDate { get; set; } = string.Empty;

        private bool _isSynced;
        public bool IsSynced
        {
            get => _isSynced;
            set
            {
                _isSynced = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSynced)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SyncStatusText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SyncButtonLabel)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SyncButtonIcon)));
            }
        }

        public string SyncStatusText => IsSynced ? "Synced" : "Not Synced";

        /// <summary>"Sync" for unsynced transactions; "Re-sync" once already synced (to allow re-push on data loss).</summary>
        public string SyncButtonLabel => IsSynced ? "Re-sync" : "Sync";

        /// <summary>Icon changes to reflect re-sync intent.</summary>
        public string SyncButtonIcon => IsSynced ? "CloudSync" : "Sync";

        // Sync button enabled only while sync is enabled in App Settings.
        public bool CanSync => ProWeighSuitePlatforms.Services.SyncGate.Enabled;
    }
}
