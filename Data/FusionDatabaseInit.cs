// FUSION-PLAN.md Step 2 — isolated Fusion transaction schema.
// Same DB file as Standard; shared config/users/masters untouched.
// fusion_* tables have NO FK to weighing_* and are never touched by Standard code.
//
// Coconut (Fusion Link v3) extension: fusion_suppliers + fusion_plan_lots,
// RFID/quality/batch-target columns on tubs/inward/batches, idempotent
// migrations via PRAGMA table_info. Fresh databases start empty (settings
// defaults only); no sample suppliers, tubs or inward rows are seeded.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace ProWeighSuitePlatforms.Data
{
    public sealed class FusionDatabaseInit
    {
        private readonly string _dbPath;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _initialized;

        public FusionDatabaseInit(string dbPath) => _dbPath = dbPath;

        public async Task EnsureCreatedAsync()
        {
            if (_initialized) return;
            await _gate.WaitAsync();
            try
            {
                if (_initialized) return;
                await CreateTablesAsync();
                await MigrateAndSeedAsync();
                _initialized = true;
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task CreateTablesAsync()
        {
            var dir = System.IO.Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            using var conn = new SqliteConnection($"Data Source={_dbPath};Cache=Shared");
            await conn.OpenAsync();
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            await pragma.ExecuteNonQueryAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS fusion_tubs (
                tub_id TEXT PRIMARY KEY,
                capacity_kg REAL NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT 'Empty',
                bay_id TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS fusion_batches (
                batch_no TEXT PRIMARY KEY,
                target_kg REAL NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT 'Open',
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
            );
            CREATE TABLE IF NOT EXISTS fusion_batch_lots (
                lot_no TEXT PRIMARY KEY,
                batch_no TEXT NOT NULL REFERENCES fusion_batches(batch_no) ON DELETE CASCADE,
                tub_id TEXT NOT NULL DEFAULT '' REFERENCES fusion_tubs(tub_id) ON DELETE RESTRICT,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
            );
            CREATE TABLE IF NOT EXISTS fusion_transactions (
                id TEXT PRIMARY KEY,
                batch_no TEXT NOT NULL DEFAULT '' REFERENCES fusion_batches(batch_no) ON DELETE RESTRICT,
                tub_id TEXT NOT NULL DEFAULT '' REFERENCES fusion_tubs(tub_id) ON DELETE RESTRICT,
                supplier_id INTEGER NOT NULL DEFAULT 0,
                weight_kg REAL NOT NULL DEFAULT 0,
                recorded_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                is_synced INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS fusion_tub_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tub_id TEXT NOT NULL DEFAULT '' REFERENCES fusion_tubs(tub_id) ON DELETE CASCADE,
                event_type TEXT NOT NULL DEFAULT '',
                weight_kg REAL NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
            );
            CREATE TABLE IF NOT EXISTS fusion_bay_inventory (
                tub_id TEXT PRIMARY KEY REFERENCES fusion_tubs(tub_id) ON DELETE CASCADE,
                bay_id TEXT NOT NULL DEFAULT '',
                weight_kg REAL NOT NULL DEFAULT 0,
                updated_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
            );
            CREATE TABLE IF NOT EXISTS fusion_inward (
                grn_no TEXT PRIMARY KEY,
                supplier_id INTEGER NOT NULL DEFAULT 0,
                weight_kg REAL NOT NULL DEFAULT 0,
                received_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
            );
            CREATE TABLE IF NOT EXISTS fusion_equipment (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT 'Online',
                last_seen TEXT NOT NULL DEFAULT (datetime('now','localtime'))
            );
            CREATE TABLE IF NOT EXISTS fusion_tickets (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT 'Open',
                created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
            );
            CREATE INDEX IF NOT EXISTS idx_fusion_txn_batch ON fusion_transactions(batch_no);
            CREATE INDEX IF NOT EXISTS idx_fusion_txn_tub ON fusion_transactions(tub_id);
            CREATE INDEX IF NOT EXISTS idx_fusion_tub_bay ON fusion_tubs(bay_id);
            CREATE INDEX IF NOT EXISTS idx_fusion_events_tub ON fusion_tub_events(tub_id);
            CREATE INDEX IF NOT EXISTS idx_fusion_lots_batch ON fusion_batch_lots(batch_no);
            CREATE INDEX IF NOT EXISTS idx_fusion_inward_supplier ON fusion_inward(supplier_id);
            CREATE INDEX IF NOT EXISTS idx_fusion_bay_inventory_bay ON fusion_bay_inventory(bay_id);";
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection conn, string table)
        {
            var cols = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table})";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                cols.Add(reader.GetString(1));
            return cols;
        }

        private static async Task AddColumnAsync(SqliteConnection conn, string table, HashSet<string> existing, string column, string ddl)
        {
            if (existing.Contains(column)) return;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {ddl}";
            await cmd.ExecuteNonQueryAsync();
            existing.Add(column);
        }

        private static async Task DropColumnIfExistsAsync(SqliteConnection conn, string table, HashSet<string> existing, string column)
        {
            if (!existing.Contains(column)) return;
            using var cmd = conn.CreateCommand();
            // SQLite 3.35+ supports DROP COLUMN; fallback to no-op on older engines.
            cmd.CommandText = $"ALTER TABLE {table} DROP COLUMN {column}";
            try { await cmd.ExecuteNonQueryAsync(); }
            catch { /* ignore: older SQLite or in-use */ }
            existing.Remove(column);
        }

        private async Task MigrateAndSeedAsync()
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath};Cache=Shared");
            await conn.OpenAsync();
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = OFF; PRAGMA busy_timeout=5000;";
            await pragma.ExecuteNonQueryAsync();

            // New tables (coconut supplier master + FEFO plan lots).
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS fusion_suppliers (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL DEFAULT '',
                    location TEXT NOT NULL DEFAULT '',
                    phone TEXT NOT NULL DEFAULT '',
                    is_active INTEGER NOT NULL DEFAULT 1
                );
                CREATE TABLE IF NOT EXISTS fusion_plan_lots (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    batch_no TEXT NOT NULL DEFAULT '',
                    lot_no TEXT NOT NULL DEFAULT '',
                    supplier_name TEXT NOT NULL DEFAULT '',
                    grn_no TEXT NOT NULL DEFAULT '',
                    bay_id TEXT NOT NULL DEFAULT '',
                    best_before TEXT NOT NULL DEFAULT '',
                    days_left INTEGER NOT NULL DEFAULT 0,
                    tubs INTEGER NOT NULL DEFAULT 0,
                    nuts INTEGER NOT NULL DEFAULT 0,
                    weight_kg REAL NOT NULL DEFAULT 0,
                    colour TEXT NOT NULL DEFAULT '',
                    tubs_in_pool INTEGER NOT NULL DEFAULT 0,
                    cycles_per_tub REAL NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_fusion_plan_lots_batch ON fusion_plan_lots(batch_no);
                CREATE TABLE IF NOT EXISTS fusion_settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL DEFAULT ''
                );
                CREATE TABLE IF NOT EXISTS fusion_bays (
                    bay_id TEXT PRIMARY KEY,
                    max_capacity_nuts REAL NOT NULL DEFAULT 0,
                    is_active INTEGER NOT NULL DEFAULT 1
                );";
                await cmd.ExecuteNonQueryAsync();
            }

            // Remove shelf_life_days from fusion_suppliers (per requirement: no shelf on supplier).
            var supCols = await GetColumnsAsync(conn, "fusion_suppliers");
            await DropColumnIfExistsAsync(conn, "fusion_suppliers", supCols, "shelf_life_days");

            // Extended columns on existing tables.
            var tubs = await GetColumnsAsync(conn, "fusion_tubs");
            await AddColumnAsync(conn, "fusion_tubs", tubs, "epc", "epc TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_tubs", tubs, "colour", "colour TEXT NOT NULL DEFAULT 'Orange'");
            await AddColumnAsync(conn, "fusion_tubs", tubs, "used_cycles", "used_cycles INTEGER NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_tubs", tubs, "is_blocked", "is_blocked INTEGER NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_tubs", tubs, "ageing_date", "ageing_date TEXT NOT NULL DEFAULT ''");

            var inward = await GetColumnsAsync(conn, "fusion_inward");
            await AddColumnAsync(conn, "fusion_inward", inward, "supplier_name", "supplier_name TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_inward", inward, "ticket_no", "ticket_no TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_inward", inward, "inward_date", "inward_date TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_inward", inward, "bay_id", "bay_id TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_inward", inward, "material_name", "material_name TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_inward", inward, "shelf_life_days", "shelf_life_days INTEGER NOT NULL DEFAULT 10");
            await AddColumnAsync(conn, "fusion_inward", inward, "coconut_count", "coconut_count REAL NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_inward", inward, "sample_kg", "sample_kg REAL NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_inward", inward, "spoil_kg", "spoil_kg REAL NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_inward", inward, "water_loss_kg", "water_loss_kg REAL NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_inward", inward, "broken_kg", "broken_kg REAL NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_inward", inward, "rejection_kg", "rejection_kg REAL NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_inward", inward, "consumed_kg", "consumed_kg REAL NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_inward", inward, "consumed_nuts", "consumed_nuts REAL NOT NULL DEFAULT 0");

            // Weighbridge ticket numbers (e.g. WB_001) must be unique across inward
            // stock; supplier stock is derived from the same rows. Blank legacy
            // tickets stay exempt so old rows never block startup.
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ux_fusion_inward_ticket ON fusion_inward(TRIM(ticket_no) COLLATE NOCASE) WHERE TRIM(ticket_no) <> '';";
                try { await cmd.ExecuteNonQueryAsync(); }
                catch { /* legacy duplicate tickets: app-level check still guards new entries */ }
            }

            var batches = await GetColumnsAsync(conn, "fusion_batches");
            await AddColumnAsync(conn, "fusion_batches", batches, "batch_date", "batch_date TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_batches", batches, "supplier_mode", "supplier_mode TEXT NOT NULL DEFAULT 'auto'");
            await AddColumnAsync(conn, "fusion_batches", batches, "supplier_name", "supplier_name TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_batches", batches, "tub_mode", "tub_mode TEXT NOT NULL DEFAULT 'colour'");
            await AddColumnAsync(conn, "fusion_batches", batches, "lot_mode", "lot_mode TEXT NOT NULL DEFAULT 'all'");
            await AddColumnAsync(conn, "fusion_batches", batches, "target_apw_kg", "target_apw_kg REAL NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_batches", batches, "nuts_per_tub", "nuts_per_tub INTEGER NOT NULL DEFAULT 50");
            await AddColumnAsync(conn, "fusion_batches", batches, "target_hours", "target_hours REAL NOT NULL DEFAULT 16");
            await AddColumnAsync(conn, "fusion_batches", batches, "tub_loop_min", "tub_loop_min REAL NOT NULL DEFAULT 10");

            var bays = await GetColumnsAsync(conn, "fusion_bays");
            await AddColumnAsync(conn, "fusion_bays", bays, "is_active", "is_active INTEGER NOT NULL DEFAULT 1");

            // Live batch run state (running/paused/completed + turn-around tracking).
            var runBatches = await GetColumnsAsync(conn, "fusion_batches");
            await AddColumnAsync(conn, "fusion_batches", runBatches, "started_at", "started_at TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_batches", runBatches, "paused_ms", "paused_ms INTEGER NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_batches", runBatches, "pause_started_at", "pause_started_at TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_batches", runBatches, "pause_count", "pause_count INTEGER NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_batches", runBatches, "completed_at", "completed_at TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_batches", runBatches, "operator_name", "operator_name TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_batches", runBatches, "target_tubs", "target_tubs INTEGER NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_batches", runBatches, "target_nuts", "target_nuts INTEGER NOT NULL DEFAULT 0");

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS fusion_batch_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    batch_no TEXT NOT NULL DEFAULT '',
                    event TEXT NOT NULL DEFAULT '',
                    at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                );
                CREATE INDEX IF NOT EXISTS idx_fusion_batch_events_batch ON fusion_batch_events(batch_no);";
                await cmd.ExecuteNonQueryAsync();
            }

            // Per-lot run progress (done counts, pause/complete flags).
            var runLots = await GetColumnsAsync(conn, "fusion_plan_lots");
            await AddColumnAsync(conn, "fusion_plan_lots", runLots, "done_tubs", "done_tubs INTEGER NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_plan_lots", runLots, "done_kg", "done_kg REAL NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_plan_lots", runLots, "is_paused", "is_paused INTEGER NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_plan_lots", runLots, "is_completed", "is_completed INTEGER NOT NULL DEFAULT 0");

            // Enriched weigh transactions for the live view ledger.
            var runTx = await GetColumnsAsync(conn, "fusion_transactions");
            await AddColumnAsync(conn, "fusion_transactions", runTx, "epc", "epc TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_transactions", runTx, "tub_cycles", "tub_cycles INTEGER NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_transactions", runTx, "supplier_name", "supplier_name TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_transactions", runTx, "grn_no", "grn_no TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_transactions", runTx, "bay_id", "bay_id TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_transactions", runTx, "lot_no", "lot_no TEXT NOT NULL DEFAULT ''");
            await AddColumnAsync(conn, "fusion_transactions", runTx, "nuts", "nuts INTEGER NOT NULL DEFAULT 0");
            await AddColumnAsync(conn, "fusion_transactions", runTx, "operator_name", "operator_name TEXT NOT NULL DEFAULT ''");

            // Persisted tub assignment (batch_no) so a restart never strands tubs.
            var runTubs = await GetColumnsAsync(conn, "fusion_tubs");
            await AddColumnAsync(conn, "fusion_tubs", runTubs, "assigned_to", "assigned_to TEXT NOT NULL DEFAULT ''");

            // Mirror fusion_suppliers into the shared suppliers table and backfill
            // supplier_id on inward/transactions (shared integer PK, per
            // FUSION-PLAN). Idempotent: only fills rows where supplier_id is 0.
            await BackfillSharedSupplierIdsAsync(conn);

            // EPC IDs are RFID identity and must be unique. Blank legacy EPCs stay exempt
            // so old rows never block startup; the app still guards new entries.
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ux_fusion_tubs_epc ON fusion_tubs(epc) WHERE epc IS NOT NULL AND epc <> '';";
                try { await cmd.ExecuteNonQueryAsync(); }
                catch { /* legacy duplicate EPCs: app-level check still guards new entries */ }
            }

            await SeedAsync(conn);
        }

        private static async Task SeedAsync(SqliteConnection conn)
        {
            // Fresh databases start empty: no sample suppliers, tubs or inward rows.
            // Only Fusion settings defaults are seeded (configuration, not data).
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT OR IGNORE INTO fusion_settings (key, value) VALUES ('near_expiry_days', '7'), ('expired_below_days', '0')";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        /// <summary>
        /// supplier_id in fusion_inward / fusion_transactions references the shared
        /// suppliers table (integer PK). Fusion suppliers live in fusion_suppliers
        /// (text ids) and are referenced by NAME elsewhere, so mirror them into the
        /// shared table and backfill the ids. Idempotent — runs on every startup
        /// but only fills rows whose supplier_id is still 0.
        /// </summary>
        private static async Task BackfillSharedSupplierIdsAsync(SqliteConnection conn)
        {
            try
            {
                using (var hasSuppliers = conn.CreateCommand())
                {
                    hasSuppliers.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='suppliers'";
                    var exists = Convert.ToInt32(await hasSuppliers.ExecuteScalarAsync() ?? 0) > 0;
                    if (!exists) return; // shared table not created yet — nothing to mirror into
                }

                using (var mirror = conn.CreateCommand())
                {
                    mirror.CommandText = @"
                    INSERT INTO suppliers (supplier_code, supplier_name, display_name, supplier_type, business_category, status, tax_id, contact_person, phone, email, created_at)
                    SELECT fs.id, fs.name, fs.name, '', '', 1, '', '', '', '', datetime('now','localtime')
                    FROM fusion_suppliers fs
                    WHERE NOT EXISTS (SELECT 1 FROM suppliers sh WHERE LOWER(TRIM(sh.supplier_code)) = LOWER(fs.id))
                      AND NOT EXISTS (SELECT 1 FROM suppliers sh2 WHERE LOWER(TRIM(sh2.supplier_name)) = LOWER(TRIM(fs.name)));";
                    await mirror.ExecuteNonQueryAsync();
                }

                using (var inward = conn.CreateCommand())
                {
                    inward.CommandText = @"
                    UPDATE fusion_inward SET supplier_id =
                        (SELECT sh.id FROM suppliers sh
                         WHERE LOWER(TRIM(sh.supplier_name)) = LOWER(TRIM(fusion_inward.supplier_name))
                            OR LOWER(TRIM(sh.display_name)) = LOWER(TRIM(fusion_inward.supplier_name))
                         ORDER BY sh.id LIMIT 1)
                    WHERE (supplier_id = 0 OR supplier_id IS NULL) AND TRIM(supplier_name) <> ''
                      AND EXISTS (SELECT 1 FROM suppliers sh2
                                  WHERE LOWER(TRIM(sh2.supplier_name)) = LOWER(TRIM(fusion_inward.supplier_name))
                                     OR LOWER(TRIM(sh2.display_name)) = LOWER(TRIM(fusion_inward.supplier_name)));";
                    await inward.ExecuteNonQueryAsync();
                }

                using (var txns = conn.CreateCommand())
                {
                    txns.CommandText = @"
                    UPDATE fusion_transactions SET supplier_id =
                        (SELECT sh.id FROM suppliers sh
                         WHERE LOWER(TRIM(sh.supplier_name)) = LOWER(TRIM(fusion_transactions.supplier_name))
                            OR LOWER(TRIM(sh.display_name)) = LOWER(TRIM(fusion_transactions.supplier_name))
                         ORDER BY sh.id LIMIT 1)
                    WHERE (supplier_id = 0 OR supplier_id IS NULL) AND TRIM(supplier_name) <> ''
                      AND EXISTS (SELECT 1 FROM suppliers sh2
                                  WHERE LOWER(TRIM(sh2.supplier_name)) = LOWER(TRIM(fusion_transactions.supplier_name))
                                     OR LOWER(TRIM(sh2.display_name)) = LOWER(TRIM(fusion_transactions.supplier_name)));";
                    await txns.ExecuteNonQueryAsync();
                }
            }
            catch
            {
                // Best-effort: shared table schema differences must never block
                // Fusion startup; weigh-time resolution still fills new rows.
            }
        }
    }
}
