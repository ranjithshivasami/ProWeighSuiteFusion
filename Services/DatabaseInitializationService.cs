using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using Microsoft.Data.Sqlite;
using ProWeighSuitePlatforms.Configuration;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Service to initialize the database with seed data on first installation
    /// </summary>
    public class DatabaseInitializationService
    {
        private readonly string _dbPath;
        private readonly IReadOnlyList<IProductDatabaseInitializer> _productInitializers
            = Array.Empty<IProductDatabaseInitializer>();

        public DatabaseInitializationService(
            IEnumerable<IProductDatabaseInitializer>? productInitializers = null)
        {
            _productInitializers = productInitializers?.ToList()
                ?? new List<IProductDatabaseInitializer>();
            _dbPath = AppConfigurationManager.GetDatabasePath();

            // Ensure any legacy DBs are migrated to the new filename
            TryMigrateLegacyDatabases();
        }

        public DatabaseInitializationService(string dbPath)
        {
            _dbPath = dbPath;
        }

        private void TryMigrateLegacyDatabases()
        {
            try
            {
                var legacyNames = new[] { "materials.db", "jitscale.db" };

                // Locations to search for legacy DBs: Roaming (ApplicationData) and Local
                var possibleAppDirs = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                };

                foreach (var appBase in possibleAppDirs)
                {
                    if (string.IsNullOrEmpty(appBase)) continue;

                    var appDir = Path.Combine(appBase, "ProWeighSuiteFusion");
                    if (!Directory.Exists(appDir)) continue;

                    foreach (var legacy in legacyNames)
                    {
                        var legacyPath = Path.Combine(appDir, legacy);
                        if (!File.Exists(legacyPath)) continue;

                        // If target already exists, skip
                        if (File.Exists(_dbPath)) return;

                        // Ensure target directory exists
                        var targetDir = Path.GetDirectoryName(_dbPath);
                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                            Directory.CreateDirectory(targetDir);

                        // Create backup before copying
                        var backupPath = Path.Combine(appDir, legacy + ".bak");
                        File.Copy(legacyPath, backupPath, overwrite: true);

                        // Copy legacy file to new filename
                        File.Copy(legacyPath, _dbPath);
                        return;
                    }
                }
            }
            catch
            {
                // ignore migration errors - initialization will attempt to create new database
            }
        }

        /// <summary>
        /// Initialize all database tables and seed default data
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                // Ensure directory exists
                var dir = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Test database connection first
                await TestDatabaseConnectionAsync();

                // Initialize Users table and seed default users
                await SafeInitializeAsync(async () =>
                {
                    var userRepo = new UserRepository(_dbPath);
                    await userRepo.InitializeAsync();
                }, "Users");

                // Initialize Role Permissions table and seed default permissions
                await SafeInitializeAsync(async () =>
                {
                    var rolePermRepo = new RolePermissionRepository(_dbPath);
                    await rolePermRepo.InitializeAsync();

                    // Auto-discover permission resources (menus/features) added while the
                    // app was offline and register them so admins can assign them per role.
                    var permissionSync = new PermissionSyncService(_dbPath);
                    var added = await permissionSync.SyncAllPermissionsAsync();
                    if (added > 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[DatabaseInitialization] Auto-registered {added} new permission resource(s) for role assignment.");
                    }
                }, "Role Permissions");

                // Initialize other repositories
                await InitializeOtherTablesAsync();
            }
            catch (Exception ex)
            {
                // Log the error but don't fail - database might already be initialized
                System.Diagnostics.Debug.WriteLine($"Database initialization warning: {ex.Message}");

                // Only throw if database is completely inaccessible
                if (!await VerifyDatabaseAccessibleAsync())
                {
                    throw new Exception($"Failed to initialize application database:\n\n{ex.Message}\n\nPlease ensure you have write permissions to:\n{_dbPath}", ex);
                }
            }
        }

        private async Task SafeInitializeAsync(Func<Task> initAction, string componentName)
        {
            try
            {
                await initAction();
            }
            catch (SqliteException ex) when (ex.Message.Contains("already exists"))
            {
                // Table already exists - this is fine, just continue
                System.Diagnostics.Debug.WriteLine($"{componentName} table already exists - skipping creation");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing {componentName}: {ex.Message}");
                // Continue with other initializations
            }
        }

        private async Task TestDatabaseConnectionAsync()
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                await conn.OpenAsync();

                // Test write access
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE IF NOT EXISTS _init_test (id INTEGER)";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "DROP TABLE IF EXISTS _init_test";
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Cannot access database at {_dbPath}. Please check permissions.", ex);
            }
        }

        private async Task<bool> VerifyDatabaseAccessibleAsync()
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                await conn.OpenAsync();

                // Check if essential tables exist
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('users', 'materials')";
                using var reader = await cmd.ExecuteReaderAsync();

                return await reader.ReadAsync(); // If we can read, database is accessible
            }
            catch
            {
                return false;
            }
        }

        private async Task InitializeOtherTablesAsync()
        {
            // Initialize Materials and related tables
            await SafeInitializeAsync(async () =>
            {
                var materialRepo = new MaterialRepository(_dbPath);
                await materialRepo.InitializeAsync();
            }, "Materials");

            // Initialize Categories - tables created empty, no seed (per fresh install requirement: only users/roles/permissions seeded)
            await SafeInitializeAsync(async () =>
            {
                var categoryRepo = new CategoryRepository(_dbPath);
                await categoryRepo.InitializeAsync();
            }, "Categories");

            // Initialize Variants - tables created empty, no seed
            await SafeInitializeAsync(async () =>
            {
                var variantRepo = new VariantRepository(_dbPath);
                await variantRepo.InitializeAsync();
            }, "Variants");

            // Initialize Suppliers - tables created empty, no seed
            await SafeInitializeAsync(async () =>
            {
                var supplierRepo = new SupplierRepository(_dbPath);
                await supplierRepo.InitializeAsync();
            }, "Suppliers");

            // Initialize Scale Settings
            await SafeInitializeAsync(async () =>
            {
                var scaleRepo = new ScaleSettingsRepository(_dbPath);
                await scaleRepo.InitializeAsync();
            }, "Scale Settings");

            if (_productInitializers.Count > 0)
            {
                foreach (var initializer in _productInitializers)
                {
                    await SafeInitializeAsync(
                        () => initializer.InitializeAsync(_dbPath), initializer.Name);
                }
            }
            else
            {
                await SafeInitializeAsync(async () =>
                {
                    var weighingRepo = new WeighingRepository(_dbPath);
                    await weighingRepo.InitializeAsync();
                }, "Weighing");
            }

            // Initialize Company Settings
            await SafeInitializeAsync(async () =>
            {
                var companyRepo = new CompanySettingsRepository(_dbPath);
                await companyRepo.InitializeAsync();
            }, "Company Settings");
        }

        // Seed methods removed: fresh install must leave categories/variants empty.
        // Only users (UserRepository.cs:97) and role_permissions (RolePermissionRepository.cs:49 + PermissionSyncService.cs:75) are seeded.

        /// <summary>
        /// Check if database exists
        /// </summary>
        public bool DatabaseExists()
        {
            return File.Exists(_dbPath);
        }

        /// <summary>
        /// Get database file path
        /// </summary>
        public string GetDatabasePath()
        {
            return _dbPath;
        }
    }
}
