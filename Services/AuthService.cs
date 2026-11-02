using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Configuration;

namespace ProWeighSuitePlatforms.Services
{
    public class AuthService
    {
        private readonly string _dbPath;

        public AuthService()
        {
            _dbPath = AppConfigurationManager.GetDatabasePath();

            TryMigrateLegacyDatabases();
            EnsureDatabase();
        }

        private void TryMigrateLegacyDatabases()
        {
            try
            {
                var localApp = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var appDir = Path.Combine(localApp, "ProWeighSuitePlatforms");
                var legacyNames = new[] { "materials.db", "jitscale.db" };

                if (!Directory.Exists(appDir)) return;

                foreach (var legacy in legacyNames)
                {
                    var legacyPath = Path.Combine(appDir, legacy);
                    if (!File.Exists(legacyPath)) continue;

                    // If target already exists, skip
                    if (File.Exists(_dbPath)) break;

                    // Create backup before moving
                    var backupPath = Path.Combine(appDir, legacy + ".bak");
                    File.Copy(legacyPath, backupPath, overwrite: true);

                    // Copy legacy file to new filename
                    File.Copy(legacyPath, _dbPath);
                    break;
                }
            }
            catch
            {
                // ignore
            }
        }

        private void EnsureDatabase()
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    username TEXT NOT NULL UNIQUE COLLATE NOCASE,
    password_hash TEXT NOT NULL,
    role INTEGER NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
";
            cmd.ExecuteNonQuery();

            // Check if created_at, updated_at, and email columns exist (for migration)
            cmd.CommandText = "PRAGMA table_info(users)";
            using var reader = cmd.ExecuteReader();
            var hasCreatedAt = false;
            var hasUpdatedAt = false;
            var hasEmail = false;

            while (reader.Read())
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
                cmd.ExecuteNonQuery();
            }

            if (!hasCreatedAt)
            {
                cmd.CommandText = "ALTER TABLE users ADD COLUMN created_at TEXT";
                cmd.ExecuteNonQuery();

                // Update existing rows with current timestamp
                cmd.CommandText = "UPDATE users SET created_at = datetime('now') WHERE created_at IS NULL";
                cmd.ExecuteNonQuery();
            }

            if (!hasUpdatedAt)
            {
                cmd.CommandText = "ALTER TABLE users ADD COLUMN updated_at TEXT";
                cmd.ExecuteNonQuery();

                // Update existing rows with current timestamp
                cmd.CommandText = "UPDATE users SET updated_at = datetime('now') WHERE updated_at IS NULL";
                cmd.ExecuteNonQuery();
            }

            // Seed default users if table is empty
            cmd.CommandText = "SELECT COUNT(*) FROM users";
            var userCount = Convert.ToInt32(cmd.ExecuteScalar());

            if (userCount == 0)
            {
                using var sha256 = SHA256.Create();

                // Super Admin
                var superHash = Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes("administrator")));
                cmd.CommandText = "INSERT INTO users (username, email, password_hash, role, created_at, updated_at) VALUES (@u, @e, @p, @r, @created, @updated)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@u", "administrator");
                cmd.Parameters.AddWithValue("@e", "admin@proweighsuite.local");
                cmd.Parameters.AddWithValue("@p", superHash);
                cmd.Parameters.AddWithValue("@r", (int)UserRole.SuperAdmin);
                cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();

                // Admin
                var adminHash = Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes("admin")));
                cmd.CommandText = "INSERT INTO users (username, email, password_hash, role, created_at, updated_at) VALUES (@u, @e, @p, @r, @created, @updated)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@u", "admin");
                cmd.Parameters.AddWithValue("@e", "manager@proweighsuite.local");
                cmd.Parameters.AddWithValue("@p", adminHash);
                cmd.Parameters.AddWithValue("@r", (int)UserRole.Admin);
                cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();

                // Operator
                var operatorHash = Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes("operator")));
                cmd.CommandText = "INSERT INTO users (username, email, password_hash, role, created_at, updated_at) VALUES (@u, @e, @p, @r, @created, @updated)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@u", "operator");
                cmd.Parameters.AddWithValue("@e", "operator@proweighsuite.local");
                cmd.Parameters.AddWithValue("@p", operatorHash);
                cmd.Parameters.AddWithValue("@r", (int)UserRole.Operator);
                cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }
        }

        public async Task<User?> AuthenticateAsync(string username, string password)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, username, email, password_hash, role, created_at, updated_at FROM users WHERE username = @u COLLATE NOCASE LIMIT 1";
            cmd.Parameters.AddWithValue("@u", username);

            using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                var storedHash = rdr.GetString(3);
                var inputHash = HashPassword(password);

                if (storedHash == inputHash)
                {
                    return new User
                    {
                        Id = rdr.GetInt32(0),
                        Username = rdr.GetString(1),
                        Email = rdr.IsDBNull(2) ? string.Empty : rdr.GetString(2),
                        PasswordHash = storedHash,
                        Role = (UserRole)rdr.GetInt32(4),
                        CreatedAt = ParseDateTime(rdr, 5),
                        UpdatedAt = ParseDateTime(rdr, 6)
                    };
                }
            }

            return null;
        }

        /// <summary>
        /// Looks up a user by username WITHOUT verifying a password.
        /// Used by the Super Admin OTP login flow to identify the account and its role/email.
        /// </summary>
        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, username, email, password_hash, role, created_at, updated_at FROM users WHERE username = @u COLLATE NOCASE LIMIT 1";
            cmd.Parameters.AddWithValue("@u", username);

            using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                return new User
                {
                    Id = rdr.GetInt32(0),
                    Username = rdr.GetString(1),
                    Email = rdr.IsDBNull(2) ? string.Empty : rdr.GetString(2),
                    PasswordHash = rdr.GetString(3),
                    Role = (UserRole)rdr.GetInt32(4),
                    CreatedAt = ParseDateTime(rdr, 5),
                    UpdatedAt = ParseDateTime(rdr, 6)
                };
            }

            return null;
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Resets the password for a SuperAdmin or Admin user back to its default (username = password).
        /// Only resets users with role SuperAdmin or Admin. Returns true if reset was successful.
        /// </summary>
        public async Task<bool> ResetAdminPasswordAsync(string username)
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            // Verify the user exists and is a SuperAdmin or Admin
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT id, role FROM users WHERE username = @u COLLATE NOCASE LIMIT 1";
            checkCmd.Parameters.AddWithValue("@u", username);

            using var rdr = await checkCmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync())
                return false;

            var role = (UserRole)rdr.GetInt32(1);
            var userId = rdr.GetInt32(0);
            rdr.Close();

            if (role != UserRole.SuperAdmin && role != UserRole.Admin)
                return false;

            // Reset password to the username (default convention)
            var defaultHash = HashPassword(username.ToLowerInvariant());
            using var updateCmd = conn.CreateCommand();
            updateCmd.CommandText = "UPDATE users SET password_hash = @p, updated_at = @updated WHERE id = @id";
            updateCmd.Parameters.AddWithValue("@p", defaultHash);
            updateCmd.Parameters.AddWithValue("@updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            updateCmd.Parameters.AddWithValue("@id", userId);

            var rows = await updateCmd.ExecuteNonQueryAsync();
            return rows > 0;
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