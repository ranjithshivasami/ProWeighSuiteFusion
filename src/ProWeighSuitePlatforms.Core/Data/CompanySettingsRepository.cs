using System;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;
using Microsoft.Data.Sqlite;

namespace ProWeighSuitePlatforms.Data
{
    public class CompanySettingsRepository
    {
        private readonly string _connectionString;

        public CompanySettingsRepository(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        public async Task InitializeAsync()
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            
            // Check if table exists
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='company_settings'";
            var tableExists = await cmd.ExecuteScalarAsync() != null;

            if (!tableExists)
            {
                // Create new table with all columns
                cmd.CommandText = @"
                    CREATE TABLE company_settings (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        company_name TEXT NOT NULL,
                        material_code_prefix TEXT NOT NULL,
                        address TEXT NOT NULL DEFAULT '',
                        gst_no TEXT NOT NULL DEFAULT '',
                        contact TEXT NOT NULL DEFAULT '',
                        report_email TEXT NOT NULL DEFAULT '',
                        created_at TEXT NOT NULL,
                        updated_at TEXT NOT NULL
                    )";
                await cmd.ExecuteNonQueryAsync();

                // Insert default company settings
                cmd.CommandText = @"
                    INSERT INTO company_settings (company_name, material_code_prefix, address, gst_no, contact, report_email, created_at, updated_at)
                    VALUES ('Your Company Name', 'MAT', '', '', '', '', datetime('now'), datetime('now'))";
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                // Table exists, check and add missing columns
                await AddColumnIfNotExistsAsync(conn, "company_settings", "address", "TEXT NOT NULL DEFAULT ''");
                await AddColumnIfNotExistsAsync(conn, "company_settings", "gst_no", "TEXT NOT NULL DEFAULT ''");
                await AddColumnIfNotExistsAsync(conn, "company_settings", "contact", "TEXT NOT NULL DEFAULT ''");
                await AddColumnIfNotExistsAsync(conn, "company_settings", "report_email", "TEXT NOT NULL DEFAULT ''");

                // Ensure at least one row exists (for existing databases)
                cmd.CommandText = "SELECT COUNT(*) FROM company_settings";
                var countObj = await cmd.ExecuteScalarAsync();
                var count = countObj == null || countObj == DBNull.Value ? 0L : (long)countObj;
                if (count == 0)
                {
                    cmd.CommandText = @"
                        INSERT INTO company_settings (company_name, material_code_prefix, address, gst_no, contact, report_email, created_at, updated_at)
                        VALUES ('Your Company Name', 'MAT', '', '', '', '', datetime('now'), datetime('now'))";
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task AddColumnIfNotExistsAsync(SqliteConnection conn, string tableName, string columnName, string columnDefinition)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({tableName})";
            
            using var reader = await cmd.ExecuteReaderAsync();
            bool columnExists = false;
            
            while (await reader.ReadAsync())
            {
                if (reader.GetString(1) == columnName)
                {
                    columnExists = true;
                    break;
                }
            }
            reader.Close();

            if (!columnExists)
            {
                cmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<CompanySettings?> GetAsync()
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, company_name, material_code_prefix, address, gst_no, contact, report_email, created_at, updated_at
                FROM company_settings
                LIMIT 1";

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new CompanySettings
                {
                    Id = reader.GetInt32(0),
                    CompanyName = reader.GetString(1),
                    MaterialCodePrefix = reader.GetString(2),
                    Address = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    GSTNo = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Contact = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    ReportEmail = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    CreatedAt = DateTime.Parse(reader.GetString(7)),
                    UpdatedAt = DateTime.Parse(reader.GetString(8))
                };
            }

            return null;
        }

        public async Task<int> SaveAsync(CompanySettings settings)
        {
            EntityTextNormalizer.Trim(settings);
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var existing = await GetAsync();

            var cmd = conn.CreateCommand();
            if (existing == null)
            {
                cmd.CommandText = @"
                    INSERT INTO company_settings (company_name, material_code_prefix, address, gst_no, contact, report_email, created_at, updated_at)
                    VALUES ($companyName, $materialCodePrefix, $address, $gstNo, $contact, $reportEmail, $createdAt, $updatedAt);
                    SELECT last_insert_rowid();";

                cmd.Parameters.AddWithValue("$companyName", settings.CompanyName);
                cmd.Parameters.AddWithValue("$materialCodePrefix", settings.MaterialCodePrefix);
                cmd.Parameters.AddWithValue("$address", settings.Address);
                cmd.Parameters.AddWithValue("$gstNo", settings.GSTNo);
                cmd.Parameters.AddWithValue("$contact", settings.Contact);
                cmd.Parameters.AddWithValue("$reportEmail", settings.ReportEmail);
                cmd.Parameters.AddWithValue("$createdAt", settings.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("$updatedAt", settings.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"));

                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            else
            {
                cmd.CommandText = @"
                    UPDATE company_settings 
                    SET company_name = $companyName,
                        material_code_prefix = $materialCodePrefix,
                        address = $address,
                        gst_no = $gstNo,
                        contact = $contact,
                        report_email = $reportEmail,
                        updated_at = $updatedAt
                    WHERE id = $id";

                cmd.Parameters.AddWithValue("$id", existing.Id);
                cmd.Parameters.AddWithValue("$companyName", settings.CompanyName);
                cmd.Parameters.AddWithValue("$materialCodePrefix", settings.MaterialCodePrefix);
                cmd.Parameters.AddWithValue("$address", settings.Address);
                cmd.Parameters.AddWithValue("$gstNo", settings.GSTNo);
                cmd.Parameters.AddWithValue("$contact", settings.Contact);
                cmd.Parameters.AddWithValue("$reportEmail", settings.ReportEmail);
                cmd.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                return await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}
