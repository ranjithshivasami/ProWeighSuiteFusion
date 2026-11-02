using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data.Interfaces;
using ProWeighSuitePlatforms.Models;
using Microsoft.Data.Sqlite;

namespace ProWeighSuitePlatforms.Data
{
    public class VariantRepository : IVariantRepository
    {
        private readonly string _connectionString;

        public VariantRepository(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        private SqliteConnection GetConnection() => new SqliteConnection(_connectionString);

        public async Task InitializeAsync()
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();

            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS variants (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    description TEXT,
                    active INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL
                )";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = @"
                SELECT COUNT(*) 
                FROM pragma_table_info('variants') 
                WHERE name='active'";
            var activeObj = await cmd.ExecuteScalarAsync();
            var activeColumnExists = activeObj != null && activeObj != DBNull.Value && (long)activeObj > 0;

            if (!activeColumnExists)
            {
                cmd.CommandText = "ALTER TABLE variants ADD COLUMN active INTEGER NOT NULL DEFAULT 1";
                await cmd.ExecuteNonQueryAsync();
            }

            cmd.CommandText = @"
                SELECT COUNT(*) 
                FROM pragma_table_info('variants') 
                WHERE name='description'";
            var descObj = await cmd.ExecuteScalarAsync();
            var descriptionColumnExists = descObj != null && descObj != DBNull.Value && (long)descObj > 0;

            if (!descriptionColumnExists)
            {
                cmd.CommandText = "ALTER TABLE variants ADD COLUMN description TEXT";
                await cmd.ExecuteNonQueryAsync();
            }

            cmd.CommandText = @"
                SELECT COUNT(*) 
                FROM pragma_table_info('variants') 
                WHERE name='created_at'";
            var createdObj = await cmd.ExecuteScalarAsync();
            var createdAtColumnExists = createdObj != null && createdObj != DBNull.Value && (long)createdObj > 0;

            if (!createdAtColumnExists)
            {
                cmd.CommandText = "ALTER TABLE variants ADD COLUMN created_at TEXT NOT NULL DEFAULT ''";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = @"
                    UPDATE variants 
                    SET created_at = datetime('now') 
                    WHERE created_at = ''";
                await cmd.ExecuteNonQueryAsync();
            }

            cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_variants_name ON variants(LOWER(name))";
            await cmd.ExecuteNonQueryAsync();
        }

        private static readonly System.Collections.Generic.HashSet<string> _tableExistsCache = new(System.StringComparer.OrdinalIgnoreCase);
        private static readonly object _cacheLock = new();

        private async Task<bool> TableExistsAsync(string tableName)
        {
            lock (_cacheLock) { if (_tableExistsCache.Contains(_connectionString + "|" + tableName)) return true; }
            using var conn = GetConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name = $name LIMIT 1";
            cmd.Parameters.AddWithValue("$name", tableName);
            var result = await cmd.ExecuteScalarAsync();
            var exists = result != null && result != DBNull.Value;
            if (exists) lock (_cacheLock) { _tableExistsCache.Add(_connectionString + "|" + tableName); }
            return exists;
        }

        public async Task<List<Variant>> GetAllAsync()
        {
            // If table doesn't exist yet (initialization pending), return empty list instead of throwing
            if (!await TableExistsAsync("variants"))
            {
                return new List<Variant>();
            }

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, name, description, active, created_at
                FROM variants
                ORDER BY name";

            var variants = new List<Variant>();
            using var reader = await cmd.ExecuteReaderAsync();
            int serialNumber = 1;
            while (await reader.ReadAsync())
            {
                variants.Add(new Variant
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    Name = reader.GetString(reader.GetOrdinal("name")),
                    Description = reader.IsDBNull(reader.GetOrdinal("description")) ? string.Empty : reader.GetString(reader.GetOrdinal("description")),
                    Active = reader.GetInt32(reader.GetOrdinal("active")) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
                    SerialNumber = serialNumber++
                });
            }

            return variants;
        }

        public async Task<Variant?> GetByIdAsync(int id)
        {
            if (!await TableExistsAsync("variants"))
                return null;

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, name, description, active, created_at
                FROM variants
                WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new Variant
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    Name = reader.GetString(reader.GetOrdinal("name")),
                    Description = reader.IsDBNull(reader.GetOrdinal("description")) ? string.Empty : reader.GetString(reader.GetOrdinal("description")),
                    Active = reader.GetInt32(reader.GetOrdinal("active")) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")))
                };
            }

            return null;
        }

        public async Task<int> AddAsync(Variant variant)
        {
            EntityTextNormalizer.Trim(variant);

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO variants (name, description, active, created_at)
                VALUES ($name, $description, $active, $createdAt);
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("$name", variant.Name);
            cmd.Parameters.AddWithValue("$description", variant.Description ?? string.Empty);
            cmd.Parameters.AddWithValue("$active", variant.Active ? 1 : 0);
            cmd.Parameters.AddWithValue("$createdAt", variant.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task<int> UpdateAsync(Variant variant)
        {
            EntityTextNormalizer.Trim(variant);

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE variants 
                SET name = $name, description = $description, active = $active
                WHERE id = $id";

            cmd.Parameters.AddWithValue("$id", variant.Id);
            cmd.Parameters.AddWithValue("$name", variant.Name);
            cmd.Parameters.AddWithValue("$description", variant.Description ?? string.Empty);
            cmd.Parameters.AddWithValue("$active", variant.Active ? 1 : 0);

            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> DeleteAsync(int id)
        {
            if (!await TableExistsAsync("variants"))
                return 0;

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM variants WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);

            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<bool> NameExistsAsync(string name, int? excludeId = null)
        {
            if (!await TableExistsAsync("variants"))
                return false;

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();

            if (excludeId.HasValue)
            {
                cmd.CommandText = "SELECT COUNT(1) FROM variants WHERE name = $name COLLATE NOCASE AND id <> $id";
                cmd.Parameters.AddWithValue("$name", name?.Trim() ?? string.Empty);
                cmd.Parameters.AddWithValue("$id", excludeId.Value);
            }
            else
            {
                cmd.CommandText = "SELECT COUNT(1) FROM variants WHERE name = $name COLLATE NOCASE";
                cmd.Parameters.AddWithValue("$name", name?.Trim() ?? string.Empty);
            }

            var result = (long?)await cmd.ExecuteScalarAsync();
            return result > 0;
        }
    }
}
