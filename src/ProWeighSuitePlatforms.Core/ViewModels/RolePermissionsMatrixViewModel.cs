using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Hosting;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class PermissionMatrixRow : BaseViewModel
    {
        public Permission Permission { get; set; }
        public string PermissionName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;

        private bool _superAdminGranted;
        public bool SuperAdminGranted
        {
            get => _superAdminGranted;
            set => SetProperty(ref _superAdminGranted, value);
        }

        private bool _adminGranted;
        public bool AdminGranted
        {
            get => _adminGranted;
            set => SetProperty(ref _adminGranted, value);
        }

        private bool _operatorGranted;
        public bool OperatorGranted
        {
            get => _operatorGranted;
            set => SetProperty(ref _operatorGranted, value);
        }
    }

    public class RolePermissionsMatrixViewModel : BaseViewModel
    {
        private readonly MainViewModel _main;
        private readonly IHostContext _host;
        private readonly RolePermissionService _service;
        private readonly AuditService _auditService;
        private readonly PermissionSyncService _syncService;

        private string? _resolvedScopeModuleId;

        public ObservableCollection<PermissionMatrixRow> PermissionRows { get; } = new();

        public string Title => "Role Permissions Matrix";

        private string _scopeDisplayName = "All Modules";
        public string ScopeDisplayName
        {
            get => _scopeDisplayName;
            private set => SetProperty(ref _scopeDisplayName, value);
        }

        public User? CurrentUser => _main.CurrentUser;
        
        public bool CanManagePermissions => HasPermission(CurrentUser, Permission.EditRolePermissions);

        public ICommand SaveCommand { get; }
        public ICommand ResetToDefaultsCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SyncMenusCommand { get; }

        private bool _hasChanges;
        public bool HasChanges
        {
            get => _hasChanges;
            set => SetProperty(ref _hasChanges, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private string _syncStatusMessage = string.Empty;
        public string SyncStatusMessage
        {
            get => _syncStatusMessage;
            set
            {
                if (SetProperty(ref _syncStatusMessage, value))
                    RaisePropertyChanged(nameof(HasSyncStatusMessage));
            }
        }

        public bool HasSyncStatusMessage => !string.IsNullOrEmpty(SyncStatusMessage);

        public RolePermissionsMatrixViewModel(MainViewModel main) : this(main, HostContextLocator.Current!)
        {
        }

        public RolePermissionsMatrixViewModel(MainViewModel main, IHostContext host)
        {
            _main = main;
            _host = host;
            _auditService = new AuditService();

            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuitePlatforms", "proweighsuiteplatform.db");

            _service = new RolePermissionService(new RolePermissionRepository(dbPath));
            _syncService = new PermissionSyncService(dbPath);

            SaveCommand = new RelayCommand(async _ => await SaveChangesAsync(), _ => CanManagePermissions && HasChanges && !IsLoading);
            ResetToDefaultsCommand = new RelayCommand(async _ => await ResetToDefaultsAsync(), _ => CanManagePermissions && !IsLoading);
            RefreshCommand = new RelayCommand(async _ => await LoadPermissionsAsync(), _ => !IsLoading);
            SyncMenusCommand = new RelayCommand(async _ => await SyncMenusAsync(), _ => CanManagePermissions && !IsLoading);

            // Load data asynchronously without blocking
            System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
            {
                await InitializeAsync();
            });
        }

        public RolePermissionsMatrixViewModel() : this(new MainViewModel(), HostContextLocator.Current!) { }

        private async Task InitializeAsync()
        {
            if (IsLoading) return;
            
            IsLoading = true;
            
            try
            {
                System.Diagnostics.Debug.WriteLine("=== RolePermissionsMatrixViewModel Initializing ===");
                
                await _service.InitializeAsync();
                System.Diagnostics.Debug.WriteLine("Service initialized");

                // Auto-discover any permission resources (menus/features) added since the
                // last run and register them so they are immediately assignable per role.
                try
                {
                    var added = await _syncService.SyncAllPermissionsAsync();
                    if (added > 0)
                    {
                        SyncStatusMessage = $"Auto-sync: {added} new permission(s) detected and registered. Assign them to the appropriate roles below.";
                        System.Diagnostics.Debug.WriteLine($"Permission matrix auto-synced: {added} new permission(s) added.");
                        await _auditService.LogPermissionsSyncedAsync(CurrentUser?.Username ?? "System", added);
                    }
                }
                catch (Exception syncEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Permission auto-sync failed: {syncEx.Message}");
                }

                await LoadPermissionsAsync();
                System.Diagnostics.Debug.WriteLine($"Loaded {PermissionRows.Count} permission rows");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in InitializeAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                
                // Show error on UI thread
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error initializing permissions: {ex.Message}\n\nPlease restart the application.", 
                        "Initialization Error",
                        MessageBoxButton.OK, 
                        MessageBoxImage.Error);
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadPermissionsAsync()
        {
            if (IsLoading) return;
            
            IsLoading = true;
            
            try
            {
                System.Diagnostics.Debug.WriteLine("=== LoadPermissionsAsync Started ===");
                
                var rolePermissions = await _service.GetAllAsync();
                System.Diagnostics.Debug.WriteLine($"Retrieved {rolePermissions.Count} role permissions from database");

                // Group permissions by Permission enum
                var permissionGroups = rolePermissions
                    .GroupBy(rp => rp.Permission)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // Clear on UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    PermissionRows.Clear();
                });

                // Get all permissions from the enum
                var allPermissions = Enum.GetValues(typeof(Permission)).Cast<Permission>().ToList();
                System.Diagnostics.Debug.WriteLine($"Processing {allPermissions.Count} permissions from enum");

                // Show only the permissions relevant to the build/module in use
                // (Nexus flavor -> Nexus menus, Fusion flavor -> Fusion menus).
                var scopedPermissions = ResolveScopedPermissions();

                foreach (var permission in allPermissions)
                {
                    if (scopedPermissions != null && !scopedPermissions.Contains(permission))
                        continue;

                    var row = new PermissionMatrixRow
                    {
                        Permission = permission,
                        PermissionName = GetPermissionDisplayName(permission),
                        Category = GetPermissionCategory(permission)
                    };

                    // Get permissions for this permission
                    if (permissionGroups.TryGetValue(permission, out var rolePerms))
                    {
                        row.SuperAdminGranted = rolePerms.Any(rp => rp.Role == UserRole.SuperAdmin && rp.IsGranted);
                        row.AdminGranted = rolePerms.Any(rp => rp.Role == UserRole.Admin && rp.IsGranted);
                        row.OperatorGranted = rolePerms.Any(rp => rp.Role == UserRole.Operator && rp.IsGranted);
                    }

                    // Subscribe to property changed events to track changes
                    row.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName is nameof(PermissionMatrixRow.SuperAdminGranted) or 
                            nameof(PermissionMatrixRow.AdminGranted) or 
                            nameof(PermissionMatrixRow.OperatorGranted))
                        {
                            HasChanges = true;
                        }
                    };

                    // Add on UI thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        PermissionRows.Add(row);
                    });
                }

                HasChanges = false;
                System.Diagnostics.Debug.WriteLine("=== LoadPermissionsAsync Completed Successfully ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in LoadPermissionsAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error loading permissions: {ex.Message}\n\nDetails: {ex.InnerException?.Message}", 
                        "Load Error",
                        MessageBoxButton.OK, 
                        MessageBoxImage.Error);
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Determines which module's permission scope the matrix should display:
        /// single-product hosts scope to that product; multi-product development
        /// hosts scope to the module the user is currently working in. Returns
        /// null when no module can be resolved (matrix shows all permissions).
        /// </summary>
        private string? ResolveScopeModuleId()
        {
            try
            {
                var productModules = _host.CoreHost?.Modules.Modules
                    .Where(module => !string.Equals(
                        module.Metadata.ModuleId, "Core", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (productModules is { Count: 1 })
                    return MapToRegistryModuleId(productModules[0].Metadata.ModuleId);

                if (_main.CurrentViewModel is ShellViewModel shell &&
                    !string.IsNullOrWhiteSpace(shell.ActiveTransactionModuleId))
                    return MapToRegistryModuleId(shell.ActiveTransactionModuleId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Scope module resolution failed: {ex.Message}");
            }

            return null;
        }

        private static string MapToRegistryModuleId(string moduleId)
        {
            if (string.Equals(moduleId, "StandardWeighing", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(moduleId, "HybridPlc", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(moduleId, "HybridPlcRfid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(moduleId, "Inventory", StringComparison.OrdinalIgnoreCase))
            {
                return "Standard";
            }

            return moduleId;
        }

        /// <summary>
        /// Loads module-registered permission definitions for the resolved module.
        /// Core permissions are included for every product module. Returns null
        /// (show every permission) when the module cannot be resolved or has no scope.
        /// </summary>
        private HashSet<Permission>? ResolveScopedPermissions()
        {
            try
            {
                var moduleId = ResolveScopeModuleId();
                _resolvedScopeModuleId = moduleId;
                if (string.IsNullOrWhiteSpace(moduleId))
                {
                    ScopeDisplayName = "All Modules";
                    return null;
                }

                var scope = new HashSet<Permission>();
                var host = _host.CoreHost;
                if (host is not null)
                {
                    foreach (var definition in host.Permissions.All)
                    {
                        if (!string.Equals(definition.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(definition.ModuleId, "Core", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var legacyKey = definition.LegacyKey ?? definition.Key;
                        if (Enum.TryParse<Permission>(legacyKey, true, out var permission))
                            scope.Add(permission);
                    }
                }

                if (scope.Count == 0)
                {
                    ScopeDisplayName = "All Modules";
                    return null;
                }

                ScopeDisplayName = moduleId;
                System.Diagnostics.Debug.WriteLine($"Permission matrix scoped to module '{moduleId}' ({scope.Count} permissions)");
                return scope;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Scope resolution failed: {ex.Message}");
                _resolvedScopeModuleId = null;
                ScopeDisplayName = "All Modules";
                return null;
            }
        }

        private ProWeighSuitePlatforms.Core.Navigation.PermissionDefinition? FindRegistryDefinition(Permission permission)
        {
            var host = _host.CoreHost;
            if (host is null)
                return null;

            var matches = host.Permissions.All
                .Where(definition =>
                    Enum.TryParse<Permission>(definition.LegacyKey ?? definition.Key, true, out var mapped) &&
                    mapped == permission)
                .ToList();

            if (_resolvedScopeModuleId is not null)
            {
                var scoped = matches.FirstOrDefault(definition =>
                    string.Equals(definition.ModuleId, _resolvedScopeModuleId, StringComparison.OrdinalIgnoreCase));
                if (scoped is not null)
                    return scoped;
            }

            return matches.FirstOrDefault(definition =>
                string.Equals(definition.ModuleId, "Core", StringComparison.OrdinalIgnoreCase))
                ?? matches.FirstOrDefault();
        }

        private async Task SyncMenusAsync()
        {
            if (IsLoading) return;

            if (!CanManagePermissions)
            {
                MessageBox.Show("You do not have permission to sync permissions.", "Access Denied",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsLoading = true;
            try
            {
                var added = await _syncService.SyncAllPermissionsAsync();
                SyncStatusMessage = added > 0
                    ? $"Sync complete: {added} new permission(s) detected and added. Assign them to the appropriate roles below."
                    : "Sync complete: all permissions are already up to date.";

                await _auditService.LogPermissionsSyncedAsync(CurrentUser?.Username ?? "Unknown", added);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in SyncMenusAsync: {ex.Message}");
                MessageBox.Show($"Error syncing permissions: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }

            // Reload the grid so any newly added permission rows appear immediately
            await LoadPermissionsAsync();
        }

        private async Task SaveChangesAsync()
        {
            if (IsLoading) return;
            
            IsLoading = true;
            
            try
            {
                if (!CanManagePermissions)
                {
                    MessageBox.Show("You do not have permission to manage permissions.", "Access Denied",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var updates = new List<(UserRole role, Permission permission, bool isGranted)>();

                foreach (var row in PermissionRows)
                {
                    updates.Add((UserRole.SuperAdmin, row.Permission, row.SuperAdminGranted));
                    updates.Add((UserRole.Admin, row.Permission, row.AdminGranted));
                    updates.Add((UserRole.Operator, row.Permission, row.OperatorGranted));
                }

                var modifiedBy = CurrentUser?.Username ?? "Unknown";
                await _service.UpdateMultiplePermissionsAsync(updates, modifiedBy);
                await _auditService.LogPermissionsChangedAsync(modifiedBy, updates.Count);

                // Clear the authorization service cache to reload permissions
                var authService = new AuthorizationService();
                authService.ClearCache();
                await authService.LoadPermissionsFromDatabaseAsync();

                HasChanges = false;

                MessageBox.Show("Permissions saved successfully!\n\nChanges will take effect immediately.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in SaveChangesAsync: {ex.Message}");
                MessageBox.Show($"Error saving permissions: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ResetToDefaultsAsync()
        {
            if (IsLoading) return;
            
            IsLoading = true;
            
            try
            {
                if (!CanManagePermissions)
                {
                    MessageBox.Show("You do not have permission to reset permissions.", "Access Denied",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var modifiedBy = CurrentUser?.Username ?? "Unknown";
                var result = MessageBox.Show(
                    "Are you sure you want to reset all permissions to default values?\n\n" +
                    "This will overwrite all current permission settings.",
                    "Confirm Reset",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    var success = await _service.ResetToDefaultsAsync();

                    if (success)
                    {
                        // Re-register any permission resources not part of the default set so
                        // newly added menus/features remain assignable after a reset.
                        var reSynced = 0;
                        try
                        {
                            reSynced = await _syncService.SyncAllPermissionsAsync();
                        }
                        catch (Exception syncEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Permission re-sync after reset failed: {syncEx.Message}");
                        }
                        if (reSynced > 0)
                        {
                            SyncStatusMessage = $"Reset complete: {reSynced} new permission(s) re-registered. Assign them to the appropriate roles below.";
                        }

                        // Clear the authorization service cache
                        var authService = new AuthorizationService();
                        authService.ClearCache();
                        await authService.LoadPermissionsFromDatabaseAsync();

                        await LoadPermissionsAsync();

                        await _auditService.LogPermissionsResetAsync(modifiedBy);
                        MessageBox.Show("Permissions reset to defaults successfully!",
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Failed to reset permissions.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in ResetToDefaultsAsync: {ex.Message}");
                await _auditService.LogErrorAsync(CurrentUser?.Username ?? "Unknown", "Reset role permissions", ex);
                MessageBox.Show($"Error resetting permissions: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private string GetPermissionDisplayName(Permission permission)
        {
            var definition = FindRegistryDefinition(permission);
            if (definition is not null && !string.IsNullOrWhiteSpace(definition.DisplayName))
                return definition.DisplayName;

            return permission switch
            {
                Permission.ViewDashboard => "View Dashboard",
                Permission.StartWeighing => "Start Weighing",
                Permission.AcceptWeight => "Accept Weight",
                Permission.PauseBatch => "Pause Batch",
                Permission.AbortBatch => "Abort Batch",
                
                Permission.ViewMaterials => "View Materials",
                Permission.CreateMaterial => "Create Material",
                Permission.EditMaterial => "Edit Material",
                Permission.DeleteMaterial => "Delete Material",
                Permission.ImportMaterial => "Import Materials (CSV)",
                Permission.DownloadMaterialTemplate => "Download Material Template",
                Permission.ExportMaterial => "Export Materials",
                
                Permission.ViewRecipes => "View Recipes",
                Permission.CreateRecipe => "Create Recipe",
                Permission.EditRecipe => "Edit Recipe",
                Permission.DeleteRecipe => "Delete Recipe",
                Permission.PrintRecipe => "Print Recipe",
                
                Permission.ViewCategory => "View Category",
                Permission.CreateCategory => "Create Category",
                Permission.EditCategory => "Edit Category",
                Permission.DeleteCategory => "Delete Category",
                Permission.ImportCategory => "Import Categories (CSV)",
                Permission.DownloadCategoryTemplate => "Download Category Template",
                Permission.ExportCategory => "Export Categories",
                
                Permission.ViewVariant => "View Variant",
                Permission.CreateVariant => "Create Variant",
                Permission.EditVariant => "Edit Variant",
                Permission.DeleteVariant => "Delete Variant",
                Permission.ImportVariant => "Import Variants (CSV)",
                Permission.DownloadVariantTemplate => "Download Variant Template",
                Permission.ExportVariant => "Export Variants",

                Permission.ViewSupplier => "View Supplier",
                Permission.CreateSupplier => "Create Supplier",
                Permission.EditSupplier => "Edit Supplier",
                Permission.DeleteSupplier => "Delete Supplier",
                Permission.ImportSupplier => "Import Suppliers (CSV)",
                Permission.DownloadSupplierTemplate => "Download Supplier Template",
                Permission.ExportSupplier => "Export Suppliers",
                
                Permission.ViewScaleSettings => "View Scale Settings",
                Permission.CreateScaleSettings => "Create Scale Settings",
                Permission.EditScaleSettings => "Edit Scale Settings",
                Permission.DeleteScaleSettings => "Delete Scale Settings",
                
                Permission.ViewUsers => "View Users",
                Permission.CreateUser => "Create User",
                Permission.EditUser => "Edit User",
                Permission.DeleteUser => "Delete User",
                Permission.ChangeUserRole => "Change User Role",
                
                Permission.ViewReports => "View Reports",
                Permission.ExportReports => "Export Reports",
                
                Permission.ViewSystemSettings => "View System Settings",
                Permission.EditSystemSettings => "Edit System Settings",
                
                Permission.ViewRolePermissions => "View Role Permissions",
                Permission.EditRolePermissions => "Edit Role Permissions",
                
                Permission.ViewCompanySettings => "View Company Settings",
                Permission.EditCompanySettings => "Edit Company Settings",
                
                Permission.ViewModules => "View Module Matrix",
                Permission.EditModules => "Edit Module Matrix",
                
                _ => permission.ToString()
            };
        }

        private string GetPermissionCategory(Permission permission)
        {
            var definition = FindRegistryDefinition(permission);
            if (definition is not null && !string.IsNullOrWhiteSpace(definition.Group))
                return definition.Group;

            return permission switch
            {
                Permission.ViewDashboard => "Dashboard",
                Permission.StartWeighing => "Dashboard",
                Permission.AcceptWeight => "Dashboard",
                Permission.PauseBatch => "Dashboard",
                Permission.AbortBatch => "Dashboard",
                
                Permission.ViewMaterials => "Materials",
                Permission.CreateMaterial => "Materials",
                Permission.EditMaterial => "Materials",
                Permission.DeleteMaterial => "Materials",
                Permission.ImportMaterial => "Materials",
                Permission.DownloadMaterialTemplate => "Materials",
                Permission.ExportMaterial => "Materials",
                
                Permission.ViewRecipes => "Recipes",
                Permission.CreateRecipe => "Recipes",
                Permission.EditRecipe => "Recipes",
                Permission.DeleteRecipe => "Recipes",
                Permission.PrintRecipe => "Recipes",
                
                Permission.ViewCategory => "Category",
                Permission.CreateCategory => "Category",
                Permission.EditCategory => "Category",
                Permission.DeleteCategory => "Category",
                Permission.ImportCategory => "Category",
                Permission.DownloadCategoryTemplate => "Category",
                Permission.ExportCategory => "Category",
                
                Permission.ViewVariant => "Variant",
                Permission.CreateVariant => "Variant",
                Permission.EditVariant => "Variant",
                Permission.DeleteVariant => "Variant",
                Permission.ImportVariant => "Variant",
                Permission.DownloadVariantTemplate => "Variant",
                Permission.ExportVariant => "Variant",

                Permission.ViewSupplier => "Supplier",
                Permission.CreateSupplier => "Supplier",
                Permission.EditSupplier => "Supplier",
                Permission.DeleteSupplier => "Supplier",
                Permission.ImportSupplier => "Supplier",
                Permission.DownloadSupplierTemplate => "Supplier",
                Permission.ExportSupplier => "Supplier",
                
                Permission.ViewScaleSettings => "Scale Settings",
                Permission.CreateScaleSettings => "Scale Settings",
                Permission.EditScaleSettings => "Scale Settings",
                Permission.DeleteScaleSettings => "Scale Settings",
                
                Permission.ViewUsers => "User Management",
                Permission.CreateUser => "User Management",
                Permission.EditUser => "User Management",
                Permission.DeleteUser => "User Management",
                Permission.ChangeUserRole => "User Management",
                
                Permission.ViewReports => "Reports",
                Permission.ExportReports => "Reports",
                
                Permission.ViewSystemSettings => "System Settings",
                Permission.EditSystemSettings => "System Settings",
                
                Permission.ViewRolePermissions => "Permissions",
                Permission.EditRolePermissions => "Permissions",
                
                Permission.ViewCompanySettings => "Company Settings",
                Permission.EditCompanySettings => "Company Settings",
                
                Permission.ViewModules => "Module Management",
                Permission.EditModules => "Module Management",
                
                _ => "Other"
            };
        }
    }
}
