using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace ProWeighSuitePlatforms.Data
{
    public class DatabaseMigration
    {
        private readonly string _dbPath;

        public DatabaseMigration(string dbPath)
        {
            _dbPath = dbPath;
        }

        private SqliteConnection GetConnection()
            => new SqliteConnection($"Data Source={_dbPath}");

        /// <summary>
        /// Ensures recipe columns on weighing_batches are nullable. If any of the recipe columns exist and are NOT NULL,
        /// this will recreate the table with those columns nullable and copy existing data over.
        /// </summary>
        public async Task EnsureRecipeColumnsNullableAsync()
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            try
            {
                if (!await AreRecipeColumnsNotNullableAsync(conn))
                {
                    // Nothing to do
                    System.Diagnostics.Debug.WriteLine("[DatabaseMigration] Recipe columns are already nullable or do not exist.");
                    return;
                }

                using var transaction = conn.BeginTransaction();

                // Create new table with recipe columns nullable
                var createCmd = conn.CreateCommand();
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
                    recipe_id INTEGER,
                    recipe_code TEXT,
                    recipe_name TEXT,
                    completed_date TEXT,
                    notes TEXT
                );
                ";
                await createCmd.ExecuteNonQueryAsync();

                // Copy data. Use NULL for recipe columns if they didn't exist previously.
                var copyCmd = conn.CreateCommand();
                copyCmd.CommandText = @"
                INSERT INTO weighing_batches_new (
                    id, batch_number, batch_size, total_batches, current_batch_number,
                    status, started_by, started_date, recipe_id, recipe_code, recipe_name, completed_date, notes)
                SELECT
                    id, batch_number, batch_size, total_batches, current_batch_number,
                    status, started_by, started_date,
                    -- Use existing columns if present, otherwise NULL
                    CASE WHEN EXISTS(SELECT 1 FROM pragma_table_info('weighing_batches') WHERE name='recipe_id') THEN recipe_id ELSE NULL END,
                    CASE WHEN EXISTS(SELECT 1 FROM pragma_table_info('weighing_batches') WHERE name='recipe_code') THEN recipe_code ELSE NULL END,
                    CASE WHEN EXISTS(SELECT 1 FROM pragma_table_info('weighing_batches') WHERE name='recipe_name') THEN recipe_name ELSE NULL END,
                    completed_date, notes
                FROM weighing_batches;
                ";

                await copyCmd.ExecuteNonQueryAsync();

                // Drop old table and rename new table
                var dropCmd = conn.CreateCommand();
                dropCmd.CommandText = "DROP TABLE IF EXISTS weighing_batches;";
                await dropCmd.ExecuteNonQueryAsync();

                var renameCmd = conn.CreateCommand();
                renameCmd.CommandText = "ALTER TABLE weighing_batches_new RENAME TO weighing_batches;";
                await renameCmd.ExecuteNonQueryAsync();

                // Recreate index
                var indexCmd = conn.CreateCommand();
                indexCmd.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_weighing_batches_batch_number ON weighing_batches(batch_number);
                CREATE INDEX IF NOT EXISTS idx_weighing_batches_date ON weighing_batches(started_date);
                ";
                await indexCmd.ExecuteNonQueryAsync();

                transaction.Commit();

                System.Diagnostics.Debug.WriteLine("[DatabaseMigration] Recreated weighing_batches with nullable recipe columns.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseMigration] Failed to ensure recipe columns nullable: {ex.Message}");
                throw;
            }
        }

        private async Task<bool> AreRecipeColumnsNotNullableAsync(SqliteConnection conn)
        {
            try
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(weighing_batches)";

                bool found = false;
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var name = reader.GetString(1);
                    var notnull = reader.GetInt32(3); // 0 = nullable, 1 = NOT NULL
                    if ((string.Equals(name, "recipe_id", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(name, "recipe_code", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(name, "recipe_name", StringComparison.OrdinalIgnoreCase)) && notnull == 1)
                    {
                        found = true;
                        break;
                    }
                }

                return found;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Migrates the database schema to remove recipe fields and update batch_number calculation
        /// (Original removal migration - kept for reference)
        /// </summary>
        public async Task MigrateToRemoveRecipeFieldsAsync()
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            try
            {
                // Start transaction
                using var transaction = conn.BeginTransaction();

                // 1. Create new weighing_batches table without recipe fields
                var createNewBatchesTable = conn.CreateCommand();
                createNewBatchesTable.CommandText = @"
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
                );
                ";
                await createNewBatchesTable.ExecuteNonQueryAsync();

                // 2. Copy data from old table to new table (excluding recipe fields)
                var copyBatchesData = conn.CreateCommand();
                copyBatchesData.CommandText = @"
                INSERT INTO weighing_batches_new 
                    (id, batch_number, batch_size, total_batches, current_batch_number, 
                     status, started_by, started_date, completed_date, notes)
                SELECT 
                    id, batch_number, batch_size, total_batches, current_batch_number,
                    status, started_by, started_date, completed_date, notes
                FROM weighing_batches;
                ";
                await copyBatchesData.ExecuteNonQueryAsync();

                // 3. Drop old table
                var dropOldBatchesTable = conn.CreateCommand();
                dropOldBatchesTable.CommandText = "DROP TABLE IF EXISTS weighing_batches;";
                await dropOldBatchesTable.ExecuteNonQueryAsync();

                // 4. Rename new table to original name
                var renameBatchesTable = conn.CreateCommand();
                renameBatchesTable.CommandText = "ALTER TABLE weighing_batches_new RENAME TO weighing_batches;";
                await renameBatchesTable.ExecuteNonQueryAsync();

                // 5. Create new weighing_transactions table without recipe fields
                var createNewTransactionsTable = conn.CreateCommand();
                createNewTransactionsTable.CommandText = @"
                CREATE TABLE IF NOT EXISTS weighing_transactions_new (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
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
                ";
                await createNewTransactionsTable.ExecuteNonQueryAsync();

                // 6. Update batch_number calculation and copy data
                // Format: {batch_number from weighing_batches}-{sequence}-{id}
                var updateAndCopyTransactions = conn.CreateCommand();
                updateAndCopyTransactions.CommandText = @"
                INSERT INTO weighing_transactions_new 
                    (id, batch_id, batch_number, material_code, material_name, sequence,
                     target_weight, lower_weight, upper_weight, actual_weight, deviation,
                     status, weighed_by, weighed_date, printed_date, customer_name,
                     manufacturing_date, expiry_date, equipment_id, location, remarks, tare_weight)
                SELECT 
                    t.id,
                    t.batch_id,
                    CASE 
                        WHEN b.batch_number IS NOT NULL 
                        THEN b.batch_number || '-' || t.sequence || '-' || t.id
                        ELSE t.batch_number
                    END as batch_number,
                    t.material_code,
                    t.material_name,
                    t.sequence,
                    t.target_weight,
                    t.lower_weight,
                    t.upper_weight,
                    t.actual_weight,
                    t.deviation,
                    t.status,
                    t.weighed_by,
                    t.weighed_date,
                    t.printed_date,
                    t.customer_name,
                    t.manufacturing_date,
                    t.expiry_date,
                    t.equipment_id,
                    t.location,
                    t.remarks,
                    t.tare_weight
                FROM weighing_transactions t
                LEFT JOIN weighing_batches b ON t.batch_id = b.id;
                ";
                await updateAndCopyTransactions.ExecuteNonQueryAsync();

                // 7. Drop old transactions table
                var dropOldTransactionsTable = conn.CreateCommand();
                dropOldTransactionsTable.CommandText = "DROP TABLE IF EXISTS weighing_transactions;";
                await dropOldTransactionsTable.ExecuteNonQueryAsync();

                // 8. Rename new transactions table
                var renameTransactionsTable = conn.CreateCommand();
                renameTransactionsTable.CommandText = "ALTER TABLE weighing_transactions_new RENAME TO weighing_transactions;";
                await renameTransactionsTable.ExecuteNonQueryAsync();

                // 9. Recreate indexes
                var createIndexes = conn.CreateCommand();
                createIndexes.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_weighing_batches_batch_number ON weighing_batches(batch_number);
                CREATE INDEX IF NOT EXISTS idx_weighing_batches_date ON weighing_batches(started_date);
                CREATE INDEX IF NOT EXISTS idx_weighing_transactions_batch_id ON weighing_transactions(batch_id);
                CREATE INDEX IF NOT EXISTS idx_weighing_transactions_batch_number ON weighing_transactions(batch_number);
                CREATE INDEX IF NOT EXISTS idx_weighing_transactions_date ON weighing_transactions(weighed_date);
                CREATE INDEX IF NOT EXISTS idx_weighing_transactions_material ON weighing_transactions(material_code);
                ";
                await createIndexes.ExecuteNonQueryAsync();

                // Commit transaction
                transaction.Commit();
                
                System.Diagnostics.Debug.WriteLine("[DatabaseMigration] Successfully migrated database schema (removed recipe fields)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseMigration] Migration failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Re-adds the recipe columns if they are missing (revert previous removal)
        /// </summary>
        public async Task MigrateToReAddRecipeFieldsAsync()
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            try
            {
                using var transaction = conn.BeginTransaction();

                // Add recipe fields to weighing_batches if missing
                await AddColumnIfNotExistsAsync(conn, "weighing_batches", "recipe_id", "INTEGER");
                await AddColumnIfNotExistsAsync(conn, "weighing_batches", "recipe_code", "TEXT");
                await AddColumnIfNotExistsAsync(conn, "weighing_batches", "recipe_name", "TEXT");

                // Add recipe fields to weighing_transactions if missing
                await AddColumnIfNotExistsAsync(conn, "weighing_transactions", "recipe_code", "TEXT");
                await AddColumnIfNotExistsAsync(conn, "weighing_transactions", "recipe_name", "TEXT");

                transaction.Commit();

                System.Diagnostics.Debug.WriteLine("[DatabaseMigration] Re-added recipe columns where missing");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseMigration] Re-add migration failed: {ex.Message}");
                throw;
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
                    if (reader.GetString(1).Equals(columnName, StringComparison.OrdinalIgnoreCase))
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
                    System.Diagnostics.Debug.WriteLine($"[DatabaseMigration] Added column {columnName} to {tableName}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseMigration] Error adding column {columnName} to {tableName}: {ex.Message}");
                // Swallow exception to allow other columns to be attempted
            }
        }

        /// <summary>
        /// Checks if migration is needed (i.e., recipe columns are missing and should be re-added)
        /// </summary>
        public async Task<bool> IsMigrationNeededAsync()
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            try
            {
                bool batchesNeed = await TableMissingAnyColumnsAsync(conn, "weighing_batches", new[] { "recipe_id", "recipe_code", "recipe_name" });
                bool transactionsNeed = await TableMissingAnyColumnsAsync(conn, "weighing_transactions", new[] { "recipe_code", "recipe_name" });
                return batchesNeed || transactionsNeed;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TableMissingAnyColumnsAsync(SqliteConnection conn, string tableName, string[] requiredColumns)
        {
            try
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info({tableName})";

                var existing = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    existing.Add(reader.GetString(1));
                }

                foreach (var col in requiredColumns)
                {
                    if (!existing.Contains(col))
                        return true; // Missing at least one required column
                }

                return false; // All required columns present
            }
            catch
            {
                // If table doesn't exist or other error, indicate migration is not needed here
                return false;
            }
        }
    }
}
