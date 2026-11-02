using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;
using Microsoft.Data.Sqlite;
using System.Diagnostics;

namespace ProWeighSuitePlatforms.Data
{
    public class MaterialRepository : IMaterialRepository
    {
        private readonly string _dbPath;

        public MaterialRepository(string dbPath)
        {
            _dbPath = dbPath;
            Debug.WriteLine($"[MaterialRepository] DB Path: {_dbPath}");
        }

        private SqliteConnection GetConnection() => new SqliteConnection($"Data Source={_dbPath}");

        public async Task InitializeAsync()
        {
            Debug.WriteLine($"[MaterialRepository] InitializeAsync starting for DB: {_dbPath}");

            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='materials'";
            var tableExists = await cmd.ExecuteScalarAsync() != null;
            Debug.WriteLine($"[MaterialRepository] materials table exists: {tableExists}");

            if (!tableExists)
            {
                cmd.CommandText = @"
                    CREATE TABLE materials (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        product_name TEXT NOT NULL,
                        product_code TEXT NOT NULL UNIQUE,
                        category_id INTEGER NOT NULL,
                        variant_id INTEGER NOT NULL,
                        is_active INTEGER NOT NULL DEFAULT 1,
                        shelf_life INTEGER NOT NULL DEFAULT 365,
                        target_weight REAL NOT NULL DEFAULT 0,
                        target_weight_uom INTEGER NOT NULL DEFAULT 0,
                        check_weigher_required INTEGER NOT NULL DEFAULT 0,
                        t_minus_percent REAL NOT NULL DEFAULT 0,
                        t_plus_percent REAL NOT NULL DEFAULT 0,
                        tolerance_mode TEXT NOT NULL DEFAULT 'Relative',
                        min_weight REAL NOT NULL DEFAULT 0,
                        max_weight REAL NOT NULL DEFAULT 0,
                        created_date TEXT NOT NULL,
                        modified_date TEXT,
                        image_path TEXT,
                        FOREIGN KEY (category_id) REFERENCES categories(id),
                        FOREIGN KEY (variant_id) REFERENCES variants(id)
                    )";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_materials_product_code ON materials(product_code)";
                await cmd.ExecuteNonQueryAsync();

                Debug.WriteLine("[MaterialRepository] materials table and index created");
                return;
            }

            // If table exists, verify expected columns and perform migrations if needed
            cmd.CommandText = "PRAGMA table_info('materials')";
            var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    existingColumns.Add(reader.GetString(1)); // name column
                }
            }

            var expectedColumns = new[]
            {
                "id", "product_name", "product_code", "category_id", "variant_id",
                "is_active", "shelf_life", "target_weight", "target_weight_uom", "check_weigher_required",
                "t_minus_percent", "t_plus_percent", "tolerance_mode", "min_weight", "max_weight",
                "created_date", "modified_date", "image_path"
            };

            bool needsRecreateForName = !existingColumns.Contains("product_name") && existingColumns.Contains("name");

            Debug.WriteLine($"[MaterialRepository] existing columns: {string.Join(",", existingColumns)}");
            Debug.WriteLine($"[MaterialRepository] needsRecreateForName: {needsRecreateForName}");

            if (needsRecreateForName)
            {
                // Recreate table mapping old 'name' column to new 'product_name'
                cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS materials_new (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    product_name TEXT NOT NULL,
                    product_code TEXT NOT NULL UNIQUE,
                    category_id INTEGER NOT NULL DEFAULT 0,
                    variant_id INTEGER NOT NULL DEFAULT 0,
                    is_active INTEGER NOT NULL DEFAULT 1,
                    shelf_life INTEGER NOT NULL DEFAULT 365,
                    target_weight REAL NOT NULL DEFAULT 0,
                    target_weight_uom INTEGER NOT NULL DEFAULT 0,
                    check_weigher_required INTEGER NOT NULL DEFAULT 0,
                    t_minus_percent REAL NOT NULL DEFAULT 0,
                    t_plus_percent REAL NOT NULL DEFAULT 0,
                    tolerance_mode TEXT NOT NULL DEFAULT 'Relative',
                    min_weight REAL NOT NULL DEFAULT 0,
                    max_weight REAL NOT NULL DEFAULT 0,
                    created_date TEXT NOT NULL DEFAULT '',
                    modified_date TEXT,
                    image_path TEXT
                );";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = @"
                INSERT INTO materials_new (id, product_name, product_code, category_id, variant_id, is_active, shelf_life, target_weight, target_weight_uom, check_weigher_required, t_minus_percent, t_plus_percent, tolerance_mode, min_weight, max_weight, created_date, modified_date, image_path)
                SELECT id, name, product_code, 
                       CASE WHEN category_id IS NULL THEN 0 ELSE category_id END,
                       CASE WHEN variant_id IS NULL THEN 0 ELSE variant_id END,
                       CASE WHEN is_active IS NULL THEN 1 ELSE is_active END,
                       CASE WHEN shelf_life IS NULL THEN 365 ELSE shelf_life END,
                       CASE WHEN target_weight IS NULL THEN 0 ELSE target_weight END,
                       CASE WHEN target_weight_uom IS NULL THEN 0 ELSE target_weight_uom END,
                       CASE WHEN check_weigher_required IS NULL THEN 0 ELSE check_weigher_required END,
                       CASE WHEN t_minus_percent IS NULL THEN 0 ELSE t_minus_percent END,
                       CASE WHEN t_plus_percent IS NULL THEN 0 ELSE t_plus_percent END,
                       COALESCE(tolerance_mode, 'Relative'), COALESCE(min_weight, 0), COALESCE(max_weight, 0),
                       CASE WHEN created_date IS NULL THEN datetime('now') ELSE created_date END,
                       modified_date, NULL
                FROM materials;";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "DROP TABLE materials;";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "ALTER TABLE materials_new RENAME TO materials;";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_materials_product_code ON materials(product_code)";
                await cmd.ExecuteNonQueryAsync();

                Debug.WriteLine("[MaterialRepository] Recreated materials table mapping 'name' to 'product_name'");
                return;
            }

            // For other missing columns, add them using ALTER TABLE ADD COLUMN
            foreach (var col in expectedColumns)
            {
                if (existingColumns.Contains(col)) continue;

                switch (col.ToLowerInvariant())
                {
                    case "product_name":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN product_name TEXT NOT NULL DEFAULT ''";
                        break;
                    case "product_code":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN product_code TEXT NOT NULL DEFAULT ''";
                        break;
                    case "category_id":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN category_id INTEGER NOT NULL DEFAULT 0";
                        break;
                    case "variant_id":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN variant_id INTEGER NOT NULL DEFAULT 0";
                        break;
                    case "is_active":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN is_active INTEGER NOT NULL DEFAULT 1";
                        break;
                    case "shelf_life":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN shelf_life INTEGER NOT NULL DEFAULT 365";
                        break;
                    case "target_weight":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN target_weight REAL NOT NULL DEFAULT 0";
                        break;
                    case "target_weight_uom":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN target_weight_uom INTEGER NOT NULL DEFAULT 0";
                        break;
                    case "check_weigher_required":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN check_weigher_required INTEGER NOT NULL DEFAULT 0";
                        break;
                    case "t_minus_percent":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN t_minus_percent REAL NOT NULL DEFAULT 0";
                        break;
                    case "t_plus_percent":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN t_plus_percent REAL NOT NULL DEFAULT 0";
                        break;
                    case "tolerance_mode":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN tolerance_mode TEXT NOT NULL DEFAULT 'Relative'";
                        break;
                    case "min_weight":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN min_weight REAL NOT NULL DEFAULT 0";
                        break;
                    case "max_weight":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN max_weight REAL NOT NULL DEFAULT 0";
                        break;
                    case "created_date":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN created_date TEXT NOT NULL DEFAULT ''";
                        break;
                    case "modified_date":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN modified_date TEXT";
                        break;
                    case "image_path":
                        cmd.CommandText = "ALTER TABLE materials ADD COLUMN image_path TEXT";
                        break;
                    case "id":
                        // can't add id primary key easily; skip
                        continue;
                    default:
                        continue;
                }

                try
                {
                    await cmd.ExecuteNonQueryAsync();
                    Debug.WriteLine($"[MaterialRepository] Added missing column: {col}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MaterialRepository] Failed to add column {col}: {ex}");
                    // continue with other columns
                }
            }

            // Ensure the unique index exists
            try
            {
                cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_materials_product_code ON materials(product_code)";
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MaterialRepository] Failed to create unique index: {ex}");
                // ignore
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _tableExistsCache = new();

        private async Task<bool> TableExistsAsync(string tableName)
        {
            var key = _dbPath + "|" + tableName;
            if (_tableExistsCache.TryGetValue(key, out var cached) && cached) return true;
            using var conn = GetConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name = $name LIMIT 1";
            cmd.Parameters.AddWithValue("$name", tableName);
            var result = await cmd.ExecuteScalarAsync();
            var exists = result != null && result != DBNull.Value;
            if (exists) _tableExistsCache[key] = true;
            return exists;
        }

        public async Task<string> GenerateNextProductCodeAsync()
        {
            if (!await TableExistsAsync("materials"))
                throw new InvalidOperationException("Materials table is not initialized yet. Please initialize the database before creating materials.");

            using var conn = GetConnection();
            await conn.OpenAsync();

            // Get company settings prefix
            var prefixCmd = conn.CreateCommand();
            prefixCmd.CommandText = "SELECT material_code_prefix FROM company_settings LIMIT 1";
            var prefixResult = await prefixCmd.ExecuteScalarAsync();
            string? prefix = prefixResult?.ToString();
            Debug.WriteLine($"[MaterialRepository] Company material code prefix: '{prefix}'");
            if (string.IsNullOrWhiteSpace(prefix))
            {
                Debug.WriteLine("[MaterialRepository] Company material code prefix is not configured.");
                throw new InvalidOperationException("Company material code prefix is not configured. Please set it in Company Settings before creating materials.");
            }

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(MAX(CAST(SUBSTR(product_code, LENGTH($prefix) + 1) AS INTEGER)), 0) + 1 
                FROM materials 
                WHERE product_code LIKE $prefix || '%'";
            cmd.Parameters.AddWithValue("$prefix", prefix);

            var nextNumber = await cmd.ExecuteScalarAsync();
            var code = Convert.ToInt32(nextNumber);
            return $"{prefix}{code:D5}";
        }

        public async Task<int> CreateAsync(Material material)
        {
            if (!await TableExistsAsync("materials"))
                throw new InvalidOperationException("Materials table is not initialized yet. Please initialize the database before creating materials.");

            EntityTextNormalizer.Trim(material);
            using var conn = GetConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO materials (
                    product_name, product_code, category_id, variant_id, 
                    is_active, shelf_life, target_weight, target_weight_uom, check_weigher_required,
                    t_minus_percent, t_plus_percent, tolerance_mode, min_weight, max_weight, created_date, image_path
                ) VALUES (
                    @name, @code, @categoryId, @variantId,
                    @isActive, @shelfLife, @targetWeight, @targetWeightUOM, @checkWeigher,
                    @tMinus, @tPlus, @toleranceMode, @minWeight, @maxWeight, @created, @imagePath
                ); 
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@name", material.ProductName);
            cmd.Parameters.AddWithValue("@code", material.ProductCode);
            cmd.Parameters.AddWithValue("@categoryId", material.CategoryId);
            cmd.Parameters.AddWithValue("@variantId", material.VariantId);
            cmd.Parameters.AddWithValue("@isActive", material.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@shelfLife", material.ShelfLife);
            cmd.Parameters.AddWithValue("@targetWeight", material.TargetWeight);
            cmd.Parameters.AddWithValue("@targetWeightUOM", (int)material.TargetWeightUOM);
            cmd.Parameters.AddWithValue("@checkWeigher", material.CheckWeigherRequired ? 1 : 0);
            cmd.Parameters.AddWithValue("@tMinus", material.TMinusPercent);
            cmd.Parameters.AddWithValue("@tPlus", material.TPlusPercent);
            cmd.Parameters.AddWithValue("@toleranceMode", material.ToleranceMode);
            cmd.Parameters.AddWithValue("@minWeight", material.MinWeight);
            cmd.Parameters.AddWithValue("@maxWeight", material.MaxWeight);
            cmd.Parameters.AddWithValue("@created", material.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@imagePath", (object?)material.ImagePath ?? DBNull.Value);

            var idObj = await cmd.ExecuteScalarAsync();
            var id = idObj == null || idObj == DBNull.Value ? 0L : (long)idObj;
            material.Id = (int)id;
            Debug.WriteLine($"[MaterialRepository] Created material id: {material.Id}");
            return material.Id;
        }

        public async Task<Material?> GetAsync(int id)
        {
            if (!await TableExistsAsync("materials"))
                return null;

            using var conn = GetConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            // Avoid joining tables that may not exist (categories/variants) - build SELECT dynamically
            var categoriesExist = await TableExistsAsync("categories");
            var variantsExist = await TableExistsAsync("variants");
            string selectCategory = categoriesExist ? "c.name as category_name" : "'' as category_name";
            string selectVariant = variantsExist ? "v.name as variant_name" : "'' as variant_name";
            string joins = "";
            if (categoriesExist) joins += " LEFT JOIN categories c ON m.category_id = c.id";
            if (variantsExist) joins += " LEFT JOIN variants v ON m.variant_id = v.id";

            cmd.CommandText = $@"
                SELECT 
                    m.id, m.product_name, m.product_code, m.category_id, m.variant_id,
                    m.is_active, m.shelf_life, m.target_weight, m.target_weight_uom, m.check_weigher_required,
                    m.t_minus_percent, m.t_plus_percent, m.tolerance_mode, m.min_weight, m.max_weight, m.created_date, m.modified_date, m.image_path,
                    {selectCategory}, {selectVariant}
                FROM materials m
                {joins}
                WHERE m.id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapMaterial(reader);
            }
            return null;
        }

        public async Task<Material?> GetByCodeAsync(string productCode)
        {
            if (!await TableExistsAsync("materials"))
                return null;

            using var conn = GetConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            var categoriesExist = await TableExistsAsync("categories");
            var variantsExist = await TableExistsAsync("variants");
            string selectCategory = categoriesExist ? "c.name as category_name" : "'' as category_name";
            string selectVariant = variantsExist ? "v.name as variant_name" : "'' as variant_name";
            string joins = "";
            if (categoriesExist) joins += " LEFT JOIN categories c ON m.category_id = c.id";
            if (variantsExist) joins += " LEFT JOIN variants v ON m.variant_id = v.id";

            cmd.CommandText = $@"
                SELECT 
                    m.id, m.product_name, m.product_code, m.category_id, m.variant_id,
                    m.is_active, m.shelf_life, m.target_weight, m.target_weight_uom, m.check_weigher_required,
                    m.t_minus_percent, m.t_plus_percent, m.tolerance_mode, m.min_weight, m.max_weight, m.created_date, m.modified_date, m.image_path,
                    {selectCategory}, {selectVariant}
                FROM materials m
                {joins}
                WHERE m.product_code = @code COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@code", productCode?.Trim() ?? string.Empty);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapMaterial(reader);
            }
            return null;
        }

        public async Task<IEnumerable<Material>> GetAllAsync()
        {
            var list = new List<Material>();

            if (!await TableExistsAsync("materials"))
                return list;

            using var conn = GetConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            var categoriesExist = await TableExistsAsync("categories");
            var variantsExist = await TableExistsAsync("variants");
            string selectCategory = categoriesExist ? "c.name as category_name" : "'' as category_name";
            string selectVariant = variantsExist ? "v.name as variant_name" : "'' as variant_name";
            string joins = "";
            if (categoriesExist) joins += " LEFT JOIN categories c ON m.category_id = c.id";
            if (variantsExist) joins += " LEFT JOIN variants v ON m.variant_id = v.id";

            cmd.CommandText = $@"
                SELECT 
                    m.id, m.product_name, m.product_code, m.category_id, m.variant_id,
                    m.is_active, m.shelf_life, m.target_weight, m.target_weight_uom, m.check_weigher_required,
                    m.t_minus_percent, m.t_plus_percent, m.tolerance_mode, m.min_weight, m.max_weight, m.created_date, m.modified_date, m.image_path,
                    {selectCategory}, {selectVariant}
                FROM materials m
                {joins}
                ORDER BY m.product_name";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(MapMaterial(reader));
            }
            Debug.WriteLine($"[MaterialRepository] Retrieved {list.Count} materials");
            return list;
        }

        public async Task<int> UpdateAsync(Material material)
        {
            if (!await TableExistsAsync("materials"))
                return 0;

            EntityTextNormalizer.Trim(material);
            using var conn = GetConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE materials SET 
                    product_name = @name, 
                    product_code = @code, 
                    category_id = @categoryId,
                    variant_id = @variantId,
                    is_active = @isActive,
                    shelf_life = @shelfLife,
                    target_weight = @targetWeight,
                    target_weight_uom = @targetWeightUom,
                    check_weigher_required = @checkWeigher,
                    t_minus_percent = @tMinus,
                    t_plus_percent = @tPlus,
                    tolerance_mode = @toleranceMode,
                    min_weight = @minWeight,
                    max_weight = @maxWeight,
                    modified_date = @modified,
                    image_path = @imagePath
                WHERE id = @id";

            cmd.Parameters.AddWithValue("@name", material.ProductName);
            cmd.Parameters.AddWithValue("@code", material.ProductCode);
            cmd.Parameters.AddWithValue("@categoryId", material.CategoryId);
            cmd.Parameters.AddWithValue("@variantId", material.VariantId);
            cmd.Parameters.AddWithValue("@isActive", material.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@shelfLife", material.ShelfLife);
            cmd.Parameters.AddWithValue("@targetWeight", material.TargetWeight);
            cmd.Parameters.AddWithValue("@targetWeightUom", (int)material.TargetWeightUOM);
            cmd.Parameters.AddWithValue("@checkWeigher", material.CheckWeigherRequired ? 1 : 0);
            cmd.Parameters.AddWithValue("@tMinus", material.TMinusPercent);
            cmd.Parameters.AddWithValue("@tPlus", material.TPlusPercent);
            cmd.Parameters.AddWithValue("@toleranceMode", material.ToleranceMode);
            cmd.Parameters.AddWithValue("@minWeight", material.MinWeight);
            cmd.Parameters.AddWithValue("@maxWeight", material.MaxWeight);
            cmd.Parameters.AddWithValue("@modified", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@imagePath", (object?)material.ImagePath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", material.Id);

            var rows = await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine($"[MaterialRepository] Updated material id {material.Id}, rows affected: {rows}");
            return rows;
        }

        public async Task<int> DeleteAsync(int id)
        {
            if (!await TableExistsAsync("materials"))
                return 0;

            using var conn = GetConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM materials WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            var rows = await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine($"[MaterialRepository] Deleted material id {id}, rows affected: {rows}");
            return rows;
        }

        public async Task<bool> ProductCodeExistsAsync(string productCode, int? excludeId = null)
        {
            if (!await TableExistsAsync("materials"))
                return false;

            using var conn = GetConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = excludeId.HasValue
                ? "SELECT COUNT(1) FROM materials WHERE product_code = @code AND id <> @id"
                : "SELECT COUNT(1) FROM materials WHERE product_code = @code";
            cmd.Parameters.AddWithValue("@code", productCode);
            if (excludeId.HasValue)
            {
                cmd.Parameters.AddWithValue("@id", excludeId.Value);
            }
            var resultObj = await cmd.ExecuteScalarAsync();
            var result = resultObj == null || resultObj == DBNull.Value ? 0L : (long)resultObj;
            var exists = result > 0;
            Debug.WriteLine($"[MaterialRepository] ProductCodeExists check for '{productCode}' (exclude {excludeId}): {exists}");
            return exists;
        }

        private Material MapMaterial(SqliteDataReader reader)
        {
            int imagePathIdx = -1;
            try { imagePathIdx = reader.GetOrdinal("image_path"); } catch { }
            return new Material
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                ProductName = reader.GetString(reader.GetOrdinal("product_name")),
                ProductCode = reader.GetString(reader.GetOrdinal("product_code")),
                CategoryId = reader.GetInt32(reader.GetOrdinal("category_id")),
                VariantId = reader.GetInt32(reader.GetOrdinal("variant_id")),
                IsActive = reader.GetInt32(reader.GetOrdinal("is_active")) == 1,
                ShelfLife = reader.GetInt32(reader.GetOrdinal("shelf_life")),
                TargetWeight = reader.GetDouble(reader.GetOrdinal("target_weight")),
                TargetWeightUOM = (WeightUOM)reader.GetInt32(reader.GetOrdinal("target_weight_uom")),
                CheckWeigherRequired = reader.GetInt32(reader.GetOrdinal("check_weigher_required")) == 1,
                TMinusPercent = reader.GetDouble(reader.GetOrdinal("t_minus_percent")),
                TPlusPercent = reader.GetDouble(reader.GetOrdinal("t_plus_percent")),
                ToleranceMode = reader.IsDBNull(reader.GetOrdinal("tolerance_mode")) ? "Relative" : reader.GetString(reader.GetOrdinal("tolerance_mode")),
                MinWeight = reader.GetDouble(reader.GetOrdinal("min_weight")),
                MaxWeight = reader.GetDouble(reader.GetOrdinal("max_weight")),
                CreatedDate = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_date"))),
                ModifiedDate = reader.IsDBNull(reader.GetOrdinal("modified_date")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("modified_date"))),
                ImagePath = imagePathIdx >= 0 && !reader.IsDBNull(imagePathIdx) ? reader.GetString(imagePathIdx) : null,
                CategoryName = reader.IsDBNull(reader.GetOrdinal("category_name")) ? string.Empty : reader.GetString(reader.GetOrdinal("category_name")),
                VariantName = reader.IsDBNull(reader.GetOrdinal("variant_name")) ? string.Empty : reader.GetString(reader.GetOrdinal("variant_name"))
            };
        }

        public async Task<string?> GetCompanyMaterialCodePrefixAsync()
        {
            if (!await TableExistsAsync("company_settings"))
                return null;

            using var conn = GetConnection();
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT material_code_prefix FROM company_settings LIMIT 1";
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString();
        }
    }
}