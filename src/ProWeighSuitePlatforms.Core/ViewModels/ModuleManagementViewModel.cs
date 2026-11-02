// Phase 6 — SuperAdmin module matrix (mirrors RolePermissionsMatrix pattern).
// Core rows: checked + readonly. Weighing rows: per-role checks + single Active radio.
// Inventory rows: per-role checks (multi); ParentModuleId reserved for future dependents
// (child enabled only when parent enabled — enforced on save).
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Hosting;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.ViewModels
{
    public sealed class ModuleMatrixRow : BaseViewModel
    {
        public string ModuleId { get; }
        public string DisplayName { get; }
        public string GroupName { get; }
        public string? ParentModuleId { get; }
        public bool IsCore => GroupName == ModuleAssignmentRepository.GroupCore;
        public bool IsWeighing => GroupName == ModuleAssignmentRepository.GroupWeighing;

        private bool _superAdmin;
        public bool SuperAdminEnabled { get => _superAdmin; set => SetProperty(ref _superAdmin, value); }
        private bool _admin;
        public bool AdminEnabled { get => _admin; set => SetProperty(ref _admin, value); }
        private bool _operator;
        public bool OperatorEnabled { get => _operator; set => SetProperty(ref _operator, value); }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        public ModuleMatrixRow(string id, string name, string group, string? parent)
        {
            ModuleId = id;
            DisplayName = name;
            GroupName = group;
            ParentModuleId = parent;
        }
    }

    public class ModuleManagementViewModel : BaseViewModel
    {
        public string Title => "Module Management";

        private readonly MainViewModel _main;
        private readonly IHostContext _host;
        private readonly ModuleAssignmentRepository _repo;
        private readonly AuditService _audit;

        public ObservableCollection<ModuleMatrixRow> Rows { get; } = new();

        public User? CurrentUser => _main.CurrentUser;
        public bool CanManageModules => CurrentUser?.Role == UserRole.SuperAdmin
            || HasPermission(CurrentUser, Permission.EditModules);

        private string _activeWeighing = ModuleAssignmentRepository.DefaultActiveWeighing;
        public string ActiveWeighingModuleId
        {
            get => _activeWeighing;
            set
            {
                if (SetProperty(ref _activeWeighing, value))
                    foreach (var r in Rows.Where(r => r.IsWeighing))
                        r.IsActive = string.Equals(r.ModuleId, value, StringComparison.OrdinalIgnoreCase);
            }
        }

        private string _status = string.Empty;
        public string StatusMessage { get => _status; set => SetProperty(ref _status, value); }

        public ICommand SaveCommand { get; }
        public ICommand RefreshCommand { get; }

        public ModuleManagementViewModel(MainViewModel main) : this(main, HostContextLocator.Current!)
        {
        }

        public ModuleManagementViewModel(MainViewModel main, IHostContext host)
        {
            _main = main;
            _host = host;
            _audit = new AuditService();
            _repo = new ModuleAssignmentRepository(AppConfigurationManager.GetDatabasePath());
            SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => CanManageModules);
            RefreshCommand = new RelayCommand(async _ => await LoadAsync());
            _ = LoadAsync();
        }

        public ModuleManagementViewModel() : this(new MainViewModel(), HostContextLocator.Current!) { }

        public async Task LoadAsync()
        {
            try
            {
                await _repo.InitializeAsync();
                var enabled = await _repo.GetEnabledMapAsync();
                ActiveWeighingModuleId = await _repo.GetActiveWeighingModuleAsync();
                Rows.Clear();
                foreach (var d in ModuleAssignmentRepository.SeedDefinitions)
                {
                    var row = new ModuleMatrixRow(d.ModuleId, d.DisplayName, d.GroupName, d.ParentModuleId);
                    if (d.GroupName == ModuleAssignmentRepository.GroupCore)
                    {
                        row.SuperAdminEnabled = row.AdminEnabled = row.OperatorEnabled = true;
                    }
                    else
                    {
                        enabled.TryGetValue(d.ModuleId, out var roles);
                        row.SuperAdminEnabled = roles == null || roles.Contains(UserRole.SuperAdmin);
                        row.AdminEnabled = roles == null || roles.Contains(UserRole.Admin);
                        row.OperatorEnabled = roles == null || roles.Contains(UserRole.Operator);
                    }
                    row.IsActive = row.IsWeighing &&
                        string.Equals(row.ModuleId, ActiveWeighingModuleId, StringComparison.OrdinalIgnoreCase);
                    Rows.Add(row);
                }
                StatusMessage = "Loaded. Core modules are always on (readonly).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Load failed: {ex.Message}";
            }
        }

        public async Task SaveAsync()
        {
            try
            {
                CheckPermissionOrThrow(_main.CurrentUser, Permission.EditModules);
            }
            catch (UnauthorizedAccessException)
            {
                // SuperAdmin bypass: explicit role check (fresh installs may lack the new permission row).
                if (_main.CurrentUser?.Role != UserRole.SuperAdmin)
                {
                    MessageBox.Show("You do not have permission to edit modules.", "Access Denied",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            try
            {
                await _repo.InitializeAsync();
                var by = _main.CurrentUser?.Username ?? "System";
                // Dependent guard: child saved only when parent enabled for that role.
                foreach (var r in Rows.Where(r => !r.IsCore))
                {
                    await _repo.SetRoleModuleAsync(UserRole.SuperAdmin, r.ModuleId, GuardParent(r, UserRole.SuperAdmin), by);
                    await _repo.SetRoleModuleAsync(UserRole.Admin, r.ModuleId, GuardParent(r, UserRole.Admin), by);
                    await _repo.SetRoleModuleAsync(UserRole.Operator, r.ModuleId, GuardParent(r, UserRole.Operator), by);
                }
                var active = Rows.FirstOrDefault(r => r.IsWeighing && r.IsActive)?.ModuleId
                    ?? ModuleAssignmentRepository.DefaultActiveWeighing;
                await _repo.SetActiveWeighingModuleAsync(active, ModuleAssignmentRepository.WeighingOnly.Instance);
                ActiveWeighingModuleId = active;
                await _audit.LogSettingsChangedAsync(by, "Module Management", null,
                    $"ActiveWeighing={active}, roles updated");
                _host.NotifyNavigation(ProWeighSuitePlatforms.Core.Navigation.NavigationChangeReason.ModuleStateChanged);
                StatusMessage = $"Saved. Active weighing: {active}. Takes effect on next navigation (Shell refreshes).";
                MessageBox.Show(StatusMessage, "Modules Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Save failed: {ex.Message}";
                MessageBox.Show(StatusMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool GuardParent(ModuleMatrixRow row, UserRole role)
        {
            bool own = role == UserRole.SuperAdmin ? row.SuperAdminEnabled
                : role == UserRole.Admin ? row.AdminEnabled : row.OperatorEnabled;
            if (!own || string.IsNullOrEmpty(row.ParentModuleId)) return own;
            var parent = Rows.FirstOrDefault(r => r.ModuleId == row.ParentModuleId);
            if (parent == null) return own;
            bool parentOn = role == UserRole.SuperAdmin ? parent.SuperAdminEnabled
                : role == UserRole.Admin ? parent.AdminEnabled : parent.OperatorEnabled;
            return own && parentOn;
        }
    }
}
