using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ProWeighSuitePlatforms.Models;
using System.Linq;
using System.Threading;
using ProWeighSuitePlatforms.Services;
using System.Diagnostics;
using System.Text.Json;
using System.Globalization;
using UUIDNext;

namespace ProWeighSuitePlatforms.Data
{
    public class WeighingRepository
    {
        // Notify when a transaction is inserted: (dbPath, insertedId)
        public static event Action<string, string>? TransactionInserted;

        private readonly string _dbPath;
        private readonly SemaphoreSlim _initSemaphore = new(1,1);
        private bool _isInitialized = false;

        public WeighingRepository(string dbPath)
        {
            _dbPath = dbPath;
        }

        private SqliteConnection GetConnection()
            => new SqliteConnection($"Data Source={_dbPath};Cache=Shared");

        private async Task OpenWithPragmasAsync(SqliteConnection conn)
        {
            await conn.OpenAsync();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL;";
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }

        private async Task EnsureInitializedAsync()
        {
            if (_isInitialized) return;
            await _initSemaphore.WaitAsync();
            try
            {
                if (_isInitialized) return;
                await InitializeAsync();
                _isInitialized = true;
            }
            finally
            {
                _initSemaphore.Release();
            }
        }

        public async Task InitializeAsync()
        {
            var dir = System.IO.Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            using var conn = GetConnection();
            await conn.OpenAsync();

            // Enable foreign keys, WAL and busy timeout to avoid "database is locked" crashes
            var pragmaCmd = conn.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL;";
            await pragmaCmd.ExecuteNonQueryAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS weighing_batches (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                batch_number TEXT NOT NULL UNIQUE,
                batch_size REAL NOT NULL,
                total_batches INTEGER NOT NULL,
                current_batch_number INTEGER NOT NULL,
                status TEXT NOT NULL,
                started_by TEXT NOT NULL,
                started_date TEXT NOT NULL,
                completed_date TEXT,
                notes TEXT
            );

            CREATE TABLE IF NOT EXISTS weighing_transactions (
                id TEXT PRIMARY KEY,
                batch_id INTEGER,
                batch_number TEXT NOT NULL,
                material_code TEXT NOT NULL,
                material_name TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                target_weight REAL NOT NULL,
                lower_weight REAL NOT NULL,
                upper_weight REAL NOT NULL,
                actual_weight REAL NOT NULL,
                deviation REAL NOT NULL,
                status TEXT NOT NULL,
                weighed_by TEXT NOT NULL,
                weighed_date TEXT NOT NULL,
                printed_date TEXT,
                customer_name TEXT,
                manufacturing_date TEXT,
                expiry_date TEXT,
                equipment_id TEXT,
                location TEXT,
                remarks TEXT,
                tare_weight REAL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_weighing_batches_batch_number ON weighing_batches(batch_number);
            CREATE INDEX IF NOT EXISTS idx_weighing_batches_date ON weighing_batches(started_date);
            CREATE INDEX IF NOT EXISTS idx_weighing_transactions_batch_id ON weighing_transactions(batch_id);
            CREATE INDEX IF NOT EXISTS idx_weighing_transactions_batch_number ON weighing_transactions(batch_number);
            CREATE INDEX IF NOT EXISTS idx_weighing_transactions_date ON weighing_transactions(weighed_date);
            CREATE INDEX IF NOT EXISTS idx_weighing_transactions_material ON weighing_transactions(material_code);
            ";
            await cmd.ExecuteNonQueryAsync();

            // If older DBs still have a non-nullable recipe_code column (removed from app), migrate to remove it
            await RemoveRecipeCodeIfExistsAsync(conn);
            await RemoveRecipeCodeFromTransactionsIfExistsAsync(conn);

            // Add new columns to existing table if they don't exist
            await AddColumnIfNotExistsAsync(conn, "weighing_transactions", "customer_name", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "weighing_transactions", "manufacturing_date", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "weighing_transactions", "expiry_date", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "weighing_transactions", "equipment_id", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "weighing_transactions", "location", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "weighing_transactions", "remarks", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "weighing_transactions", "tare_weight", "REAL DEFAULT 0");
            await AddColumnIfNotExistsAsync(conn, "weighing_transactions", "is_synced", "INTEGER DEFAULT 0");
            await AddColumnIfNotExistsAsync(conn, "weighing_transactions", "server_response", "TEXT");

            // Reprint tracking table
            var reprintCmd = conn.CreateCommand();
            reprintCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS reprint_records (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                transaction_id TEXT NOT NULL,
                batch_number TEXT NOT NULL DEFAULT '',
                material_code TEXT NOT NULL DEFAULT '',
                reason TEXT NOT NULL,
                reprinted_by TEXT NOT NULL,
                reprinted_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_reprint_transaction ON reprint_records(transaction_id);
            ";
            await reprintCmd.ExecuteNonQueryAsync();

            // Migrate existing integer-id transactions table to TEXT primary key if needed
            await MigrateTransactionIdToTextAsync(conn);
        }

        /// <summary>
        /// If the weighing_transactions table still has an INTEGER primary key (legacy),
        /// rebuilds it with a TEXT primary key, converting existing integer ids to strings.
        /// </summary>
        private async Task MigrateTransactionIdToTextAsync(SqliteConnection conn)
        {
            try
            {
                var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = "PRAGMA table_info(weighing_transactions);";
                string idType = string.Empty;
                using var reader = await checkCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (reader.GetString(1) == "id")
                    {
                        idType = reader.GetString(2).ToUpperInvariant();
                        break;
                    }
                }

                // Already TEXT � nothing to do
                if (idType == "TEXT") return;

                using var tx = conn.BeginTransaction();
                try
                {
                    var disableFk = conn.CreateCommand();
                    disableFk.Transaction = tx;
                    disableFk.CommandText = "PRAGMA foreign_keys = OFF;";
                    await disableFk.ExecuteNonQueryAsync();

                    var createNew = conn.CreateCommand();
                    createNew.Transaction = tx;
                    createNew.CommandText = @"
                        CREATE TABLE IF NOT EXISTS weighing_transactions_new (
                            id TEXT PRIMARY KEY,
                            batch_id INTEGER,
                            batch_number TEXT NOT NULL,
                            material_code TEXT NOT NULL,
                            material_name TEXT NOT NULL,
                            sequence INTEGER NOT NULL,
                            target_weight REAL NOT NULL,
                            lower_weight REAL NOT NULL,
                            upper_weight REAL NOT NULL,
                            actual_weight REAL NOT NULL,
                            deviation REAL NOT NULL,
                            status TEXT NOT NULL,
                            weighed_by TEXT NOT NULL,
                            weighed_date TEXT NOT NULL,
                            printed_date TEXT,
                            customer_name TEXT,
                            manufacturing_date TEXT,
                            expiry_date TEXT,
                            equipment_id TEXT,
                            location TEXT,
                            remarks TEXT,
                            tare_weight REAL DEFAULT 0,
                            is_synced INTEGER DEFAULT 0
                        );";
                    await createNew.ExecuteNonQueryAsync();

                    // Copy rows, casting the integer id to TEXT so existing lookups by batch still work
                    var copy = conn.CreateCommand();
                    copy.Transaction = tx;
                    copy.CommandText = @"
                        INSERT INTO weighing_transactions_new
                            (id, batch_id, batch_number, material_code, material_name, sequence,
                             target_weight, lower_weight, upper_weight, actual_weight, deviation,
                             status, weighed_by, weighed_date, printed_date, customer_name,
                             manufacturing_date, expiry_date, equipment_id, location, remarks,
                             tare_weight, is_synced)
                        SELECT CAST(id AS TEXT), batch_id, batch_number, material_code, material_name, sequence,
                               target_weight, lower_weight, upper_weight, actual_weight, deviation,
                               status, weighed_by, weighed_date, printed_date, customer_name,
                               manufacturing_date, expiry_date, equipment_id, location, remarks,
                               COALESCE(tare_weight, 0), COALESCE(is_synced, 0)
                        FROM weighing_transactions;";
                    await copy.ExecuteNonQueryAsync();

                    var drop = conn.CreateCommand();
                    drop.Transaction = tx;
                    drop.CommandText = "DROP TABLE IF EXISTS weighing_transactions;";
                    await drop.ExecuteNonQueryAsync();

                    var rename = conn.CreateCommand();
                    rename.Transaction = tx;
                    rename.CommandText = "ALTER TABLE weighing_transactions_new RENAME TO weighing_transactions;";
                    await rename.ExecuteNonQueryAsync();

                    var idx = conn.CreateCommand();
                    idx.Transaction = tx;
                    idx.CommandText = @"
                        CREATE INDEX IF NOT EXISTS idx_weighing_transactions_batch_id ON weighing_transactions(batch_id);
                        CREATE INDEX IF NOT EXISTS idx_weighing_transactions_batch_number ON weighing_transactions(batch_number);
                        CREATE INDEX IF NOT EXISTS idx_weighing_transactions_date ON weighing_transactions(weighed_date);
                        CREATE INDEX IF NOT EXISTS idx_weighing_transactions_material ON weighing_transactions(material_code);";
                    await idx.ExecuteNonQueryAsync();

                    var enableFk = conn.CreateCommand();
                    enableFk.Transaction = tx;
                    enableFk.CommandText = "PRAGMA foreign_keys = ON;";
                    await enableFk.ExecuteNonQueryAsync();

                    tx.Commit();
                    Debug.WriteLine("[WeighingRepository] Migrated weighing_transactions.id from INTEGER to TEXT.");
                }
                catch
                {
                    try { tx.Rollback(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WeighingRepository] Migration error (non-fatal): {ex.Message}");
            }
        }

        private async Task RemoveRecipeCodeIfExistsAsync(SqliteConnection conn)
        {
            try
            {
                var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = "PRAGMA table_info(weighing_batches);";
                var existingColumns = new List<string>();
                using var reader = await checkCmd.ExecuteReaderAsync();
                bool hasRecipe = false;
                while (await reader.ReadAsync())
                {
                    var colName = reader.GetString(1);
                    existingColumns.Add(colName);
                    if (string.Equals(colName, "recipe_code", StringComparison.OrdinalIgnoreCase))
                        hasRecipe = true;
                }

                if (!hasRecipe)
                    return;

                using var transaction = conn.BeginTransaction();
                try
                {
                    // Disable foreign keys during migration
                    var disableFk = conn.CreateCommand();
                    disableFk.Transaction = transaction;
                    disableFk.CommandText = "PRAGMA foreign_keys = OFF;";
                    await disableFk.ExecuteNonQueryAsync();

                    // Create a new table without recipe_code
                    var createCmd = conn.CreateCommand();
                    createCmd.Transaction = transaction;
                    createCmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS weighing_batches_new (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            batch_number TEXT NOT NULL UNIQUE,
                            batch_size REAL NOT NULL,
                            total_batches INTEGER NOT NULL,
                            current_batch_number INTEGER NOT NULL,
                            status TEXT NOT NULL,
                            started_by TEXT NOT NULL,
                            started_date TEXT NOT NULL,
                            completed_date TEXT,
                            notes TEXT
                        );";
                    await createCmd.ExecuteNonQueryAsync();

                    // Copy data from old table into new table excluding recipe_code
                    var colsToCopy = existingColumns.Where(c => !string.Equals(c, "recipe_code", StringComparison.OrdinalIgnoreCase)).ToList();
                    var colList = string.Join(", ", colsToCopy);

                    var copyCmd = conn.CreateCommand();
                    copyCmd.Transaction = transaction;
                    copyCmd.CommandText = $"INSERT INTO weighing_batches_new ({colList}) SELECT {colList} FROM weighing_batches;";
                    await copyCmd.ExecuteNonQueryAsync();

                    // Drop old table and rename new
                    var dropCmd = conn.CreateCommand();
                    dropCmd.Transaction = transaction;
                    dropCmd.CommandText = "DROP TABLE IF EXISTS weighing_batches;";
                    await dropCmd.ExecuteNonQueryAsync();

                    var renameCmd = conn.CreateCommand();
                    renameCmd.Transaction = transaction;
                    renameCmd.CommandText = "ALTER TABLE weighing_batches_new RENAME TO weighing_batches;";
                    await renameCmd.ExecuteNonQueryAsync();

                    // Recreate essential indexes
                    var idxCmd = conn.CreateCommand();
                    idxCmd.Transaction = transaction;
                    idxCmd.CommandText = @"
                        CREATE INDEX IF NOT EXISTS idx_weighing_batches_batch_number ON weighing_batches(batch_number);
                        CREATE INDEX IF NOT EXISTS idx_weighing_batches_date ON weighing_batches(started_date);
                    ";
                    await idxCmd.ExecuteNonQueryAsync();

                    // Re-enable foreign keys
                    var enableFk = conn.CreateCommand();
                    enableFk.Transaction = transaction;
                    enableFk.CommandText = "PRAGMA foreign_keys = ON;";
                    await enableFk.ExecuteNonQueryAsync();

                    transaction.Commit();
                }
                catch
                {
                    try { transaction.Rollback(); } catch { }
                }
            }
            catch
            {
                // ignore migration errors
            }
        }

        private async Task RemoveRecipeCodeFromTransactionsIfExistsAsync(SqliteConnection conn)
        {
            try
            {
                var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = "PRAGMA table_info(weighing_transactions);";
                var existingColumns = new List<string>();
                using var reader = await checkCmd.ExecuteReaderAsync();
                bool hasRecipe = false;
                while (await reader.ReadAsync())
                {
                    var colName = reader.GetString(1);
                    existingColumns.Add(colName);
                    if (string.Equals(colName, "recipe_code", StringComparison.OrdinalIgnoreCase))
                        hasRecipe = true;
                }

                if (!hasRecipe)
                    return;

                using var transaction = conn.BeginTransaction();
                try
                {
                    var disableFk = conn.CreateCommand();
                    disableFk.Transaction = transaction;
                    disableFk.CommandText = "PRAGMA foreign_keys = OFF;";
                    await disableFk.ExecuteNonQueryAsync();

                    var createCmd = conn.CreateCommand();
                    createCmd.Transaction = transaction;
                    createCmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS weighing_transactions_new (
                            id TEXT PRIMARY KEY,
                            batch_id INTEGER,
                            batch_number TEXT NOT NULL,
                            material_code TEXT NOT NULL,
                            material_name TEXT NOT NULL,
                            sequence INTEGER NOT NULL,
                            target_weight REAL NOT NULL,
                            lower_weight REAL NOT NULL,
                            upper_weight REAL NOT NULL,
                            actual_weight REAL NOT NULL,
                            deviation REAL NOT NULL,
                            status TEXT NOT NULL,
                            weighed_by TEXT NOT NULL,
                            weighed_date TEXT NOT NULL,
                            printed_date TEXT,
                            customer_name TEXT,
                            manufacturing_date TEXT,
                            expiry_date TEXT,
                            equipment_id TEXT,
                            location TEXT,
                            remarks TEXT,
                            tare_weight REAL DEFAULT 0
                        );";
                    await createCmd.ExecuteNonQueryAsync();

                    var colsToCopy = existingColumns.Where(c => !string.Equals(c, "recipe_code", StringComparison.OrdinalIgnoreCase)).ToList();
                    var colList = string.Join(", ", colsToCopy);

                    var copyCmd = conn.CreateCommand();
                    copyCmd.Transaction = transaction;
                    copyCmd.CommandText = $"INSERT INTO weighing_transactions_new ({colList}) SELECT {colList} FROM weighing_transactions;";
                    await copyCmd.ExecuteNonQueryAsync();

                    var dropCmd = conn.CreateCommand();
                    dropCmd.Transaction = transaction;
                    dropCmd.CommandText = "DROP TABLE IF EXISTS weighing_transactions;";
                    await dropCmd.ExecuteNonQueryAsync();

                    var renameCmd = conn.CreateCommand();
                    renameCmd.Transaction = transaction;
                    renameCmd.CommandText = "ALTER TABLE weighing_transactions_new RENAME TO weighing_transactions;";
                    await renameCmd.ExecuteNonQueryAsync();

                    var idxCmd = conn.CreateCommand();
                    idxCmd.Transaction = transaction;
                    idxCmd.CommandText = @"
                        CREATE INDEX IF NOT EXISTS idx_weighing_transactions_batch_id ON weighing_transactions(batch_id);
                        CREATE INDEX IF NOT EXISTS idx_weighing_transactions_batch_number ON weighing_transactions(batch_number);
                        CREATE INDEX IF NOT EXISTS idx_weighing_transactions_date ON weighing_transactions(weighed_date);
                        CREATE INDEX IF NOT EXISTS idx_weighing_transactions_material ON weighing_transactions(material_code);
                    ";
                    await idxCmd.ExecuteNonQueryAsync();

                    var enableFk = conn.CreateCommand();
                    enableFk.Transaction = transaction;
                    enableFk.CommandText = "PRAGMA foreign_keys = ON;";
                    await enableFk.ExecuteNonQueryAsync();

                    transaction.Commit();
                }
                catch
                {
                    try { transaction.Rollback(); } catch { }
                }
            }
            catch
            {
                // ignore migration errors
            }
        }

        private async Task AddColumnIfNotExistsAsync(SqliteConnection conn, string tableName, string columnName, string columnType)
        {
            try
            {
                var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = $"PRAGMA table_info({tableName})";
                using var reader = await checkCmd.ExecuteReaderAsync();

                bool columnExists = false;
                while (await reader.ReadAsync())
                {
                    if (reader.GetString(1) == columnName)
                    {
                        columnExists = true;
                        break;
                    }
                }

                if (!columnExists)
                {
                    var alterCmd = conn.CreateCommand();
                    alterCmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType}";
                    await alterCmd.ExecuteNonQueryAsync();
                }
            }
            catch
            {
                // Column might already exist or other error - continue
            }
        }

        // Batch Operations
        public async Task<int> CreateBatchAsync(WeighingBatch batch)
        {
            EntityTextNormalizer.Trim(batch);
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            // Try inserting, but if UNIQUE constraint on batch_number happens (concurrent or race), retry with a new sequence
            int maxAttempts = 5;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                INSERT INTO weighing_batches (
                    batch_number, batch_size, total_batches, current_batch_number, 
                    status, started_by, started_date, notes
                ) VALUES (
                    $batchNumber, $batchSize, $totalBatches, $currentBatchNumber,
                    $status, $startedBy, $startedDate, $notes
                );
                SELECT last_insert_rowid();
                ";

                cmd.Parameters.AddWithValue("$batchNumber", batch.BatchNumber);
                cmd.Parameters.AddWithValue("$batchSize", batch.BatchSize);
                cmd.Parameters.AddWithValue("$totalBatches", batch.TotalBatches);
                cmd.Parameters.AddWithValue("$currentBatchNumber", batch.CurrentBatchNumber);
                cmd.Parameters.AddWithValue("$status", batch.Status);
                cmd.Parameters.AddWithValue("$startedBy", batch.StartedBy);
                cmd.Parameters.AddWithValue("$startedDate", batch.StartedDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$notes", batch.Notes ?? string.Empty);

                try
                {
                    var result = await cmd.ExecuteScalarAsync();
                    return Convert.ToInt32(result);
                }
                catch (Microsoft.Data.Sqlite.SqliteException ex)
                {
                    // SQLITE_CONSTRAINT (code 19) indicates unique constraint violation
                    if (ex.SqliteErrorCode == 19 && ex.Message != null && ex.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var seq = await GetNextBatchSequenceForTodayAsync();
                            var now = DateTime.Now;
                            // Preserve the ScaleNumber prefix from the original batch number (everything before the date segment)
                            var parts = batch.BatchNumber.Split('-');
                            var scalePrefix = parts.Length >= 1 ? parts[0] : "0";
                            batch.BatchNumber = $"{scalePrefix}-{now:yyyyMMdd}-{seq}";
                            await Task.Delay(50);
                            continue;
                        }
                        catch
                        {
                            throw;
                        }
                    }

                    // Unknown sqlite error - rethrow
                    throw;
                }
            }

            throw new Exception("Failed to create batch after multiple attempts due to batch_number conflicts.");
        }

        public async Task<WeighingBatch?> GetBatchByIdAsync(int batchId)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT id, batch_number, batch_size, total_batches, current_batch_number, 
                   status, started_by, started_date, completed_date, notes
            FROM weighing_batches
            WHERE id = $batchId
            ";
            cmd.Parameters.AddWithValue("$batchId", batchId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new WeighingBatch
                {
                    Id = reader.GetInt32(0),
                    BatchNumber = reader.GetString(1),
                    BatchSize = reader.GetDouble(2),
                    TotalBatches = reader.GetInt32(3),
                    CurrentBatchNumber = reader.GetInt32(4),
                    Status = reader.GetString(5),
                    StartedBy = reader.GetString(6),
                    StartedDate = DateTime.Parse(reader.GetString(7)),
                    CompletedDate = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8)),
                    Notes = reader.IsDBNull(9) ? string.Empty : reader.GetString(9)
                };
            }
            return null;
        }

        public async Task<WeighingBatch?> GetActiveBatchAsync(int batchId)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT id, batch_number, batch_size, total_batches, current_batch_number, 
                   status, started_by, started_date, completed_date, notes
            FROM weighing_batches
            WHERE id = $batchId AND status = 'InProgress'
            ";
            cmd.Parameters.AddWithValue("$batchId", batchId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new WeighingBatch
                {
                    Id = reader.GetInt32(0),
                    BatchNumber = reader.GetString(1),
                    BatchSize = reader.GetDouble(2),
                    TotalBatches = reader.GetInt32(3),
                    CurrentBatchNumber = reader.GetInt32(4),
                    Status = reader.GetString(5),
                    StartedBy = reader.GetString(6),
                    StartedDate = DateTime.Parse(reader.GetString(7)),
                    CompletedDate = reader.IsDBNull(8) ? null : DateTime.Parse(reader.GetString(8)),
                    Notes = reader.IsDBNull(9) ? string.Empty : reader.GetString(9)
                };
            }
            return null;
        }

        public async Task UpdateBatchStatusAsync(int batchId, string status, DateTime? completedDate = null)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            UPDATE weighing_batches
            SET status = $status, completed_date = $completedDate
            WHERE id = $id
            ";
            cmd.Parameters.AddWithValue("$id", batchId);
            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$completedDate", completedDate.HasValue ? completedDate.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateBatchCurrentNumberAsync(int batchId, int currentNumber)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            UPDATE weighing_batches
            SET current_batch_number = $current
            WHERE id = $id
            ";
            cmd.Parameters.AddWithValue("$id", batchId);
            cmd.Parameters.AddWithValue("$current", currentNumber);

            await cmd.ExecuteNonQueryAsync();
        }

        // Transaction Operations
        public async Task<string> CreateTransactionAsync(WeighingTransaction transaction)
        {
            EntityTextNormalizer.Trim(transaction);
            await EnsureInitializedAsync();

            // Business rule: "no product" records (measured weight below the presence
            // threshold) must never be persisted. Guarded here at the data layer as
            // defense-in-depth in case any caller bypasses the UI-level check.
            var normalizedStatus = transaction.Status?.Trim().ToUpperInvariant().Replace(" ", string.Empty);
            if (normalizedStatus == "NOPRODUCT")
                throw new InvalidOperationException("Transactions with status NOPRODUCT cannot be saved.");

            // Generate UUID v7 � time-ordered, globally unique
            var newId = Uuid.NewDatabaseFriendly(UUIDNext.Database.SQLite).ToString();
            transaction.Id = newId;

            // If location or equipment id not provided, try to load from scale config JSON
            try
            {
                if (string.IsNullOrWhiteSpace(transaction.Location) || string.IsNullOrWhiteSpace(transaction.EquipmentId))
                {
                    var cfg = new ScaleConfigService();
                    var settings = await cfg.LoadSettingsAsync();
                    if (settings != null)
                    {
                        if (string.IsNullOrWhiteSpace(transaction.EquipmentId))
                            transaction.EquipmentId = !string.IsNullOrWhiteSpace(settings.ScaleName) ? settings.ScaleName : settings.PortName;
                        if (string.IsNullOrWhiteSpace(transaction.Location))
                            transaction.Location = settings.Location ?? string.Empty;
                    }
                }
            }
            catch { }

            Debug.WriteLine($"[WeighingRepository] Inserting transaction id={newId} into DB: {_dbPath}");

            using var conn = GetConnection();
            await OpenWithPragmasAsync(conn);

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO weighing_transactions (
                id, batch_id, batch_number, material_code, material_name, sequence,
                target_weight, lower_weight, upper_weight, actual_weight, deviation,
                status, weighed_by, weighed_date, customer_name, manufacturing_date, expiry_date,
                equipment_id, location, remarks, tare_weight, is_synced
            ) VALUES (
                $id, $batchId, $batchNumber, $materialCode, $materialName, $sequence,
                $targetWeight, $lowerWeight, $upperWeight, $actualWeight, $deviation,
                $status, $weighedBy, $weighedDate, $customerName, $manufacturingDate, $expiryDate,
                $equipmentId, $location, $remarks, $tareWeight, 0
            );
            ";

            cmd.Parameters.AddWithValue("$id", newId);
            cmd.Parameters.AddWithValue("$batchId", transaction.BatchId);
            cmd.Parameters.AddWithValue("$batchNumber", transaction.BatchNumber);
            cmd.Parameters.AddWithValue("$materialCode", transaction.MaterialCode);
            cmd.Parameters.AddWithValue("$materialName", transaction.MaterialName);
            cmd.Parameters.AddWithValue("$sequence", transaction.Sequence);
            cmd.Parameters.AddWithValue("$targetWeight", transaction.TargetWeight);
            cmd.Parameters.AddWithValue("$lowerWeight", transaction.LowerWeight);
            cmd.Parameters.AddWithValue("$upperWeight", transaction.UpperWeight);
            cmd.Parameters.AddWithValue("$actualWeight", transaction.ActualWeight);
            cmd.Parameters.AddWithValue("$deviation", transaction.Deviation);
            cmd.Parameters.AddWithValue("$status", transaction.Status);
            cmd.Parameters.AddWithValue("$weighedBy", transaction.WeighedBy);
            cmd.Parameters.AddWithValue("$weighedDate", transaction.WeighedDate ?? string.Empty);
            cmd.Parameters.AddWithValue("$customerName", transaction.CustomerName ?? string.Empty);
            cmd.Parameters.AddWithValue("$manufacturingDate", transaction.ManufacturingDate ?? string.Empty);
            cmd.Parameters.AddWithValue("$expiryDate", transaction.ExpiryDate ?? string.Empty);
            cmd.Parameters.AddWithValue("$equipmentId", transaction.EquipmentId ?? string.Empty);
            cmd.Parameters.AddWithValue("$location", transaction.Location ?? string.Empty);
            cmd.Parameters.AddWithValue("$remarks", transaction.Remarks ?? string.Empty);
            cmd.Parameters.AddWithValue("$tareWeight", transaction.TareWeight);

            // Retry on SQLITE_BUSY / database is locked (common when DailyReset or LiteQueue writes concurrently)
            int retries = 3;
            while (true)
            {
                try { await cmd.ExecuteNonQueryAsync(); break; }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 5 && retries-- > 0)
                {
                    await Task.Delay(100);
                }
            }

            Debug.WriteLine($"[WeighingRepository] Inserted transaction id={newId} into DB: {_dbPath}");

            try { TransactionInserted?.Invoke(_dbPath, newId); } catch { }

            return newId;
        }

        public async Task UpdateTransactionPrintedAsync(string transactionId)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            UPDATE weighing_transactions
            SET printed_date = $printedDate
            WHERE id = $id
            ";
            cmd.Parameters.AddWithValue("$id", transactionId);
            cmd.Parameters.AddWithValue("$printedDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<WeighingTransaction>> GetBatchTransactionsAsync(int batchId)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT id, batch_id, batch_number, material_code, material_name, sequence,
                   target_weight, lower_weight, upper_weight, actual_weight, deviation,
                   status, weighed_by, weighed_date, printed_date, customer_name, manufacturing_date,
                   expiry_date, equipment_id, location, remarks, tare_weight, COALESCE(is_synced, 0) as is_synced, server_response
            FROM weighing_transactions
            WHERE batch_id = $batchId
            ORDER BY sequence
            ";
            cmd.Parameters.AddWithValue("$batchId", batchId);

            var transactions = new List<WeighingTransaction>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                transactions.Add(ReadTransaction(reader));
            }
            return transactions;
        }

        public async Task<int> GetNextBatchSequenceForTodayAsync()
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            var today = DateTime.Now.ToString("yyyyMMdd");

            // New format: {ScaleNumber}-{YYYYMMDD}-{BatchSeq}-{TransactionSeq}
            // The base batch number (before any transaction suffix) matches: *-{today}-{integer}
            // We look for batch_number entries containing '-{today}-' and extract the sequence after the date.
            cmd.CommandText = @"
            SELECT COALESCE(MAX(
                CASE
                    WHEN batch_number LIKE '%-' || $today || '-%' THEN
                        CAST(
                            CASE
                                WHEN INSTR(SUBSTR(batch_number, INSTR(batch_number, '-' || $today || '-') + 10), '-') > 0
                                THEN SUBSTR(batch_number,
                                            INSTR(batch_number, '-' || $today || '-') + 10,
                                            INSTR(SUBSTR(batch_number, INSTR(batch_number, '-' || $today || '-') + 10), '-') - 1)
                                ELSE SUBSTR(batch_number, INSTR(batch_number, '-' || $today || '-') + 10)
                            END
                        AS INTEGER)
                    ELSE 0
                END
            ), 0) + 1
            FROM weighing_batches
            ";
            cmd.Parameters.AddWithValue("$today", today);

            var result = await cmd.ExecuteScalarAsync();
            return result != null ? Convert.ToInt32(result) : 1;
        }

        public async Task<string> GetLastTransactionIdAsync()
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            // UUID v7 is lexicographically time-ordered, so MAX(id) gives the latest
            cmd.CommandText = "SELECT COALESCE(MAX(id), '') FROM weighing_transactions";

            var result = await cmd.ExecuteScalarAsync();
            return result as string ?? string.Empty;
        }

        public async Task<string> GetLastTransactionBatchNumberAsync()
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT batch_number FROM weighing_transactions ORDER BY id DESC LIMIT 1";

            var result = await cmd.ExecuteScalarAsync();
            return result as string ?? string.Empty;
        }

        public async Task<bool> HasMaterialBeenWeighedAsync(int batchId, string materialCode)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT COUNT(*)
            FROM weighing_transactions
            WHERE batch_id = $batchId AND material_code = $materialCode
            ";
            cmd.Parameters.AddWithValue("$batchId", batchId);
            cmd.Parameters.AddWithValue("$materialCode", materialCode);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }

        public async Task UpdateTransactionBatchNumberAsync(string transactionId, string batchNumber)
        {
            transactionId = transactionId?.Trim() ?? string.Empty;
            batchNumber = batchNumber?.Trim() ?? string.Empty;
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            UPDATE weighing_transactions
            SET batch_number = $batchNumber
            WHERE id = $id
            ";
            cmd.Parameters.AddWithValue("$id", transactionId);
            cmd.Parameters.AddWithValue("$batchNumber", batchNumber);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<WeighingTransaction?> GetTransactionByIdAsync(string transactionId)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT id, batch_id, batch_number, material_code, material_name, sequence,
                   target_weight, lower_weight, upper_weight, actual_weight, deviation,
                   status, weighed_by, weighed_date, printed_date, customer_name, manufacturing_date,
                   expiry_date, equipment_id, location, remarks, tare_weight, COALESCE(is_synced, 0) as is_synced, server_response
            FROM weighing_transactions
            WHERE id = $id
            ";
            cmd.Parameters.AddWithValue("$id", transactionId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return ReadTransaction(reader);
            return null;
        }

        public async Task<List<WeighingTransaction>> GetTransactionsByBatchNumberAsync(string batchNumber)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT id, batch_id, batch_number, material_code, material_name, sequence,
                   target_weight, lower_weight, upper_weight, actual_weight, deviation,
                   status, weighed_by, weighed_date, printed_date, customer_name, manufacturing_date,
                   expiry_date, equipment_id, location, remarks, tare_weight, COALESCE(is_synced, 0) as is_synced, server_response
            FROM weighing_transactions
            WHERE batch_number LIKE $prefix
            ORDER BY sequence
            ";
            cmd.Parameters.AddWithValue("$prefix", batchNumber.Trim() + "%");

            var transactions = new List<WeighingTransaction>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                transactions.Add(ReadTransaction(reader));
            return transactions;
        }

        private WeighingTransaction ReadTransaction(SqliteDataReader reader)
        {
            var categoryOrdinal = TryGetColumnOrdinal(reader, "category_name");
            var variantOrdinal = TryGetColumnOrdinal(reader, "variant_name");
            var serverResponseOrdinal = TryGetColumnOrdinal(reader, "server_response");
            return new WeighingTransaction
            {
                Id = reader.GetString(0),
                BatchId = reader.GetInt32(1),
                BatchNumber = reader.GetString(2),
                MaterialCode = reader.GetString(3),
                MaterialName = reader.GetString(4),
                Sequence = reader.GetInt32(5),
                TargetWeight = reader.GetDouble(6),
                LowerWeight = reader.GetDouble(7),
                UpperWeight = reader.GetDouble(8),
                ActualWeight = reader.GetDouble(9),
                Deviation = reader.GetDouble(10),
                Status = reader.GetString(11),
                WeighedBy = reader.GetString(12),
                WeighedDate = reader.GetString(13),
                WeighedDateValue = DateTime.TryParse(reader.GetString(13), out var wdv) ? wdv : DateTime.MinValue,
                PrintedDate = reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                PrintedDateValue = reader.IsDBNull(14) ? null : (DateTime?)DateTime.Parse(reader.GetString(14)),
                CustomerName = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                ManufacturingDate = reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
                ExpiryDate = reader.IsDBNull(17) ? string.Empty : reader.GetString(17),
                EquipmentId = reader.IsDBNull(18) ? string.Empty : reader.GetString(18),
                Location = reader.IsDBNull(19) ? string.Empty : reader.GetString(19),
                Remarks = reader.IsDBNull(20) ? string.Empty : reader.GetString(20),
                TareWeight = reader.IsDBNull(21) ? 0.0 : reader.GetDouble(21),
                IsSynced = !reader.IsDBNull(22) && reader.GetInt32(22) == 1,
                ServerResponse = serverResponseOrdinal >= 0 && !reader.IsDBNull(serverResponseOrdinal) ? reader.GetString(serverResponseOrdinal) : null,
                Category = categoryOrdinal >= 0 && !reader.IsDBNull(categoryOrdinal) ? reader.GetString(categoryOrdinal) : string.Empty,
                Variant = variantOrdinal >= 0 && !reader.IsDBNull(variantOrdinal) ? reader.GetString(variantOrdinal) : string.Empty
            };
        }

        private static int TryGetColumnOrdinal(SqliteDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        public async Task UpdateTransactionSyncStatusAsync(string transactionId, bool isSynced)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            UPDATE weighing_transactions
            SET is_synced = $isSynced
            WHERE id = $id
            ";
            cmd.Parameters.AddWithValue("$id", transactionId);
            cmd.Parameters.AddWithValue("$isSynced", isSynced ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateTransactionServerResponseAsync(string transactionId, string? responseJson)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            UPDATE weighing_transactions
            SET server_response = $response
            WHERE id = $id
            ";
            cmd.Parameters.AddWithValue("$id", transactionId);
            cmd.Parameters.AddWithValue("$response", string.IsNullOrEmpty(responseJson) ? DBNull.Value : (object)responseJson);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<WeighingTransaction>> GetRecentTransactionsAsync(int limit = 100)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
            SELECT wt.id, wt.batch_id, wt.batch_number, wt.material_code, wt.material_name, wt.sequence,
                   wt.target_weight, wt.lower_weight, wt.upper_weight, wt.actual_weight, wt.deviation,
                   wt.status, wt.weighed_by, wt.weighed_date, wt.printed_date, wt.customer_name, wt.manufacturing_date,
                   wt.expiry_date, wt.equipment_id, wt.location, wt.remarks,                    wt.tare_weight,
                   COALESCE(wt.is_synced, 0) as is_synced,
                   wt.server_response,
                   COALESCE(c.name, '') as category_name, COALESCE(v.name, '') as variant_name
            FROM weighing_transactions wt
            LEFT JOIN materials m ON m.product_code = wt.material_code COLLATE NOCASE
            LEFT JOIN categories c ON m.category_id = c.id
            LEFT JOIN variants v ON m.variant_id = v.id
            ORDER BY wt.id DESC
            LIMIT $limit
            ";
            cmd.Parameters.AddWithValue("$limit", limit);

            var transactions = new List<WeighingTransaction>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                transactions.Add(ReadTransaction(reader));
            }
            return transactions;
        }

        /// <summary>
        /// Returns last N transactions for current local date only, ordered ascending (oldest first).
        /// Uses weighed_date TEXT "yyyy-MM-dd HH:mm:ss" filtered by date('now','localtime').
        /// </summary>
        public async Task<List<WeighingTransaction>> GetRecentTransactionsForTodayAsync(int limit = 6)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            // Inner query gets last N of today DESC, outer re-orders ASC
            cmd.CommandText = @"
            SELECT id, batch_id, batch_number, material_code, material_name, sequence,
                    target_weight, lower_weight, upper_weight, actual_weight, deviation,
                    status, weighed_by, weighed_date, printed_date, customer_name, manufacturing_date,
                    expiry_date, equipment_id, location, remarks, tare_weight, is_synced, server_response,
                    category_name, variant_name
            FROM (
                SELECT wt.id, wt.batch_id, wt.batch_number, wt.material_code, wt.material_name, wt.sequence,
                        wt.target_weight, wt.lower_weight, wt.upper_weight, wt.actual_weight, wt.deviation,
                        wt.status, wt.weighed_by, wt.weighed_date, wt.printed_date, wt.customer_name, wt.manufacturing_date,
                        wt.expiry_date, wt.equipment_id, wt.location, wt.remarks, wt.tare_weight,
                        COALESCE(wt.is_synced, 0) as is_synced,
                        wt.server_response,
                        COALESCE(c.name, '') as category_name, COALESCE(v.name, '') as variant_name
                 FROM weighing_transactions wt
                 LEFT JOIN materials m ON m.product_code = wt.material_code COLLATE NOCASE
                LEFT JOIN categories c ON m.category_id = c.id
                LEFT JOIN variants v ON m.variant_id = v.id
                WHERE date(REPLACE(wt.weighed_date, '.', ':')) = date('now','localtime')
                ORDER BY wt.id DESC
                LIMIT $limit
            )
            ORDER BY id ASC
            ";
            cmd.Parameters.AddWithValue("$limit", limit);

            var transactions = new List<WeighingTransaction>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                transactions.Add(ReadTransaction(reader));
            }
            return transactions;
        }

        public async Task<List<WeighingTransaction>> GetTransactionsByFilterAsync(DateTime? fromDate, DateTime? toDate, bool? isSynced, int limit = 0)
        {
            var (items, _) = await GetTransactionsPagedAsync(fromDate, toDate, isSynced, null, null, limit > 0 ? limit : 0, 0);
            return items;
        }

        public async Task<(List<WeighingTransaction> Items, int TotalCount)> GetTransactionsPagedAsync(DateTime? fromDate, DateTime? toDate, bool? isSynced, string? status, string? search, int limit, int offset)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var whereClauses = new List<string>();
            var countCmd = conn.CreateCommand();
            var dataCmd = conn.CreateCommand();

            // Build shared WHERE clauses with separate parameter sets for count and data
            var parameters = new List<(string Name, object Value)>();

            if (fromDate.HasValue)
            {
                var from = fromDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 00:00:00";
                whereClauses.Add("REPLACE(wt.weighed_date, '.', ':') >= $fromDate");
                parameters.Add(("$fromDate", from));
            }

            if (toDate.HasValue)
            {
                var to = toDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 23:59:59";
                whereClauses.Add("REPLACE(wt.weighed_date, '.', ':') <= $toDate");
                parameters.Add(("$toDate", to));
            }

            if (isSynced.HasValue)
            {
                whereClauses.Add("COALESCE(wt.is_synced, 0) = $isSynced");
                parameters.Add(("$isSynced", isSynced.Value ? 1 : 0));
            }

            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                whereClauses.Add("wt.status = $status");
                parameters.Add(("$status", status!.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = $"%{search.Trim()}%";
                whereClauses.Add("(wt.batch_number LIKE $search COLLATE NOCASE OR wt.material_code LIKE $search COLLATE NOCASE OR wt.material_name LIKE $search COLLATE NOCASE OR wt.weighed_by LIKE $search COLLATE NOCASE)");
                parameters.Add(("$search", term));
            }

            var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

            // Total count query (uses same WHERE, no JOIN for speed when possible, but keep JOIN if search needs it)
            countCmd.CommandText = $"SELECT COUNT(*) FROM weighing_transactions wt {whereClause}";
            foreach (var (n, v) in parameters) countCmd.Parameters.AddWithValue(n, v);
            var totalObj = await countCmd.ExecuteScalarAsync();
            var totalCount = totalObj != null && totalObj != DBNull.Value ? Convert.ToInt32(totalObj) : 0;

            if (totalCount == 0) return (new List<WeighingTransaction>(), 0);

            var limitClause = limit > 0 ? "LIMIT $limit OFFSET $offset" : "";
            dataCmd.CommandText = $@"
            SELECT wt.id, wt.batch_id, wt.batch_number, wt.material_code, wt.material_name, wt.sequence,
                   wt.target_weight, wt.lower_weight, wt.upper_weight, wt.actual_weight, wt.deviation,
                   wt.status, wt.weighed_by, wt.weighed_date, wt.printed_date, wt.customer_name, wt.manufacturing_date,
                   wt.expiry_date, wt.equipment_id, wt.location, wt.remarks, wt.tare_weight,
                   COALESCE(wt.is_synced, 0) as is_synced,
                   wt.server_response,
                   COALESCE(c.name, '') as category_name, COALESCE(v.name, '') as variant_name
            FROM weighing_transactions wt
            LEFT JOIN materials m ON m.product_code = wt.material_code COLLATE NOCASE
            LEFT JOIN categories c ON m.category_id = c.id
            LEFT JOIN variants v ON m.variant_id = v.id
            {whereClause}
            ORDER BY wt.id DESC
            {limitClause}
            ";
            foreach (var (n, v) in parameters) dataCmd.Parameters.AddWithValue(n, v);
            if (limit > 0)
            {
                dataCmd.Parameters.AddWithValue("$limit", limit);
                dataCmd.Parameters.AddWithValue("$offset", offset);
            }

            var transactions = new List<WeighingTransaction>();
            using var reader = await dataCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                transactions.Add(ReadTransaction(reader));
            }
            return (transactions, totalCount);
        }

        public async Task<DashboardAggregates> GetDashboardAggregatesAsync(DateTime? fromDate, DateTime? toDate, string? status)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var whereClauses = new List<string>();
            var parameters = new List<(string Name, object Value)>();

            if (fromDate.HasValue)
            {
                var from = fromDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 00:00:00";
                whereClauses.Add("REPLACE(weighed_date, '.', ':') >= $fromDate");
                parameters.Add(("$fromDate", from));
            }
            if (toDate.HasValue)
            {
                var to = toDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 23:59:59";
                whereClauses.Add("REPLACE(weighed_date, '.', ':') <= $toDate");
                parameters.Add(("$toDate", to));
            }
            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                whereClauses.Add("status = $status");
                parameters.Add(("$status", status!.Trim()));
            }

            var whereClause = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

            // Single query for summary cards - 5K/day = 150K/month, aggregates in SQL avoid loading all rows
            var summaryCmd = conn.CreateCommand();
            summaryCmd.CommandText = $@"
            SELECT 
                COUNT(*) as totalCount,
                COALESCE(SUM(actual_weight),0) as totalWeight,
                COUNT(DISTINCT material_code) as distinctMaterials,
                COALESCE(AVG(actual_weight),0) as avgWeight
            FROM weighing_transactions {whereClause}";
            foreach (var (n, v) in parameters) summaryCmd.Parameters.AddWithValue(n, v);

            var agg = new DashboardAggregates();
            using (var reader = await summaryCmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    agg.FilteredTransactions = reader.GetInt32(0);
                    agg.FilteredWeight = Math.Round(reader.GetDouble(1), 3);
                    agg.TotalMaterials = reader.GetInt32(2);
                    agg.AvgWeight = Math.Round(reader.GetDouble(3), 3);
                }
            }

            // Today aggregates - separate WHERE today
            var todayCmd = conn.CreateCommand();
            var todayWhere = whereClause;
            var todayParam = "date('now','localtime')";
            // Add today filter on weighed_date
            var todayFilter = "date(REPLACE(weighed_date, '.', ':')) = " + todayParam;
            todayWhere = string.IsNullOrEmpty(whereClause) ? $"WHERE {todayFilter}" : $"{whereClause} AND {todayFilter}";
            todayCmd.CommandText = $"SELECT COUNT(*), COALESCE(SUM(actual_weight),0) FROM weighing_transactions {todayWhere}";
            foreach (var (n, v) in parameters) todayCmd.Parameters.AddWithValue(n, v);
            using (var reader = await todayCmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    agg.TodayTransactions = reader.GetInt32(0);
                    agg.TodayWeight = Math.Round(reader.GetDouble(1), 3);
                }
            }

            // Daily trend - grouped by date in SQL (limit to 30 days for chart)
            var dailyCmd = conn.CreateCommand();
            dailyCmd.CommandText = $@"
            SELECT date(REPLACE(weighed_date, '.', ':')) as d, COUNT(*) as cnt, COALESCE(SUM(actual_weight),0) as w
            FROM weighing_transactions {whereClause}
            GROUP BY date(REPLACE(weighed_date, '.', ':'))
            ORDER BY d ASC
            LIMIT 30";
            foreach (var (n, v) in parameters) dailyCmd.Parameters.AddWithValue(n, v);
            using (var reader = await dailyCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    agg.DailyTrend.Add(new DailyWeightPoint
                    {
                        Date = DateTime.TryParse(reader.GetString(0), out var d) ? d : DateTime.MinValue,
                        Count = reader.GetInt32(1),
                        Weight = Math.Round(reader.GetDouble(2), 2)
                    });
                }
            }

            // Top materials by weight in SQL - top 15 + others
            var matCmd = conn.CreateCommand();
            matCmd.CommandText = $@"
            SELECT COALESCE(NULLIF(material_name,''), material_code) as name, COALESCE(SUM(actual_weight),0) as w
            FROM weighing_transactions {whereClause}
            GROUP BY COALESCE(NULLIF(material_name,''), material_code)
            ORDER BY w DESC
            LIMIT 15";
            foreach (var (n, v) in parameters) matCmd.Parameters.AddWithValue(n, v);
            using (var reader = await matCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    agg.TopMaterials.Add(new MaterialWeightPoint { Name = reader.GetString(0), Weight = Math.Round(reader.GetDouble(1), 2) });
                }
            }

            return agg;
        }

        public async Task<(int Total, int Synced, int Unsynced)> GetSyncCountsAsync()
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT 
                COUNT(*) as total,
                SUM(CASE WHEN COALESCE(is_synced, 0) = 1 THEN 1 ELSE 0 END) as synced,
                SUM(CASE WHEN COALESCE(is_synced, 0) = 0 THEN 1 ELSE 0 END) as unsynced
            FROM weighing_transactions
            ";

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
            }
            return (0, 0, 0);
        }

        public class DashboardAggregates
        {
            public int TodayTransactions { get; set; }
            public double TodayWeight { get; set; }
            public int FilteredTransactions { get; set; }
            public double FilteredWeight { get; set; }
            public int TotalMaterials { get; set; }
            public double AvgWeight { get; set; }
            public List<DailyWeightPoint> DailyTrend { get; set; } = new();
            public List<MaterialWeightPoint> TopMaterials { get; set; } = new();
        }

        public class DailyWeightPoint
        {
            public DateTime Date { get; set; }
            public int Count { get; set; }
            public double Weight { get; set; }
        }

        public class MaterialWeightPoint
        {
            public string Name { get; set; } = string.Empty;
            public double Weight { get; set; }
        }

        public async Task<int> SaveReprintRecordAsync(ReprintRecord record)
        {
            EntityTextNormalizer.Trim(record);
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO reprint_records (transaction_id, batch_number, material_code, reason, reprinted_by, reprinted_at)
            VALUES ($transactionId, $batchNumber, $materialCode, $reason, $reprintedBy, $reprintedAt);
            SELECT last_insert_rowid();
            ";
            cmd.Parameters.AddWithValue("$transactionId", record.TransactionId);
            cmd.Parameters.AddWithValue("$batchNumber", record.BatchNumber ?? string.Empty);
            cmd.Parameters.AddWithValue("$materialCode", record.MaterialCode ?? string.Empty);
            cmd.Parameters.AddWithValue("$reason", record.Reason);
            cmd.Parameters.AddWithValue("$reprintedBy", record.ReprintedBy);
            cmd.Parameters.AddWithValue("$reprintedAt", record.ReprintedAt);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task DeleteTransactionAsync(string transactionId)
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM weighing_transactions WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", transactionId?.Trim() ?? string.Empty);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> DeleteAllUnsyncedAsync()
        {
            await EnsureInitializedAsync();
            using var conn = GetConnection();
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM weighing_transactions WHERE COALESCE(is_synced,0) = 0";
            return await cmd.ExecuteNonQueryAsync();
        }
    }
}
