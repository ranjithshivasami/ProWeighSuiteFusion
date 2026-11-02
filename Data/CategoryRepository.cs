using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data.Interfaces;
using ProWeighSuitePlatforms.Models;
using Microsoft.Data.Sqlite;

namespace ProWeighSuitePlatforms.Data
{
    public class CategoryRepository : ICategoryRepository
    {
        private readonly string _connectionString;

        public CategoryRepository(string dbPath)
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
                CREATE TABLE IF NOT EXISTS categories (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    description TEXT,
                    status INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL
                )";
            await cmd.ExecuteNonQueryAsync();

            // Check if old 'Code' column exists and drop it
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('categories') 
            WHERE name='Code';
            ";
            var codeColumnExists = (long?)await cmd.ExecuteScalarAsync() > 0;

            if (codeColumnExists)
            {
                // SQLite doesn't support DROP COLUMN, so we need to recreate the table
                cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS categories_new (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    description TEXT,
                    status INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL
                );
                
                INSERT INTO categories_new (id, name, description, status, created_at)
                SELECT id, name, description, 
                       COALESCE(status, 1), 
                       COALESCE(created_at, datetime('now'))
                FROM categories;
                
                DROP TABLE categories;
                
                ALTER TABLE categories_new RENAME TO categories;
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migration: Add status column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('categories') 
            WHERE name='status';
            ";
            var columnExists = (long?)await cmd.ExecuteScalarAsync() > 0;

            if (!columnExists)
            {
                cmd.CommandText = @"
                ALTER TABLE categories 
                ADD COLUMN status INTEGER NOT NULL DEFAULT 1;
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // Migration: Add created_at column if it doesn't exist
            cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM pragma_table_info('categories') 
            WHERE name='created_at';
            ";
            var createdAtExists = (long?)await cmd.ExecuteScalarAsync() > 0;

            if (!createdAtExists)
            {
                cmd.CommandText = @"
                ALTER TABLE categories 
                ADD COLUMN created_at TEXT NOT NULL DEFAULT '';
                ";
                await cmd.ExecuteNonQueryAsync();

                // Update existing records with current timestamp
                cmd.CommandText = @"
                UPDATE categories 
                SET created_at = datetime('now') 
                WHERE created_at = '';
                ";
                await cmd.ExecuteNonQueryAsync();
            }
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

        public async Task<List<Category>> GetAllAsync()
        {
            if (!await TableExistsAsync("categories"))
            {
                return new List<Category>();
            }

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, name, description, status, created_at
                FROM categories
                ORDER BY name";

            var categories = new List<Category>();
            using var reader = await cmd.ExecuteReaderAsync();
            int serialNumber = 1;
            while (await reader.ReadAsync())
            {
                categories.Add(new Category
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Status = reader.GetInt32(3) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(4)),
                    SerialNumber = serialNumber++
                });
            }

            return categories;
        }

        public async Task<Category?> GetByIdAsync(int id)
        {
            if (!await TableExistsAsync("categories"))
                return null;

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, name, description, status, created_at
                FROM categories
                WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new Category
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Status = reader.GetInt32(3) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(4))
                };
            }

            return null;
        }

        public async Task<int> AddAsync(Category category)
        {
            EntityTextNormalizer.Trim(category);

            // Ensure table exists before inserting
            if (!await TableExistsAsync("categories"))
            {
                await InitializeAsync();
            }

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO categories (name, description, status, created_at)
                VALUES ($name, $description, $status, $createdAt);
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("$name", category.Name);
            cmd.Parameters.AddWithValue("$description", category.Description ?? string.Empty);
            cmd.Parameters.AddWithValue("$status", category.Status ? 1 : 0);
            cmd.Parameters.AddWithValue("$createdAt", category.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task<int> UpdateAsync(Category category)
        {
            EntityTextNormalizer.Trim(category);

            if (!await TableExistsAsync("categories"))
            {
                // nothing to update
                return 0;
            }

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE categories 
                SET name = $name, description = $description, status = $status
                WHERE id = $id";

            cmd.Parameters.AddWithValue("$id", category.Id);
            cmd.Parameters.AddWithValue("$name", category.Name);
            cmd.Parameters.AddWithValue("$description", category.Description ?? string.Empty);
            cmd.Parameters.AddWithValue("$status", category.Status ? 1 : 0);

            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> DeleteAsync(int id)
        {
            if (!await TableExistsAsync("categories"))
                return 0;

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM categories WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);

            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task SaveAsync(Category category)
        {
            if (category.Id > 0)
            {
                await UpdateAsync(category);
            }
            else
            {
                await AddAsync(category);
            }
        }

        public async Task<bool> NameExistsAsync(string name, int? excludeId = null)
        {
            if (!await TableExistsAsync("categories"))
                return false;

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();

            if (excludeId.HasValue)
            {
                cmd.CommandText = "SELECT COUNT(1) FROM categories WHERE name = $name COLLATE NOCASE AND id <> $id";
                cmd.Parameters.AddWithValue("$name", name?.Trim() ?? string.Empty);
                cmd.Parameters.AddWithValue("$id", excludeId.Value);
            }
            else
            {
                cmd.CommandText = "SELECT COUNT(1) FROM categories WHERE name = $name COLLATE NOCASE";
                cmd.Parameters.AddWithValue("$name", name?.Trim() ?? string.Empty);
            }

            var result = (long?)await cmd.ExecuteScalarAsync();
            return result > 0;
        }

        // Implement IRepository<Category> methods
        public async Task<int> CreateAsync(Category entity) => await AddAsync(entity);
        public async Task<Category?> GetAsync(int id) => await GetByIdAsync(id);
        async Task<IEnumerable<Category>> IRepository<Category>.GetAllAsync() => await GetAllAsync();
    }
}
