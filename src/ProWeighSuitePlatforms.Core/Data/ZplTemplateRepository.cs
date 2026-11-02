using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;
using Microsoft.Data.Sqlite;

namespace ProWeighSuitePlatforms.Data
{
    public class ZplTemplateRepository
    {
        private readonly string _dbPath;

        public ZplTemplateRepository(string dbPath)
        {
            _dbPath = dbPath;
        }

        private SqliteConnection GetConnection()
            => new SqliteConnection($"Data Source={_dbPath}");

        public async Task InitializeAsync()
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS zpl_label_templates (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    description TEXT NOT NULL DEFAULT '',
                    zpl_code TEXT NOT NULL,
                    label_width_mm INTEGER NOT NULL DEFAULT 100,
                    label_height_mm INTEGER NOT NULL DEFAULT 50,
                    dpi INTEGER NOT NULL DEFAULT 203,
                    is_active INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    modified_at TEXT
                )";
            await cmd.ExecuteNonQueryAsync();

            // Migration: add dpi column if missing (for older databases)
            await AddColumnIfNotExistsAsync(conn, "zpl_label_templates", "dpi", "INTEGER NOT NULL DEFAULT 203");
        }

        private async Task AddColumnIfNotExistsAsync(SqliteConnection conn, string table, string column, string type)
        {
            try
            {
                var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = $"PRAGMA table_info({table})";
                using var reader = await checkCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                        return;
                }

                var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
                await alterCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Column might already exist
            }
        }

        public async Task<List<ZplLabelTemplate>> GetAllAsync()
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, name, description, zpl_code, label_width_mm, label_height_mm, dpi,
                       is_active, created_at, modified_at
                FROM zpl_label_templates
                ORDER BY name";

            var list = new List<ZplLabelTemplate>();
            using var reader = await cmd.ExecuteReaderAsync();
            int serial = 1;
            while (await reader.ReadAsync())
            {
                list.Add(ReadTemplate(reader, serial++));
            }
            return list;
        }

        public async Task<ZplLabelTemplate?> GetByIdAsync(int id)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, name, description, zpl_code, label_width_mm, label_height_mm, dpi,
                       is_active, created_at, modified_at
                FROM zpl_label_templates
                WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return ReadTemplate(reader);
            return null;
        }

        public async Task<ZplLabelTemplate?> GetActiveAsync()
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, name, description, zpl_code, label_width_mm, label_height_mm, dpi,
                       is_active, created_at, modified_at
                FROM zpl_label_templates
                WHERE is_active = 1
                LIMIT 1";

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return ReadTemplate(reader);
            return null;
        }

        public async Task<int> CreateAsync(ZplLabelTemplate template)
        {
            EntityTextNormalizer.Trim(template);
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO zpl_label_templates
                    (name, description, zpl_code, label_width_mm, label_height_mm, dpi, is_active, created_at)
                VALUES
                    ($name, $desc, $zpl, $w, $h, $dpi, $active, $created);
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("$name", template.Name);
            cmd.Parameters.AddWithValue("$desc", template.Description ?? string.Empty);
            cmd.Parameters.AddWithValue("$zpl", template.ZplCode);
            cmd.Parameters.AddWithValue("$w", template.LabelWidthMm);
            cmd.Parameters.AddWithValue("$h", template.LabelHeightMm);
            cmd.Parameters.AddWithValue("$dpi", template.Dpi);
            cmd.Parameters.AddWithValue("$active", template.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$created", template.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task<int> UpdateAsync(ZplLabelTemplate template)
        {
            EntityTextNormalizer.Trim(template);
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE zpl_label_templates
                SET name = $name,
                    description = $desc,
                    zpl_code = $zpl,
                    label_width_mm = $w,
                    label_height_mm = $h,
                    dpi = $dpi,
                    is_active = $active,
                    modified_at = $modified
                WHERE id = $id";

            cmd.Parameters.AddWithValue("$id", template.Id);
            cmd.Parameters.AddWithValue("$name", template.Name);
            cmd.Parameters.AddWithValue("$desc", template.Description ?? string.Empty);
            cmd.Parameters.AddWithValue("$zpl", template.ZplCode);
            cmd.Parameters.AddWithValue("$w", template.LabelWidthMm);
            cmd.Parameters.AddWithValue("$h", template.LabelHeightMm);
            cmd.Parameters.AddWithValue("$dpi", template.Dpi);
            cmd.Parameters.AddWithValue("$active", template.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$modified", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> DeleteAsync(int id)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM zpl_label_templates WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);

            return await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Sets the given template as active and deactivates all others.
        /// </summary>
        public async Task SetActiveAsync(int id)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            using var tx = conn.BeginTransaction();
            try
            {
                var deactivateCmd = conn.CreateCommand();
                deactivateCmd.Transaction = tx;
                deactivateCmd.CommandText = "UPDATE zpl_label_templates SET is_active = 0";
                await deactivateCmd.ExecuteNonQueryAsync();

                var activateCmd = conn.CreateCommand();
                activateCmd.Transaction = tx;
                activateCmd.CommandText = "UPDATE zpl_label_templates SET is_active = 1 WHERE id = $id";
                activateCmd.Parameters.AddWithValue("$id", id);
                await activateCmd.ExecuteNonQueryAsync();

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public async Task<bool> NameExistsAsync(string name, int? excludeId = null)
        {
            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            if (excludeId.HasValue)
            {
                cmd.CommandText = "SELECT COUNT(1) FROM zpl_label_templates WHERE LOWER(name) = LOWER($name) AND id <> $id";
                cmd.Parameters.AddWithValue("$name", name);
                cmd.Parameters.AddWithValue("$id", excludeId.Value);
            }
            else
            {
                cmd.CommandText = "SELECT COUNT(1) FROM zpl_label_templates WHERE LOWER(name) = LOWER($name)";
                cmd.Parameters.AddWithValue("$name", name);
            }

            var result = (long?)await cmd.ExecuteScalarAsync();
            return result > 0;
        }

        private ZplLabelTemplate ReadTemplate(SqliteDataReader reader, int serial = 0)
        {
            return new ZplLabelTemplate
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                ZplCode = reader.GetString(3),
                LabelWidthMm = reader.GetInt32(4),
                LabelHeightMm = reader.GetInt32(5),
                Dpi = reader.GetInt32(6),
                IsActive = reader.GetInt32(7) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(8)),
                ModifiedAt = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9)),
                SerialNumber = serial
            };
        }
    }
}
