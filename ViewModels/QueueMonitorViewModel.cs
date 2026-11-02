using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Contracts;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class QueueMonitorViewModel : INotifyPropertyChanged
    {
        private readonly DispatcherTimer _timer;
        private bool _isRefreshing;

        // Static event to allow external components to request queue monitor refresh
        public static event EventHandler? RefreshRequested;

        public event PropertyChangedEventHandler? PropertyChanged;

        private int _queueSize;
        public int QueueSize
        {
            get => _queueSize;
            set { _queueSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasItems)); }
        }

        private int _successCount;
        public int SuccessCount
        {
            get => _successCount;
            set { _successCount = value; OnPropertyChanged(); }
        }

        private int _failureCount;
        public int FailureCount
        {
            get => _failureCount;
            set { _failureCount = value; OnPropertyChanged(); }
        }

        private int _totalDbSynced;
        public int TotalDbSynced
        {
            get => _totalDbSynced;
            set { _totalDbSynced = value; OnPropertyChanged(); }
        }

        private int _totalDbUnsynced;
        public int TotalDbUnsynced
        {
            get => _totalDbUnsynced;
            set { _totalDbUnsynced = value; OnPropertyChanged(); }
        }

        private string _lastSync = "N/A";
        public string LastSync
        {
            get => _lastSync;
            set { _lastSync = value; OnPropertyChanged(); }
        }

        private string _status = "Initializing...";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        private string _apiUrl = "Loading...";
        public string ApiUrl
        {
            get => _apiUrl;
            set { _apiUrl = value; OnPropertyChanged(); }
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
        }

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
        public bool HasItems => QueueSize > 0;

        public ICommand RefreshCommand { get; }
        public ICommand ClearQueueCommand { get; }
        public ICommand DeleteSuccessfulCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand ShowDebugInfoCommand { get; }
        public ICommand ForceClearCommand { get; }

        private readonly ITransactionRetryQueue _queue;

        public QueueMonitorViewModel(ITransactionRetryQueue queue)
        {
            _queue = queue;
            RefreshCommand = new RelayCommand(async _ => await RefreshStatusAsync());
            ClearQueueCommand = new RelayCommand(_ => ClearQueue(), _ => QueueSize > 0);
            DeleteSuccessfulCommand = new RelayCommand(_ => DeleteSuccessfulItems(), _ => SuccessCount > 0);
            CloseCommand = new RelayCommand(_ => CloseWindow());
            ShowDebugInfoCommand = new RelayCommand(_ => ShowDebugInfo());
            ForceClearCommand = new RelayCommand(_ => ForceClearQueue());

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += async (s, e) => await RefreshStatusAsync();
            _timer.Start();

            // Refresh immediately when the daily auto-reset clears the queue
            DailyResetService.DailyResetCompleted += OnDailyResetCompleted;

            // Subscribe to external refresh requests
            RefreshRequested += async (s, e) => await RefreshStatusAsync();

            _ = InitializeAsync();
        }

        private void OnDailyResetCompleted(object? sender, DailyResetEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[QueueMonitor] Daily reset detected ({e.ResetDate:yyyy-MM-dd}); refreshing counts.");
            _ = RefreshStatusAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                await RefreshStatusAsync();
            }
            catch (Exception ex)
            {
                Status = $"Initialization error: {ex.Message}";
            }
        }

        private async Task RefreshStatusAsync()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                var stats = _queue.GetStats();
                QueueSize = stats.Pending;
                SuccessCount = stats.Success;
                FailureCount = stats.Failures;

                // Read persisted sync counts from SQLite
                try
                {
                    var dbPath = AppConfigurationManager.GetDatabasePath();
                    var repo = new WeighingRepository(dbPath);
                    var (total, synced, unsynced) = await repo.GetSyncCountsAsync();
                    TotalDbSynced = synced;
                    TotalDbUnsynced = unsynced;
                }
                catch
                {
                    // ignore DB errors during refresh
                }

                LastSync = DateTime.Now.ToString("HH:mm:ss");

                // Update status message
                // NOTE: All counters reflect Primary API URL sync status only
                if (QueueSize > 0 && FailureCount > 0)
                {
                    Status = $"?? {QueueSize} pending - {FailureCount} failed (Primary API) in this session";
                    ErrorMessage = "?? Some items are failing to sync to Primary API. Check API endpoint in Settings > Sync Settings.\n" +
                                 "Items will retry up to 3 times before moving to dead letter queue.\n" +
                                 "Note: Counters track Primary API URL sync status only.";
                }
                else if (QueueSize > 0)
                {
                    Status = $"?? Processing {QueueSize} item(s)...";
                    ErrorMessage = string.Empty;
                }
                else if (FailureCount > 0)
                {
                    Status = $"? Queue empty - {SuccessCount} synced, {FailureCount} failed (Primary API) in this session";
                    ErrorMessage = $"?? {FailureCount} transaction(s) failed to sync to Primary API in this session.\n" +
                                 "They were moved to dead letter queue. Check API endpoint in Settings > Sync Settings.\n" +
                                 "Note: Counters track Primary API URL sync status only.";
                }
                else if (SuccessCount > 0)
                {
                    Status = $"? All synced - {SuccessCount} successful (Primary API) in this session";
                    ErrorMessage = string.Empty;
                }
                else
                {
                    Status = "? Queue empty - waiting for transactions";
                    ErrorMessage = string.Empty;
                }

                // Load API URL once
                if (ApiUrl == "Loading..." || ApiUrl == "Not configured" || string.IsNullOrEmpty(ApiUrl))
                {
                    try
                    {
                        var syncService = new SyncSettingsService();
                        var settings = await syncService.LoadAsync();
                        ApiUrl = settings?.ApiBaseUrl ?? "Not configured";

                        // Only show Primary API URL in Queue Monitor (secondary is for internal testing only)
                        // IMPORTANT: Sync counters track ONLY the Primary API URL success/failure
                        if (!string.IsNullOrEmpty(settings?.ApiBaseUrl2))
                        {
                            ApiUrl += " (Primary only - counters track primary sync status)";
                        }

                        if (string.IsNullOrEmpty(ApiUrl) || ApiUrl == "Not configured")
                            ErrorMessage = "?? API endpoint not configured!\nGo to Settings > Sync Settings to configure the API URL.";
                    }
                    catch
                    {
                        ApiUrl = "Error loading settings";
                    }
                }
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void ClearQueue()
        {
            try
            {
                var totalItems = QueueSize + SuccessCount + FailureCount;
                if (totalItems == 0)
                {
                    MessageBox.Show("Queue is Empty\n\nThere are no items in the current session to clear.",
                        "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var result = MessageBox.Show(
                    $"Clear Current Session Queue?\n\n" +
                    $"Pending: {QueueSize}\nSynced: {SuccessCount}\nFailed: {FailureCount}\n" +
                    $"Total: {totalItems} items\n\nThis action cannot be undone!",
                    "Clear Queue", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    _queue.ClearQueue();
                    _ = RefreshStatusAsync();
                    MessageBox.Show("Queue cleared successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteSuccessfulItems()
        {
            try
            {
                if (SuccessCount == 0)
                {
                    MessageBox.Show("No successful items to remove.", "Information",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var result = MessageBox.Show(
                    $"Remove {SuccessCount} successfully synced item(s) from current session?\n\n" +
                    "This will reset the success count to 0.",
                    "Remove Successful Items", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _queue.RemoveSuccessfulItems();
                    _ = RefreshStatusAsync();
                    MessageBox.Show("Successful items removed!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseWindow()
        {
            _timer?.Stop();

            // Unsubscribe from the static event to prevent memory leaks
            RefreshRequested -= async (s, e) => await RefreshStatusAsync();
            DailyResetService.DailyResetCompleted -= OnDailyResetCompleted;

            foreach (Window window in Application.Current.Windows)
            {
                if (window.DataContext == this) { window.Close(); break; }
            }
        }

        /// <summary>
        /// Triggers a refresh for all active Queue Monitor instances.
        /// Can be called from external components after queue-related operations.
        /// </summary>
        public static void RequestRefresh()
        {
            RefreshRequested?.Invoke(null, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine("[QueueMonitor] External refresh requested");
        }

        private async void ShowDebugInfo()
        {
            try
            {
                var debugInfo = await Task.Run(() => _queue.GetQueueDebugInfo());
                var debugWindow = new Window
                {
                    Title = "Queue Debug Information",
                    Width = 700, Height = 500,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = new System.Windows.Controls.ScrollViewer
                    {
                        Content = new System.Windows.Controls.TextBlock
                        {
                            Text = debugInfo,
                            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                            FontSize = 12, Margin = new Thickness(15),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                };
                debugWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ForceClearQueue()
        {
            try
            {
                var result = MessageBox.Show(
                    "FORCE CLEAR ALL DATA\n\n" +
                    "This will aggressively remove ALL queue data.\n" +
                    "Use only if regular Clear doesn't work.\n\n" +
                    $"Pending: {QueueSize}\nSynced: {SuccessCount}\nFailed: {FailureCount}\n\n" +
                    "Continue?",
                    "Force Clear", MessageBoxButton.YesNo, MessageBoxImage.Exclamation);

                if (result == MessageBoxResult.Yes)
                {
                    _queue.ForceCleanupAllData();
                    await RefreshStatusAsync();
                    MessageBox.Show("Force clear completed!", "Done",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
