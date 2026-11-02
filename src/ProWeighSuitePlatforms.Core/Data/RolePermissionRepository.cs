using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;
using Microsoft.Data.Sqlite;

namespace ProWeighSuitePlatforms.Data
{
    public class RolePermissionRepository
    {
        private readonly string _dbPath;

        public RolePermissionRepository(string dbPath)
        {
            _dbPath = dbPath;
        }

        public async Task InitializeAsync()
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS role_permissions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    role INTEGER NOT NULL,
                    permission INTEGER NOT NULL,
                    is_granted INTEGER NOT NULL DEFAULT 1,
                    created_date TEXT NOT NULL,
                    modified_date TEXT,
                    modified_by TEXT,
                    UNIQUE(role, permission)
                )";
            await cmd.ExecuteNonQueryAsync();

            // Check if table is empty and seed with default permissions
            cmd.CommandText = "SELECT COUNT(*) FROM role_permissions";
            var countObj = await cmd.ExecuteScalarAsync();
            var count = countObj == null || countObj == DBNull.Value ? 0L : (long)countObj;

            if (count == 0)
            {
                await SeedDefaultPermissionsAsync(conn);
            }
            else
            {
                // Migrate: ensure any newly added permissions exist for each role
                await EnsureNewPermissionsAsync(conn);
            }
        }

        /// <summary>
        /// Inserts any permissions from the default set that are missing for a role,
        /// so existing databases automatically get newly introduced permissions.
        /// </summary>
        private async Task EnsureNewPermissionsAsync(SqliteConnection conn)
        {
            var defaults = GetDefaultPermissions();
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            foreach (var (role, permissions) in defaults)
            {
                foreach (var permission in permissions)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT OR IGNORE INTO role_permissions
                            (role, permission, is_granted, created_date, modified_by)
                        VALUES
                            (@role, @permission, 1, @created, 'System')";
                    cmd.Parameters.AddWithValue("@role", (int)role);
                    cmd.Parameters.AddWithValue("@permission", (int)permission);
                    cmd.Parameters.AddWithValue("@created", now);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task SeedDefaultPermissionsAsync(SqliteConnection conn)
        {
            var defaultPermissions = GetDefaultPermissions();

            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var (role, permissions) in defaultPermissions)
                {
                    foreach (var permission in permissions)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT INTO role_permissions (role, permission, is_granted, created_date, modified_by)
                            VALUES (@role, @permission, @isGranted, @created, @modifiedBy)";
                        
                        cmd.Parameters.AddWithValue("@role", (int)role);
                        cmd.Parameters.AddWithValue("@permission", (int)permission);
                        cmd.Parameters.AddWithValue("@isGranted", 1);
                        cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.Parameters.AddWithValue("@modifiedBy", "System");
                        
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private Dictionary<UserRole, List<Permission>> GetDefaultPermissions()
        {
            return new Dictionary<UserRole, List<Permission>>
            {
                {
                    UserRole.SuperAdmin, new List<Permission>
                    {
                        Permission.ViewDashboard, Permission.StartWeighing, Permission.AcceptWeight,
                        Permission.PauseBatch, Permission.AbortBatch,
                        Permission.ViewMaterials, Permission.CreateMaterial, Permission.EditMaterial, Permission.DeleteMaterial,
                        Permission.ImportMaterial, Permission.DownloadMaterialTemplate, Permission.ExportMaterial,
                        Permission.ViewRecipes, Permission.CreateRecipe, Permission.EditRecipe, Permission.DeleteRecipe, Permission.PrintRecipe,
                        Permission.ViewCategory, Permission.CreateCategory, Permission.EditCategory, Permission.DeleteCategory,
                        Permission.ImportCategory, Permission.DownloadCategoryTemplate, Permission.ExportCategory,
                        Permission.ViewVariant, Permission.CreateVariant, Permission.EditVariant, Permission.DeleteVariant,
                        Permission.ImportVariant, Permission.DownloadVariantTemplate, Permission.ExportVariant,
                        Permission.ViewSupplier, Permission.CreateSupplier, Permission.EditSupplier, Permission.DeleteSupplier,
                        Permission.ImportSupplier, Permission.DownloadSupplierTemplate, Permission.ExportSupplier,
                        Permission.ViewScaleSettings, Permission.CreateScaleSettings, Permission.EditScaleSettings, Permission.DeleteScaleSettings,
                        Permission.ViewUsers, Permission.CreateUser, Permission.EditUser, Permission.DeleteUser, Permission.ChangeUserRole,
                        Permission.ViewReports, Permission.ExportReports,
                        Permission.ViewSystemSettings, Permission.EditSystemSettings,
                        Permission.ViewRolePermissions, Permission.EditRolePermissions,
                        Permission.ViewCompanySettings, Permission.EditCompanySettings,
                        Permission.ViewModules, Permission.EditModules
                    }
                },
                {
                    UserRole.Admin, new List<Permission>
                    {
                        Permission.ViewDashboard, Permission.StartWeighing, Permission.AcceptWeight,
                        Permission.PauseBatch, Permission.AbortBatch,
                        Permission.ViewMaterials, Permission.CreateMaterial, Permission.EditMaterial,
                        Permission.ImportMaterial, Permission.DownloadMaterialTemplate, Permission.ExportMaterial,
                        Permission.ViewRecipes, Permission.CreateRecipe, Permission.EditRecipe, Permission.PrintRecipe,
                        Permission.ViewCategory, Permission.CreateCategory, Permission.EditCategory,
                        Permission.ImportCategory, Permission.DownloadCategoryTemplate, Permission.ExportCategory,
                        Permission.ViewVariant, Permission.CreateVariant, Permission.EditVariant,
                        Permission.ImportVariant, Permission.DownloadVariantTemplate, Permission.ExportVariant,
                        Permission.ViewSupplier, Permission.CreateSupplier, Permission.EditSupplier,
                        Permission.ImportSupplier, Permission.DownloadSupplierTemplate, Permission.ExportSupplier,
                        Permission.ViewScaleSettings, Permission.CreateScaleSettings, Permission.EditScaleSettings,
                        Permission.ViewUsers, Permission.CreateUser, Permission.EditUser,
                        Permission.ViewReports, Permission.ExportReports,
                        Permission.ViewCompanySettings, Permission.EditCompanySettings,
                        Permission.ViewModules
                    }
                },
                {
                    UserRole.Operator, new List<Permission>
                    {
                        Permission.ViewDashboard, Permission.StartWeighing, Permission.AcceptWeight,
                        Permission.ViewMaterials,
                        Permission.ViewRecipes, Permission.PrintRecipe,
                        Permission.ViewReports,
                        Permission.DownloadCategoryTemplate,
                        Permission.DownloadVariantTemplate,
                        Permission.DownloadSupplierTemplate,
                        Permission.DownloadMaterialTemplate
                    }
                }
            };
        }

        public async Task<List<RolePermission>> GetAllAsync()
        {
            var list = new List<RolePermission>();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, role, permission, is_granted, created_date, modified_date, modified_by
                FROM role_permissions
                ORDER BY role, permission";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new RolePermission
                {
                    Id = reader.GetInt32(0),
                    Role = (UserRole)reader.GetInt32(1),
                    Permission = (Permission)reader.GetInt32(2),
                    IsGranted = reader.GetInt32(3) == 1,
                    CreatedDate = DateTime.Parse(reader.GetString(4)),
                    ModifiedDate = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
                    ModifiedBy = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
                });
            }

            return list;
        }

        public async Task<Dictionary<UserRole, HashSet<Permission>>> GetRolePermissionsMapAsync()
        {
            var map = new Dictionary<UserRole, HashSet<Permission>>();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT role, permission
                FROM role_permissions
                WHERE is_granted = 1";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var role = (UserRole)reader.GetInt32(0);
                var permission = (Permission)reader.GetInt32(1);

                if (!map.ContainsKey(role))
                {
                    map[role] = new HashSet<Permission>();
                }

                map[role].Add(permission);
            }

            return map;
        }

        public async Task<int> UpdatePermissionAsync(UserRole role, Permission permission, bool isGranted, string modifiedBy)
        {
            modifiedBy = modifiedBy?.Trim() ?? string.Empty;
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO role_permissions (role, permission, is_granted, created_date, modified_by)
                VALUES (@role, @permission, @isGranted, @created, @modifiedBy)
                ON CONFLICT(role, permission) 
                DO UPDATE SET 
                    is_granted = @isGranted,
                    modified_date = @modified,
                    modified_by = @modifiedBy";

            cmd.Parameters.AddWithValue("@role", (int)role);
            cmd.Parameters.AddWithValue("@permission", (int)permission);
            cmd.Parameters.AddWithValue("@isGranted", isGranted ? 1 : 0);
            cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@modified", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@modifiedBy", modifiedBy);

            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> UpdateMultiplePermissionsAsync(List<(UserRole role, Permission permission, bool isGranted)> updates, string modifiedBy)
        {
            modifiedBy = modifiedBy?.Trim() ?? string.Empty;
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var tx = conn.BeginTransaction();
            try
            {
                int totalUpdated = 0;
                foreach (var (role, permission, isGranted) in updates)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT INTO role_permissions (role, permission, is_granted, created_date, modified_by)
                        VALUES (@role, @permission, @isGranted, @created, @modifiedBy)
                        ON CONFLICT(role, permission) 
                        DO UPDATE SET 
                            is_granted = @isGranted,
                            modified_date = @modified,
                            modified_by = @modifiedBy";

                    cmd.Parameters.AddWithValue("@role", (int)role);
                    cmd.Parameters.AddWithValue("@permission", (int)permission);
                    cmd.Parameters.AddWithValue("@isGranted", isGranted ? 1 : 0);
                    cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@modified", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@modifiedBy", modifiedBy);

                    totalUpdated += await cmd.ExecuteNonQueryAsync();
                }

                tx.Commit();
                return totalUpdated;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public async Task<bool> ResetToDefaultsAsync()
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            using var tx = conn.BeginTransaction();
            try
            {
                // Delete all existing permissions
                using var deleteCmd = conn.CreateCommand();
                deleteCmd.Transaction = tx;
                deleteCmd.CommandText = "DELETE FROM role_permissions";
                await deleteCmd.ExecuteNonQueryAsync();

                // Seed with defaults
                await SeedDefaultPermissionsAsync(conn);

                tx.Commit();
                return true;
            }
            catch
            {
                tx.Rollback();
                return false;
            }
        }
    }
}
