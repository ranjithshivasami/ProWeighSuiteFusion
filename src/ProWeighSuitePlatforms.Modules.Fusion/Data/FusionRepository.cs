// FUSION-PLAN.md Step 4 — CRUD + dashboard queries over fusion_* only.
// Same connection/pragma discipline as WeighingRepository/InventoryExtensionRepository.
// Never references weighing_* tables.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ProWeighSuitePlatforms.Modules.Fusion;

namespace ProWeighSuitePlatforms.Data.Fusion
{
    public sealed class FusionRepository
    {
        private readonly string _connectionString;

        public FusionRepository(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        private async Task<SqliteConnection> OpenAsync()
        {
            var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout=5000;";
            await pragma.ExecuteNonQueryAsync();
            return conn;
        }

        private static string GetString(SqliteDataReader r, string name)
        {
            var o = r.GetOrdinal(name);
            return r.IsDBNull(o) ? string.Empty : r.GetString(o);
        }

        private static double GetDouble(SqliteDataReader r, string name)
        {
            var o = r.GetOrdinal(name);
            return r.IsDBNull(o) ? 0 : r.GetDouble(o);
        }

        // ===== Tubs =====
        public async Task<List<FusionTub>> GetTubsAsync()
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT tub_id, capacity_kg, status, bay_id, epc, colour, used_cycles, is_blocked, ageing_date, assigned_to FROM fusion_tubs ORDER BY tub_id";
            var list = new List<FusionTub>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var blkOrd = reader.GetOrdinal("is_blocked");
                var cycOrd = reader.GetOrdinal("used_cycles");
                list.Add(new FusionTub
                {
                    TubId = GetString(reader, "tub_id"),
                    CapacityKg = GetDouble(reader, "capacity_kg"),
                    Status = GetString(reader, "status"),
                    BayId = GetString(reader, "bay_id"),
                    Epc = GetString(reader, "epc"),
                    Colour = GetString(reader, "colour"),
                    UsedCycles = reader.IsDBNull(cycOrd) ? 0 : reader.GetInt32(cycOrd),
                    IsBlocked = !reader.IsDBNull(blkOrd) && reader.GetInt32(blkOrd) != 0,
                    AgeingDate = GetString(reader, "ageing_date"),
                    AssignedTo = GetString(reader, "assigned_to"),
                });
            }
            return list;
        }

        public async Task UpsertTubAsync(string tubId, double capacityKg, string status, string bayId)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO fusion_tubs (tub_id, capacity_kg, status, bay_id)
            VALUES ($id, $cap, $status, $bay)
            ON CONFLICT(tub_id) DO UPDATE SET
                capacity_kg=excluded.capacity_kg, status=excluded.status, bay_id=excluded.bay_id;";
            cmd.Parameters.AddWithValue("$id", tubId);
            cmd.Parameters.AddWithValue("$cap", capacityKg);
            cmd.Parameters.AddWithValue("$status", status ?? "Empty");
            cmd.Parameters.AddWithValue("$bay", bayId ?? string.Empty);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteTubAsync(string tubId)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM fusion_tubs WHERE tub_id = $id";
            cmd.Parameters.AddWithValue("$id", tubId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task AddTubAsync(string tubId, string epc, string colour, double capacityKg, string bayId, string ageingDate)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO fusion_tubs (tub_id, capacity_kg, status, bay_id, epc, colour, used_cycles, is_blocked, ageing_date) VALUES ($id, $cap, 'Empty', $bay, $epc, $col, 0, 0, $age)";
            cmd.Parameters.AddWithValue("$id", tubId);
            cmd.Parameters.AddWithValue("$cap", capacityKg);
            cmd.Parameters.AddWithValue("$bay", bayId ?? string.Empty);
            cmd.Parameters.AddWithValue("$epc", epc ?? string.Empty);
            cmd.Parameters.AddWithValue("$col", string.IsNullOrWhiteSpace(colour) ? "Orange" : colour);
            cmd.Parameters.AddWithValue("$age", string.IsNullOrWhiteSpace(ageingDate) ? DateTime.Today.ToString("yyyy-MM-dd") : ageingDate);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateTubCyclesAsync(string tubId, int cycles)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fusion_tubs SET used_cycles=$c WHERE tub_id=$id";
            cmd.Parameters.AddWithValue("$c", Math.Max(0, cycles));
            cmd.Parameters.AddWithValue("$id", tubId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateTubAsync(string tubId, string epc, string colour, double capacityKg, string bayId)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fusion_tubs SET epc=$epc, colour=$col, capacity_kg=$cap, bay_id=$bay WHERE tub_id=$id";
            cmd.Parameters.AddWithValue("$epc", epc ?? string.Empty);
            cmd.Parameters.AddWithValue("$col", string.IsNullOrWhiteSpace(colour) ? "Orange" : colour);
            cmd.Parameters.AddWithValue("$cap", capacityKg > 0 ? capacityKg : 50);
            cmd.Parameters.AddWithValue("$bay", bayId ?? string.Empty);
            cmd.Parameters.AddWithValue("$id", tubId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetTubBlockedAsync(string tubId, bool blocked)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fusion_tubs SET is_blocked=$b WHERE tub_id=$id";
            cmd.Parameters.AddWithValue("$b", blocked ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", tubId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ReplaceTubAsync(string tubId, string today)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fusion_tubs SET used_cycles=0, ageing_date=$d WHERE tub_id=$id";
            cmd.Parameters.AddWithValue("$d", today);
            cmd.Parameters.AddWithValue("$id", tubId);
            await cmd.ExecuteNonQueryAsync();
        }

        // ===== Suppliers (coconut master, fusion_* only) =====
        public async Task<List<FusionSupplier>> GetSuppliersAsync()
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, location, phone, is_active FROM fusion_suppliers ORDER BY id";
            var list = new List<FusionSupplier>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var actOrd = reader.GetOrdinal("is_active");
                list.Add(new FusionSupplier
                {
                    Id = GetString(reader, "id"),
                    Name = GetString(reader, "name"),
                    Location = GetString(reader, "location"),
                    Phone = GetString(reader, "phone"),
                    IsActive = reader.IsDBNull(actOrd) || reader.GetInt32(actOrd) != 0,
                });
            }
            return list;
        }

        public async Task AddSupplierAsync(string name, string location, string phone)
        {
            var supplierName = (name ?? string.Empty).Trim();
            string id;
            await using (var conn = await OpenAsync())
            {
                using var count = conn.CreateCommand();
                count.CommandText = "SELECT COUNT(*) FROM fusion_suppliers";
                var n = Convert.ToInt32(await count.ExecuteScalarAsync());
                id = $"S{(n + 1):D3}";
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO fusion_suppliers (id, name, location, phone, is_active) VALUES ($id,$n,$l,$p,1)";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$n", supplierName);
                cmd.Parameters.AddWithValue("$l", location ?? string.Empty);
                cmd.Parameters.AddWithValue("$p", phone ?? string.Empty);
                await cmd.ExecuteNonQueryAsync();
            }
            // Mirror into the shared suppliers table so fusion_transactions /
            // fusion_inward supplier_id (shared integer PK) resolves for this
            // supplier from day one.
            if (!string.IsNullOrWhiteSpace(supplierName))
            {
                try { await EnsureSharedSupplierAsync(id, supplierName); }
                catch { /* mirror is best-effort; weigh-time resolution retries */ }
            }
        }

        public async Task SetSupplierActiveAsync(string id, bool active)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fusion_suppliers SET is_active=$a WHERE id=$id";
            cmd.Parameters.AddWithValue("$a", active ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateSupplierPhoneAsync(string id, string phone)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fusion_suppliers SET phone=$p WHERE id=$id";
            cmd.Parameters.AddWithValue("$p", phone ?? string.Empty);
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateSupplierAsync(string id, string name, string location, string phone)
        {
            var newName = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("Supplier name is required.");
            await using var conn = await OpenAsync();
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
            string oldName = string.Empty;
            try
            {
                using (var cur = conn.CreateCommand())
                {
                    cur.Transaction = tx;
                    cur.CommandText = "SELECT name FROM fusion_suppliers WHERE id=$id";
                    cur.Parameters.AddWithValue("$id", id);
                    oldName = (await cur.ExecuteScalarAsync())?.ToString() ?? string.Empty;
                }
                if (string.IsNullOrWhiteSpace(oldName))
                    throw new InvalidOperationException($"Supplier {id} not found.");
                using (var upd = conn.CreateCommand())
                {
                    upd.Transaction = tx;
                    upd.CommandText = "UPDATE fusion_suppliers SET name=$n, location=$l, phone=$p WHERE id=$id";
                    upd.Parameters.AddWithValue("$n", newName);
                    upd.Parameters.AddWithValue("$l", location ?? string.Empty);
                    upd.Parameters.AddWithValue("$p", phone ?? string.Empty);
                    upd.Parameters.AddWithValue("$id", id);
                    await upd.ExecuteNonQueryAsync();
                }
                // Inward/batches/plan-lots reference suppliers by name; keep history coherent on rename.
                if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var (table, column) in new[]
                    {
                        ("fusion_inward", "supplier_name"),
                        ("fusion_batches", "supplier_name"),
                        ("fusion_plan_lots", "supplier_name"),
                    })
                    {
                        try
                        {
                            using var mv = conn.CreateCommand();
                            mv.Transaction = tx;
                            mv.CommandText = $"UPDATE {table} SET {column}=$n WHERE {column}=$o";
                            mv.Parameters.AddWithValue("$n", newName);
                            mv.Parameters.AddWithValue("$o", oldName);
                            await mv.ExecuteNonQueryAsync();
                        }
                        catch { /* table may not exist yet */ }
                    }
                }
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
            // Keep the shared suppliers mirror in sync (best-effort): rename any
            // shared row tagged with this fusion id or matching the old name,
            // then ensure the mirror exists.
            try
            {
                if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
                {
                    await using var conn2 = await OpenAsync();
                    using var mv2 = conn2.CreateCommand();
                    mv2.CommandText = @"
                    UPDATE suppliers SET supplier_name=$n, display_name=$d
                    WHERE LOWER(TRIM(supplier_code)) = LOWER($c)
                       OR LOWER(TRIM(supplier_name)) = LOWER($o)";
                    mv2.Parameters.AddWithValue("$n", newName);
                    mv2.Parameters.AddWithValue("$d", newName);
                    mv2.Parameters.AddWithValue("$c", id ?? string.Empty);
                    mv2.Parameters.AddWithValue("$o", oldName ?? string.Empty);
                    await mv2.ExecuteNonQueryAsync();
                }
                await EnsureSharedSupplierAsync(id ?? string.Empty, newName);
            }
            catch { /* mirror is best-effort; weigh-time resolution retries */ }
        }

        // ===== Batches + lots =====
        public async Task<List<FusionBatch>> GetBatchesAsync()
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT batch_no, target_kg, status, created_at, batch_date, supplier_mode, supplier_name, tub_mode, lot_mode, target_apw_kg, nuts_per_tub, target_hours, tub_loop_min, started_at, paused_ms, pause_started_at, pause_count, completed_at, operator_name, target_tubs, target_nuts FROM fusion_batches ORDER BY created_at DESC";
            var list = new List<FusionBatch>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var apwOrd = reader.GetOrdinal("target_apw_kg");
                var npOrd = reader.GetOrdinal("nuts_per_tub");
                var thOrd = reader.GetOrdinal("target_hours");
                var lpOrd = reader.GetOrdinal("tub_loop_min");
                var pmOrd = reader.GetOrdinal("paused_ms");
                var pcOrd = reader.GetOrdinal("pause_count");
                var ttOrd = reader.GetOrdinal("target_tubs");
                var tnOrd = reader.GetOrdinal("target_nuts");
                list.Add(new FusionBatch
                {
                    BatchNo = GetString(reader, "batch_no"),
                    TargetKg = GetDouble(reader, "target_kg"),
                    Status = GetString(reader, "status"),
                    CreatedAt = GetString(reader, "created_at"),
                    BatchDate = GetString(reader, "batch_date"),
                    SupplierMode = GetString(reader, "supplier_mode"),
                    SupplierName = GetString(reader, "supplier_name"),
                    TubMode = GetString(reader, "tub_mode"),
                    LotMode = GetString(reader, "lot_mode"),
                    TargetApwKg = reader.IsDBNull(apwOrd) ? 0 : reader.GetDouble(apwOrd),
                    NutsPerTub = reader.IsDBNull(npOrd) ? 50 : reader.GetInt32(npOrd),
                    TargetHours = reader.IsDBNull(thOrd) ? 16 : reader.GetDouble(thOrd),
                    TubLoopMin = reader.IsDBNull(lpOrd) ? 10 : reader.GetDouble(lpOrd),
                    StartedAt = GetString(reader, "started_at"),
                    PausedMs = reader.IsDBNull(pmOrd) ? 0 : reader.GetInt64(pmOrd),
                    PauseStartedAt = GetString(reader, "pause_started_at"),
                    PauseCount = reader.IsDBNull(pcOrd) ? 0 : reader.GetInt32(pcOrd),
                    CompletedAt = GetString(reader, "completed_at"),
                    OperatorName = GetString(reader, "operator_name"),
                    TargetTubs = reader.IsDBNull(ttOrd) ? 0 : reader.GetInt32(ttOrd),
                    TargetNuts = reader.IsDBNull(tnOrd) ? 0 : reader.GetInt32(tnOrd),
                });
            }
            return list;
        }

        public async Task CreateBatchAsync(string batchNo, double targetKg)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO fusion_batches (batch_no, target_kg, status, created_at)
            VALUES ($no, $target, 'Open', datetime('now','localtime'));";
            cmd.Parameters.AddWithValue("$no", batchNo);
            cmd.Parameters.AddWithValue("$target", targetKg);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task CreateBatchFullAsync(FusionBatch batch)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO fusion_batches (batch_no, target_kg, status, created_at, batch_date, supplier_mode, supplier_name, tub_mode, lot_mode, target_apw_kg, nuts_per_tub, target_hours, tub_loop_min, started_at, paused_ms, pause_started_at, pause_count, completed_at, operator_name, target_tubs, target_nuts)
            VALUES ($no, $target, 'running', datetime('now','localtime'), $bd, $sm, $sn, $tm, $lm, $apw, $np, $th, $lp, datetime('now','localtime'), 0, '', 0, '', $op, $tt, $tn);";
            cmd.Parameters.AddWithValue("$no", batch.BatchNo);
            cmd.Parameters.AddWithValue("$target", batch.TargetKg);
            cmd.Parameters.AddWithValue("$bd", batch.BatchDate ?? string.Empty);
            cmd.Parameters.AddWithValue("$sm", batch.SupplierMode ?? "auto");
            cmd.Parameters.AddWithValue("$sn", batch.SupplierName ?? string.Empty);
            cmd.Parameters.AddWithValue("$tm", batch.TubMode ?? "colour");
            cmd.Parameters.AddWithValue("$lm", batch.LotMode ?? "all");
            cmd.Parameters.AddWithValue("$apw", batch.TargetApwKg);
            cmd.Parameters.AddWithValue("$np", batch.NutsPerTub);
            cmd.Parameters.AddWithValue("$th", batch.TargetHours);
            cmd.Parameters.AddWithValue("$lp", batch.TubLoopMin);
            cmd.Parameters.AddWithValue("$op", batch.OperatorName ?? string.Empty);
            cmd.Parameters.AddWithValue("$tt", batch.TargetTubs);
            cmd.Parameters.AddWithValue("$tn", batch.TargetNuts);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SavePlanLotsAsync(string batchNo, List<FusionPlanLot> lots)
        {
            await using var conn = await OpenAsync();
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
            try
            {
                using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = "DELETE FROM fusion_plan_lots WHERE batch_no=$b";
                del.Parameters.AddWithValue("$b", batchNo);
                await del.ExecuteNonQueryAsync();
                foreach (var l in lots)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                    INSERT INTO fusion_plan_lots (batch_no, lot_no, supplier_name, grn_no, bay_id, best_before, days_left, tubs, nuts, weight_kg, colour, tubs_in_pool, cycles_per_tub, done_tubs, done_kg, is_paused, is_completed)
                    VALUES ($b,$lot,$s,$g,$bay,$bb,$dl,$t,$n,$w,$c,$p,$cyc,0,0,0,0)";
                    cmd.Parameters.AddWithValue("$b", batchNo);
                    cmd.Parameters.AddWithValue("$lot", l.LotNo);
                    cmd.Parameters.AddWithValue("$s", l.SupplierName);
                    cmd.Parameters.AddWithValue("$g", l.GrnNo);
                    cmd.Parameters.AddWithValue("$bay", l.BayId);
                    cmd.Parameters.AddWithValue("$bb", l.BestBefore);
                    cmd.Parameters.AddWithValue("$dl", l.DaysLeft);
                    cmd.Parameters.AddWithValue("$t", l.Tubs);
                    cmd.Parameters.AddWithValue("$n", l.Nuts);
                    cmd.Parameters.AddWithValue("$w", l.WeightKg);
                    cmd.Parameters.AddWithValue("$c", l.Colour);
                    cmd.Parameters.AddWithValue("$p", l.TubsInPool);
                    cmd.Parameters.AddWithValue("$cyc", l.CyclesPerTub);
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<List<FusionPlanLot>> GetPlanLotsAsync(string batchNo)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT lot_no, batch_no, supplier_name, grn_no, bay_id, best_before, days_left, tubs, nuts, weight_kg, colour, tubs_in_pool, cycles_per_tub, done_tubs, done_kg, is_paused, is_completed FROM fusion_plan_lots WHERE batch_no=$b ORDER BY id";
            cmd.Parameters.AddWithValue("$b", batchNo);
            var list = new List<FusionPlanLot>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var dlOrd = reader.GetOrdinal("days_left");
                var tOrd = reader.GetOrdinal("tubs");
                var nOrd = reader.GetOrdinal("nuts");
                var pOrd = reader.GetOrdinal("tubs_in_pool");
                var cOrd = reader.GetOrdinal("cycles_per_tub");
                var dtOrd = reader.GetOrdinal("done_tubs");
                var dkOrd = reader.GetOrdinal("done_kg");
                var pzOrd = reader.GetOrdinal("is_paused");
                var fnOrd = reader.GetOrdinal("is_completed");
                list.Add(new FusionPlanLot
                {
                    LotNo = GetString(reader, "lot_no"),
                    BatchNo = GetString(reader, "batch_no"),
                    SupplierName = GetString(reader, "supplier_name"),
                    GrnNo = GetString(reader, "grn_no"),
                    BayId = GetString(reader, "bay_id"),
                    BestBefore = GetString(reader, "best_before"),
                    DaysLeft = reader.IsDBNull(dlOrd) ? 0 : reader.GetInt32(dlOrd),
                    Tubs = reader.IsDBNull(tOrd) ? 0 : reader.GetInt32(tOrd),
                    Nuts = reader.IsDBNull(nOrd) ? 0 : reader.GetInt32(nOrd),
                    WeightKg = GetDouble(reader, "weight_kg"),
                    Colour = GetString(reader, "colour"),
                    TubsInPool = reader.IsDBNull(pOrd) ? 0 : reader.GetInt32(pOrd),
                    CyclesPerTub = reader.IsDBNull(cOrd) ? 0 : reader.GetDouble(cOrd),
                    DoneTubs = reader.IsDBNull(dtOrd) ? 0 : reader.GetInt32(dtOrd),
                    DoneKg = reader.IsDBNull(dkOrd) ? 0 : reader.GetDouble(dkOrd),
                    IsPaused = !reader.IsDBNull(pzOrd) && reader.GetInt32(pzOrd) != 0,
                    IsCompleted = !reader.IsDBNull(fnOrd) && reader.GetInt32(fnOrd) != 0,
                });
            }
            return list;
        }

        public async Task InsertPlanLotAsync(FusionPlanLot l)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO fusion_plan_lots (batch_no, lot_no, supplier_name, grn_no, bay_id, best_before, days_left, tubs, nuts, weight_kg, colour, tubs_in_pool, cycles_per_tub, done_tubs, done_kg, is_paused, is_completed)
            VALUES ($b,$lot,$s,$g,$bay,$bb,$dl,$t,$n,$w,$c,$p,$cyc,0,0,0,0)";
            cmd.Parameters.AddWithValue("$b", l.BatchNo);
            cmd.Parameters.AddWithValue("$lot", l.LotNo);
            cmd.Parameters.AddWithValue("$s", l.SupplierName);
            cmd.Parameters.AddWithValue("$g", l.GrnNo);
            cmd.Parameters.AddWithValue("$bay", l.BayId);
            cmd.Parameters.AddWithValue("$bb", l.BestBefore);
            cmd.Parameters.AddWithValue("$dl", l.DaysLeft);
            cmd.Parameters.AddWithValue("$t", l.Tubs);
            cmd.Parameters.AddWithValue("$n", l.Nuts);
            cmd.Parameters.AddWithValue("$w", l.WeightKg);
            cmd.Parameters.AddWithValue("$c", l.Colour);
            cmd.Parameters.AddWithValue("$p", l.TubsInPool);
            cmd.Parameters.AddWithValue("$cyc", l.CyclesPerTub);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> GetBatchSequenceAsync(string datePart)        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM fusion_batches WHERE batch_no LIKE $p";
            cmd.Parameters.AddWithValue("$p", $"BATCH-{datePart}-%");
            var v = await cmd.ExecuteScalarAsync();
            return (v == null || v == DBNull.Value ? 0 : Convert.ToInt32(v)) + 1;
        }

        public async Task CloseBatchAsync(string batchNo)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fusion_batches SET status='Closed' WHERE batch_no=$no";
            cmd.Parameters.AddWithValue("$no", batchNo);
            await cmd.ExecuteNonQueryAsync();
        }

        // ===== Live batch run (running / paused / completed + turn-around) =====
        public async Task AddBatchEventAsync(string batchNo, string evt)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO fusion_batch_events (batch_no, event, at) VALUES ($b, $e, datetime('now','localtime'))";
            cmd.Parameters.AddWithValue("$b", batchNo);
            cmd.Parameters.AddWithValue("$e", evt ?? string.Empty);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<FusionBatchEvent>> GetBatchEventsAsync(string batchNo)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT batch_no, event, at FROM fusion_batch_events WHERE batch_no=$b ORDER BY id";
            cmd.Parameters.AddWithValue("$b", batchNo);
            var list = new List<FusionBatchEvent>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(new FusionBatchEvent
                {
                    BatchNo = GetString(reader, "batch_no"),
                    Event = GetString(reader, "event"),
                    At = GetString(reader, "at"),
                });
            return list;
        }

        public async Task PauseBatchAsync(string batchNo)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fusion_batches SET status='paused', pause_started_at=datetime('now','localtime'), pause_count=pause_count+1 WHERE batch_no=$b AND status='running'";
            cmd.Parameters.AddWithValue("$b", batchNo);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ResumeBatchAsync(string batchNo)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            UPDATE fusion_batches SET status='running',
                paused_ms = paused_ms + CAST((strftime('%s','now','localtime') - strftime('%s',pause_started_at))*1000 AS INTEGER),
                pause_started_at=''
            WHERE batch_no=$b AND status='paused' AND pause_started_at<>'';";
            cmd.Parameters.AddWithValue("$b", batchNo);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task CompleteBatchRunAsync(string batchNo)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            UPDATE fusion_batches SET status='completed', completed_at=datetime('now','localtime'),
                paused_ms = paused_ms + CASE WHEN pause_started_at<>'' THEN CAST((strftime('%s','now','localtime') - strftime('%s',pause_started_at))*1000 AS INTEGER) ELSE 0 END,
                pause_started_at=''
            WHERE batch_no=$b AND status IN ('running','paused');";
            cmd.Parameters.AddWithValue("$b", batchNo);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateLotRunAsync(string batchNo, string lotNo, int doneTubs, double doneKg)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fusion_plan_lots SET done_tubs=$t, done_kg=$k WHERE batch_no=$b AND lot_no=$l";
            cmd.Parameters.AddWithValue("$t", Math.Max(0, doneTubs));
            cmd.Parameters.AddWithValue("$k", Math.Max(0, doneKg));
            cmd.Parameters.AddWithValue("$b", batchNo);
            cmd.Parameters.AddWithValue("$l", lotNo);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetLotPausedAsync(string batchNo, string lotNo, bool paused)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fusion_plan_lots SET is_paused=$p WHERE batch_no=$b AND lot_no=$l";
            cmd.Parameters.AddWithValue("$p", paused ? 1 : 0);
            cmd.Parameters.AddWithValue("$b", batchNo);
            cmd.Parameters.AddWithValue("$l", lotNo);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetLotCompletedAsync(string batchNo, string lotNo, bool completed)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fusion_plan_lots SET is_completed=$c WHERE batch_no=$b AND lot_no=$l";
            cmd.Parameters.AddWithValue("$c", completed ? 1 : 0);
            cmd.Parameters.AddWithValue("$b", batchNo);
            cmd.Parameters.AddWithValue("$l", lotNo);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RecordWeighTransactionAsync(FusionTransaction tx)
        {
            await using var conn = await OpenAsync();
            await using var txx = (SqliteTransaction)await conn.BeginTransactionAsync();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = txx;
                cmd.CommandText = @"
                INSERT INTO fusion_transactions (id, batch_no, tub_id, supplier_id, weight_kg, recorded_at, is_synced, epc, tub_cycles, supplier_name, grn_no, bay_id, lot_no, nuts, operator_name)
                VALUES ($id, $batch, $tub, $sup, $w, datetime('now','localtime'), 0, $epc, $cyc, $sn, $grn, $bay, $lot, $nuts, $op);";
                cmd.Parameters.AddWithValue("$id", tx.Id);
                cmd.Parameters.AddWithValue("$batch", tx.BatchNo);
                cmd.Parameters.AddWithValue("$tub", tx.TubId);
                cmd.Parameters.AddWithValue("$sup", tx.SupplierId);
                cmd.Parameters.AddWithValue("$w", tx.WeightKg);
                cmd.Parameters.AddWithValue("$epc", tx.Epc ?? string.Empty);
                cmd.Parameters.AddWithValue("$cyc", tx.TubCycles);
                cmd.Parameters.AddWithValue("$sn", tx.SupplierName ?? string.Empty);
                cmd.Parameters.AddWithValue("$grn", tx.GrnNo ?? string.Empty);
                cmd.Parameters.AddWithValue("$bay", tx.BayId ?? string.Empty);
                cmd.Parameters.AddWithValue("$lot", tx.LotNo ?? string.Empty);
                cmd.Parameters.AddWithValue("$nuts", tx.Nuts);
                cmd.Parameters.AddWithValue("$op", tx.OperatorName ?? string.Empty);
                await cmd.ExecuteNonQueryAsync();

                using var ev = conn.CreateCommand();
                ev.Transaction = txx;
                ev.CommandText = @"
                INSERT INTO fusion_tub_events (tub_id, event_type, weight_kg, created_at)
                VALUES ($tub, 'WEIGHED', $w, datetime('now','localtime'));";
                ev.Parameters.AddWithValue("$tub", tx.TubId);
                ev.Parameters.AddWithValue("$w", tx.WeightKg);
                await ev.ExecuteNonQueryAsync();

                using var cy = conn.CreateCommand();
                cy.Transaction = txx;
                cy.CommandText = "UPDATE fusion_tubs SET used_cycles=$c WHERE tub_id=$tub";
                cy.Parameters.AddWithValue("$c", Math.Max(0, tx.TubCycles));
                cy.Parameters.AddWithValue("$tub", tx.TubId);
                await cy.ExecuteNonQueryAsync();

                await txx.CommitAsync();
            }
            catch
            {
                await txx.RollbackAsync();
                throw;
            }
        }

        public async Task<Modules.Fusion.FusionInward?> GetInwardByGrnAsync(string grnNo)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {InwardColumns} FROM fusion_inward WHERE grn_no=$g LIMIT 1";
            cmd.Parameters.AddWithValue("$g", grnNo ?? string.Empty);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync()) return MapInward(reader);
            return null;
        }

        // Full live-weigh write in ONE SQLite transaction: ledger row + tub event +
        // tub cycle bump + lot counters + GRN consumption. Either everything commits
        // or nothing does (no half-weighed state on crash at 12/min cadence).
        public async Task RecordLiveWeighAsync(Modules.Fusion.FusionTransaction tx, int doneTubs, double doneKg)
        {
            await using var conn = await OpenAsync();
            await using var txx = (SqliteTransaction)await conn.BeginTransactionAsync();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = txx;
                cmd.CommandText = @"
                INSERT INTO fusion_transactions (id, batch_no, tub_id, supplier_id, weight_kg, recorded_at, is_synced, epc, tub_cycles, supplier_name, grn_no, bay_id, lot_no, nuts, operator_name)
                VALUES ($id, $batch, $tub, $sup, $w, datetime('now','localtime'), 0, $epc, $cyc, $sn, $grn, $bay, $lot, $nuts, $op);";
                cmd.Parameters.AddWithValue("$id", tx.Id);
                cmd.Parameters.AddWithValue("$batch", tx.BatchNo);
                cmd.Parameters.AddWithValue("$tub", tx.TubId);
                cmd.Parameters.AddWithValue("$sup", tx.SupplierId);
                cmd.Parameters.AddWithValue("$w", tx.WeightKg);
                cmd.Parameters.AddWithValue("$epc", tx.Epc ?? string.Empty);
                cmd.Parameters.AddWithValue("$cyc", tx.TubCycles);
                cmd.Parameters.AddWithValue("$sn", tx.SupplierName ?? string.Empty);
                cmd.Parameters.AddWithValue("$grn", tx.GrnNo ?? string.Empty);
                cmd.Parameters.AddWithValue("$bay", tx.BayId ?? string.Empty);
                cmd.Parameters.AddWithValue("$lot", tx.LotNo ?? string.Empty);
                cmd.Parameters.AddWithValue("$nuts", tx.Nuts);
                cmd.Parameters.AddWithValue("$op", tx.OperatorName ?? string.Empty);
                await cmd.ExecuteNonQueryAsync();

                using var ev = conn.CreateCommand();
                ev.Transaction = txx;
                ev.CommandText = @"
                INSERT INTO fusion_tub_events (tub_id, event_type, weight_kg, created_at)
                VALUES ($tub, 'WEIGHED', $w, datetime('now','localtime'));";
                ev.Parameters.AddWithValue("$tub", tx.TubId);
                ev.Parameters.AddWithValue("$w", tx.WeightKg);
                await ev.ExecuteNonQueryAsync();

                using var cy = conn.CreateCommand();
                cy.Transaction = txx;
                cy.CommandText = "UPDATE fusion_tubs SET used_cycles=$c WHERE tub_id=$tub";
                cy.Parameters.AddWithValue("$c", Math.Max(0, tx.TubCycles));
                cy.Parameters.AddWithValue("$tub", tx.TubId);
                await cy.ExecuteNonQueryAsync();

                using var lot = conn.CreateCommand();
                lot.Transaction = txx;
                lot.CommandText = "UPDATE fusion_plan_lots SET done_tubs=$t, done_kg=$k WHERE batch_no=$b AND lot_no=$l";
                lot.Parameters.AddWithValue("$t", Math.Max(0, doneTubs));
                lot.Parameters.AddWithValue("$k", Math.Max(0, doneKg));
                lot.Parameters.AddWithValue("$b", tx.BatchNo);
                lot.Parameters.AddWithValue("$l", tx.LotNo ?? string.Empty);
                await lot.ExecuteNonQueryAsync();

                if (!string.IsNullOrWhiteSpace(tx.GrnNo))
                {
                    using var inw = conn.CreateCommand();
                    inw.Transaction = txx;
                    inw.CommandText = "UPDATE fusion_inward SET consumed_kg=consumed_kg+$k, consumed_nuts=consumed_nuts+$n WHERE grn_no=$g";
                    inw.Parameters.AddWithValue("$k", Math.Max(0, tx.WeightKg));
                    inw.Parameters.AddWithValue("$n", Math.Max(0, tx.Nuts));
                    inw.Parameters.AddWithValue("$g", tx.GrnNo);
                    await inw.ExecuteNonQueryAsync();
                }

                await txx.CommitAsync();
            }
            catch
            {
                await txx.RollbackAsync();
                throw;
            }
        }

        public async Task SetTubsAssignedAsync(IEnumerable<string> tubIds, string batchNo)
        {
            var ids = tubIds?.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList() ?? new List<string>();
            if (ids.Count == 0) return;
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            var names = ids.Select((_, i) => "$t" + i).ToList();
            cmd.CommandText = $"UPDATE fusion_tubs SET assigned_to=$b WHERE tub_id IN ({string.Join(",", names)})";
            cmd.Parameters.AddWithValue("$b", batchNo ?? string.Empty);
            for (int i = 0; i < ids.Count; i++)
                cmd.Parameters.AddWithValue(names[i], ids[i]);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ClearBatchAssignmentsAsync(string batchNo)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fusion_tubs SET assigned_to='' WHERE assigned_to=$b";
            cmd.Parameters.AddWithValue("$b", batchNo);
            await cmd.ExecuteNonQueryAsync();
        }

        // Release individual tubs from their running batch so they can be assigned again.
        public async Task<int> ReleaseTubsAsync(IEnumerable<string> tubIds)
        {
            var ids = tubIds?.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList() ?? new List<string>();
            if (ids.Count == 0) return 0;
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            var names = ids.Select((_, i) => "$t" + i).ToList();
            cmd.CommandText = $"UPDATE fusion_tubs SET assigned_to='' WHERE tub_id IN ({string.Join(",", names)})";
            for (int i = 0; i < ids.Count; i++)
                cmd.Parameters.AddWithValue(names[i], ids[i]);
            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task AddInwardConsumptionAsync(string grnNo, double kg, double nuts)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fusion_inward SET consumed_kg=consumed_kg+$k, consumed_nuts=consumed_nuts+$n WHERE grn_no=$g";
            cmd.Parameters.AddWithValue("$k", Math.Max(0, kg));
            cmd.Parameters.AddWithValue("$n", Math.Max(0, nuts));
            cmd.Parameters.AddWithValue("$g", grnNo);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<FusionLot>> GetLotsAsync(string batchNo)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT lot_no, batch_no, tub_id, created_at FROM fusion_batch_lots WHERE batch_no=$no ORDER BY created_at";
            cmd.Parameters.AddWithValue("$no", batchNo);
            var list = new List<FusionLot>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new FusionLot
                {
                    LotNo = GetString(reader, "lot_no"),
                    BatchNo = GetString(reader, "batch_no"),
                    TubId = GetString(reader, "tub_id"),
                    CreatedAt = GetString(reader, "created_at"),
                });
            }
            return list;
        }

        public async Task AddLotAsync(string lotNo, string batchNo, string tubId)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO fusion_batch_lots (lot_no, batch_no, tub_id, created_at)
            VALUES ($lot, $batch, $tub, datetime('now','localtime'));";
            cmd.Parameters.AddWithValue("$lot", lotNo);
            cmd.Parameters.AddWithValue("$batch", batchNo);
            cmd.Parameters.AddWithValue("$tub", tubId);
            await cmd.ExecuteNonQueryAsync();
        }

        // ===== Transactions =====
        public async Task RecordTransactionAsync(string id, string batchNo, string tubId, int supplierId, double weightKg)
        {
            await using var conn = await OpenAsync();
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                INSERT INTO fusion_transactions (id, batch_no, tub_id, supplier_id, weight_kg, recorded_at, is_synced)
                VALUES ($id, $batch, $tub, $sup, $w, datetime('now','localtime'), 0);";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$batch", batchNo);
                cmd.Parameters.AddWithValue("$tub", tubId);
                cmd.Parameters.AddWithValue("$sup", supplierId);
                cmd.Parameters.AddWithValue("$w", weightKg);
                await cmd.ExecuteNonQueryAsync();

                using var ev = conn.CreateCommand();
                ev.Transaction = tx;
                ev.CommandText = @"
                INSERT INTO fusion_tub_events (tub_id, event_type, weight_kg, created_at)
                VALUES ($tub, 'WEIGHED', $w, datetime('now','localtime'));";
                ev.Parameters.AddWithValue("$tub", tubId);
                ev.Parameters.AddWithValue("$w", weightKg);
                await ev.ExecuteNonQueryAsync();

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<List<FusionTransaction>> GetTransactionsAsync(string? batchNo, int limit = 200)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            if (string.IsNullOrWhiteSpace(batchNo))
            {
                cmd.CommandText = "SELECT id, batch_no, tub_id, supplier_id, weight_kg, recorded_at, epc, tub_cycles, supplier_name, grn_no, bay_id, lot_no, nuts, operator_name FROM fusion_transactions ORDER BY recorded_at DESC LIMIT $n";
                cmd.Parameters.AddWithValue("$n", limit);
            }
            else
            {
                cmd.CommandText = "SELECT id, batch_no, tub_id, supplier_id, weight_kg, recorded_at, epc, tub_cycles, supplier_name, grn_no, bay_id, lot_no, nuts, operator_name FROM fusion_transactions WHERE batch_no=$b ORDER BY recorded_at DESC LIMIT $n";
                cmd.Parameters.AddWithValue("$b", batchNo);
                cmd.Parameters.AddWithValue("$n", limit);
            }
            var list = new List<FusionTransaction>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var o = reader.GetOrdinal("supplier_id");
                var tcOrd = reader.GetOrdinal("tub_cycles");
                var nOrd = reader.GetOrdinal("nuts");
                list.Add(new FusionTransaction
                {
                    Id = GetString(reader, "id"),
                    BatchNo = GetString(reader, "batch_no"),
                    TubId = GetString(reader, "tub_id"),
                    SupplierId = reader.IsDBNull(o) ? 0 : reader.GetInt32(o),
                    WeightKg = GetDouble(reader, "weight_kg"),
                    RecordedAt = GetString(reader, "recorded_at"),
                    Epc = GetString(reader, "epc"),
                    TubCycles = reader.IsDBNull(tcOrd) ? 0 : reader.GetInt32(tcOrd),
                    SupplierName = GetString(reader, "supplier_name"),
                    GrnNo = GetString(reader, "grn_no"),
                    BayId = GetString(reader, "bay_id"),
                    LotNo = GetString(reader, "lot_no"),
                    Nuts = reader.IsDBNull(nOrd) ? 0 : reader.GetInt32(nOrd),
                    OperatorName = GetString(reader, "operator_name"),
                });
            }
            return list;
        }

        public async Task<(int Count, double TotalKg)> GetTodayStatsAsync()
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(weight_kg),0) FROM fusion_transactions WHERE date(recorded_at)=date('now','localtime')";
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return (reader.IsDBNull(0) ? 0 : reader.GetInt32(0), reader.IsDBNull(1) ? 0 : reader.GetDouble(1));
            return (0, 0);
        }

        // ===== Inward =====
        private static FusionInward MapInward(SqliteDataReader reader)
        {
            var supOrd = reader.GetOrdinal("supplier_id");
            var shelfOrd = reader.GetOrdinal("shelf_life_days");
            var cntOrd = reader.GetOrdinal("coconut_count");
            string material = string.Empty;
            try { material = GetString(reader, "material_name"); } catch { material = string.Empty; }
            return new FusionInward
            {
                GrnNo = GetString(reader, "grn_no"),
                SupplierId = reader.IsDBNull(supOrd) ? 0 : reader.GetInt32(supOrd),
                WeightKg = GetDouble(reader, "weight_kg"),
                ReceivedAt = GetString(reader, "received_at"),
                SupplierName = GetString(reader, "supplier_name"),
                TicketNo = GetString(reader, "ticket_no"),
                InwardDate = GetString(reader, "inward_date"),
                BayId = GetString(reader, "bay_id"),
                MaterialName = material,
                ShelfLifeDays = reader.IsDBNull(shelfOrd) ? 10 : reader.GetInt32(shelfOrd),
                CoconutCount = reader.IsDBNull(cntOrd) ? 0 : reader.GetDouble(cntOrd),
                SampleKg = GetDouble(reader, "sample_kg"),
                SpoilKg = GetDouble(reader, "spoil_kg"),
                WaterLossKg = GetDouble(reader, "water_loss_kg"),
                BrokenKg = GetDouble(reader, "broken_kg"),
                RejectionKg = GetDouble(reader, "rejection_kg"),
                ConsumedKg = GetDouble(reader, "consumed_kg"),
                ConsumedNuts = GetDouble(reader, "consumed_nuts"),
            };
        }

        private const string InwardColumns = "grn_no, supplier_id, weight_kg, received_at, supplier_name, ticket_no, inward_date, bay_id, material_name, shelf_life_days, coconut_count, sample_kg, spoil_kg, water_loss_kg, broken_kg, rejection_kg, consumed_kg, consumed_nuts";

        public async Task<List<FusionInward>> GetInwardAsync(int limit = 200)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {InwardColumns} FROM fusion_inward ORDER BY received_at DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$n", limit);
            var list = new List<FusionInward>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) list.Add(MapInward(reader));
            return list;
        }

        public async Task AddInwardAsync(string grnNo, int supplierId, double weightKg)
        {
            await AddInwardFullAsync(new FusionInward
            {
                GrnNo = grnNo,
                SupplierId = supplierId,
                WeightKg = weightKg,
                ReceivedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                InwardDate = DateTime.Today.ToString("yyyy-MM-dd"),
            });
        }

        public async Task AddInwardFullAsync(FusionInward row)
        {
            await using var conn = await OpenAsync();
            // Ensure material_name exists on older DBs (idempotent).
            using (var mig = conn.CreateCommand())
            {
                mig.CommandText = "PRAGMA table_info(fusion_inward)";
                var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                await using (var r = await mig.ExecuteReaderAsync())
                    while (await r.ReadAsync()) cols.Add(r.GetString(1));
                if (!cols.Contains("material_name"))
                {
                    using var add = conn.CreateCommand();
                    add.CommandText = "ALTER TABLE fusion_inward ADD COLUMN material_name TEXT NOT NULL DEFAULT ''";
                    await add.ExecuteNonQueryAsync();
                }
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO fusion_inward
                (grn_no, supplier_id, weight_kg, received_at, supplier_name, ticket_no, inward_date, bay_id, material_name, shelf_life_days, coconut_count, sample_kg, spoil_kg, water_loss_kg, broken_kg, rejection_kg, consumed_kg, consumed_nuts)
            VALUES ($grn, $sup, $w, $rec, $sn, $tk, $dt, $bay, $mat, $sl, $n, $qs, $qp, $qw, $qb, $qr, 0, 0);";
            cmd.Parameters.AddWithValue("$grn", row.GrnNo);
            cmd.Parameters.AddWithValue("$sup", row.SupplierId);
            cmd.Parameters.AddWithValue("$w", row.WeightKg);
            cmd.Parameters.AddWithValue("$rec", string.IsNullOrEmpty(row.ReceivedAt) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : row.ReceivedAt);
            cmd.Parameters.AddWithValue("$sn", row.SupplierName ?? string.Empty);
            cmd.Parameters.AddWithValue("$tk", row.TicketNo ?? string.Empty);
            cmd.Parameters.AddWithValue("$dt", string.IsNullOrEmpty(row.InwardDate) ? DateTime.Today.ToString("yyyy-MM-dd") : row.InwardDate);
            cmd.Parameters.AddWithValue("$bay", row.BayId ?? string.Empty);
            cmd.Parameters.AddWithValue("$mat", row.MaterialName ?? string.Empty);
            cmd.Parameters.AddWithValue("$sl", row.ShelfLifeDays);
            cmd.Parameters.AddWithValue("$n", row.CoconutCount);
            cmd.Parameters.AddWithValue("$qs", row.SampleKg);
            cmd.Parameters.AddWithValue("$qp", row.SpoilKg);
            cmd.Parameters.AddWithValue("$qw", row.WaterLossKg);
            cmd.Parameters.AddWithValue("$qb", row.BrokenKg);
            cmd.Parameters.AddWithValue("$qr", row.RejectionKg);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateInwardFullAsync(FusionInward row)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE fusion_inward SET supplier_id=$sup, weight_kg=$w, received_at=$rec, supplier_name=$sn, ticket_no=$tk, inward_date=$dt, bay_id=$bay, material_name=$mat, shelf_life_days=$sl, coconut_count=$n WHERE grn_no=$grn";
            cmd.Parameters.AddWithValue("$sup", row.SupplierId);
            cmd.Parameters.AddWithValue("$w", row.WeightKg);
            cmd.Parameters.AddWithValue("$rec", string.IsNullOrEmpty(row.ReceivedAt) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : row.ReceivedAt);
            cmd.Parameters.AddWithValue("$sn", row.SupplierName ?? string.Empty);
            cmd.Parameters.AddWithValue("$tk", row.TicketNo ?? string.Empty);
            cmd.Parameters.AddWithValue("$dt", string.IsNullOrEmpty(row.InwardDate) ? DateTime.Today.ToString("yyyy-MM-dd") : row.InwardDate);
            cmd.Parameters.AddWithValue("$bay", row.BayId ?? string.Empty);
            cmd.Parameters.AddWithValue("$mat", row.MaterialName ?? string.Empty);
            cmd.Parameters.AddWithValue("$sl", row.ShelfLifeDays);
            cmd.Parameters.AddWithValue("$n", row.CoconutCount);
            cmd.Parameters.AddWithValue("$grn", row.GrnNo);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateDeductionsAsync(string grnNo, double sample, double spoil, double water, double broken, double rejection)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fusion_inward SET sample_kg=$a, spoil_kg=$b, water_loss_kg=$c, broken_kg=$d, rejection_kg=$e WHERE grn_no=$g";
            cmd.Parameters.AddWithValue("$a", Math.Max(0, sample));
            cmd.Parameters.AddWithValue("$b", Math.Max(0, spoil));
            cmd.Parameters.AddWithValue("$c", Math.Max(0, water));
            cmd.Parameters.AddWithValue("$d", Math.Max(0, broken));
            cmd.Parameters.AddWithValue("$e", Math.Max(0, rejection));
            cmd.Parameters.AddWithValue("$g", grnNo);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> GetInwardSequenceAsync()
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            // Use the highest numeric suffix rather than COUNT(*) so deleted rows
            // never cause a duplicate GRN.
            cmd.CommandText = "SELECT COALESCE(MAX(CAST(SUBSTR(grn_no, 5) AS INTEGER)), 0) FROM fusion_inward WHERE grn_no LIKE 'GRN-%'";
            var v = await cmd.ExecuteScalarAsync();
            return (v == null || v == DBNull.Value ? 0 : Convert.ToInt32(v)) + 1;
        }

        // Returns the next free GRN, skipping any existing value (legacy/imported rows).
        public async Task<string> GetNextInwardGrnAsync()
        {
            var n = await GetInwardSequenceAsync();
            await using var conn = await OpenAsync();
            while (true)
            {
                var candidate = $"GRN-{n}";
                using var check = conn.CreateCommand();
                check.CommandText = "SELECT 1 FROM fusion_inward WHERE grn_no=$g COLLATE NOCASE LIMIT 1";
                check.Parameters.AddWithValue("$g", candidate);
                if (await check.ExecuteScalarAsync() is null)
                    return candidate;
                n++;
            }
        }

        // Weighbridge ticket numbers are unique across inward stock (and therefore
        // supplier stock, which is built from the same inward rows).
        public async Task<bool> InwardTicketExistsAsync(string ticketNo, string? excludeGrnNo = null)
        {
            var ticket = (ticketNo ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(ticket))
                return false;
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*) FROM fusion_inward
                WHERE TRIM(ticket_no) <> ''
                  AND TRIM(ticket_no) COLLATE NOCASE = $t
                  AND ($ex IS NULL OR grn_no <> $ex);";
            cmd.Parameters.AddWithValue("$t", ticket);
            cmd.Parameters.AddWithValue("$ex", (object?)excludeGrnNo ?? DBNull.Value);
            var v = await cmd.ExecuteScalarAsync();
            return (v == null || v == DBNull.Value ? 0 : Convert.ToInt32(v)) > 0;
        }

        // ===== Shared materials master (read-only: name + shelf life for inward) =====
        public async Task<List<(string Name, int ShelfLife)>> GetActiveMaterialsAsync()
        {
            var list = new List<(string, int)>();
            await using var conn = await OpenAsync();
            using var check = conn.CreateCommand();
            check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='materials'";
            var exists = await check.ExecuteScalarAsync() != null;
            if (!exists) return list;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT product_name, shelf_life FROM materials WHERE is_active=1 ORDER BY product_name";
            try
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    var shelf = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    if (!string.IsNullOrWhiteSpace(name))
                        list.Add((name.Trim(), shelf));
                }
            }
            catch { /* shared table schema variant — return what we have */ }
            return list;
        }

        // ===== Bay inventory =====
        public async Task<List<FusionBayItem>> GetBayInventoryAsync()
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT tub_id, bay_id, weight_kg, updated_at FROM fusion_bay_inventory ORDER BY bay_id, tub_id";
            var list = new List<FusionBayItem>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new FusionBayItem
                {
                    TubId = GetString(reader, "tub_id"),
                    BayId = GetString(reader, "bay_id"),
                    WeightKg = GetDouble(reader, "weight_kg"),
                    UpdatedAt = GetString(reader, "updated_at"),
                });
            }
            return list;
        }

        public async Task UpsertBayInventoryAsync(string tubId, string bayId, double weightKg)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO fusion_bay_inventory (tub_id, bay_id, weight_kg, updated_at)
            VALUES ($tub, $bay, $w, datetime('now','localtime'))
            ON CONFLICT(tub_id) DO UPDATE SET
                bay_id=excluded.bay_id, weight_kg=excluded.weight_kg, updated_at=excluded.updated_at;";
            cmd.Parameters.AddWithValue("$tub", tubId);
            cmd.Parameters.AddWithValue("$bay", bayId ?? string.Empty);
            cmd.Parameters.AddWithValue("$w", weightKg);
            await cmd.ExecuteNonQueryAsync();
        }

        // ===== Traceability =====
        public async Task<List<FusionTubEvent>> GetTubEventsAsync(string tubId, int limit = 200)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, tub_id, event_type, weight_kg, created_at FROM fusion_tub_events WHERE tub_id=$t ORDER BY id DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$t", tubId);
            cmd.Parameters.AddWithValue("$n", limit);
            var list = new List<FusionTubEvent>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var o = reader.GetOrdinal("id");
                list.Add(new FusionTubEvent
                {
                    Id = reader.IsDBNull(o) ? 0 : reader.GetInt64(o),
                    TubId = GetString(reader, "tub_id"),
                    EventType = GetString(reader, "event_type"),
                    WeightKg = GetDouble(reader, "weight_kg"),
                    CreatedAt = GetString(reader, "created_at"),
                });
            }
            return list;
        }

        public async Task LogTubEventAsync(string tubId, string eventType, double weightKg)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO fusion_tub_events (tub_id, event_type, weight_kg, created_at)
            VALUES ($tub, $ev, $w, datetime('now','localtime'));";
            cmd.Parameters.AddWithValue("$tub", tubId);
            cmd.Parameters.AddWithValue("$ev", eventType ?? string.Empty);
            cmd.Parameters.AddWithValue("$w", weightKg);
            await cmd.ExecuteNonQueryAsync();
        }

        // ===== Dashboard (Step 5 — fusion_* only) =====
        public sealed record FusionKpis(
            int TodayCount, double TodayKg, int ActiveBatches,
            int TotalTubs, int ActiveTubs, int AvailableTubs, int CompletedToday, double TargetKg);

        public async Task<FusionKpis> GetDashboardKpisAsync()
        {
            await using var conn = await OpenAsync();
            async Task<T> ScalarAsync<T>(string sql)
            {
                using var c = conn.CreateCommand();
                c.CommandText = sql;
                var v = await c.ExecuteScalarAsync();
                if (v == null || v == DBNull.Value) return default!;
                return (T)Convert.ChangeType(v, typeof(T));
            }
            var todayCount = await ScalarAsync<int>(
                "SELECT COUNT(*) FROM fusion_transactions WHERE date(recorded_at)=date('now','localtime')");
            var todayKg = await ScalarAsync<double>(
                "SELECT COALESCE(SUM(weight_kg),0) FROM fusion_transactions WHERE date(recorded_at)=date('now','localtime')");
            var activeBatches = await ScalarAsync<int>(
                "SELECT COUNT(*) FROM fusion_batches WHERE status IN ('Open','running','paused')");
            var totalTubs = await ScalarAsync<int>("SELECT COUNT(*) FROM fusion_tubs");
            var activeTubs = await ScalarAsync<int>("SELECT COUNT(*) FROM fusion_tubs WHERE assigned_to IS NOT NULL AND assigned_to <> ''");
            var availableTubs = await ScalarAsync<int>("SELECT COUNT(*) FROM fusion_tubs WHERE (is_blocked IS NULL OR is_blocked = 0) AND (assigned_to IS NULL OR assigned_to = '')");
            var completedToday = await ScalarAsync<int>(
                "SELECT COUNT(DISTINCT tub_id) FROM fusion_transactions WHERE date(recorded_at)=date('now','localtime')");
            var targetKg = await ScalarAsync<double>(
                "SELECT COALESCE(SUM(target_kg),0) FROM fusion_batches WHERE status IN ('Open','running','paused')");
            return new FusionKpis(todayCount, todayKg, activeBatches, totalTubs, activeTubs, availableTubs, completedToday, targetKg);
        }

        public async Task<List<(int SupplierId, double Kg)>> GetSupplierShareAsync()
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT supplier_id, SUM(weight_kg) FROM fusion_transactions
            WHERE date(recorded_at)=date('now','localtime')
            GROUP BY supplier_id ORDER BY 2 DESC";
            var list = new List<(int, double)>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add((reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                          reader.IsDBNull(1) ? 0 : reader.GetDouble(1)));
            return list;
        }

        public async Task<Dictionary<int, string>> GetSupplierNamesAsync(IEnumerable<int> ids)
        {
            var map = new Dictionary<int, string>();
            var distinct = ids.Distinct().Where(i => i > 0).ToArray();
            if (distinct.Length == 0) return map;
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            // Shared suppliers table (same DB) — read-only name lookup, no FK.
            var parms = string.Join(",", distinct.Select((_, i) => "$p" + i));
            cmd.CommandText = $"SELECT id, supplier_name FROM suppliers WHERE id IN ({parms})";
            for (int i = 0; i < distinct.Length; i++)
                cmd.Parameters.AddWithValue("$p" + i, distinct[i]);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                map[reader.GetInt32(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            return map;
        }

        /// <summary>
        /// Resolves the shared suppliers.id for a supplier name.
        /// fusion_transactions.supplier_id / fusion_inward.supplier_id reference
        /// the shared suppliers table (integer PK, per FUSION-PLAN). Fusion
        /// suppliers live in fusion_suppliers (text ids like S001) and lots only
        /// carry the supplier NAME, so this mirrors the Fusion supplier into the
        /// shared table when needed and returns its integer id. 0 = no match.
        /// </summary>
        public async Task<int> ResolveSupplierIdByNameAsync(string? supplierName)
        {
            var name = (supplierName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name)) return 0;
            try
            {
                await using var conn = await OpenAsync();
                // 1) Fusion supplier master (id S001…) → mirror into shared suppliers.
                string? fusionId;
                using (var c1 = conn.CreateCommand())
                {
                    c1.CommandText = "SELECT id FROM fusion_suppliers WHERE LOWER(TRIM(name)) = LOWER($n) LIMIT 1";
                    c1.Parameters.AddWithValue("$n", name);
                    fusionId = (await c1.ExecuteScalarAsync())?.ToString();
                }
                if (!string.IsNullOrWhiteSpace(fusionId))
                    return await EnsureSharedSupplierAsync(fusionId, name);
                // 2) Existing shared supplier by name.
                using (var c2 = conn.CreateCommand())
                {
                    c2.CommandText = @"
                    SELECT id FROM suppliers
                    WHERE LOWER(TRIM(supplier_name)) = LOWER($n)
                       OR LOWER(TRIM(display_name)) = LOWER($n)
                    ORDER BY id LIMIT 1";
                    c2.Parameters.AddWithValue("$n", name);
                    var result = await c2.ExecuteScalarAsync();
                    return result is null ? 0 : Convert.ToInt32(result);
                }
            }
            catch
            {
                return 0; // shared suppliers table unavailable — keep legacy 0
            }
        }

        /// <summary>
        /// Mirrors a Fusion supplier (fusion_suppliers id + name) into the shared
        /// suppliers table and returns the shared integer id. Reuses an existing
        /// shared row (matched by supplier_code, then by name) and keeps its
        /// name/code in sync; creates one when missing.
        /// </summary>
        public async Task<int> EnsureSharedSupplierAsync(string fusionId, string name)
        {
            var n = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(n)) return 0;
            await using var conn = await OpenAsync();
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
            try
            {
                long? id = null;
                using (var byCode = conn.CreateCommand())
                {
                    byCode.Transaction = tx;
                    byCode.CommandText = "SELECT id, supplier_name, display_name FROM suppliers WHERE LOWER(TRIM(supplier_code)) = LOWER($c) LIMIT 1";
                    byCode.Parameters.AddWithValue("$c", fusionId ?? string.Empty);
                    await using var rc = await byCode.ExecuteReaderAsync();
                    if (await rc.ReadAsync())
                    {
                        id = rc.GetInt64(0);
                        string curName = rc.IsDBNull(1) ? string.Empty : rc.GetString(1);
                        string curDisplay = rc.IsDBNull(2) ? string.Empty : rc.GetString(2);
                        if (!string.Equals(curName, n, StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(curDisplay, n, StringComparison.OrdinalIgnoreCase))
                        {
                            using var sync = conn.CreateCommand();
                            sync.Transaction = tx;
                            sync.CommandText = "UPDATE suppliers SET supplier_name=$n, display_name=$d WHERE id=$id";
                            sync.Parameters.AddWithValue("$n", n);
                            sync.Parameters.AddWithValue("$d", n);
                            sync.Parameters.AddWithValue("$id", id.Value);
                            await sync.ExecuteNonQueryAsync();
                        }
                    }
                }
                if (id == null)
                {
                    using (var byName = conn.CreateCommand())
                    {
                        byName.Transaction = tx;
                        byName.CommandText = @"
                        SELECT id, supplier_code FROM suppliers
                        WHERE LOWER(TRIM(supplier_name)) = LOWER($n) OR LOWER(TRIM(display_name)) = LOWER($n)
                        ORDER BY id LIMIT 1";
                        byName.Parameters.AddWithValue("$n", n);
                        await using var rn = await byName.ExecuteReaderAsync();
                        if (await rn.ReadAsync())
                        {
                            id = rn.GetInt64(0);
                            string? code = rn.IsDBNull(1) ? null : rn.GetString(1);
                            // Tag the shared row with the fusion id when it has no code yet.
                            if (string.IsNullOrWhiteSpace(code))
                            {
                                using var tag = conn.CreateCommand();
                                tag.Transaction = tx;
                                tag.CommandText = "UPDATE suppliers SET supplier_code=$c WHERE id=$id";
                                tag.Parameters.AddWithValue("$c", fusionId);
                                tag.Parameters.AddWithValue("$id", id.Value);
                                await tag.ExecuteNonQueryAsync();
                            }
                        }
                    }
                }
                if (id == null)
                {
                    using var ins = conn.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = @"
                    INSERT INTO suppliers (supplier_code, supplier_name, display_name, supplier_type, business_category, status, tax_id, contact_person, phone, email, created_at)
                    VALUES ($c, $n, $d, '', '', 1, '', '', '', '', datetime('now','localtime'));
                    SELECT last_insert_rowid();";
                    ins.Parameters.AddWithValue("$c", fusionId ?? string.Empty);
                    ins.Parameters.AddWithValue("$n", n);
                    ins.Parameters.AddWithValue("$d", n);
                    id = Convert.ToInt64(await ins.ExecuteScalarAsync());
                }
                await tx.CommitAsync();
                return (int)id.Value;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<List<(string Label, double Kg)>> GetHourlyAsync()
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT strftime('%H', recorded_at) AS h, SUM(weight_kg) FROM fusion_transactions
            WHERE date(recorded_at)=date('now','localtime')
            GROUP BY h ORDER BY h";
            var list = new List<(string, double)>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add((reader.GetString(0) + "h", reader.IsDBNull(1) ? 0 : reader.GetDouble(1)));
            return list;
        }

        public async Task<List<(string Label, double Kg)>> GetDailyAsync(int days = 7)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT date(recorded_at) AS d, SUM(weight_kg) FROM fusion_transactions
            WHERE date(recorded_at) >= date('now','localtime', $back)
            GROUP BY d ORDER BY d";
            cmd.Parameters.AddWithValue("$back", $"-{days - 1} days");
            var list = new List<(string, double)>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add((reader.GetString(0)[5..], reader.IsDBNull(1) ? 0 : reader.GetDouble(1)));
            return list;
        }

        public async Task<List<(string Label, double Kg)>> GetBatchTotalsAsync()
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT batch_no, SUM(weight_kg) FROM fusion_transactions
            WHERE date(recorded_at)=date('now','localtime')
            GROUP BY batch_no ORDER BY 2 DESC";
            var list = new List<(string, double)>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add((reader.GetString(0), reader.IsDBNull(1) ? 0 : reader.GetDouble(1)));
            return list;
        }

        public async Task<List<(string Label, double Kg)>> GetBayTotalsAsync()
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT COALESCE(NULLIF(t.bay_id,''),'(unassigned)'), SUM(x.weight_kg)
            FROM fusion_transactions x LEFT JOIN fusion_tubs t ON t.tub_id=x.tub_id
            WHERE date(x.recorded_at)=date('now','localtime')
            GROUP BY 1 ORDER BY 2 DESC";
            var list = new List<(string, double)>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add((reader.GetString(0), reader.IsDBNull(1) ? 0 : reader.GetDouble(1)));
            return list;
        }

        // ===== Fusion settings (extensible key-value) =====
        public async Task<Dictionary<string, string>> GetSettingsAsync()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using var conn = await OpenAsync();
            using var check = conn.CreateCommand();
            check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='fusion_settings'";
            if (await check.ExecuteScalarAsync() == null) return map;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM fusion_settings";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                map[reader.IsDBNull(0) ? string.Empty : reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            return map;
        }

        public async Task<string> GetSettingAsync(string key, string defaultValue = "")
        {
            await using var conn = await OpenAsync();
            using var check = conn.CreateCommand();
            check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='fusion_settings'";
            if (await check.ExecuteScalarAsync() == null) return defaultValue;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM fusion_settings WHERE key=$k";
            cmd.Parameters.AddWithValue("$k", key ?? string.Empty);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? defaultValue : Convert.ToString(v) ?? defaultValue;
        }

        public async Task<int> GetNearExpiryDaysAsync(int defaultValue = 7)
        {
            var raw = await GetSettingAsync("near_expiry_days", defaultValue.ToString());
            return int.TryParse(raw, out var v) && v >= 0 ? v : defaultValue;
        }

        public async Task<(int NearExpiryDays, int ExpiredBelowDays)> GetConditionThresholdsAsync()
        {
            var settings = await GetSettingsAsync();
            int near = 7, expiredBelow = 0;
            if (settings.TryGetValue("near_expiry_days", out var nRaw) && int.TryParse(nRaw, out var n) && n >= 0)
                near = n;
            if (settings.TryGetValue("expired_below_days", out var eRaw) && int.TryParse(eRaw, out var e))
                expiredBelow = Math.Min(e, near);
            return (near, expiredBelow);
        }

        public async Task<(int ReplaceAtCycles, double WarnPercent)> GetTubReplacementSettingsAsync()
        {
            var settings = await GetSettingsAsync();
            int replaceAt = 2000;
            double warn = 80;
            if (settings.TryGetValue("tub_replace_at_cycles", out var rRaw) && int.TryParse(rRaw, out var r) && r > 0)
                replaceAt = r;
            if (settings.TryGetValue("tub_warn_percent", out var wRaw)
                && double.TryParse(wRaw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var w)
                && w >= 1 && w <= 99)
                warn = w;
            return (replaceAt, warn);
        }

        public async Task SetSettingAsync(string key, string value)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS fusion_settings (key TEXT PRIMARY KEY, value TEXT NOT NULL DEFAULT '');
            INSERT INTO fusion_settings (key, value) VALUES ($k, $v)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
            cmd.Parameters.AddWithValue("$k", key ?? string.Empty);
            cmd.Parameters.AddWithValue("$v", value ?? string.Empty);
            await cmd.ExecuteNonQueryAsync();
        }

        // ===== Coconut store bays (capacities; occupancy derives from fusion_inward) =====
        public sealed record FusionBay(string BayId, double MaxCapacityNuts, bool IsActive = true);

        public async Task<List<FusionBay>> GetBaysAsync()
        {
            var list = new List<FusionBay>();
            await using var conn = await OpenAsync();
            using var check = conn.CreateCommand();
            check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='fusion_bays'";
            if (await check.ExecuteScalarAsync() == null) return list;
            // is_active may not exist on older DBs — probe columns first.
            var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = "PRAGMA table_info(fusion_bays)";
                await using var pr = await pragma.ExecuteReaderAsync();
                while (await pr.ReadAsync())
                    cols.Add(pr.GetString(1));
            }
            bool hasActive = cols.Contains("is_active");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = hasActive
                ? "SELECT bay_id, max_capacity_nuts, is_active FROM fusion_bays ORDER BY bay_id"
                : "SELECT bay_id, max_capacity_nuts FROM fusion_bays ORDER BY bay_id";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var cap = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
                bool active = true;
                if (hasActive)
                {
                    var ord = reader.GetOrdinal("is_active");
                    active = reader.IsDBNull(ord) || reader.GetInt32(ord) != 0;
                }
                if (!string.IsNullOrWhiteSpace(id))
                    list.Add(new FusionBay(id.Trim(), Math.Max(0, cap), active));
            }
            return list;
        }

        public async Task<List<FusionBay>> GetActiveBaysAsync()
        {
            var all = await GetBaysAsync();
            return all.Where(b => b.IsActive).ToList();
        }

        public async Task UpsertBayAsync(string bayId, double maxCapacityNuts)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS fusion_bays (bay_id TEXT PRIMARY KEY, max_capacity_nuts REAL NOT NULL DEFAULT 0, is_active INTEGER NOT NULL DEFAULT 1);
            INSERT INTO fusion_bays (bay_id, max_capacity_nuts, is_active) VALUES ($b, $c, 1)
            ON CONFLICT(bay_id) DO UPDATE SET max_capacity_nuts=excluded.max_capacity_nuts;";
            cmd.Parameters.AddWithValue("$b", bayId.Trim());
            cmd.Parameters.AddWithValue("$c", Math.Max(0, maxCapacityNuts));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetBayActiveAsync(string bayId, bool active)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS fusion_bays (bay_id TEXT PRIMARY KEY, max_capacity_nuts REAL NOT NULL DEFAULT 0, is_active INTEGER NOT NULL DEFAULT 1);
            UPDATE fusion_bays SET is_active=$a WHERE bay_id=$b;";
            cmd.Parameters.AddWithValue("$a", active ? 1 : 0);
            cmd.Parameters.AddWithValue("$b", bayId.Trim());
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RenameBayAsync(string oldBayId, string newBayId, double maxCapacityNuts)
        {
            var oldId = (oldBayId ?? string.Empty).Trim();
            var newId = (newBayId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(oldId) || string.IsNullOrWhiteSpace(newId))
                throw new ArgumentException("Bay id is required.");
            await using var conn = await OpenAsync();
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
            try
            {
                // If the id is unchanged, just update capacity.
                if (string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
                {
                    using var upd = conn.CreateCommand();
                    upd.Transaction = tx;
                    upd.CommandText = "UPDATE fusion_bays SET max_capacity_nuts=$c WHERE bay_id=$b";
                    upd.Parameters.AddWithValue("$c", Math.Max(0, maxCapacityNuts));
                    upd.Parameters.AddWithValue("$b", oldId);
                    await upd.ExecuteNonQueryAsync();
                }
                else
                {
                    using var chk = conn.CreateCommand();
                    chk.Transaction = tx;
                    chk.CommandText = "SELECT COUNT(*) FROM fusion_bays WHERE bay_id=$b COLLATE NOCASE";
                    chk.Parameters.AddWithValue("$b", newId);
                    var exists = Convert.ToInt32(await chk.ExecuteScalarAsync()) > 0;
                    if (exists)
                        throw new InvalidOperationException($"Bay {newId} already exists.");
                    using var cur = conn.CreateCommand();
                    cur.Transaction = tx;
                    cur.CommandText = "SELECT max_capacity_nuts, is_active FROM fusion_bays WHERE bay_id=$b";
                    cur.Parameters.AddWithValue("$b", oldId);
                    double cap = maxCapacityNuts;
                    int active = 1;
                    await using (var r = await cur.ExecuteReaderAsync())
                    {
                        if (await r.ReadAsync())
                        {
                            if (!r.IsDBNull(0)) cap = Math.Max(0, r.GetDouble(0));
                            // is_active column may be missing on very old DBs; default active.
                            try
                            {
                                var ao = r.GetOrdinal("is_active");
                                if (!r.IsDBNull(ao)) active = r.GetInt32(ao) != 0 ? 1 : 0;
                            }
                            catch { /* older schema without is_active */ }
                            cap = Math.Max(0, maxCapacityNuts);
                        }
                    }
                    using var ins = conn.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = "INSERT INTO fusion_bays (bay_id, max_capacity_nuts, is_active) VALUES ($n, $c, $a)";
                    ins.Parameters.AddWithValue("$n", newId);
                    ins.Parameters.AddWithValue("$c", cap);
                    ins.Parameters.AddWithValue("$a", active);
                    await ins.ExecuteNonQueryAsync();
                    using var del = conn.CreateCommand();
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM fusion_bays WHERE bay_id=$b";
                    del.Parameters.AddWithValue("$b", oldId);
                    await del.ExecuteNonQueryAsync();
                    // Keep history coherent: inward rows referencing the old bay id move to the new id.
                    // fusion_inward has no FK to fusion_bays, so this is a safe string update.
                    try
                    {
                        using var mv = conn.CreateCommand();
                        mv.Transaction = tx;
                        mv.CommandText = "UPDATE fusion_inward SET bay_id=$n WHERE bay_id=$b";
                        mv.Parameters.AddWithValue("$n", newId);
                        mv.Parameters.AddWithValue("$b", oldId);
                        await mv.ExecuteNonQueryAsync();
                    }
                    catch { /* inward table may not exist yet */ }
                }
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task EnsureBaysExistAsync(IEnumerable<string> bayIds, double defaultCapacity)
        {
            var distinct = bayIds.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (distinct.Count == 0) return;
            await using var conn = await OpenAsync();
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
            try
            {
                using var mk = conn.CreateCommand();
                mk.Transaction = tx;
                mk.CommandText = "CREATE TABLE IF NOT EXISTS fusion_bays (bay_id TEXT PRIMARY KEY, max_capacity_nuts REAL NOT NULL DEFAULT 0, is_active INTEGER NOT NULL DEFAULT 1);";
                await mk.ExecuteNonQueryAsync();
                foreach (var b in distinct)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT OR IGNORE INTO fusion_bays (bay_id, max_capacity_nuts, is_active) VALUES ($b, $c, 1)";
                    cmd.Parameters.AddWithValue("$b", b);
                    cmd.Parameters.AddWithValue("$c", Math.Max(0, defaultCapacity));
                    await cmd.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // ===== Equipment health (Step 6) =====
        public sealed record FusionEquipment(string Id, string Name, string Status, string LastSeen);

        public async Task<List<FusionEquipment>> GetEquipmentAsync()
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, status, last_seen FROM fusion_equipment ORDER BY name";
            var list = new List<FusionEquipment>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(new FusionEquipment(GetString(reader, "id"), GetString(reader, "name"),
                    GetString(reader, "status"), GetString(reader, "last_seen")));
            return list;
        }

        public async Task UpsertEquipmentAsync(string id, string name, string status)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO fusion_equipment (id, name, status, last_seen)
            VALUES ($id, $name, $status, datetime('now','localtime'))
            ON CONFLICT(id) DO UPDATE SET
                name=excluded.name, status=excluded.status, last_seen=excluded.last_seen;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$name", name ?? string.Empty);
            cmd.Parameters.AddWithValue("$status", status ?? "Online");
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteEquipmentAsync(string id)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM fusion_equipment WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // ===== Support tickets (Step 6) =====
        public sealed record FusionTicket(string Id, string Title, string Status, string CreatedAt);

        public async Task<List<FusionTicket>> GetTicketsAsync(bool openOnly = false)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = openOnly
                ? "SELECT id, title, status, created_at FROM fusion_tickets WHERE status='Open' ORDER BY created_at DESC"
                : "SELECT id, title, status, created_at FROM fusion_tickets ORDER BY created_at DESC";
            var list = new List<FusionTicket>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add(new FusionTicket(GetString(reader, "id"), GetString(reader, "title"),
                    GetString(reader, "status"), GetString(reader, "created_at")));
            return list;
        }

        public async Task AddTicketAsync(string title)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO fusion_tickets (id, title, status, created_at)
            VALUES ($id, $title, 'Open', datetime('now','localtime'));";
            cmd.Parameters.AddWithValue("$id", $"TKT-{DateTime.Now:yyyyMMddHHmmssfff}");
            cmd.Parameters.AddWithValue("$title", title ?? string.Empty);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task SetTicketStatusAsync(string id, string status)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE fusion_tickets SET status=$s WHERE id=$id";
            cmd.Parameters.AddWithValue("$s", status);
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // ===== Reports (Step 6 — date-filtered fusion transactions) =====
        public async Task<List<FusionTransaction>> GetTransactionsByDateAsync(string fromDate, string toDate, int limit = 2000)
        {
            await using var conn = await OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT id, batch_no, tub_id, supplier_id, weight_kg, recorded_at FROM fusion_transactions
            WHERE date(recorded_at) >= date($from) AND date(recorded_at) <= date($to)
            ORDER BY recorded_at DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$from", fromDate);
            cmd.Parameters.AddWithValue("$to", toDate);
            cmd.Parameters.AddWithValue("$n", limit);
            var list = new List<FusionTransaction>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var o = reader.GetOrdinal("supplier_id");
                list.Add(new FusionTransaction
                {
                    Id = GetString(reader, "id"),
                    BatchNo = GetString(reader, "batch_no"),
                    TubId = GetString(reader, "tub_id"),
                    SupplierId = reader.IsDBNull(o) ? 0 : reader.GetInt32(o),
                    WeightKg = GetDouble(reader, "weight_kg"),
                    RecordedAt = GetString(reader, "recorded_at"),
                });
            }
            return list;
        }
    }
}
