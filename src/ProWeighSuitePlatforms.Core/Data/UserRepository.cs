using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace ProWeighSuitePlatforms.Data
{
    public interface IUserRepository
    {
        Task<int> CreateAsync(User user);
        Task<User?> GetAsync(int id);
        Task<User?> GetByUsernameAsync(string username);
        Task<IEnumerable<User>> GetAllAsync();
        Task<int> UpdateAsync(User user);
        Task<int> DeleteAsync(int id);
        Task<bool> UsernameExistsAsync(string username, int? excludeUserId = null);
        Task<bool> EmailExistsAsync(string email, int? excludeUserId = null);
        Task<int> GetUserCountAsync();
        Task InitializeAsync();
    }

    public class UserRepository : IUserRepository
    {
        private readonly string _dbPath;

        public UserRepository(string dbPath)
        {
            _dbPath = dbPath;
        }

        public async Task InitializeAsync()
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS users (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    password_hash TEXT NOT NULL,
                    role INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );";
            await cmd.ExecuteNonQueryAsync();

            // Check if created_at and updated_at columns exist
            cmd.CommandText = "PRAGMA table_info(users)";
            using var reader = await cmd.ExecuteReaderAsync();
            var hasCreatedAt = false;
            var hasUpdatedAt = false;
            var hasEmail = false;
            
            while (await reader.ReadAsync())
            {
                var colName = reader.GetString(1);
                if (colName == "created_at") hasCreatedAt = true;
                if (colName == "updated_at") hasUpdatedAt = true;
                if (colName == "email") hasEmail = true;
            }
            reader.Close();

            if (!hasEmail)
            {
                cmd.CommandText = "ALTER TABLE users ADD COLUMN email TEXT NOT NULL DEFAULT ''";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!hasCreatedAt)
            {
                cmd.CommandText = "ALTER TABLE users ADD COLUMN created_at TEXT";
                await cmd.ExecuteNonQueryAsync();
                
                // Update existing rows with current timestamp
                cmd.CommandText = "UPDATE users SET created_at = datetime('now') WHERE created_at IS NULL";
                await cmd.ExecuteNonQueryAsync();
            }

            if (!hasUpdatedAt)
            {
                cmd.CommandText = "ALTER TABLE users ADD COLUMN updated_at TEXT";
                await cmd.ExecuteNonQueryAsync();
                
                // Update existing rows with current timestamp
                cmd.CommandText = "UPDATE users SET updated_at = datetime('now') WHERE updated_at IS NULL";
                await cmd.ExecuteNonQueryAsync();
            }

            // Seed default users if table is empty
            cmd.CommandText = "SELECT COUNT(*) FROM users";
            var userCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            
            if (userCount == 0)
            {
                // Use SHA256 hashing for passwords
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                
                // Super Admin
                var superHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes("administrator")));
                cmd.CommandText = "INSERT INTO users (username, email, password_hash, role, created_at, updated_at) VALUES (@u, @e, @p, @r, @created, @updated)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@u", "administrator");
                cmd.Parameters.AddWithValue("@e", "admin@proweighsuite.local");
                cmd.Parameters.AddWithValue("@p", superHash);
                cmd.Parameters.AddWithValue("@r", (int)UserRole.SuperAdmin);
                cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                await cmd.ExecuteNonQueryAsync();

                // Admin
                var adminHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes("admin")));
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@u", "admin");
                cmd.Parameters.AddWithValue("@e", "manager@proweighsuite.local");
                cmd.Parameters.AddWithValue("@p", adminHash);
                cmd.Parameters.AddWithValue("@r", (int)UserRole.Admin);
                cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                await cmd.ExecuteNonQueryAsync();

                // Operator
                var operatorHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes("operator")));
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@u", "operator");
                cmd.Parameters.AddWithValue("@e", "operator@proweighsuite.local");
                cmd.Parameters.AddWithValue("@p", operatorHash);
                cmd.Parameters.AddWithValue("@r", (int)UserRole.Operator);
                cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<int> CreateAsync(User user)
        {
            EntityTextNormalizer.Trim(user);
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO users (username, email, password_hash, role, created_at, updated_at)
                VALUES (@username, @email, @hash, @role, @created, @updated);
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@username", user.Username);
            cmd.Parameters.AddWithValue("@email", user.Email);
            cmd.Parameters.AddWithValue("@hash", user.PasswordHash);
            cmd.Parameters.AddWithValue("@role", (int)user.Role);
            cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            var id = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(id);
        }

        public async Task<User?> GetAsync(int id)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, username, email, password_hash, role, created_at, updated_at FROM users WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new User
                {
                    Id = reader.GetInt32(0),
                    Username = reader.GetString(1),
                    Email = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    PasswordHash = reader.GetString(3),
                    Role = (UserRole)reader.GetInt32(4),
                    CreatedAt = ParseDateTime(reader, 5),
                    UpdatedAt = ParseDateTime(reader, 6)
                };
            }

            return null;
        }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, username, email, password_hash, role, created_at, updated_at FROM users WHERE username = @username COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@username", username);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new User
                {
                    Id = reader.GetInt32(0),
                    Username = reader.GetString(1),
                    Email = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    PasswordHash = reader.GetString(3),
                    Role = (UserRole)reader.GetInt32(4),
                    CreatedAt = ParseDateTime(reader, 5),
                    UpdatedAt = ParseDateTime(reader, 6)
                };
            }

            return null;
        }

        public async Task<IEnumerable<User>> GetAllAsync()
        {
            var users = new List<User>();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, username, email, password_hash, role, created_at, updated_at FROM users ORDER BY username";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                users.Add(new User
                {
                    Id = reader.GetInt32(0),
                    Username = reader.GetString(1),
                    Email = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    PasswordHash = reader.GetString(3),
                    Role = (UserRole)reader.GetInt32(4),
                    CreatedAt = ParseDateTime(reader, 5),
                    UpdatedAt = ParseDateTime(reader, 6)
                });
            }

            return users;
        }

        public async Task<int> UpdateAsync(User user)
        {
            EntityTextNormalizer.Trim(user);
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE users 
                SET username = @username,
                    email = @email,
                    password_hash = @hash,
                    role = @role,
                    updated_at = @updated
                WHERE id = @id";

            cmd.Parameters.AddWithValue("@id", user.Id);
            cmd.Parameters.AddWithValue("@username", user.Username);
            cmd.Parameters.AddWithValue("@email", user.Email);
            cmd.Parameters.AddWithValue("@hash", user.PasswordHash);
            cmd.Parameters.AddWithValue("@role", (int)user.Role);
            cmd.Parameters.AddWithValue("@updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> DeleteAsync(int id)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM users WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);

            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<bool> UsernameExistsAsync(string username, int? excludeUserId = null)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();

            if (excludeUserId.HasValue)
            {
                cmd.CommandText = "SELECT COUNT(*) FROM users WHERE username = @username COLLATE NOCASE AND id != @excludeId";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@excludeId", excludeUserId.Value);
            }
            else
            {
                cmd.CommandText = "SELECT COUNT(*) FROM users WHERE username = @username COLLATE NOCASE";
                cmd.Parameters.AddWithValue("@username", username);
            }

            var count = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(count) > 0;
        }

        public async Task<bool> EmailExistsAsync(string email, int? excludeUserId = null)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();

            if (excludeUserId.HasValue)
            {
                cmd.CommandText = "SELECT COUNT(*) FROM users WHERE email = @email COLLATE NOCASE AND id != @excludeId";
                cmd.Parameters.AddWithValue("@email", email);
                cmd.Parameters.AddWithValue("@excludeId", excludeUserId.Value);
            }
            else
            {
                cmd.CommandText = "SELECT COUNT(*) FROM users WHERE email = @email COLLATE NOCASE";
                cmd.Parameters.AddWithValue("@email", email);
            }

            var count = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(count) > 0;
        }

        public async Task<int> GetUserCountAsync()
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM users";

            var count = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(count);
        }

        private DateTime ParseDateTime(SqliteDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
                return DateTime.Now;

            var value = reader.GetString(index);
            if (DateTime.TryParse(value, out var dt))
                return dt;

            return DateTime.Now;
        }
    }
}
