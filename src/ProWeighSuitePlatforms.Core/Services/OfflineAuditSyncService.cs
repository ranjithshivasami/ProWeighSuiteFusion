using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Offline-first audit sync service with automatic retry when internet is restored
    /// </summary>
    public class OfflineAuditSyncService : IDisposable
    {
        private readonly AuditService _auditService;
        private readonly string _queuePath;
        private readonly string _syncEndpoint;
        private readonly System.Timers.Timer _retryTimer;
        private readonly SemaphoreSlim _syncLock = new(1, 1);
        private bool _isDisposed;

        // Sync status tracking
        public event EventHandler<SyncStatusChangedEventArgs>? SyncStatusChanged;
        public bool IsOnline { get; private set; }
        public int PendingSyncCount { get; private set; }
        public DateTime? LastSuccessfulSync { get; private set; }
        public DateTime? LastSyncAttempt { get; private set; }

        public OfflineAuditSyncService(AuditService auditService, string syncEndpoint, int retryIntervalMinutes = 5)
        {
            _auditService = auditService;
            _syncEndpoint = syncEndpoint;

            // Create queue directory
            var dbPath = AppConfigurationManager.GetDatabasePath();
            var queueDir = Path.Combine(Path.GetDirectoryName(dbPath) ?? "", "AuditSyncQueue");
            if (!Directory.Exists(queueDir))
            {
                Directory.CreateDirectory(queueDir);
            }
            _queuePath = queueDir;

            // Setup retry timer (checks every X minutes if there are pending items)
            _retryTimer = new System.Timers.Timer(TimeSpan.FromMinutes(retryIntervalMinutes).TotalMilliseconds);
            _retryTimer.Elapsed += async (s, e) => await ProcessQueueAsync();
            _retryTimer.AutoReset = true;
            _retryTimer.Start();

            // Initialize status
            _ = UpdateStatusAsync();
        }

        /// <summary>
        /// Queue audit entry for sync (called during weighing when internet might be down)
        /// </summary>
        public async Task QueueForSyncAsync(AuditEntry entry)
        {
            try
            {
                var queueItem = new AuditQueueItem
                {
                    Id = Guid.NewGuid().ToString(),
                    QueuedAt = DateTime.UtcNow,
                    RetryCount = 0,
                    SyncStatus = AuditSyncItemStatus.Pending,
                    Entry = entry
                };

                var fileName = $"audit_{queueItem.Id}_{DateTime.Now:yyyyMMddHHmmss}.json";
                var filePath = Path.Combine(_queuePath, fileName);

                var json = JsonSerializer.Serialize(queueItem, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, json);

                PendingSyncCount++;
                OnSyncStatusChanged(new SyncStatusChangedEventArgs
                {
                    Status = AuditSyncStatus.Queued,
                    Message = $"Audit entry queued for sync (ID: {queueItem.Id})",
                    PendingCount = PendingSyncCount
                });

                System.Diagnostics.Debug.WriteLine($"[OfflineAuditSync] Queued audit entry: {entry.Action} by {entry.Username}");

                // Try immediate sync if online and sync is enabled
                if (IsOnline && SyncGate.Enabled)
                {
                    _ = Task.Run(async () => await TrySyncSingleItemAsync(queueItem));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OfflineAuditSync] Error queuing entry: {ex.Message}");
            }
        }

        /// <summary>
        /// Process entire queue (called by timer or manually)
        /// </summary>
        public async Task ProcessQueueAsync()
        {
            if (!await _syncLock.WaitAsync(0))
            {
                // Already processing
                return;
            }

            try
            {
                // Sync master switch from App Settings. When disabled, skip the
                // upload entirely; the timer re-checks on its next interval so
                // re-enabling resumes without a restart.
                if (!SyncGate.Enabled)
                {
                    System.Diagnostics.Debug.WriteLine("[OfflineAuditSync] Sync disabled by settings - upload skipped.");
                    return;
                }

                LastSyncAttempt = DateTime.UtcNow;

                // Test connectivity first
                IsOnline = await TestConnectivityAsync();

                if (!IsOnline)
                {
                    OnSyncStatusChanged(new SyncStatusChangedEventArgs
                    {
                        Status = AuditSyncStatus.Offline,
                        Message = "Internet connection not available. Will retry later.",
                        PendingCount = PendingSyncCount
                    });
                    return;
                }

                // Get all queued items (only Pending and Failed status)
                var queuedFiles = Directory.GetFiles(_queuePath, "audit_*.json");
                
                // Filter to only process pending/failed items
                var itemsToProcess = new List<(string file, AuditQueueItem item)>();
                foreach (var file in queuedFiles)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var item = JsonSerializer.Deserialize<AuditQueueItem>(json);
                        
                        if (item != null && (item.SyncStatus == AuditSyncItemStatus.Pending || 
                                             item.SyncStatus == AuditSyncItemStatus.Failed))
                        {
                            itemsToProcess.Add((file, item));
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OfflineAuditSync] Error reading queue file: {ex.Message}");
                    }
                }

                PendingSyncCount = itemsToProcess.Count;

                if (itemsToProcess.Count == 0)
                {
                    OnSyncStatusChanged(new SyncStatusChangedEventArgs
                    {
                        Status = AuditSyncStatus.Idle,
                        Message = "No pending audit entries to sync",
                        PendingCount = 0
                    });
                    return;
                }

                OnSyncStatusChanged(new SyncStatusChangedEventArgs
                {
                    Status = AuditSyncStatus.Syncing,
                    Message = $"Starting sync of {itemsToProcess.Count} pending entries...",
                    PendingCount = itemsToProcess.Count
                });

                int successCount = 0;
                int failureCount = 0;

                foreach (var (file, queueItem) in itemsToProcess.OrderBy(x => x.item.QueuedAt))
                {
                    try
                    {
                        var success = await TrySyncSingleItemAsync(queueItem);
                        if (success)
                        {
                            // Mark as synced and move to Synced folder
                            queueItem.SyncStatus = AuditSyncItemStatus.Synced;
                            queueItem.LastSyncedAt = DateTime.UtcNow;
                            
                            var syncedDir = Path.Combine(_queuePath, "Synced");
                            if (!Directory.Exists(syncedDir))
                                Directory.CreateDirectory(syncedDir);

                            var syncedPath = Path.Combine(syncedDir, Path.GetFileName(file));
                            var updatedJson = JsonSerializer.Serialize(queueItem, new JsonSerializerOptions { WriteIndented = true });
                            await File.WriteAllTextAsync(syncedPath, updatedJson);
                            
                            // Delete from queue
                            File.Delete(file);
                            
                            successCount++;
                            PendingSyncCount--;
                        }
                        else
                        {
                            // Update retry count and mark as failed
                            queueItem.RetryCount++;
                            queueItem.LastRetryAt = DateTime.UtcNow;
                            queueItem.SyncStatus = AuditSyncItemStatus.Failed;
                            queueItem.LastError = "Sync failed - check network connection";
                            
                            var updatedJson = JsonSerializer.Serialize(queueItem, new JsonSerializerOptions { WriteIndented = true });
                            await File.WriteAllTextAsync(file, updatedJson);
                            
                            failureCount++;

                            // Give up after 10 retries (move to permanent failed folder)
                            if (queueItem.RetryCount >= 10)
                            {
                                queueItem.SyncStatus = AuditSyncItemStatus.PermanentlyFailed;
                                queueItem.LastError = "Max retry attempts reached (10)";
                                
                                var failedDir = Path.Combine(_queuePath, "Failed");
                                if (!Directory.Exists(failedDir))
                                    Directory.CreateDirectory(failedDir);

                                var failedPath = Path.Combine(failedDir, Path.GetFileName(file));
                                var failedJson = JsonSerializer.Serialize(queueItem, new JsonSerializerOptions { WriteIndented = true });
                                await File.WriteAllTextAsync(failedPath, failedJson);
                                
                                File.Delete(file);
                                PendingSyncCount--;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OfflineAuditSync] Error processing queue item: {ex.Message}");
                        failureCount++;
                    }
                }

                if (successCount > 0)
                {
                    LastSuccessfulSync = DateTime.UtcNow;
                }

                OnSyncStatusChanged(new SyncStatusChangedEventArgs
                {
                    Status = failureCount == 0 ? AuditSyncStatus.Completed : AuditSyncStatus.PartialSuccess,
                    Message = $"Sync completed: {successCount} succeeded, {failureCount} failed, {PendingSyncCount} pending",
                    PendingCount = PendingSyncCount
                });
            }
            finally
            {
                _syncLock.Release();
            }
        }

        /// <summary>
        /// Retry all failed items
        /// </summary>
        public async Task RetryFailedItemsAsync()
        {
            var failedDir = Path.Combine(_queuePath, "Failed");
            if (!Directory.Exists(failedDir))
                return;

            var failedFiles = Directory.GetFiles(failedDir, "audit_*.json");
            
            foreach (var file in failedFiles)
            {
                try
                {
                    // Move back to queue directory and reset status
                    var json = await File.ReadAllTextAsync(file);
                    var item = JsonSerializer.Deserialize<AuditQueueItem>(json);
                    
                    if (item != null)
                    {
                        item.SyncStatus = AuditSyncItemStatus.Pending;
                        item.RetryCount = 0;
                        item.LastError = null;
                        
                        var queuePath = Path.Combine(_queuePath, Path.GetFileName(file));
                        var updatedJson = JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(queuePath, updatedJson);
                        
                        File.Delete(file);
                        PendingSyncCount++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OfflineAuditSync] Error retrying failed item: {ex.Message}");
                }
            }

            // Trigger immediate sync
            await ProcessQueueAsync();
        }

        /// <summary>
        /// Get sync history/statistics
        /// </summary>
        public async Task<AuditSyncHistory> GetSyncHistoryAsync()
        {
            var history = new AuditSyncHistory();

            // Count pending items
            var queuedFiles = Directory.GetFiles(_queuePath, "audit_*.json");
            foreach (var file in queuedFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var item = JsonSerializer.Deserialize<AuditQueueItem>(json);
                    
                    if (item != null)
                    {
                        if (item.SyncStatus == AuditSyncItemStatus.Pending)
                            history.PendingCount++;
                        else if (item.SyncStatus == AuditSyncItemStatus.Failed)
                            history.FailedCount++;
                    }
                }
                catch { }
            }

            // Count synced items
            var syncedDir = Path.Combine(_queuePath, "Synced");
            if (Directory.Exists(syncedDir))
            {
                var syncedFiles = Directory.GetFiles(syncedDir, "audit_*.json");
                history.SyncedCount = syncedFiles.Length;
                
                // Get most recent sync time
                if (syncedFiles.Length > 0)
                {
                    foreach (var file in syncedFiles.OrderByDescending(f => File.GetLastWriteTime(f)).Take(1))
                    {
                        try
                        {
                            var json = await File.ReadAllTextAsync(file);
                            var item = JsonSerializer.Deserialize<AuditQueueItem>(json);
                            if (item?.LastSyncedAt != null)
                                history.LastSyncTime = item.LastSyncedAt;
                        }
                        catch { }
                    }
                }
            }

            // Count permanently failed items
            var failedDir = Path.Combine(_queuePath, "Failed");
            if (Directory.Exists(failedDir))
            {
                history.PermanentlyFailedCount = Directory.GetFiles(failedDir, "audit_*.json").Length;
            }

            history.TotalProcessed = history.SyncedCount + history.PermanentlyFailedCount;
            
            return history;
        }

        /// <summary>
        /// Try to sync a single audit entry
        /// </summary>
        private async Task<bool> TrySyncSingleItemAsync(AuditQueueItem queueItem)
        {
            try
            {
                // Create payload with single entry
                var payload = new AuditSyncPayload
                {
                    SyncBatchId = queueItem.Id,
                    SyncTimestamp = DateTime.UtcNow,
                    SourceSystem = Environment.MachineName,
                    AppVersion = AppVersionProvider.GetVersion(),
                    DateRangeFrom = queueItem.Entry.Timestamp,
                    DateRangeTo = queueItem.Entry.Timestamp,
                    Metadata = new Dictionary<string, string>
                    {
                        { "queued_at", queueItem.QueuedAt.ToString("O") },
                        { "retry_count", queueItem.RetryCount.ToString() },
                        { "sync_type", "offline_queue" }
                    }
                };

                payload.AuditEntries.Add(new AuditSyncEntry
                {
                    EntryId = queueItem.Id,
                    Timestamp = queueItem.Entry.Timestamp,
                    Action = queueItem.Entry.Action,
                    Category = queueItem.Entry.Category.ToString(),
                    Username = queueItem.Entry.Username,
                    EntityType = queueItem.Entry.EntityType,
                    EntityId = queueItem.Entry.EntityId,
                    Details = queueItem.Entry.Details,
                    OldValue = queueItem.Entry.OldValue,
                    NewValue = queueItem.Entry.NewValue,
                    IpAddress = queueItem.Entry.IpAddress,
                    MachineName = queueItem.Entry.MachineName,
                    Severity = queueItem.Entry.Severity.ToString()
                });

                // Send to server
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var json = JsonSerializer.Serialize(payload, jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                httpClient.DefaultRequestHeaders.Add("X-Sync-Batch-Id", payload.SyncBatchId);
                httpClient.DefaultRequestHeaders.Add("X-Source-System", payload.SourceSystem);
                httpClient.DefaultRequestHeaders.Add("X-App-Version", payload.AppVersion);

                var response = await httpClient.PostAsync(_syncEndpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[OfflineAuditSync] Successfully synced entry: {queueItem.Entry.Action}");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[OfflineAuditSync] Sync failed: HTTP {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OfflineAuditSync] Sync error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test connectivity to sync endpoint
        /// </summary>
        private async Task<bool> TestConnectivityAsync()
        {
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await httpClient.GetAsync(_syncEndpoint + "/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Update sync status
        /// </summary>
        private async Task UpdateStatusAsync()
        {
            try
            {
                var queuedFiles = Directory.GetFiles(_queuePath, "audit_*.json");
                PendingSyncCount = queuedFiles.Length;
                IsOnline = await TestConnectivityAsync();
            }
            catch
            {
                // Ignore
            }
        }

        /// <summary>
        /// Force immediate sync attempt
        /// </summary>
        public async Task ForceSyncAsync()
        {
            await ProcessQueueAsync();
        }

        /// <summary>
        /// Get current queue status
        /// </summary>
        public async Task<QueueStatus> GetQueueStatusAsync()
        {
            await UpdateStatusAsync();

            return new QueueStatus
            {
                IsOnline = IsOnline,
                PendingCount = PendingSyncCount,
                LastSuccessfulSync = LastSuccessfulSync,
                LastSyncAttempt = LastSyncAttempt,
                QueuePath = _queuePath
            };
        }

        /// <summary>
        /// Clear synced items (to save disk space)
        /// </summary>
        public void ClearSyncedItems()
        {
            var syncedDir = Path.Combine(_queuePath, "Synced");
            if (Directory.Exists(syncedDir))
            {
                var files = Directory.GetFiles(syncedDir);
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }
        }

        /// <summary>
        /// Clear failed items (manual intervention)
        /// </summary>
        public void ClearFailedItems()
        {
            var failedDir = Path.Combine(_queuePath, "Failed");
            if (Directory.Exists(failedDir))
            {
                var files = Directory.GetFiles(failedDir);
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }
        }

        protected virtual void OnSyncStatusChanged(SyncStatusChangedEventArgs e)
        {
            SyncStatusChanged?.Invoke(this, e);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _retryTimer?.Stop();
                _retryTimer?.Dispose();
                _syncLock?.Dispose();
                _isDisposed = true;
            }
        }
    }

    /// <summary>
    /// Queued audit item for offline sync
    /// </summary>
    public class AuditQueueItem
    {
        public string Id { get; set; } = string.Empty;
        public DateTime QueuedAt { get; set; }
        public int RetryCount { get; set; }
        public DateTime? LastRetryAt { get; set; }
        public AuditSyncItemStatus SyncStatus { get; set; } = AuditSyncItemStatus.Pending;
        public DateTime? LastSyncedAt { get; set; }
        public string? LastError { get; set; }
        public AuditEntry Entry { get; set; } = new();
    }

    /// <summary>
    /// Sync status for individual queue items
    /// </summary>
    public enum AuditSyncItemStatus
    {
        Pending,
        Synced,
        Failed,
        PermanentlyFailed
    }

    /// <summary>
    /// Queue status information
    /// </summary>
    public class QueueStatus
    {
        public bool IsOnline { get; set; }
        public int PendingCount { get; set; }
        public DateTime? LastSuccessfulSync { get; set; }
        public DateTime? LastSyncAttempt { get; set; }
        public string QueuePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Audit sync history and statistics
    /// </summary>
    public class AuditSyncHistory
    {
        public int PendingCount { get; set; }
        public int SyncedCount { get; set; }
        public int FailedCount { get; set; }
        public int PermanentlyFailedCount { get; set; }
        public int TotalProcessed { get; set; }
        public DateTime? LastSyncTime { get; set; }
    }

    /// <summary>
    /// Sync status enum
    /// </summary>
    public enum AuditSyncStatus
    {
        Idle,
        Queued,
        Syncing,
        Completed,
        PartialSuccess,
        Failed,
        Offline
    }

    /// <summary>
    /// Sync status changed event args
    /// </summary>
    public class SyncStatusChangedEventArgs : EventArgs
    {
        public AuditSyncStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public int PendingCount { get; set; }
    }
}

