using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Data;

namespace ProWeighSuitePlatforms.Services
{
    public class AuthorizationService
    {
        private static Dictionary<UserRole, HashSet<Permission>>? _rolePermissions;
        private static readonly object _lock = new();
        private static DateTime _lastLoaded = DateTime.MinValue;
        private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

        // Phase 2: lets the Core navigation layer rebuild menus/guards when an
        // administrator changes the role-permission matrix.
        public static event EventHandler? PermissionsChanged;

        public async Task LoadPermissionsFromDatabaseAsync()
        {
            var dbPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "ProWeighSuitePlatforms", "proweighsuiteplatform.db");

            var repository = new RolePermissionRepository(dbPath);
            await repository.InitializeAsync();

            var permissions = await repository.GetRolePermissionsMapAsync();

            lock (_lock)
            {
                _rolePermissions = permissions;
                _lastLoaded = DateTime.Now;
            }

            RaisePermissionsChanged();
        }

        private async Task<Dictionary<UserRole, HashSet<Permission>>> GetRolePermissionsAsync()
        {
            lock (_lock)
            {
                // Check if cache is still valid
                if (_rolePermissions != null && (DateTime.Now - _lastLoaded) < CacheExpiry)
                {
                    return _rolePermissions;
                }
            }

            // Cache expired or not loaded, reload from database
            await LoadPermissionsFromDatabaseAsync();

            lock (_lock)
            {
                return _rolePermissions ?? GetDefaultPermissions();
            }
        }

        private static Dictionary<UserRole, HashSet<Permission>> GetDefaultPermissions()
        {
            // Fallback to default permissions if database fails
            return new Dictionary<UserRole, HashSet<Permission>>
            {
                {
                    UserRole.SuperAdmin, new HashSet<Permission>
                    {
                        // Dashboard - Full Access
                        Permission.ViewDashboard,
                        Permission.StartWeighing,
                        Permission.AcceptWeight,
                        Permission.PauseBatch,
                        Permission.AbortBatch,
                        
                        // Materials - Full Access
                        Permission.ViewMaterials,
                        Permission.CreateMaterial,
                        Permission.EditMaterial,
                        Permission.DeleteMaterial,
                        Permission.ImportMaterial,
                        Permission.DownloadMaterialTemplate,
                        Permission.ExportMaterial,
                        
                        // Recipes - Full Access
                        Permission.ViewRecipes,
                        Permission.CreateRecipe,
                        Permission.EditRecipe,
                        Permission.DeleteRecipe,
                        Permission.PrintRecipe,
                        
                        // Category - Full Access
                        Permission.ViewCategory,
                        Permission.CreateCategory,
                        Permission.EditCategory,
                        Permission.DeleteCategory,
                        Permission.ImportCategory,
                        Permission.DownloadCategoryTemplate,
                        Permission.ExportCategory,
                        
                        // Variant - Full Access
                        Permission.ViewVariant,
                        Permission.CreateVariant,
                        Permission.EditVariant,
                        Permission.DeleteVariant,
                        Permission.ImportVariant,
                        Permission.DownloadVariantTemplate,
                        Permission.ExportVariant,

                        // Supplier - Full Access
                        Permission.ViewSupplier,
                        Permission.CreateSupplier,
                        Permission.EditSupplier,
                        Permission.DeleteSupplier,
                        Permission.ImportSupplier,
                        Permission.DownloadSupplierTemplate,
                        Permission.ExportSupplier,
                        
                        // Scale Settings - Full Access
                        Permission.ViewScaleSettings,
                        Permission.CreateScaleSettings,
                        Permission.EditScaleSettings,
                        Permission.DeleteScaleSettings,
                        
                        // User Management - Full Access
                        Permission.ViewUsers,
                        Permission.CreateUser,
                        Permission.EditUser,
                        Permission.DeleteUser,
                        Permission.ChangeUserRole,
                        
                        // Reports - Full Access
                        Permission.ViewReports,
                        Permission.ExportReports,
                        
                        // System Settings - Full Access
                        Permission.ViewSystemSettings,
                        Permission.EditSystemSettings,
                        
                        // Company Settings - Full Access
                        Permission.ViewCompanySettings,
                        Permission.EditCompanySettings,
                        
                        // Role Permissions - Full Access
                        Permission.ViewRolePermissions,
                        Permission.EditRolePermissions,
                        
                        // Modules - Full Access
                        Permission.ViewModules,
                        Permission.EditModules
                    }
                },
                {
                    UserRole.Admin, new HashSet<Permission>
                    {
                        // Dashboard - Full Access
                        Permission.ViewDashboard,
                        Permission.StartWeighing,
                        Permission.AcceptWeight,
                        Permission.PauseBatch,
                        Permission.AbortBatch,
                        
                        // Materials - Create/Edit only
                        Permission.ViewMaterials,
                        Permission.CreateMaterial,
                        Permission.EditMaterial,
                        Permission.ImportMaterial,
                        Permission.DownloadMaterialTemplate,
                        Permission.ExportMaterial,
                        
                        // Recipes - Create/Edit only
                        Permission.ViewRecipes,
                        Permission.CreateRecipe,
                        Permission.EditRecipe,
                        Permission.PrintRecipe,
                        
                        // Category - Create/Edit only
                        Permission.ViewCategory,
                        Permission.CreateCategory,
                        Permission.EditCategory,
                        Permission.ImportCategory,
                        Permission.DownloadCategoryTemplate,
                        Permission.ExportCategory,
                        
                        // Variant - Create/Edit only
                        Permission.ViewVariant,
                        Permission.CreateVariant,
                        Permission.EditVariant,
                        Permission.ImportVariant,
                        Permission.DownloadVariantTemplate,
                        Permission.ExportVariant,

                        // Supplier - Create/Edit only
                        Permission.ViewSupplier,
                        Permission.CreateSupplier,
                        Permission.EditSupplier,
                        Permission.ImportSupplier,
                        Permission.DownloadSupplierTemplate,
                        Permission.ExportSupplier,
                        
                        // Scale Settings - Full Access
                        Permission.ViewScaleSettings,
                        Permission.CreateScaleSettings,
                        Permission.EditScaleSettings,
                        
                        // User Management - Create/Edit only
                        Permission.ViewUsers,
                        Permission.CreateUser,
                        Permission.EditUser,
                        
                        // Reports - View/Export
                        Permission.ViewReports,
                        Permission.ExportReports,
                        
                        // Company Settings - View/Edit (Admin can manage)
                        Permission.ViewCompanySettings,
                        Permission.EditCompanySettings,
                        
                        // Modules - View only
                        Permission.ViewModules
                    }
                },
                {
                    UserRole.Operator, new HashSet<Permission>
                    {
                        // Dashboard - Weighing operations only
                        Permission.ViewDashboard,
                        Permission.StartWeighing,
                        Permission.AcceptWeight,
                        
                        // Materials - View only
                        Permission.ViewMaterials,
                        
                        // Recipes - View/Print only
                        Permission.ViewRecipes,
                        Permission.PrintRecipe,
                        
                        // Reports - View only
                        Permission.ViewReports,

                        // Download templates only (no import)
                        Permission.DownloadCategoryTemplate,
                        Permission.DownloadVariantTemplate,
                        Permission.DownloadSupplierTemplate,
                        Permission.DownloadMaterialTemplate
                    }
                }
            };
        }

        public bool HasPermission(User? user, Permission permission)
        {
            if (user == null)
            {
                return false;
            }

            var permissions = GetRolePermissionsAsync().GetAwaiter().GetResult();
            
            if (!permissions.ContainsKey(user.Role))
            {
                return false;
            }

            return permissions[user.Role].Contains(permission);
        }

        // Phase 2: async path used by the Core authorization adapter. Avoids the
        // UI-thread sync-over-async block that the legacy getters still use.
        public async Task<bool> HasPermissionAsync(
            User? user,
            Permission permission,
            CancellationToken cancellationToken = default)
        {
            if (user is null)
                return false;

            cancellationToken.ThrowIfCancellationRequested();
            var permissions = await GetRolePermissionsAsync().ConfigureAwait(false);
            return permissions.TryGetValue(user.Role, out var granted) && granted.Contains(permission);
        }

        public bool HasAnyPermission(User? user, params Permission[] permissions)
        {
            return permissions.Any(p => HasPermission(user, p));
        }

        public bool HasAllPermissions(User? user, params Permission[] permissions)
        {
            return permissions.All(p => HasPermission(user, p));
        }

        public IEnumerable<Permission> GetUserPermissions(User? user)
        {
            if (user == null)
            {
                return Enumerable.Empty<Permission>();
            }

            var permissions = GetRolePermissionsAsync().GetAwaiter().GetResult();
            
            if (!permissions.ContainsKey(user.Role))
            {
                return Enumerable.Empty<Permission>();
            }

            return permissions[user.Role];
        }

        public static string GetRoleDisplayName(UserRole role)
        {
            return role switch
            {
                UserRole.SuperAdmin => "Super Admin",
                UserRole.Admin => "Admin",
                UserRole.Operator => "Operator",
                _ => "Unknown"
            };
        }

        public void ClearCache()
        {
            lock (_lock)
            {
                _rolePermissions = null;
                _lastLoaded = DateTime.MinValue;
            }

            // Intentionally no notification: callers normally reload immediately,
            // and the reload raises PermissionsChanged once the new snapshot is in
            // place (notifying while the cache is empty can re-enter the DB load).
        }

        private static void RaisePermissionsChanged()
        {
            try
            {
                PermissionsChanged?.Invoke(null, EventArgs.Empty);
            }
            catch
            {
                // A nav-menu rebuild must never break the permission save path.
            }
        }
    }
}
