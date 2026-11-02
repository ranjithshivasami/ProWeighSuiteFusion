using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ProWeighSuitePlatforms.Contracts;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>Event data raised after an automatic daily reset completes.</summary>
    public sealed class DailyResetEventArgs : EventArgs
    {
        public DateTime ResetDate { get; init; }
        public int PendingCleared { get; init; }
        public int FailedCleared { get; init; }
    }

    /// <summary>
    /// Automatically clears the pending sync queue and the failed-sync (dead letter)
    /// records once per day at midnight. If the app is closed at midnight, the reset
    /// runs on the next application startup (a stored last-reset date is compared with
    /// the current date). A lightweight timer checks the date every minute while running.
    ///
    /// All checks run asynchronously and the heavy clearing work is offloaded to a
    /// background thread so the UI thread (login screen, weighing screen) never blocks.
    /// </summary>
    public class DailyResetService
    {
        private readonly AppSettingsService _appSettingsService = new();
        private readonly ITransactionRetryQueue _queue;
        private readonly DispatcherTimer _timer;
        private int _resetInProgress; // 0 = idle, 1 = a reset is running

        /// <summary>
        /// Raised after a daily reset has cleared pending/failed data, so open
        /// Queue Monitor / Failed Sync Viewer windows can refresh immediately.
        /// </summary>
        public static event EventHandler<DailyResetEventArgs>? DailyResetCompleted;

        public DailyResetService(ITransactionRetryQueue queue)
        {
            _queue = queue;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _timer.Tick += async (s, e) =>
            {
                try
                {
                    await CheckAndResetIfNeededAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DailyReset] Tick error: {ex.Message}");
                }
            };
        }

        /// <summary>
        /// Starts the daily reset service: performs an immediate check (covers the
        /// "app was closed at midnight" case) without blocking the UI, then starts
        /// the one-minute ticker. Call on the UI thread after queue services are up.
        /// </summary>
        public void Start()
        {
            // Fire-and-forget so startup never blocks the login screen.
            _ = CheckAndResetIfNeededAsync();
            _timer.Start();
            Debug.WriteLine("[DailyReset] Service started (checks every 1 minute)");
        }

        public void Stop()
        {
            _timer.Stop();
            Debug.WriteLine("[DailyReset] Service stopped");
        }

        /// <summary>
        /// Compares the stored last-reset date with today and performs a reset when a
        /// new day has begun. On first run (no stored date) it simply records today's
        /// date without clearing anything, to avoid a destructive first-run reset.
        /// </summary>
        public async Task CheckAndResetIfNeededAsync()
        {
            try
            {
                var settings = await _appSettingsService.LoadAsync();
                var today = DateTime.Today;

                if (string.IsNullOrWhiteSpace(settings.LastDailyResetDate))
                {
                    settings.LastDailyResetDate = today.ToString("yyyy-MM-dd");
                    await _appSettingsService.SaveAsync(settings);
                    Debug.WriteLine($"[DailyReset] No previous reset date recorded. Initialized to {today:yyyy-MM-dd} (nothing cleared).");
                    return;
                }

                if (!DateTime.TryParse(settings.LastDailyResetDate, out var lastReset))
                {
                    settings.LastDailyResetDate = today.ToString("yyyy-MM-dd");
                    await _appSettingsService.SaveAsync(settings);
                    Debug.WriteLine("[DailyReset] Stored last-reset date was invalid; re-initialized without clearing.");
                    return;
                }

                if (today > lastReset.Date)
                {
                    // Guard against concurrent startup-check + timer-tick resets.
                    if (Interlocked.CompareExchange(ref _resetInProgress, 1, 0) == 0)
                    {
                        try
                        {
                            await PerformResetAsync(settings, today);
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _resetInProgress, 0);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DailyReset] Check error: {ex.Message}");
            }
        }

        private async Task PerformResetAsync(AppSettings settings, DateTime today)
        {
            // Heavy clearing work runs on a background thread so the UI thread
            // (login screen, weighing screen) never freezes, even with a large queue.
            var (pendingCleared, failedCleared, successCleared, deadLetterCleared) = await Task.Run(() =>
            {
                var result = _queue.ResetPendingAndFailed();
                var pending = result.PendingCleared;
                var failed = result.FailedCleared;
                var success = result.SuccessCleared;
                var deadLetter = ClearDeadLetterQueue();
                return (pending, failed, success, deadLetter);
            });

            // Reload the latest settings right before saving. The copy loaded at
            // startup may be minutes old; saving it back here would clobber any
            // settings the user edited while the reset work was running
            // (e.g. App Settings saved during startup).
            AppSettings latest;
            try { latest = await _appSettingsService.LoadAsync(); }
            catch (Exception loadEx)
            {
                Debug.WriteLine($"[DailyReset] Reload before save failed, falling back to startup snapshot: {loadEx.Message}");
                latest = settings;
            }
            latest.LastDailyResetDate = today.ToString("yyyy-MM-dd");
            await _appSettingsService.SaveAsync(latest);

            Debug.WriteLine($"[DailyReset] Performed daily reset on {today:yyyy-MM-dd}: Pending={pendingCleared}, Failed(status)={failedCleared}, Success(status)={successCleared}, DeadLetter(records)={deadLetterCleared}");

            // Audit log entry
            try
            {
                var audit = new AuditService();
                await audit.LogAsync(new AuditEntry
                {
                    Action = "DailyReset",
                    Category = AuditCategory.System,
                    Username = "System",
                    Details = $"Automatic daily reset performed. Pending items cleared: {pendingCleared}, " +
                              $"Failed status records cleared: {failedCleared}, " +
                              $"Success status records cleared: {successCleared}, " +
                              $"Dead-letter records cleared: {deadLetterCleared}. Reset date: {today:yyyy-MM-dd}.",
                    Severity = AuditSeverity.Info
                });
            }
            catch (Exception logEx)
            {
                Debug.WriteLine($"[DailyReset] Audit log failed: {logEx.Message}");
            }

            // Notify open views so they refresh. Continue on the UI dispatcher in
            // case the reset was triggered from a background context.
            var args = new DailyResetEventArgs
            {
                ResetDate = today,
                PendingCleared = pendingCleared,
                FailedCleared = failedCleared + deadLetterCleared
            };

            if (Application.Current?.Dispatcher.CheckAccess() == true)
            {
                DailyResetCompleted?.Invoke(null, args);
            }
            else
            {
                Application.Current?.Dispatcher.InvokeAsync(() => DailyResetCompleted?.Invoke(null, args));
            }
        }

        /// <summary>
        /// Deletes the dead-letter (failed sync) queue file and returns the number of
        /// entries that were present, so it can be included in the audit log.
        /// </summary>
        private static int ClearDeadLetterQueue()
        {
            try
            {
                var dbPath = AppConfigurationManager.GetDatabasePath();
                var deadLetterPath = Path.Combine(Path.GetDirectoryName(dbPath) ?? "", "dead_letter_queue.json");

                if (!File.Exists(deadLetterPath))
                    return 0;

                var count = CountDeadLetterEntries(deadLetterPath);
                File.Delete(deadLetterPath);
                Debug.WriteLine($"[DailyReset] Cleared dead letter queue ({count} records): {deadLetterPath}");
                return count;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DailyReset] Failed to clear dead letter queue: {ex.Message}");
                return 0;
            }
        }

        private static int CountDeadLetterEntries(string path)
        {
            try
            {
                var lines = File.ReadAllLines(path);
                int count = 0;
                var currentJson = new StringBuilder();

                foreach (var line in lines)
                {
                    if (line.StartsWith("----"))
                    {
                        if (currentJson.Length > 0)
                        {
                            count++;
                            currentJson.Clear();
                        }
                    }
                    else
                    {
                        currentJson.AppendLine(line);
                    }
                }

                if (currentJson.Length > 0)
                    count++;

                return count;
            }
            catch
            {
                return 0;
            }
        }
    }
}
