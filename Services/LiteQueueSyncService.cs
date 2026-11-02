using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using LiteQueue;
using LiteDB;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Configuration;
using SystemJsonSerializer = System.Text.Json.JsonSerializer;
using SystemJsonOptions = System.Text.Json.JsonSerializerOptions;
using System.Collections.Generic;
using System.Linq;
using ProWeighSuitePlatforms.Contracts;

namespace ProWeighSuitePlatforms.Services
{
    public class LiteQueueSyncService : ITransactionRetryQueue
    {
        private readonly HttpClient _httpClient;
        private readonly LiteQueue<TransactionQueueItem> _queue;
        private readonly LiteDatabase _liteDb;
        private readonly LiteCollection<SyncStatusEntry> _statusCollection;
        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;
        private const int MaxRetries = 3;
        private const int RetryDelaySeconds = 5;
        private const int RequestTimeoutSeconds = 10;
        private int _successCount = 0;
        private int _failureCount = 0;
        private int _pendingCount = 0;
        private readonly string _sessionId;

        // Network-awareness
        private volatile bool _isOnline = true;
        private NetworkMonitorService? _networkMonitor;
        private readonly ManualResetEventSlim _onlineSignal = new(true);

        // Tracks which URLs failed per transaction so dead-letter entries are accurate
        private readonly Dictionary<string, List<string>> _lastFailedUrls = new();

        /// <summary>Current internet connectivity state (updated by NetworkMonitorService).</summary>
        public bool IsOnline => _isOnline;

        public LiteQueueSyncService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            
            // Generate unique session ID for this app run
            _sessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

            // Use same directory as database for queue
            var dbPath = AppConfigurationManager.GetDatabasePath();
            var queueDirectory = Path.GetDirectoryName(dbPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var queueDbPath = Path.Combine(queueDirectory, "transaction_queue.db");
            
            // Initialize LiteDB database
            _liteDb = new LiteDatabase(queueDbPath);
            
            // Initialize LiteQueue with collection from LiteDB
            var collection = _liteDb.GetCollection<QueueEntry<TransactionQueueItem>>("transaction_sync_queue");
            _queue = new LiteQueue<TransactionQueueItem>(collection);
            
            // Create separate collection to track sync status
            _statusCollection = _liteDb.GetCollection<SyncStatusEntry>("sync_status");
            _statusCollection.EnsureIndex(x => x.TransactionId, false);
            _statusCollection.EnsureIndex(x => x.SessionId, false);
            _statusCollection.EnsureIndex(x => x.Status, false);
            
            Debug.WriteLine($"[LiteQueue] Initialized queue at: {queueDbPath}");
            Debug.WriteLine($"[LiteQueue] Session ID: {_sessionId}");
            
            // CRITICAL FIX: Clean up ALL old data before calculating counts
            CleanupOldEntries();
            
            // Calculate initial counts AFTER cleanup
            RecalculateCounts();
            
            Debug.WriteLine($"[LiteQueue] Pending: {_pendingCount}, Success: {_successCount}, Failed: {_failureCount}");
            Debug.WriteLine($"[LiteQueue] HttpClient BaseAddress: {_httpClient.BaseAddress}");
        }

        private void CleanupOldEntries()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                Debug.WriteLine($"[LiteQueue] Starting cleanup...");

                var queueCollection = _liteDb.GetCollection<QueueEntry<TransactionQueueItem>>("transaction_sync_queue");

                // Load all statuses and queue items ONCE and index by TransactionId to avoid O(n^2) FindOne loops.
                // With 3000+ docs, per-item FindOne was causing 2+ minute startup blocks.
                var allStatuses = _statusCollection.FindAll().ToList();
                var statusByTxId = allStatuses
                    .Where(s => !string.IsNullOrEmpty(s.TransactionId))
                    .GroupBy(s => s.TransactionId)
                    .ToDictionary(g => g.Key, g => g.First());

                var allQueueItems = queueCollection.FindAll().ToList();
                var queueTxIds = new HashSet<string>(
                    allQueueItems.Where(q => q.Payload?.Transaction != null)
                                 .Select(q => q.Payload!.Transaction!.Id));

                // STEP 1: Remove ALL entries without a SessionId (legacy/corrupted data)
                var entriesWithoutSession = allStatuses.Where(s => string.IsNullOrEmpty(s.SessionId)).ToList();
                if (entriesWithoutSession.Any())
                {
                    foreach (var entry in entriesWithoutSession)
                        _statusCollection.Delete(entry.Id);
                    Debug.WriteLine($"[LiteQueue] Removed {entriesWithoutSession.Count} legacy entries without SessionId");
                }

                // STEP 2: Delete old Successful entries (already synced, no longer needed)
                var oldSuccessEntries = allStatuses.Where(s => !string.IsNullOrEmpty(s.SessionId) &&
                               s.SessionId != _sessionId &&
                               s.Status == SyncStatus.Success).ToList();
                if (oldSuccessEntries.Any())
                {
                    foreach (var entry in oldSuccessEntries)
                        _statusCollection.Delete(entry.Id);
                    Debug.WriteLine($"[LiteQueue] Removed {oldSuccessEntries.Count} old successful status entries");
                }

                // STEP 2b: Re-adopt or delete old Pending entries using HashSet lookup (O(1) vs O(n) FindOne)
                var oldPendingEntries = allStatuses.Where(s => !string.IsNullOrEmpty(s.SessionId) &&
                               s.SessionId != _sessionId &&
                               s.Status == SyncStatus.Pending).ToList();
                if (oldPendingEntries.Any())
                {
                    int reAdoptedCount = 0;
                    int orphanedCount = 0;
                    foreach (var entry in oldPendingEntries)
                    {
                        if (queueTxIds.Contains(entry.TransactionId))
                        {
                            entry.SessionId = _sessionId;
                            _statusCollection.Upsert(entry);
                            reAdoptedCount++;
                        }
                        else
                        {
                            _statusCollection.Delete(entry.Id);
                            orphanedCount++;
                        }
                    }
                    Debug.WriteLine($"[LiteQueue] Processed {oldPendingEntries.Count} old pending entries: {reAdoptedCount} re-adopted, {orphanedCount} orphaned (deleted) in {sw.ElapsedMilliseconds}ms");
                }

                // STEP 3: Remove corrupted queue items (null payload/transaction only).
                var corruptedItems = allQueueItems.Where(q => q.Payload?.Transaction == null).ToList();
                if (corruptedItems.Any())
                {
                    foreach (var item in corruptedItems)
                        queueCollection.Delete(item.Id);
                    Debug.WriteLine($"[LiteQueue] Removed {corruptedItems.Count} corrupted queue items (null payload)");
                }

                // STEP 4: Remove old Successful entries older than 7 days
                var cutoffTime = DateTime.UtcNow.AddDays(-7);
                var oldCompletedEntries = allStatuses.Where(s => s.Status == SyncStatus.Success &&
                               s.CompletedAt.HasValue &&
                               s.CompletedAt.Value < cutoffTime).ToList();
                if (oldCompletedEntries.Any())
                {
                    foreach (var entry in oldCompletedEntries)
                    {
                        // Re-check existence in DB (may have been deleted in STEP 2)
                        if (_statusCollection.FindById(entry.Id) != null)
                            _statusCollection.Delete(entry.Id);
                    }
                    Debug.WriteLine($"[LiteQueue] Removed {oldCompletedEntries.Count} successful entries older than 7 days");
                }

                // STEP 5: Remove queue items that have no corresponding Pending/Failed status (orphaned success)
                // Use dictionary lookup instead of per-item FindOne
                int orphanedQueueCount = 0;
                foreach (var queueItem in allQueueItems)
                {
                    if (queueItem.Payload?.Transaction == null)
                    {
                        queueCollection.Delete(queueItem.Id);
                        orphanedQueueCount++;
                        continue;
                    }
                    var txId = queueItem.Payload.Transaction.Id;
                    if (!statusByTxId.TryGetValue(txId, out var status) || status.Status == SyncStatus.Success)
                    {
                        // Double-check DB in case status was deleted above
                        var liveStatus = statusByTxId.ContainsKey(txId) ? statusByTxId[txId] : null;
                        // If it was deleted, treat as no status
                        if (liveStatus == null || liveStatus.Status == SyncStatus.Success)
                        {
                            queueCollection.Delete(queueItem.Id);
                            orphanedQueueCount++;
                        }
                    }
                }
                if (orphanedQueueCount > 0)
                    Debug.WriteLine($"[LiteQueue] Removed {orphanedQueueCount} orphaned queue items during startup cleanup");

                var pendingCount  = _statusCollection.Count(s => s.Status == SyncStatus.Pending);
                var failedCount   = _statusCollection.Count(s => s.Status == SyncStatus.Failed);
                var queueItemCount = queueCollection.Count();
                Debug.WriteLine($"[LiteQueue] Cleanup done in {sw.ElapsedMilliseconds}ms. Pending={pendingCount}, Failed={failedCount}, QueueItems={queueItemCount}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] Error during cleanup: {ex.Message} {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Removes orphaned items from the LiteQueue that shouldn't be there
        /// CRITICAL: Only items with Pending or Failed status should be in the queue
        /// </summary>
        private void CleanupOrphanedQueueItems()
        {
            try
            {
                var queueCollection = _liteDb.GetCollection<QueueEntry<TransactionQueueItem>>("transaction_sync_queue");
                var allQueueItems = queueCollection.FindAll().ToList();
                if (allQueueItems.Count == 0) return;

                var allStatuses = _statusCollection.FindAll().ToList();
                var statusByTxId = allStatuses
                    .Where(s => !string.IsNullOrEmpty(s.TransactionId))
                    .GroupBy(s => s.TransactionId)
                    .ToDictionary(g => g.Key, g => g.First());

                var itemsToRemove = new List<long>();
                int corruptedCount = 0;
                int successCount = 0;
                int noStatusCount = 0;

                foreach (var queueItem in allQueueItems)
                {
                    if (queueItem.Payload?.Transaction == null)
                    {
                        itemsToRemove.Add(queueItem.Id);
                        corruptedCount++;
                        continue;
                    }

                    var txId = queueItem.Payload.Transaction.Id;
                    if (!statusByTxId.TryGetValue(txId, out var statusEntry))
                    {
                        itemsToRemove.Add(queueItem.Id);
                        noStatusCount++;
                    }
                    else if (statusEntry.Status == SyncStatus.Success)
                    {
                        itemsToRemove.Add(queueItem.Id);
                        successCount++;
                    }
                }

                foreach (var id in itemsToRemove) queueCollection.Delete(id);

                if (itemsToRemove.Count > 0)
                {
                    Debug.WriteLine($"[LiteQueue] Removed {itemsToRemove.Count} orphaned queue items:");
                    Debug.WriteLine($"[LiteQueue]   - Corrupted (null payload): {corruptedCount}");
                    Debug.WriteLine($"[LiteQueue]   - Already synced (Success): {successCount}");
                    Debug.WriteLine($"[LiteQueue]   - No status entry: {noStatusCount}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] Error during orphaned items cleanup: {ex.Message}");
            }
        }

        private void RecalculateCounts()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                CleanupOrphanedQueueItems();

                var queueCollection = _liteDb.GetCollection<QueueEntry<TransactionQueueItem>>("transaction_sync_queue");
                var allQueueItems = queueCollection.FindAll().ToList();

                // Build dictionary once to avoid per-item FindOne (was O(n^2) -> 2 min delay)
                var allStatuses = _statusCollection.FindAll().ToList();
                var statusByTxId = allStatuses
                    .Where(s => !string.IsNullOrEmpty(s.TransactionId))
                    .GroupBy(s => s.TransactionId)
                    .ToDictionary(g => g.Key, g => g.First());

                int pendingQueue = 0;
                foreach (var qi in allQueueItems)
                {
                    if (qi.Payload?.Transaction == null) continue;
                    if (statusByTxId.TryGetValue(qi.Payload.Transaction.Id, out var s) && s.Status == SyncStatus.Pending)
                        pendingQueue++;
                }

                _pendingCount  = pendingQueue;
                _successCount  = allStatuses.Count(s => s.SessionId == _sessionId && s.Status == SyncStatus.Success);
                _failureCount  = allStatuses.Count(s => s.SessionId == _sessionId && s.Status == SyncStatus.Failed);

                Debug.WriteLine($"[LiteQueue] Counts recalculated in {sw.ElapsedMilliseconds}ms - Pending: {_pendingCount} (from {allQueueItems.Count} queue items), Success: {_successCount}, Failed: {_failureCount}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] Error recalculating counts: {ex.Message}");
                _pendingCount = 0; _successCount = 0; _failureCount = 0;
            }
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_backgroundTask != null)
            {
                Debug.WriteLine("[LiteQueue] Service already started");
                return Task.CompletedTask;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _cts.Token;

            _backgroundTask = Task.Run(() => ProcessQueueAsync(token), CancellationToken.None);
            Debug.WriteLine("[LiteQueue] ? Background sync worker started");
            Debug.WriteLine($"[LiteQueue] ? API Base URL: {_httpClient.BaseAddress}");
            Debug.WriteLine($"[LiteQueue] ? Initial - Pending: {_pendingCount}, Success: {_successCount}, Failures: {_failureCount}");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Registers the <see cref="NetworkMonitorService"/> whose connectivity events
        /// will pause / resume queue processing automatically.
        /// Call this before <see cref="StartAsync"/>.
        /// </summary>
        public void SetNetworkMonitor(NetworkMonitorService monitor)
        {
            if (_networkMonitor != null)
                _networkMonitor.ConnectivityChanged -= OnConnectivityChanged;

            _networkMonitor = monitor;
            _isOnline = monitor.IsOnline;
            _networkMonitor.ConnectivityChanged += OnConnectivityChanged;

            Debug.WriteLine($"[LiteQueue] NetworkMonitor attached. IsOnline={_isOnline}");
        }

        private void OnConnectivityChanged(bool isOnline)
        {
            _isOnline = isOnline;

            if (isOnline)
            {
                Debug.WriteLine("[LiteQueue] Internet RESTORED - queue will resume processing.");
                _onlineSignal.Set();
            }
            else
            {
                _onlineSignal.Reset();
                Debug.WriteLine("[LiteQueue] Internet LOST - queue paused. Items remain Pending.");
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_cts == null)
            {
                Debug.WriteLine("[LiteQueue] Service not running");
                return;
            }

            try
            {
                Debug.WriteLine("[LiteQueue] Stopping background worker...");
                Debug.WriteLine($"[LiteQueue] ? Final Stats - Pending: {_pendingCount}, Success: {_successCount}, Failures: {_failureCount}");
                _cts.Cancel();
                
                if (_backgroundTask != null)
                    await _backgroundTask.ConfigureAwait(false);
                
                Debug.WriteLine("[LiteQueue] Background worker stopped");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[LiteQueue] Background worker cancelled successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] Stop error: {ex.Message}");
            }
            finally
            {
                // Detach network monitor event
                if (_networkMonitor != null)
                {
                    _networkMonitor.ConnectivityChanged -= OnConnectivityChanged;
                    _networkMonitor = null;
                }

                _cts?.Dispose();
                _cts = null;
                _backgroundTask = null;
                
                // Wake the offline guard so it exits cleanly on app shutdown
                _onlineSignal.Set();
                _onlineSignal.Dispose();

                // Dispose LiteDB
                _liteDb?.Dispose();
            }
        }

        /// <summary>
        /// Clears all queue data and status entries. Use for database cleanup operations.
        /// Service must be stopped before calling this method.
        /// </summary>
        public void ClearAllQueueData()
        {
            try
            {
                Debug.WriteLine("[LiteQueue] Clearing all queue data...");

                if (_liteDb != null)
                {
                    // Get all collection names
                    var collectionNames = _liteDb.GetCollectionNames().ToList();
                    Debug.WriteLine($"[LiteQueue] Found {collectionNames.Count} collections to clear");

                    // Drop each collection
                    foreach (var collectionName in collectionNames)
                    {
                        _liteDb.DropCollection(collectionName);
                        Debug.WriteLine($"[LiteQueue] Dropped collection: {collectionName}");
                    }

                    // Reset counters
                    _pendingCount = 0;
                    _successCount = 0;
                    _failureCount = 0;

                    Debug.WriteLine("[LiteQueue] All queue data cleared successfully");
                }
                else
                {
                    Debug.WriteLine("[LiteQueue] Database already disposed");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] Error clearing queue data: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Enqueue a transaction for background synchronization
        /// </summary>
        public void EnqueueTransaction(WeighingTransaction transaction)
        {
            try
            {
                var item = new TransactionQueueItem
                {
                    Transaction = transaction,
                    AttemptCount = 0,
                    EnqueuedAt = DateTime.UtcNow,
                    IdempotencyKey = transaction.Id.ToString()
                };

                _queue.Enqueue(item);
                
                // Track as pending in status collection with current session
                _statusCollection.Upsert(new SyncStatusEntry
                {
                    TransactionId = transaction.Id,
                    SessionId = _sessionId,
                    Status = SyncStatus.Pending,
                    EnqueuedAt = DateTime.UtcNow,
                    AttemptCount = 0
                });
                
                _pendingCount++;
                
                Debug.WriteLine($"[LiteQueue] ? Enqueued transaction {transaction.Id} (Pending: {_pendingCount})");
                Debug.WriteLine($"[LiteQueue] ? Transaction details: BatchNo={transaction.BatchNumber}, Material={transaction.MaterialCode}, Weight={transaction.ActualWeight}kg");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] ? Enqueue error: {ex.Message}");
                throw;
            }
        }

        private async Task ProcessQueueAsync(CancellationToken stoppingToken)
        {
            Debug.WriteLine("[LiteQueue] ? Started processing queue (checking every 10 seconds)...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // ---- SYNC MASTER SWITCH ----
                    // When sync is disabled in App Settings, pause uploads but
                    // keep items Pending in LiteDB; re-check every 2s so enabling
                    // sync resumes without a restart.
                    if (!SyncGate.Enabled)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                        continue;
                    }

                    // ---- OFFLINE GUARD ----
                    // When internet is down, block here until _onlineSignal is Set()
                    // by OnConnectivityChanged. Items stay Pending in LiteDB.
                    if (!_isOnline)
                    {
                        Debug.WriteLine("[LiteQueue] Offline - queue paused. Waiting for internet...");
                        _onlineSignal.Wait(stoppingToken); // released instantly when internet returns
                        Debug.WriteLine("[LiteQueue] Online signal received - resuming queue.");
                        continue;
                    }

                    if (_pendingCount > 0)
                    {
                        Debug.WriteLine($"[LiteQueue] ? Queue status: {_pendingCount} items pending");
                    }

                    // Try to dequeue
                    var queueEntry = _queue.Dequeue();
                    
                    if (queueEntry != null)
                    {
                        Debug.WriteLine($"[LiteQueue] ? Successfully dequeued entry ID: {queueEntry.Id}");
                        
                        // Check if Payload is null
                        if (queueEntry.Payload == null)
                        {
                            Debug.WriteLine($"[LiteQueue] ? Warning: Dequeued entry has NULL payload! Removing corrupted entry.");
                            _failureCount++;
                            _pendingCount = Math.Max(0, _pendingCount - 1);
                            continue;
                        }
                        
                        var item = queueEntry.Payload;
                        
                        // Check if Transaction is null
                        if (item.Transaction == null)
                        {
                            Debug.WriteLine($"[LiteQueue] ? Warning: Queue item has NULL transaction! Removing corrupted entry.");
                            _failureCount++;
                            _pendingCount = Math.Max(0, _pendingCount - 1);
                            continue;
                        }
                        
                        Debug.WriteLine($"[LiteQueue] ? Processing transaction {item.Transaction.Id} (Attempt {item.AttemptCount + 1}/{MaxRetries})");
                        Debug.WriteLine($"[LiteQueue] ? Details: Batch={item.Transaction.BatchNumber}, Material={item.Transaction.MaterialCode}, Weight={item.Transaction.ActualWeight}kg");

                        // Increment attempt count BEFORE sending
                        item.AttemptCount++;
                        
                        var (success, responseBody) = await SendToApiAsync(item.Transaction, stoppingToken);

                        // Persist the raw server response for audit trails and debugging failed syncs
                        try
                        {
                            var responseRepo = new Data.WeighingRepository(AppConfigurationManager.GetDatabasePath());
                            await responseRepo.UpdateTransactionServerResponseAsync(item.Transaction.Id, responseBody);
                        }
                        catch (Exception dbEx)
                        {
                            Debug.WriteLine($"[LiteQueue] Warning: server_response update failed for {item.Transaction.Id}: {dbEx.Message}");
                        }

                        if (success)
                        {
                            _successCount++;
                            _pendingCount = Math.Max(0, _pendingCount - 1);

                            _statusCollection.Upsert(new SyncStatusEntry
                            {
                                TransactionId = item.Transaction.Id,
                                SessionId = _sessionId,
                                Status = SyncStatus.Success,
                                EnqueuedAt = item.EnqueuedAt,
                                CompletedAt = DateTime.UtcNow,
                                AttemptCount = item.AttemptCount
                            });

                            // CRITICAL FIX: Remove the successfully synced item from queue collection
                            // This prevents orphaned items from being counted in pending
                            try
                            {
                                var queueCollection = _liteDb.GetCollection<QueueEntry<TransactionQueueItem>>("transaction_sync_queue");
                                // Find and delete the queue entry for this transaction
                                var queueItem = queueCollection.FindOne(q => q.Payload != null && q.Payload.Transaction != null && q.Payload.Transaction.Id == item.Transaction.Id);
                                if (queueItem != null)
                                {
                                    queueCollection.Delete(queueItem.Id);
                                    Debug.WriteLine($"[LiteQueue] 🗑️ Removed synced transaction {item.Transaction.Id} from queue collection");
                                }
                            }
                            catch (Exception cleanupEx)
                            {
                                Debug.WriteLine($"[LiteQueue] Warning: Queue cleanup failed for {item.Transaction.Id}: {cleanupEx.Message}");
                            }

                            // Mark transaction as synced in SQLite
                            try
                            {
                                var repo = new Data.WeighingRepository(AppConfigurationManager.GetDatabasePath());
                                await repo.UpdateTransactionSyncStatusAsync(item.Transaction.Id, true);
                            }
                            catch (Exception dbEx)
                            {
                                Debug.WriteLine($"[LiteQueue] Warning: is_synced update failed for {item.Transaction.Id}: {dbEx.Message}");
                            }

                            Debug.WriteLine($"[LiteQueue] ✅ Successfully synced transaction {item.Transaction.Id} to PRIMARY API");
                            Debug.WriteLine($"[LiteQueue] 📊 Queue Monitor Stats - Pending: {_pendingCount}, Success: {_successCount}, Failures: {_failureCount}");
                            continue;
                        }
                        else
                        {
                            // Update status with attempt count
                            _statusCollection.Upsert(new SyncStatusEntry
                            {
                                TransactionId = item.Transaction.Id,
                                SessionId = _sessionId,
                                Status = SyncStatus.Pending,
                                EnqueuedAt = item.EnqueuedAt,
                                LastAttemptAt = DateTime.UtcNow,
                                AttemptCount = item.AttemptCount,
                                ErrorMessage = _lastFailedUrls.TryGetValue(item.Transaction.Id, out var fe)
                                    ? $"Attempt {item.AttemptCount}: Primary URL failed - {string.Join(", ", fe)}"
                                    : $"Attempt {item.AttemptCount}: Primary URL failed"
                            });

                            // Failed - check if max retries reached
                            if (item.AttemptCount >= MaxRetries)
                            {
                                _failureCount++;
                                _pendingCount = Math.Max(0, _pendingCount - 1);

                                _lastFailedUrls.TryGetValue(item.Transaction.Id, out var failedUrls);
                                _statusCollection.Upsert(new SyncStatusEntry
                                {
                                    TransactionId = item.Transaction.Id,
                                    SessionId = _sessionId,
                                    Status = SyncStatus.Failed,
                                    EnqueuedAt = item.EnqueuedAt,
                                    CompletedAt = DateTime.UtcNow,
                                    AttemptCount = item.AttemptCount,
                                    ErrorMessage = failedUrls?.Count > 0
                                        ? $"Failed after {MaxRetries} attempts. Unreachable: {string.Join(", ", failedUrls)}"
                                        : $"Failed after {MaxRetries} attempts"
                                });

                                Debug.WriteLine($"[LiteQueue] ❌ Transaction {item.Transaction.Id} failed to sync to PRIMARY API after {MaxRetries} attempts");
                                Debug.WriteLine($"[LiteQueue] Saving to dead letter queue...");
                                SaveToDeadLetter(item);
                                Debug.WriteLine($"[LiteQueue] 📊 Queue Monitor Stats - Pending: {_pendingCount}, Success: {_successCount}, Failures: {_failureCount}");
                                continue;
                            }
                            else
                            {
                                _queue.Enqueue(item);
                                Debug.WriteLine($"[LiteQueue] Re-queued transaction {item.Transaction.Id} (will retry: attempt {item.AttemptCount + 1}/{MaxRetries})");
                                Debug.WriteLine($"[LiteQueue] Waiting {RetryDelaySeconds} seconds before next retry...");
                                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), stoppingToken);
                            }
                        }
                    }
                    else
                    {
                        // No items — wait up to 10s but wake immediately if internet changes
                        _onlineSignal.Wait(10000, stoppingToken);
                        _onlineSignal.Reset();
                        if (_isOnline) _onlineSignal.Set(); // restore if still online
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("[LiteQueue] Processing cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LiteQueue] ? Processing error: {ex.Message}");
                    Debug.WriteLine($"[LiteQueue] Stack trace: {ex.StackTrace}");
                    _failureCount++;
                    
                    // Wait before retrying to avoid tight error loop
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }

            Debug.WriteLine($"[LiteQueue] Stopped processing queue");
            Debug.WriteLine($"[LiteQueue] ? Final Stats - Pending: {_pendingCount}, Success: {_successCount}, Failures: {_failureCount}");
        }

        private async Task<(bool Success, string ResponseBody)> SendToApiAsync(WeighingTransaction transaction, CancellationToken cancellationToken)
        {
            try
            {
                SyncSettings? syncSettings = null;
                try
                {
                    var syncSettingsService = new SyncSettingsService();
                    syncSettings = await syncSettingsService.LoadAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LiteQueue] ERROR: Failed to load sync settings: {ex.Message}");
                }

                var primaryUrl = syncSettings?.ApiBaseUrl?.TrimEnd('/') ?? string.Empty;
                var secondaryUrl = syncSettings?.ApiBaseUrl2?.TrimEnd('/') ?? string.Empty;

                // Primary URL is required — abort if not configured
                if (string.IsNullOrWhiteSpace(primaryUrl))
                {
                    Debug.WriteLine("[LiteQueue] ERROR: Primary API URL is not configured. Sync skipped. Go to Settings > Sync Settings.");
                    _lastFailedUrls[transaction.Id] = new List<string> { "(Primary URL not configured)" };
                    return (false, string.Empty);
                }

                var payload = TransactionSyncPayload.FromTransaction(transaction);
                var json = syncSettings?.FieldMappings != null && syncSettings.FieldMappings.Count > 0
                    ? payload.ToFilteredJson(syncSettings.FieldMappings)
                    : SystemJsonSerializer.Serialize(payload, new SystemJsonOptions
                    {
                        PropertyNamingPolicy = null,
                        WriteIndented = false
                    });

                Debug.WriteLine($"[LiteQueue] Sending transaction {transaction.Id} to primary: {primaryUrl}");

                // Always POST to primary first — success of primary determines IsSynced
                var (primarySuccess, primaryResponseBody) = await PostToUrlAsync(primaryUrl, json, transaction, cancellationToken);

                var failedUrls = new List<string>();
                if (!primarySuccess)
                    failedUrls.Add(primaryUrl);

                // ⚠️ IMPORTANT: Secondary is best-effort for internal testing ONLY
                // Secondary URL does NOT affect:
                //   - Queue Monitor counters (Success/Failure/Pending)
                //   - IsSynced flag in database
                //   - Dead letter queue decisions
                // Only PRIMARY URL determines sync success/failure
                if (!string.IsNullOrWhiteSpace(secondaryUrl))
                {
                    Debug.WriteLine($"[LiteQueue] [INTERNAL TEST] Sending transaction {transaction.Id} to secondary: {secondaryUrl}");
                    var (secondarySuccess, _) = await PostToUrlAsync(secondaryUrl, json, transaction, cancellationToken);
                    if (!secondarySuccess)
                    {
                        failedUrls.Add(secondaryUrl);
                        Debug.WriteLine($"[LiteQueue] [INTERNAL TEST] Secondary endpoint FAILED for transaction {transaction.Id}");
                        Debug.WriteLine($"[LiteQueue] [INTERNAL TEST] ⚠️ This failure does NOT affect Queue Monitor counters or sync status");
                    }
                    else
                    {
                        Debug.WriteLine($"[LiteQueue] [INTERNAL TEST] Secondary endpoint SUCCEEDED for transaction {transaction.Id}");
                    }
                }

                _lastFailedUrls[transaction.Id] = failedUrls;

                if (primarySuccess)
                {
                    Debug.WriteLine($"[LiteQueue] ✅ Transaction {transaction.Id} SYNCED (Primary succeeded)");
                    if (!string.IsNullOrWhiteSpace(secondaryUrl))
                    {
                        Debug.WriteLine($"[LiteQueue] [INTERNAL TEST] Secondary result for {transaction.Id}: {(failedUrls.Contains(secondaryUrl) ? "FAILED" : "SUCCESS")}");
                    }
                }
                else
                {
                    Debug.WriteLine($"[LiteQueue] ❌ Transaction {transaction.Id} SYNC FAILED (Primary failed)");
                    Debug.WriteLine($"[LiteQueue] Failed Primary URL: {primaryUrl}");
                }

                // ⚠️ IMPORTANT: Success is determined by PRIMARY endpoint ONLY
                // Secondary URL does not affect Qumonitor status or is_synced flag
                return (primarySuccess, primaryResponseBody);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] Unexpected error in SendToApiAsync: {ex.Message}");
                return (false, string.Empty);
            }
        }

        private async Task<(bool Success, string ResponseBody)> PostToUrlAsync(string url, string json, WeighingTransaction transaction, CancellationToken cancellationToken)
        {
            try
            {
                Debug.WriteLine($"[LiteQueue] POST to: {url}");

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                request.Headers.Add("Idempotency-Key", transaction.Id.ToString());

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(RequestTimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var startTime = DateTime.UtcNow;
                var response = await _httpClient.SendAsync(request, linkedCts.Token);
                var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;

                Debug.WriteLine($"[LiteQueue] [{url}] completed in {elapsed:F2}s");

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token);
                    Debug.WriteLine($"[LiteQueue] [{url}] HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                    Debug.WriteLine($"[LiteQueue] Response: {(responseBody.Length > 200 ? responseBody.Substring(0, 200) + "..." : responseBody)}");
                    return (true, responseBody);
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync(linkedCts.Token);
                    Debug.WriteLine($"[LiteQueue] [{url}] HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                    Debug.WriteLine($"[LiteQueue] Error body: {errorBody}");
                    return (false, errorBody);
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[LiteQueue] [{url}] Network error: {ex.Message}");
                if (ex.InnerException != null)
                    Debug.WriteLine($"[LiteQueue] Inner: {ex.InnerException.Message}");
                return (false, string.Empty);
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine($"[LiteQueue] [{url}] Timeout (>{RequestTimeoutSeconds}s)");
                return (false, string.Empty);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[LiteQueue] [{url}] Cancelled.");
                return (false, string.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] [{url}] Unexpected error: {ex.Message}");
                return (false, string.Empty);
            }
        }

        private void SaveToDeadLetter(TransactionQueueItem item)
        {
            try
            {
                var dbPath = AppConfigurationManager.GetDatabasePath();
                var deadLetterPath = Path.Combine(Path.GetDirectoryName(dbPath) ?? "", "dead_letter_queue.json");

                _lastFailedUrls.TryGetValue(item.Transaction.Id, out var failedUrls);
                _lastFailedUrls.Remove(item.Transaction.Id);

                var failedUrlList = failedUrls ?? new List<string>();

                List<string> allUrls = new();
                List<string> succeededUrls = new();
                // Use HttpClient BaseAddress as fallback without blocking async I/O
                var fallback = _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(fallback) && !failedUrlList.Contains(fallback))
                {
                    allUrls.Add(fallback);
                    succeededUrls.Add(fallback);
                }
                // For succeeded/failed tracking, we record fallback only; detailed primary/secondary URLs are logged via SendToApiAsync

                var errorMessage = failedUrlList.Count > 0
                    ? $"Failed after {MaxRetries} attempts. Unreachable endpoint(s): {string.Join(", ", failedUrlList)}"
                    : $"Failed after {MaxRetries} attempts.";

                var deadLetterEntry = new
                {
                    Timestamp        = DateTime.UtcNow,
                    TransactionId    = item.Transaction.Id,
                    AttemptCount     = item.AttemptCount,
                    EnqueuedAt       = item.EnqueuedAt,
                    LastErrorMessage = errorMessage,
                    AllUrls          = allUrls,
                    FailedUrls       = failedUrlList,
                    SucceededUrls    = succeededUrls,
                    Transaction      = item.Transaction,
                    Note             = "Check the failed API endpoint(s) in Settings > Sync Settings."
                };

                var json = SystemJsonSerializer.Serialize(deadLetterEntry, new SystemJsonOptions { WriteIndented = true });
                // Async-safe append without blocking thread-pool
                File.AppendAllText(deadLetterPath, json + Environment.NewLine + new string('-', 80) + Environment.NewLine);

                Debug.WriteLine($"[LiteQueue] Dead letter saved: {deadLetterPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] Dead letter save error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the number of pending items in the queue
        /// </summary>
        public int GetPendingCount()
        {
            try
            {
                return _pendingCount;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] Error getting queue count: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Get sync statistics
        /// </summary>
        public (int Success, int Failures, int Pending) GetStats()
        {
            return (_successCount, _failureCount, _pendingCount);
        }

        /// <summary>
        /// Get detailed queue information for debugging
        /// </summary>
        public string GetQueueDebugInfo()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"=== QUEUE DEBUG INFO ===");
                sb.AppendLine($"Session ID: {_sessionId}");
                sb.AppendLine($"Current Stats - Pending: {_pendingCount}, Success: {_successCount}, Failed: {_failureCount}");
                sb.AppendLine();
                
                // Show API configuration
                try
                {
                    var syncSettingsService = new SyncSettingsService();
                    var syncSettings = syncSettingsService.LoadAsync().GetAwaiter().GetResult();
                    sb.AppendLine($"=== API CONFIGURATION ===");
                    sb.AppendLine($"Configured URL (from settings): {syncSettings?.ApiBaseUrl ?? "Not configured"}");
                    sb.AppendLine($"HttpClient BaseAddress (from startup): {_httpClient.BaseAddress?.ToString() ?? "Not set"}");
                    sb.AppendLine($"Settings File: {syncSettingsService.GetSettingsFilePath()}");
                    sb.AppendLine();
                    
                    if (syncSettings?.ApiBaseUrl != _httpClient.BaseAddress?.ToString()?.TrimEnd('/'))
                    {
                        sb.AppendLine($"?? WARNING: URLs don't match!");
                        sb.AppendLine($"The HttpClient was initialized with a different URL during app startup.");
                        sb.AppendLine($"Transactions will now use the URL from settings (auto-reload enabled).");
                        sb.AppendLine();
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"Error loading API config: {ex.Message}");
                    sb.AppendLine();
                }
                
                // Check actual queue count - LIMIT to avoid loading too much data
                var queueCollection = _liteDb.GetCollection<QueueEntry<TransactionQueueItem>>("transaction_sync_queue");
                var totalQueueCount = queueCollection.Count();
                sb.AppendLine($"Items in actual LiteQueue collection: {totalQueueCount}");
                sb.AppendLine($"Pending count (tracked): {_pendingCount}");
                
                if (totalQueueCount > 0)
                {
                    sb.AppendLine($"\n=== QUEUE ITEMS (Showing first 10 of {totalQueueCount}) ===");
                    
                    // CRITICAL FIX: Use Take() on enumerable to limit items loaded
                    var limitedQueueItems = queueCollection.FindAll().Take(10).ToList();
                    
                    int count = 0;
                    foreach (var item in limitedQueueItems)
                    {
                        count++;
                        var transId = item.Payload?.Transaction?.Id ?? string.Empty;
                        var material = item.Payload?.Transaction?.MaterialCode ?? "N/A";
                        var batchNo = item.Payload?.Transaction?.BatchNumber ?? "N/A";
                        var enqueued = item.Payload?.EnqueuedAt.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A";
                        var attempts = item.Payload?.AttemptCount ?? 0;
                        sb.AppendLine($"  {count}. QueueID={item.Id}, TransID={transId}, Batch={batchNo}, Material={material}");
                        sb.AppendLine($"      Enqueued={enqueued}, Attempts={attempts}");
                    }
                    
                    if (totalQueueCount > 10)
                    {
                        sb.AppendLine($"  ... and {totalQueueCount - 10} more items");
                    }
                }
                else
                {
                    sb.AppendLine($"? Queue is empty - no pending items");
                }
                
                // Check status collection - LIMIT to avoid loading too much data
                var totalStatusCount = _statusCollection.Count();
                sb.AppendLine($"\n=== STATUS TRACKING ===");
                sb.AppendLine($"Total status entries: {totalStatusCount}");
                
                if (totalStatusCount > 0)
                {
                    // CRITICAL FIX: Get only current session items first
                    var currentSessionItems = _statusCollection
                        .Find(s => s.SessionId == _sessionId)
                        .Take(100)
                        .ToList();
                    
                    var pending = currentSessionItems.Count(s => s.Status == SyncStatus.Pending);
                    var success = currentSessionItems.Count(s => s.Status == SyncStatus.Success);
                    var failed = currentSessionItems.Count(s => s.Status == SyncStatus.Failed);
                    
                    sb.AppendLine($"\nCurrent Session ({_sessionId}):");
                    sb.AppendLine($"  Pending: {pending}, Success: {success}, Failed: {failed}");
                    
                    // Show transaction IDs for current session (limited)
                    if (pending > 0 || failed > 0)
                    {
                        var items = currentSessionItems
                            .Where(s => s.Status == SyncStatus.Pending || s.Status == SyncStatus.Failed)
                            .Take(10)
                            .ToList();
                        
                        if (items.Any())
                        {
                            sb.AppendLine($"  Transaction IDs (first 10): {string.Join(", ", items.Select(s => s.TransactionId))}");
                        }
                    }
                    
                    // Check if there are old session entries - Use Count instead of loading all
                    var oldSessionCount = _statusCollection.Count(s => s.SessionId != _sessionId);
                    if (oldSessionCount > 0)
                    {
                        sb.AppendLine($"\n?? Found {oldSessionCount} entries from previous sessions");
                        sb.AppendLine($"  These are not counted in current stats but still exist in database.");
                    }
                }
                else
                {
                    sb.AppendLine($"? No status entries - clean state");
                }
                
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error getting debug info: {ex.Message}\n\nStack trace:\n{ex.StackTrace}";
            }
        }

        /// <summary>
        /// Clear all items from the queue (use with caution!)
        /// </summary>
        public void ClearQueue()
        {
            try
            {
                Debug.WriteLine($"[LiteQueue] ??? Clearing queue... Current counts - Pending: {_pendingCount}, Success: {_successCount}, Failed: {_failureCount}");
                
                // CRITICAL FIX: Clear the actual LiteQueue collection directly from LiteDB
                var queueCollection = _liteDb.GetCollection<QueueEntry<TransactionQueueItem>>("transaction_sync_queue");
                
                // Get ALL items in queue (including orphaned ones)
                var allQueueItems = queueCollection.FindAll().ToList();
                int totalQueueItems = allQueueItems.Count;
                
                Debug.WriteLine($"[LiteQueue] ?? Found {totalQueueItems} total items in queue database (including any ghosts)");
                
                // Delete ALL items from the queue collection (not just current session)
                if (totalQueueItems > 0)
                {
                    foreach (var item in allQueueItems)
                    {
                        queueCollection.Delete(item.Id);
                    }
                    Debug.WriteLine($"[LiteQueue] ? Deleted all {totalQueueItems} items from queue database");
                }
                
                // Also try to dequeue any items that might be in memory
                int dequeuedCount = 0;
                while (_queue.Dequeue() != null)
                {
                    dequeuedCount++;
                    if (dequeuedCount > 1000) break; // Safety limit
                }
                
                if (dequeuedCount > 0)
                {
                    Debug.WriteLine($"[LiteQueue] ?? Dequeued {dequeuedCount} items from in-memory queue");
                }
                
                // Clear ONLY current session status entries
                var currentSessionItems = _statusCollection
                    .Find(s => s.SessionId == _sessionId)
                    .ToList();
                
                int sessionStatusCount = currentSessionItems.Count;
                
                Debug.WriteLine($"[LiteQueue] ?? Found {sessionStatusCount} status entries for current session");
                
                foreach (var item in currentSessionItems)
                {
                    _statusCollection.Delete(item.Id);
                }
                
                if (sessionStatusCount > 0)
                {
                    Debug.WriteLine($"[LiteQueue] ? Deleted {sessionStatusCount} current session status entries");
                }
                
                // Reset counters
                _pendingCount = 0;
                _successCount = 0;
                _failureCount = 0;
                
                // Verify the cleanup worked
                var verifyQueueCount = queueCollection.Count();
                var verifyStatusCount = _statusCollection.Count(s => s.SessionId == _sessionId);
                
                Debug.WriteLine($"[LiteQueue] ?? Verification after clear:");
                Debug.WriteLine($"[LiteQueue]    - Items in queue database: {verifyQueueCount}");
                Debug.WriteLine($"[LiteQueue]    - Status entries for current session: {verifyStatusCount}");
                Debug.WriteLine($"[LiteQueue]    - Counters: P={_pendingCount}, S={_successCount}, F={_failureCount}");
                
                if (verifyQueueCount > 0)
                {
                    Debug.WriteLine($"[LiteQueue] ?? WARNING: Queue database still has {verifyQueueCount} items after clear!");
                    Debug.WriteLine($"[LiteQueue] ?? These might be from other sessions. Try Force Clear to remove ALL data.");
                }
                else
                {
                    Debug.WriteLine($"[LiteQueue] ? Queue cleared successfully!");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] ? Error clearing queue: {ex.Message}");
                Debug.WriteLine($"[LiteQueue] Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Clears all pending queue items, failed sync records and successful status
        /// entries, resetting the pending/failed/success counters to zero for the new
        /// day. Used by the daily auto-reset.
        /// </summary>
        /// <returns>The number of pending, failed and success items that were cleared.</returns>
        public (int PendingCleared, int FailedCleared, int SuccessCleared) ResetPendingAndFailed()
        {
            int pendingCleared = 0;
            int failedCleared = 0;
            int successCleared = 0;

            try
            {
                Debug.WriteLine($"[LiteQueue] Daily reset starting... Current counts - Pending: {_pendingCount}, Success: {_successCount}, Failed: {_failureCount}");

                // 1. Clear all pending queue items
                var queueCollection = _liteDb.GetCollection<QueueEntry<TransactionQueueItem>>("transaction_sync_queue");
                var allQueueItems = queueCollection.FindAll().ToList();
                pendingCleared = allQueueItems.Count;

                foreach (var item in allQueueItems)
                    queueCollection.Delete(item.Id);

                // Drain any lingering in-memory items
                int dequeuedCount = 0;
                while (_queue.Dequeue() != null && dequeuedCount < 10000)
                    dequeuedCount++;

                Debug.WriteLine($"[LiteQueue] Daily reset: cleared {pendingCleared} pending queue items ({dequeuedCount} drained in-memory)");

                // 2. Clear Pending and Failed status entries (all sessions)
                var pendingEntries = _statusCollection
                    .Find(s => s.Status == SyncStatus.Pending)
                    .ToList();
                foreach (var entry in pendingEntries)
                    _statusCollection.Delete(entry.Id);

                var failedEntries = _statusCollection
                    .Find(s => s.Status == SyncStatus.Failed)
                    .ToList();
                failedCleared = failedEntries.Count;
                foreach (var entry in failedEntries)
                    _statusCollection.Delete(entry.Id);

                Debug.WriteLine($"[LiteQueue] Daily reset: cleared {pendingEntries.Count} pending + {failedCleared} failed status entries");

                // 3. Clear successful status entries so the success counter also starts fresh for the new day
                var successEntries = _statusCollection
                    .Find(s => s.Status == SyncStatus.Success)
                    .ToList();
                successCleared = successEntries.Count;
                foreach (var entry in successEntries)
                    _statusCollection.Delete(entry.Id);

                // 4. Reset all counters for the new day
                _pendingCount = 0;
                _failureCount = 0;
                _successCount = 0;

                Debug.WriteLine($"[LiteQueue] Daily reset complete - Pending: {_pendingCount}, Success: {_successCount}, Failed: {_failureCount}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] Error during daily reset: {ex.Message}");
                Debug.WriteLine($"[LiteQueue] Stack trace: {ex.StackTrace}");
            }

            return (pendingCleared, failedCleared, successCleared);
        }

        /// <summary>
        /// Remove only successfully synced items from the database
        /// </summary>
        public void RemoveSuccessfulItems()
        {
            try
            {
                Debug.WriteLine($"[LiteQueue] ??? Removing successful items...");
                
                // Get all successful transaction IDs
                var successfulItems = _statusCollection
                    .Find(s => s.Status == SyncStatus.Success)
                    .ToList();
                
                // Delete from status collection
                foreach (var item in successfulItems)
                {
                    _statusCollection.Delete(item.Id);
                }
                
                // Reset success count
                var removedCount = _successCount;
                _successCount = 0;
                
                Debug.WriteLine($"[LiteQueue] ? Removed {removedCount} successful items from tracking");
                Debug.WriteLine($"[LiteQueue] ?? Stats - Pending: {_pendingCount}, Success: {_successCount}, Failures: {_failureCount}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] ? Error removing successful items: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Checks if a transaction has a Failed status in the current session's sync tracking.
        /// Used to determine if re-enqueuing should adjust the failure count.
        /// </summary>
        public bool IsTransactionFailed(string transactionId)
        {
            try
            {
                var entry = _statusCollection.FindOne(s => 
                    s.TransactionId == transactionId && 
                    s.SessionId == _sessionId && 
                    s.Status == SyncStatus.Failed);
                return entry != null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] Error checking transaction failed status: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Adjusts the failure count when failed transactions are retried from the dead letter queue.
        /// Call this after re-enqueuing transactions from dead letter queue.
        /// </summary>
        /// <param name="retriedCount">Number of transactions successfully re-enqueued</param>
        public void AdjustFailureCountForRetry(int retriedCount)
        {
            try
            {
                Debug.WriteLine($"[LiteQueue] ?? Adjusting failure count for retry: reducing by {retriedCount}");
                Debug.WriteLine($"[LiteQueue] ?? Before adjustment - Pending: {_pendingCount}, Success: {_successCount}, Failures: {_failureCount}");

                // Decrement failure count by the number of retried items (don't go below 0)
                _failureCount = Math.Max(0, _failureCount - retriedCount);

                // Also clean up the failed status entries for the current session
                // since these items are now re-enqueued as pending
                var failedEntries = _statusCollection
                    .Find(s => s.SessionId == _sessionId && s.Status == SyncStatus.Failed)
                    .ToList();

                // Remove failed status entries that were re-enqueued
                // (the EnqueueTransaction method creates new Pending entries)
                int removedStatusCount = 0;
                foreach (var entry in failedEntries.Take(retriedCount))
                {
                    _statusCollection.Delete(entry.Id);
                    removedStatusCount++;
                }

                Debug.WriteLine($"[LiteQueue] ??? Removed {removedStatusCount} failed status entries");
                Debug.WriteLine($"[LiteQueue] ?? After adjustment - Pending: {_pendingCount}, Success: {_successCount}, Failures: {_failureCount}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] ? Error adjusting failure count: {ex.Message}");
            }
        }

        /// <summary>
        /// Force cleanup of all ghost/orphaned items - use when Clear doesn't work
        /// </summary>
        public void ForceCleanupAllData()
        {
            try
            {
                Debug.WriteLine($"[LiteQueue] ?? FORCE CLEANUP: Starting aggressive cleanup of ALL data...");
                
                // 1. Clear the actual queue collection directly - DELETE ALL
                var queueCollection = _liteDb.GetCollection<QueueEntry<TransactionQueueItem>>("transaction_sync_queue");
                
                // Get count before deletion
                int beforeQueueCount = queueCollection.Count();
                Debug.WriteLine($"[LiteQueue] ?? Queue database has {beforeQueueCount} items before force cleanup");
                
                // Delete ALL items from queue collection
                var allQueueItems = queueCollection.FindAll().ToList();
                foreach (var item in allQueueItems)
                {
                    queueCollection.Delete(item.Id);
                }
                Debug.WriteLine($"[LiteQueue] ? Deleted {beforeQueueCount} items from queue database");
                
                // 2. Clear ALL status entries from ALL sessions
                int beforeStatusCount = _statusCollection.Count();
                Debug.WriteLine($"[LiteQueue] ?? Status collection has {beforeStatusCount} entries before force cleanup");
                
                var allStatusItems = _statusCollection.FindAll().ToList();
                foreach (var item in allStatusItems)
                {
                    _statusCollection.Delete(item.Id);
                }
                Debug.WriteLine($"[LiteQueue] ? Deleted {beforeStatusCount} status entries");
                
                // 3. Try to dequeue any lingering in-memory items
                int dequeuedCount = 0;
                while (_queue.Dequeue() != null && dequeuedCount < 10000)
                {
                    dequeuedCount++;
                }
                
                if (dequeuedCount > 0)
                {
                    Debug.WriteLine($"[LiteQueue] ?? Dequeued {dequeuedCount} lingering in-memory items");
                }
                
                // 4. Reset all counters
                _pendingCount = 0;
                _successCount = 0;
                _failureCount = 0;
                
                // 5. Verify cleanup
                var finalQueueCount = queueCollection.Count();
                var finalStatusCount = _statusCollection.Count();
                
                Debug.WriteLine($"[LiteQueue] ?? Verification after force cleanup:");
                Debug.WriteLine($"[LiteQueue]    - Queue database: {finalQueueCount} items");
                Debug.WriteLine($"[LiteQueue]    - Status database: {finalStatusCount} entries");
                Debug.WriteLine($"[LiteQueue]    - In-memory counters: P={_pendingCount}, S={_successCount}, F={_failureCount}");
                
                if (finalQueueCount == 0 && finalStatusCount == 0)
                {
                    Debug.WriteLine($"[LiteQueue] ? ? ? FORCE CLEANUP SUCCESSFUL - All data completely wiped!");
                }
                else
                {
                    Debug.WriteLine($"[LiteQueue] ?? WARNING: Database still has data after force cleanup:");
                    Debug.WriteLine($"[LiteQueue]    Queue: {finalQueueCount}, Status: {finalStatusCount}");
                    Debug.WriteLine($"[LiteQueue] ?? Try restarting the application if items persist");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiteQueue] ? Error during force cleanup: {ex.Message}");
                Debug.WriteLine($"[LiteQueue] Stack trace: {ex.StackTrace}");
                throw;
            }
        }
    }

    /// <summary>
    /// Item stored in LiteQueue for transaction synchronization
    /// </summary>
    [Serializable]
    public class TransactionQueueItem
    {
        /// <summary>
        /// The transaction to be synchronized
        /// </summary>
        public WeighingTransaction Transaction { get; set; } = null!;

        /// <summary>
        /// Number of times we've attempted to sync this transaction
        /// </summary>
        public int AttemptCount { get; set; }

        /// <summary>
        /// When this item was first enqueued
        /// </summary>
        public DateTime EnqueuedAt { get; set; }

        /// <summary>
        /// Unique key to prevent duplicate processing
        /// </summary>
        public string IdempotencyKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// Tracks the sync status of transactions
    /// </summary>
    public class SyncStatusEntry
    {
        public int Id { get; set; }
        public string TransactionId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public SyncStatus Status { get; set; }
        public DateTime EnqueuedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? LastAttemptAt { get; set; }
        public int AttemptCount { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public enum SyncStatus
    {
        Pending = 0,
        Success = 1,
        Failed = 2
    }
}
