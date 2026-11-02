using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Auto-discovers newly added permission resources (menus / feature modules) and adds
    /// them to the role_permissions table so they become assignable in the Role Permissions
    /// Matrix without any manual intervention.
    ///
    /// A "resource" in this application is a <see cref="Permission"/> enum value: each one
    /// gates a menu / navigation item and is displayed as a row in the permission matrix.
    /// When a new feature is shipped (a new enum value added in code), it is not present in
    /// role_permissions until this service runs.
    ///
    /// Default grant policy for newly discovered resources:
    ///   - SuperAdmin: granted (true)  - the top role keeps access to every feature.
    ///   - Admin/Operator: NOT granted (false) - a new feature is never exposed before an
    ///     administrator explicitly enables it in the permission matrix.
    /// </summary>
    public class PermissionSyncService
    {
        private readonly RolePermissionRepository _repository;

        public PermissionSyncService(string? dbPath = null)
        {
            dbPath ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuitePlatforms", "proweighsuiteplatform.db");

            _repository = new RolePermissionRepository(dbPath);
        }

        /// <summary>All permission resources currently defined in code.</summary>
        public static List<Permission> GetAllPermissions() =>
            Enum.GetValues(typeof(Permission)).Cast<Permission>().ToList();

        /// <summary>All user roles currently defined in code.</summary>
        public static List<UserRole> GetAllRoles() =>
            Enum.GetValues(typeof(UserRole)).Cast<UserRole>().ToList();

        /// <summary>
        /// Returns the (role, permission) combinations that exist in code but are not yet
        /// present in the role_permissions table.
        /// </summary>
        public async Task<List<(UserRole Role, Permission Permission)>> GetMissingPermissionsAsync()
        {
            await _repository.InitializeAsync();

            var existing = await _repository.GetAllAsync();
            var existingSet = existing.Select(rp => (rp.Role, rp.Permission)).ToHashSet();

            var missing = new List<(UserRole, Permission)>();
            foreach (var role in GetAllRoles())
            {
                foreach (var permission in GetAllPermissions())
                {
                    if (!existingSet.Contains((role, permission)))
                        missing.Add((role, permission));
                }
            }
            return missing;
        }

        /// <summary>
        /// Scans for permission resources not yet present in the permission system and
        /// inserts the missing role-permission rows with the default grant policy.
        /// Returns the number of rows added (0 = already up to date).
        /// </summary>
        public async Task<int> SyncAllPermissionsAsync()
        {
            var missing = await GetMissingPermissionsAsync();
            if (missing.Count == 0)
                return 0;

            var updates = missing.Select(m => (
                m.Role,
                m.Permission,
                isGranted: m.Role == UserRole.SuperAdmin)).ToList();

            await _repository.UpdateMultiplePermissionsAsync(updates, "System (AutoSync)");
            return updates.Count;
        }
    }
}
