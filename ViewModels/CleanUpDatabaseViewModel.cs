using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Security;
using ProWeighSuitePlatforms.Navigation;
using ProWeighSuitePlatforms.Contracts;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Views;
using Microsoft.Data.Sqlite;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class CleanUpDatabaseViewModel : BaseViewModel
    {
        private readonly ICurrentUserContext _userContext;
        private readonly ITransactionRetryQueue _queue;
        private readonly string _flavor;
        private readonly AuditService _auditService;

        public string Title => "Clean Up Database";

        private bool _mastersSectionChecked;
        public bool MastersSectionChecked
        {
            get => _mastersSectionChecked;
            set => SetProperty(ref _mastersSectionChecked, value);
        }

        private bool _transactionsSectionChecked;
        public bool TransactionsSectionChecked
        {
            get => _transactionsSectionChecked;
            set => SetProperty(ref _transactionsSectionChecked, value);
        }

        private bool _auditLogsSectionChecked = true;
        public bool AuditLogsSectionChecked
        {
            get => _auditLogsSectionChecked;
            set => SetProperty(ref _auditLogsSectionChecked, value);
        }

        private bool _queueSectionChecked = true;
        public bool QueueSectionChecked
        {
            get => _queueSectionChecked;
            set => SetProperty(ref _queueSectionChecked, value);
        }

        public ICommand ClearCommand { get; }
        public ICommand BackCommand { get; }

        public CleanUpDatabaseViewModel(
            ICurrentUserContext userContext,
            ITransactionRetryQueue queue,
            string flavor)
        {
            _userContext = userContext;
            _queue = queue;
            _flavor = flavor;
            _auditService = new AuditService();

            ClearCommand = new RelayCommand(async _ => await ExecuteClearAsync(), _ => CanExecuteClear());
            BackCommand = new RelayCommand(_ => NavigateBack());
        }

        private bool CanExecuteClear()
        {
            return MastersSectionChecked || TransactionsSectionChecked || 
                   AuditLogsSectionChecked || QueueSectionChecked;
        }

        private async Task ExecuteClearAsync()
        {
            try
            {
                // Show password confirmation dialog
                var passwordConfirmed = await PasswordPromptWindow.ConfirmPasswordAsync(
                    Application.Current.MainWindow,
                    _userContext.CurrentUser?.Username ?? string.Empty);

                if (!passwordConfirmed)
                {
                    MessageBox.Show("Invalid password. Cleanup operation cancelled.", "Authentication Failed",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Perform cleanup
                var result = MessageBox.Show(
                    "Are you sure you want to proceed with database cleanup?\n\n" +
                    "This action cannot be undone and selected data will be permanently deleted.",
                    "Confirm Cleanup",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                await PerformCleanupAsync();

                // Log audit
                await _auditService.LogAsync(new AuditEntry
                {
                    Username = _userContext.CurrentUser?.Username ?? "System",
                    Action = "DatabaseCleanup",
                    Details = $"Cleaned: Masters={MastersSectionChecked}, Transactions={TransactionsSectionChecked}, " +
                             $"AuditLogs={AuditLogsSectionChecked}, Queue={QueueSectionChecked}",
                    Category = AuditCategory.System
                });

                // Refresh Queue Monitor if queue was cleaned
                if (QueueSectionChecked)
                {
                    System.Diagnostics.Debug.WriteLine("[CleanupDB] Requesting Queue Monitor refresh after queue cleanup");
                    QueueMonitorViewModel.RequestRefresh();
                }

                MessageBox.Show("Database cleanup completed successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                // Reset checkboxes
                MastersSectionChecked = false;
                TransactionsSectionChecked = false;
                AuditLogsSectionChecked = false;
                QueueSectionChecked = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during cleanup: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                await _auditService.LogAsync(new AuditEntry
                {
                    Username = _userContext.CurrentUser?.Username ?? "System",
                    Action = "DatabaseCleanup",
                    Details = $"Error: {ex.Message}",
                    Category = AuditCategory.System
                });
            }
        }

        private async Task PerformCleanupAsync()
        {
            var dbPath = AppConfigurationManager.GetDatabasePath();

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                if (MastersSectionChecked)
                {
                    // Clean masters data
                    ExecuteCommand(connection, "DELETE FROM materials");
                    ExecuteCommand(connection, "DELETE FROM categories");
                    ExecuteCommand(connection, "DELETE FROM variants");
                    ExecuteCommand(connection, "DELETE FROM scale_settings");
                    ExecuteCommand(connection, "DELETE FROM company_settings");
                    ExecuteCommand(connection, "DELETE FROM zpl_templates");
                }

                if (TransactionsSectionChecked)
                {
                    // Clean transaction data (flavor-gated: Fusion can never wipe weighing_*).
                    if (string.Equals(_flavor, "Fusion", StringComparison.OrdinalIgnoreCase))
                    {
                        ExecuteCommand(connection, "DELETE FROM fusion_transactions");
                        ExecuteCommand(connection, "DELETE FROM fusion_tub_events");
                        ExecuteCommand(connection, "DELETE FROM fusion_bay_inventory");
                        ExecuteCommand(connection, "DELETE FROM fusion_batch_lots");
                        ExecuteCommand(connection, "DELETE FROM fusion_batches");
                        ExecuteCommand(connection, "DELETE FROM fusion_inward");
                        ExecuteCommand(connection, "DELETE FROM fusion_tickets");
                    }
                    else
                    {
                        ExecuteCommand(connection, "DELETE FROM weighing_transactions");
                        ExecuteCommand(connection, "DELETE FROM weighing_batches");
                    }
                }

                if (AuditLogsSectionChecked)
                {
                    // Clean audit logs from both possible locations
                    var roamingPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    var localPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                    // Check Roaming AppData (primary location)
                    var roamingAuditLogs = Path.Combine(roamingPath, "ProWeighSuitePlatforms", "AuditLogs");
                    CleanAuditLogsDirectory(roamingAuditLogs);

                    // Check Local AppData (fallback location)
                    var localAuditLogs = Path.Combine(localPath, "ProWeighSuitePlatforms", "AuditLogs");
                    CleanAuditLogsDirectory(localAuditLogs);
                }

                if (QueueSectionChecked)
                {
                    // Clean queue data from both SQLite and LiteDB
                    System.Diagnostics.Debug.WriteLine("[CleanupDB] Starting queue cleanup...");

                    // Clean SQLite staging table (may not exist in all installations)
                    ExecuteCommand(connection, "DELETE FROM transaction_staging");
                    System.Diagnostics.Debug.WriteLine("[CleanupDB] Cleaned transaction_staging table");

                    // Commit the SQLite transaction before dealing with the external LiteDB file
                    transaction.Commit();
                    System.Diagnostics.Debug.WriteLine("[CleanupDB] SQLite transaction committed");
                }

                if (!QueueSectionChecked)
                {
                    // Only commit here if queue section wasn't checked (it commits early)
                    transaction.Commit();
                }
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch
                {
                    // Transaction may have been committed already
                }
                throw;
            }
            finally
            {
                connection.Close();
            }

            // Clean LiteDB queue file outside of SQLite transaction
            if (QueueSectionChecked)
            {
                await CleanLiteDbQueueFileAsync(dbPath);
            }
        }

        private void ExecuteCommand(SqliteConnection connection, string commandText)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = commandText;
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.Message.Contains("no such table"))
            {
                // Table doesn't exist, ignore
                System.Diagnostics.Debug.WriteLine($"Table not found: {commandText}");
            }
        }

        private void CleanAuditLogsDirectory(string auditLogsPath)
        {
            if (!Directory.Exists(auditLogsPath))
            {
                System.Diagnostics.Debug.WriteLine($"[CleanupDB] Audit logs directory not found: {auditLogsPath}");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[CleanupDB] Cleaning audit logs from: {auditLogsPath}");
            int deletedCount = 0;
            int failedCount = 0;

            foreach (var file in Directory.GetFiles(auditLogsPath, "*.log"))
            {
                try
                {
                    File.Delete(file);
                    deletedCount++;
                    System.Diagnostics.Debug.WriteLine($"[CleanupDB] Deleted: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    failedCount++;
                    System.Diagnostics.Debug.WriteLine($"[CleanupDB] Failed to delete {Path.GetFileName(file)}: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[CleanupDB] Audit logs cleanup complete. Deleted: {deletedCount}, Failed: {failedCount}");
        }

        private async Task CleanLiteDbQueueFileAsync(string mainDbPath)
        {
            try
            {
                var queueDirectory = Path.GetDirectoryName(mainDbPath) ?? 
                                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var queueDbPath = Path.Combine(queueDirectory, "transaction_queue.db");

                System.Diagnostics.Debug.WriteLine($"[CleanupDB] Looking for LiteDB queue at: {queueDbPath}");

                bool wasServiceRunning = true;
                System.Diagnostics.Debug.WriteLine("[CleanupDB] Stopping LiteQueue sync service...");

                    try
                    {
                        var stopTask = _queue.StopAsync();
                        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(10))) == stopTask;
                        if (!completed)
                        {
                            System.Diagnostics.Debug.WriteLine("[CleanupDB] Service stop timed out");
                            throw new TimeoutException("Service did not stop within the timeout period");
                        }
                        await stopTask;
                        System.Diagnostics.Debug.WriteLine("[CleanupDB] Service stopped successfully");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CleanupDB] Error stopping service: {ex.Message}");
                        throw new InvalidOperationException("Failed to stop the queue service. Please close the application and try again.", ex);
                    }

                    await Task.Delay(1500);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    await Task.Delay(1000);

                // Now delete the queue database file completely
                if (File.Exists(queueDbPath))
                {
                    System.Diagnostics.Debug.WriteLine("[CleanupDB] Attempting to delete queue database file...");

                    bool deleted = false;
                    for (int i = 0; i < 5; i++)
                    {
                        try
                        {
                            // Delete the main database file
                            File.Delete(queueDbPath);

                            // Delete associated log file if it exists
                            var queueDbLogPath = queueDbPath + "-log";
                            if (File.Exists(queueDbLogPath))
                            {
                                File.Delete(queueDbLogPath);
                                System.Diagnostics.Debug.WriteLine("[CleanupDB] Deleted queue log file");
                            }

                            // Delete journal file if it exists
                            var queueDbJournalPath = queueDbPath + "-journal";
                            if (File.Exists(queueDbJournalPath))
                            {
                                File.Delete(queueDbJournalPath);
                                System.Diagnostics.Debug.WriteLine("[CleanupDB] Deleted queue journal file");
                            }

                            System.Diagnostics.Debug.WriteLine("[CleanupDB] Queue database files deleted successfully");
                            deleted = true;
                            break;
                        }
                        catch (IOException ioEx) when (i < 4)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CleanupDB] Delete attempt {i + 1}/5 failed: {ioEx.Message}");
                            await Task.Delay(2000);
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }
                        catch (UnauthorizedAccessException uaEx) when (i < 4)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CleanupDB] Access denied on attempt {i + 1}/5: {uaEx.Message}");
                            await Task.Delay(2000);
                        }
                    }

                    if (!deleted)
                    {
                        throw new IOException("Unable to delete queue database file after 5 attempts. The file may still be locked.");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[CleanupDB] Queue database file does not exist - nothing to clean");
                }

                if (wasServiceRunning)
                {
                    System.Diagnostics.Debug.WriteLine("[CleanupDB] Restarting LiteQueue sync service...");
                    try
                    {
                        var startTask = _queue.StartAsync();
                        var started = await Task.WhenAny(startTask, Task.Delay(TimeSpan.FromSeconds(10))) == startTask;
                        if (!started)
                        {
                            System.Diagnostics.Debug.WriteLine("[CleanupDB] Service start timed out");
                            throw new TimeoutException("Service did not start within the timeout period");
                        }
                        await startTask;
                        System.Diagnostics.Debug.WriteLine("[CleanupDB] Service restarted successfully with fresh empty queue");

                        // Trigger Queue Monitor refresh to show the cleared queue immediately
                        System.Diagnostics.Debug.WriteLine("[CleanupDB] Triggering Queue Monitor refresh...");
                        QueueMonitorViewModel.RequestRefresh();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CleanupDB] Error restarting service: {ex.Message}");
                        MessageBox.Show(
                            "Queue cleanup completed successfully, but the sync service could not be restarted.\n" +
                            "Please restart the application to resume queue synchronization.",
                            "Warning",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[CleanupDB] Queue cleanup completed (service was not running)");

                    // Still trigger refresh even if service wasn't running
                    QueueMonitorViewModel.RequestRefresh();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CleanupDB] Queue cleanup failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[CleanupDB] Stack trace: {ex.StackTrace}");

                MessageBox.Show(
                    $"Unable to clear the queue database.\n\n" +
                    $"Error: {ex.Message}\n\n" +
                    $"Please close and restart the application, then try the cleanup again.",
                    "Queue Cleanup Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                    try
                    {
                        System.Diagnostics.Debug.WriteLine("[CleanupDB] Attempting to restart service after failed cleanup...");
                        var restartTask = _queue.StartAsync();
                        await Task.WhenAny(restartTask, Task.Delay(TimeSpan.FromSeconds(5)));
                    }
                    catch
                    {
                        System.Diagnostics.Debug.WriteLine("[CleanupDB] Could not restart service after failed cleanup");
                    }

                throw;
            }
        }

        private void NavigateBack()
        {
            ProductNavigationRequests.RequestShowRoute("Home");
        }
    }
}
