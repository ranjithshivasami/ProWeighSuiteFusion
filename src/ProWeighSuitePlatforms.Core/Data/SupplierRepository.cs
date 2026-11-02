using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using ProWeighSuitePlatforms.Data.Interfaces;
using ProWeighSuitePlatforms.Models;
using Microsoft.Data.Sqlite;

namespace ProWeighSuitePlatforms.Data
{
    public class SupplierRepository : ISupplierRepository
    {
        private readonly string _connectionString;

        public SupplierRepository(string dbPath)
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
                CREATE TABLE IF NOT EXISTS suppliers (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    supplier_code TEXT NOT NULL,
                    supplier_name TEXT NOT NULL,
                    display_name TEXT,
                    supplier_type TEXT NOT NULL,
                    business_category TEXT,
                    status INTEGER NOT NULL DEFAULT 1,
                    legal_entity_type TEXT,
                    registration_number TEXT,
                    tax_id TEXT NOT NULL,
                    website TEXT,
                    contact_person TEXT NOT NULL,
                    designation TEXT,
                    phone TEXT NOT NULL,
                    mobile TEXT,
                    email TEXT NOT NULL,
                    created_at TEXT NOT NULL
                )";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_suppliers_code ON suppliers(LOWER(supplier_code))";
            await cmd.ExecuteNonQueryAsync();

            // Lightweight migrations for existing DBs (older installs without new columns)
            await AddColumnIfNotExistsAsync(conn, "suppliers", "display_name", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "suppliers", "supplier_type", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "suppliers", "business_category", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "suppliers", "status", "INTEGER NOT NULL DEFAULT 1");
            await AddColumnIfNotExistsAsync(conn, "suppliers", "legal_entity_type", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "suppliers", "registration_number", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "suppliers", "tax_id", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "suppliers", "website", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "suppliers", "contact_person", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "suppliers", "designation", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "suppliers", "phone", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "suppliers", "mobile", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "suppliers", "email", "TEXT");
            await AddColumnIfNotExistsAsync(conn, "suppliers", "created_at", "TEXT");
        }

        private static async Task AddColumnIfNotExistsAsync(SqliteConnection conn, string table, string column, string type)
        {
            var check = conn.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info(@t) WHERE name = @c";
            check.Parameters.AddWithValue("@t", table);
            check.Parameters.AddWithValue("@c", column);
            var exists = Convert.ToInt64(await check.ExecuteScalarAsync() ?? 0L) > 0;
            if (!exists)
            {
                var alter = conn.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
                await alter.ExecuteNonQueryAsync();
            }
        }

        private static readonly HashSet<string> _tableExistsCache = new(StringComparer.OrdinalIgnoreCase);
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

        private static Supplier MapReader(SqliteDataReader r)
        {
            int Ord(string n) => r.GetOrdinal(n);
            string Get(string n) => r.IsDBNull(Ord(n)) ? string.Empty : r.GetString(Ord(n));
            return new Supplier
            {
                Id = r.GetInt32(Ord("id")),
                SupplierCode = Get("supplier_code"),
                SupplierName = Get("supplier_name"),
                DisplayName = Get("display_name"),
                SupplierType = Get("supplier_type"),
                BusinessCategory = Get("business_category"),
                Status = !r.IsDBNull(Ord("status")) && r.GetInt32(Ord("status")) == 1,
                LegalEntityType = Get("legal_entity_type"),
                RegistrationNumber = Get("registration_number"),
                TaxId = Get("tax_id"),
                Website = Get("website"),
                ContactPerson = Get("contact_person"),
                Designation = Get("designation"),
                Phone = Get("phone"),
                Mobile = Get("mobile"),
                Email = Get("email"),
                CreatedAt = r.IsDBNull(Ord("created_at"))
                    ? DateTime.Now
                    : DateTime.TryParse(r.GetString(Ord("created_at")), out var dt) ? dt : DateTime.Now
            };
        }

        public async Task<List<Supplier>> GetAllAsync()
        {
            if (!await TableExistsAsync("suppliers"))
                return new List<Supplier>();

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM suppliers ORDER BY supplier_name";
            var list = new List<Supplier>();
            using var reader = await cmd.ExecuteReaderAsync();
            int sn = 1;
            while (await reader.ReadAsync())
            {
                var s = MapReader(reader);
                s.SerialNumber = sn++;
                list.Add(s);
            }
            return list;
        }

        async Task<IEnumerable<Supplier>> IRepository<Supplier>.GetAllAsync() => await GetAllAsync();

        public async Task<Supplier?> GetByIdAsync(int id)
        {
            if (!await TableExistsAsync("suppliers"))
                return null;
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM suppliers WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return MapReader(reader);
            return null;
        }

        public async Task<Supplier?> GetAsync(int id) => await GetByIdAsync(id);

        public async Task<int> CreateAsync(Supplier entity) => await AddAsync(entity);

        public async Task<int> AddAsync(Supplier s)
        {
            EntityTextNormalizer.Trim(s);
            if (!await TableExistsAsync("suppliers"))
                await InitializeAsync();
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO suppliers (supplier_code, supplier_name, display_name, supplier_type, business_category,
                    status, legal_entity_type, registration_number, tax_id, website,
                    contact_person, designation, phone, mobile, email, created_at)
                VALUES ($code, $name, $display, $type, $bizcat,
                    $status, $legal, $regno, $tax, $web,
                    $contact, $desig, $phone, $mobile, $email, $created);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$code", s.SupplierCode ?? string.Empty);
            cmd.Parameters.AddWithValue("$name", s.SupplierName ?? string.Empty);
            cmd.Parameters.AddWithValue("$display", s.DisplayName ?? string.Empty);
            cmd.Parameters.AddWithValue("$type", s.SupplierType ?? string.Empty);
            cmd.Parameters.AddWithValue("$bizcat", s.BusinessCategory ?? string.Empty);
            cmd.Parameters.AddWithValue("$status", s.Status ? 1 : 0);
            cmd.Parameters.AddWithValue("$legal", s.LegalEntityType ?? string.Empty);
            cmd.Parameters.AddWithValue("$regno", s.RegistrationNumber ?? string.Empty);
            cmd.Parameters.AddWithValue("$tax", s.TaxId ?? string.Empty);
            cmd.Parameters.AddWithValue("$web", s.Website ?? string.Empty);
            cmd.Parameters.AddWithValue("$contact", s.ContactPerson ?? string.Empty);
            cmd.Parameters.AddWithValue("$desig", s.Designation ?? string.Empty);
            cmd.Parameters.AddWithValue("$phone", s.Phone ?? string.Empty);
            cmd.Parameters.AddWithValue("$mobile", s.Mobile ?? string.Empty);
            cmd.Parameters.AddWithValue("$email", s.Email ?? string.Empty);
            cmd.Parameters.AddWithValue("$created", s.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task<int> UpdateAsync(Supplier s)
        {
            EntityTextNormalizer.Trim(s);
            if (!await TableExistsAsync("suppliers"))
                return 0;
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE suppliers SET
                    supplier_code = $code, supplier_name = $name, display_name = $display,
                    supplier_type = $type, business_category = $bizcat, status = $status,
                    legal_entity_type = $legal, registration_number = $regno, tax_id = $tax, website = $web,
                    contact_person = $contact, designation = $desig, phone = $phone, mobile = $mobile, email = $email
                WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", s.Id);
            cmd.Parameters.AddWithValue("$code", s.SupplierCode ?? string.Empty);
            cmd.Parameters.AddWithValue("$name", s.SupplierName ?? string.Empty);
            cmd.Parameters.AddWithValue("$display", s.DisplayName ?? string.Empty);
            cmd.Parameters.AddWithValue("$type", s.SupplierType ?? string.Empty);
            cmd.Parameters.AddWithValue("$bizcat", s.BusinessCategory ?? string.Empty);
            cmd.Parameters.AddWithValue("$status", s.Status ? 1 : 0);
            cmd.Parameters.AddWithValue("$legal", s.LegalEntityType ?? string.Empty);
            cmd.Parameters.AddWithValue("$regno", s.RegistrationNumber ?? string.Empty);
            cmd.Parameters.AddWithValue("$tax", s.TaxId ?? string.Empty);
            cmd.Parameters.AddWithValue("$web", s.Website ?? string.Empty);
            cmd.Parameters.AddWithValue("$contact", s.ContactPerson ?? string.Empty);
            cmd.Parameters.AddWithValue("$desig", s.Designation ?? string.Empty);
            cmd.Parameters.AddWithValue("$phone", s.Phone ?? string.Empty);
            cmd.Parameters.AddWithValue("$mobile", s.Mobile ?? string.Empty);
            cmd.Parameters.AddWithValue("$email", s.Email ?? string.Empty);
            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> DeleteAsync(int id)
        {
            if (!await TableExistsAsync("suppliers"))
                return 0;
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM suppliers WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<bool> CodeExistsAsync(string code, int? excludeId = null)
        {
            if (!await TableExistsAsync("suppliers"))
                return false;
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            if (excludeId.HasValue)
            {
                cmd.CommandText = "SELECT COUNT(1) FROM suppliers WHERE supplier_code = $c COLLATE NOCASE AND id <> $id";
                cmd.Parameters.AddWithValue("$c", code?.Trim() ?? string.Empty);
                cmd.Parameters.AddWithValue("$id", excludeId.Value);
            }
            else
            {
                cmd.CommandText = "SELECT COUNT(1) FROM suppliers WHERE supplier_code = $c COLLATE NOCASE";
                cmd.Parameters.AddWithValue("$c", code?.Trim() ?? string.Empty);
            }
            return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L) > 0;
        }

        public async Task<string> GenerateNextCodeAsync(string prefix = "SUP")
        {
            if (!await TableExistsAsync("suppliers"))
                return $"{prefix}-0001";
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT supplier_code FROM suppliers WHERE supplier_code LIKE $p ORDER BY id DESC LIMIT 20";
            cmd.Parameters.AddWithValue("$p", prefix + "%");
            int max = 0;
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var code = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var digits = new string(code.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
                if (int.TryParse(digits, out var n) && n > max)
                    max = n;
            }
            return $"{prefix}-{(max + 1):D4}";
        }
    }
}
