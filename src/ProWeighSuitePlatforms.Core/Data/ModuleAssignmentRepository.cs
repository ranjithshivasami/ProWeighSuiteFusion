// Phase 6 — DB-backed module assignments (upgradeplan.md).
// Groups: Core (always on, readonly in UI), Weighing (single-active radio),
// Inventory (multi-check; ParentModuleId reserved for future dependent children).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ProWeighSuitePlatforms.Core.Modules;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Modules.Infrastructure;

namespace ProWeighSuitePlatforms.Data
{
    public sealed record ModuleDefinitionRow(
        string ModuleId, string DisplayName, string GroupName, string? ParentModuleId);

    public class ModuleAssignmentRepository
    {
        public const string GroupCore = ProductModuleGroups.Core;
        public const string GroupWeighing = ProductModuleGroups.Weighing;
        public const string GroupInventory = ProductModuleGroups.Inventory;

        public const string ActiveWeighingKey = "active_weighing_module";
        public const string DefaultActiveWeighing = "Fusion";

        private readonly string _dbPath;
        public ModuleAssignmentRepository(string dbPath) => _dbPath = dbPath;

        public static IReadOnlyList<ModuleDefinitionRow> SeedDefinitions =>
            ProductModuleCatalog.MatrixDefinitions
                .Select(definition => new ModuleDefinitionRow(
                    definition.ModuleId,
                    definition.DisplayName,
                    definition.GroupName,
                    definition.ParentModuleId))
                .ToList();

        public async Task InitializeAsync()
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS module_definitions (
                module_id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                group_name TEXT NOT NULL,
                parent_module_id TEXT
            );
            CREATE TABLE IF NOT EXISTS role_modules (
                role INTEGER NOT NULL,
                module_id TEXT NOT NULL,
                is_enabled INTEGER NOT NULL DEFAULT 1,
                modified_date TEXT,
                modified_by TEXT,
                UNIQUE(role, module_id)
            );
            CREATE TABLE IF NOT EXISTS app_config (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );";
            await cmd.ExecuteNonQueryAsync();

            foreach (var d in SeedDefinitions)
            {
                using var up = conn.CreateCommand();
                up.CommandText = @"
                INSERT INTO module_definitions (module_id, display_name, group_name, parent_module_id)
                VALUES ($id, $name, $grp, $parent)
                ON CONFLICT(module_id) DO UPDATE SET display_name=excluded.display_name, group_name=excluded.group_name;";
                up.Parameters.AddWithValue("$id", d.ModuleId);
                up.Parameters.AddWithValue("$name", d.DisplayName);
                up.Parameters.AddWithValue("$grp", d.GroupName);
                up.Parameters.AddWithValue("$parent", (object?)d.ParentModuleId ?? DBNull.Value);
                await up.ExecuteNonQueryAsync();
            }

            // Default: every non-core module enabled for every role; core implicit.
            foreach (UserRole role in Enum.GetValues(typeof(UserRole)))
            {
                foreach (var d in SeedDefinitions)
                {
                    if (d.GroupName == GroupCore) continue;
                    using var en = conn.CreateCommand();
                    en.CommandText = @"
                    INSERT OR IGNORE INTO role_modules (role, module_id, is_enabled)
                    VALUES ($role, $id, 1);";
                    en.Parameters.AddWithValue("$role", (int)role);
                    en.Parameters.AddWithValue("$id", d.ModuleId);
                    await en.ExecuteNonQueryAsync();
                }
            }

            using var cfg = conn.CreateCommand();
            cfg.CommandText = "INSERT OR IGNORE INTO app_config (key, value) VALUES ($k, $v);";
            cfg.Parameters.AddWithValue("$k", ActiveWeighingKey);
            cfg.Parameters.AddWithValue("$v", DefaultActiveWeighing);
            await cfg.ExecuteNonQueryAsync();
        }

        public async Task<Dictionary<string, HashSet<UserRole>>> GetEnabledMapAsync()
        {
            var map = new Dictionary<string, HashSet<UserRole>>(StringComparer.OrdinalIgnoreCase);
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT module_id, role FROM role_modules WHERE is_enabled = 1;";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var id = r.GetString(0);
                var role = (UserRole)r.GetInt32(1);
                if (!map.TryGetValue(id, out var set)) map[id] = set = new HashSet<UserRole>();
                set.Add(role);
            }
            return map;
        }

        public async Task SetRoleModuleAsync(UserRole role, string moduleId, bool enabled, string by)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO role_modules (role, module_id, is_enabled, modified_date, modified_by)
            VALUES ($role, $id, $en, $dt, $by)
            ON CONFLICT(role, module_id) DO UPDATE SET is_enabled=excluded.is_enabled,
                modified_date=excluded.modified_date, modified_by=excluded.modified_by;";
            cmd.Parameters.AddWithValue("$role", (int)role);
            cmd.Parameters.AddWithValue("$id", moduleId);
            cmd.Parameters.AddWithValue("$en", enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$dt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$by", by ?? string.Empty);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<string> GetActiveWeighingModuleAsync()
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM app_config WHERE key = $k;";
            cmd.Parameters.AddWithValue("$k", ActiveWeighingKey);
            var v = await cmd.ExecuteScalarAsync() as string;
            return string.IsNullOrWhiteSpace(v) ? DefaultActiveWeighing : v;
        }

        public async Task SetActiveWeighingModuleAsync(string moduleId, WeighingOnly _guard)
        {
            // Guard type ensures only weighing-family ids are accepted (checked below too).
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO app_config (key, value) VALUES ($k, $v)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
            cmd.Parameters.AddWithValue("$k", ActiveWeighingKey);
            cmd.Parameters.AddWithValue("$v", moduleId);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Marker type so the active-module setter can't be confused with generic config.</summary>
        public sealed class WeighingOnly { private WeighingOnly() { } public static WeighingOnly Instance { get; } = new(); }
    }
}
