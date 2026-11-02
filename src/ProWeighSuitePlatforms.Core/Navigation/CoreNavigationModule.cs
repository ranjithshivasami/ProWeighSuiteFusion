using System;
using ProWeighSuitePlatforms.Core.Modules;
using ProWeighSuitePlatforms.Core.Navigation;
using ProWeighSuitePlatforms.Core.Wpf.Navigation;
using ProWeighSuitePlatforms.Core.Wpf.Views;
using Microsoft.Extensions.DependencyInjection;
using ProWeighSuitePlatforms.ViewModels;
using ProWeighSuitePlatforms.Views;
using static ProWeighSuitePlatforms.Modules.Infrastructure.ModuleViewRegistration;

namespace ProWeighSuitePlatforms.Modules.Core
{
    // Phase 2: shared admin/configuration entries as Core-host contributions.
    // Route/menu IDs keep the existing legacy IDs so the Phase 3 shell can
    // replace the legacy commands without touching persisted identifiers.
    public sealed class CoreNavigationModule : IModule
    {
        public ModuleMetadata Metadata { get; } = new(
            ModuleId: "Core",
            DisplayName: "Core",
            Version: new Version(1, 0, 0),
            MinCoreVersion: new Version(1, 0, 0),
            Order: 10);

        public void Register(IModuleRegistrationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            RegisterPermissions(context);
            RegisterRoutes(context);
            RegisterMenus(context);
            RegisterProductModules(context);
        }

        public static void RegisterViews(IViewFactoryRegistryBuilder registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            RegisterView(registry, "Home", services =>
            {
                var context = Context(services);
                var main = RequireMain(context);
                return new DashboardViewModel(
                    main,
                    services.GetRequiredService<IShellNavigator>(),
                    main.CurrentUser);
            }, viewModel => new DashboardView { DataContext = viewModel });

            RegisterView(registry, "Users", services =>
                new UserManagementViewModel(RequireMain(Context(services))),
                viewModel => new UserManagementView { DataContext = viewModel },
                RouteCacheMode.KeepAlive);

            RegisterView(registry, "Permissions", services =>
                new RolePermissionsMatrixViewModel(RequireMain(Context(services))),
                viewModel => new RolePermissionsMatrixView { DataContext = viewModel });

            RegisterView(registry, "Info", services =>
                new InfoViewModel(),
                viewModel => new InfoView { DataContext = viewModel });

            RegisterView(registry, "ScaleSettings", services =>
                new SettingsViewModel(RequireMain(Context(services))),
                viewModel => new ScaleSettingsView { DataContext = viewModel });

            RegisterView(registry, "CompanySettings", services =>
                new CompanySettingsViewModel(RequireMain(Context(services))),
                viewModel => new CompanySettingsView { DataContext = viewModel });

            RegisterView(registry, "AppSettings", services =>
            {
                var branding = services.GetService<ProWeighSuitePlatforms.Core.Branding.IBrandingProvider>();
                return new AppSettingsViewModel(
                    Context(services).CurrentUsername,
                    branding?.PrimaryColor,
                    branding?.SecondaryColor,
                    branding?.ThemeName);
            }, viewModel => new AppSettingsView { DataContext = viewModel });

            RegisterView(registry, "SyncSettings", services =>
                new SyncSettingsViewModel(Context(services).CurrentUsername),
                viewModel => new SyncSettingsView { DataContext = viewModel });

            RegisterView(registry, "BackupSettings", services =>
                new BackupSettingsViewModel(services.GetRequiredService<ProWeighSuitePlatforms.Services.BackupService>()),
                viewModel => new BackupSettingsView { DataContext = viewModel });

            RegisterView(registry, "BackupLog", _ => new BackupLogViewModel(),
                viewModel => new BackupLogView { DataContext = viewModel });

            RegisterView(registry, "ReportSchedule", services =>
                new ReportScheduleViewModel(
                    services.GetRequiredService<ProWeighSuitePlatforms.Services.ScheduledReportService>()),
                viewModel => new ReportScheduleView { DataContext = viewModel });

            RegisterView(registry, "LabelTemplates", services =>
                new ZplTemplateViewModel(Context(services).CurrentUsername),
                viewModel => new ZplTemplateView { DataContext = viewModel });

            RegisterView(registry, "PrinterSetup", services =>
                new PrinterConnectionViewModel(Context(services).CurrentUsername),
                viewModel => new PrinterConnectionView { DataContext = viewModel });

            RegisterView(registry, "PlcSettings", _ => new ModbusSettingsViewModel(),
                viewModel => new ModbusSettingsView { DataContext = viewModel });

            RegisterView(registry, "ScannerSettings", services =>
                new ScannerSettingsViewModel(Context(services).CurrentUsername),
                viewModel => new ScannerSettingsView { DataContext = viewModel });

            RegisterView(registry, "CleanUpDatabase", services =>
                new CleanUpDatabaseViewModel(
                    RequireMain(Context(services)),
                    services.GetRequiredService<IShellNavigator>(),
                    services.GetRequiredService<ProWeighSuitePlatforms.Platform.Weighing.Contracts.ITransactionRetryQueue>(),
                    ProWeighSuitePlatforms.Configuration.BrandingInfo.Flavor),
                viewModel => new CleanUpDatabaseView { DataContext = viewModel });

            RegisterView(registry, "FailedSyncs", services =>
                new FailedSyncViewerViewModel(
                    services.GetRequiredService<ProWeighSuitePlatforms.Platform.Weighing.Contracts.ITransactionRetryQueue>()),
                viewModel => new FailedSyncViewerView { DataContext = viewModel });
        }

        private static void RegisterProductModules(IModuleRegistrationContext context)
        {
            AddCoreProductModule(context, "Home", "Home");
            AddCoreProductModule(context, "Master", "Master (Category/Variant/Materials/Supplier)");
            AddCoreProductModule(context, "Configuration", "Configuration");
            AddCoreProductModule(context, "QueueMonitor", "Queue Monitor");
            AddCoreProductModule(context, "FailedSyncs", "Failed Syncs");
            AddCoreProductModule(context, "AuditLogs", "Audit Logs");
            AddCoreProductModule(context, "Users", "Users");
            AddCoreProductModule(context, "Permissions", "Permissions");
            AddCoreProductModule(context, "Reports", "Reports");
            AddCoreProductModule(context, "SummaryReport", "Summary Report");
            AddCoreProductModule(context, "Info", "Info");
        }

        private static void AddCoreProductModule(
            IModuleRegistrationContext context,
            string moduleId,
            string displayName)
        {
            context.ProductModules.Add(new ProductModuleDefinition(
                moduleId,
                displayName,
                ProductModuleGroups.Core,
                IsLicenseGated: false));
        }

        private static void RegisterPermissions(IModuleRegistrationContext context)
        {
            AddPermission(context, "core.dashboard.view", "View dashboard", "Home", "ViewDashboard");
            AddPermission(context, "core.users.view", "View users", "User Management", "ViewUsers");
            AddPermission(context, "core.permissions.view", "View role permissions", "Permissions", "ViewRolePermissions");
            AddPermission(context, "core.audit.view", "View audit logs", "Audit", "ViewRolePermissions");
            AddPermission(context, "core.scale-settings.view", "View scale settings", "Configuration", "ViewScaleSettings");
            AddPermission(context, "core.company-settings.view", "View company settings", "Configuration", "ViewCompanySettings");
            AddPermission(context, "core.system-settings.manage", "Manage system settings", "Configuration", "EditSystemSettings");
        }

        private static void RegisterRoutes(IModuleRegistrationContext context)
        {
            AddRoute(context, "Home", "Home", "core.dashboard.view", MenuRegion.Primary, 10);
            AddRoute(context, "QueueMonitor", "Queue monitor", null, MenuRegion.Primary, 100);
            AddRoute(context, "FailedSyncs", "Failed syncs", null, MenuRegion.Primary, 110);

            AddRoute(context, "Users", "Users", "core.users.view", MenuRegion.Admin, 121);
            AddRoute(context, "Permissions", "Role permissions", "core.permissions.view", MenuRegion.Admin, 122);
            AddRoute(context, "AuditLogs", "Audit logs", "core.audit.view", MenuRegion.Admin, 120,
                "core.company-settings.view");
            AddRoute(context, "Info", "Info", null, MenuRegion.Admin, 160);

            AddRoute(context, "ScaleSettings", "Scale settings", "core.scale-settings.view", MenuRegion.Configuration, 10);
            AddRoute(context, "CompanySettings", "Company settings", "core.company-settings.view", MenuRegion.Configuration, 20);
            AddRoute(context, "AppSettings", "App settings", "core.company-settings.view", MenuRegion.Configuration, 30);
            AddRoute(context, "SyncSettings", "Sync settings", "core.company-settings.view", MenuRegion.Configuration, 40);
            AddRoute(context, "BackupSettings", "Backup settings", "core.company-settings.view", MenuRegion.Configuration, 50);
            AddRoute(context, "BackupLog", "Backup log", "core.company-settings.view", MenuRegion.Configuration, 60);
            AddRoute(context, "ReportSchedule", "Report schedule", "core.company-settings.view", MenuRegion.Configuration, 70);
            AddRoute(context, "LabelTemplates", "Label templates", "core.company-settings.view", MenuRegion.Configuration, 80);
            AddRoute(context, "PrinterSetup", "Printer setup", "core.company-settings.view", MenuRegion.Configuration, 90);
            AddRoute(context, "PlcSettings", "PLC settings", "core.company-settings.view", MenuRegion.Configuration, 100);
            AddRoute(context, "ScannerSettings", "Scanner settings", "core.company-settings.view", MenuRegion.Configuration, 110);
            AddRoute(context, "CleanUpDatabase", "Clean up database", "core.system-settings.manage", MenuRegion.Configuration, 120);
        }

        private static void RegisterMenus(IModuleRegistrationContext context)
        {
            AddMenu(context, "Home", "Home", MenuRegion.Primary, 10, "Home", "core.dashboard.view");
            AddMenu(context, "QueueMonitor", "Queue monitor", MenuRegion.Primary, 100, "MonitorDashboard", null, isAction: true);
            AddMenu(context, "FailedSyncs", "Failed syncs", MenuRegion.Primary, 110, "SyncAlert", null);

            AddMenu(context, "AuditLogs", "Audit logs", MenuRegion.Admin, 120, "ClipboardTextClock",
                "core.audit.view", "core.company-settings.view", isAction: true);
            AddMenu(context, "Users", "Users", MenuRegion.Admin, 121, "AccountMultiple", "core.users.view");
            AddMenu(context, "Permissions", "Role permissions", MenuRegion.Admin, 122, "ShieldAccount", "core.permissions.view");
            AddMenu(context, "Info", "Info", MenuRegion.Admin, 160, "Information", null);

            context.Menus.Add(new MenuItemDefinition(
                "Configuration",
                "Configuration",
                Region: MenuRegion.Configuration,
                Order: 90,
                IconKey: "CogOutline",
                RouteId: null,
                ModuleId: "Core",
                RequiresAuthentication: true));

            AddMenu(context, "ScaleSettings", "Scale settings", MenuRegion.Configuration, 10, "Scale", "core.scale-settings.view", parentId: "Configuration");
            AddMenu(context, "CompanySettings", "Company settings", MenuRegion.Configuration, 20, "OfficeBuilding", "core.company-settings.view", parentId: "Configuration");
            AddMenu(context, "AppSettings", "App settings", MenuRegion.Configuration, 30, "Cog", "core.company-settings.view", parentId: "Configuration");
            AddMenu(context, "SyncSettings", "Sync settings", MenuRegion.Configuration, 40, "Sync", "core.company-settings.view", parentId: "Configuration");
            AddMenu(context, "BackupSettings", "Backup settings", MenuRegion.Configuration, 50, "BackupRestore", "core.company-settings.view", parentId: "Configuration");
            AddMenu(context, "BackupLog", "Backup log", MenuRegion.Configuration, 60, "History", "core.company-settings.view", parentId: "Configuration");
            AddMenu(context, "ReportSchedule", "Report schedule", MenuRegion.Configuration, 70, "CalendarClock", "core.company-settings.view", parentId: "Configuration");
            AddMenu(context, "LabelTemplates", "Label templates", MenuRegion.Configuration, 80, "TagText", "core.company-settings.view", parentId: "Configuration");
            AddMenu(context, "PrinterSetup", "Printer setup", MenuRegion.Configuration, 90, "Printer", "core.company-settings.view", parentId: "Configuration");
            AddMenu(context, "PlcSettings", "PLC settings", MenuRegion.Configuration, 100, "Chip", "core.company-settings.view", parentId: "Configuration");
            AddMenu(context, "ScannerSettings", "Scanner settings", MenuRegion.Configuration, 110, "BarcodeScan", "core.company-settings.view", parentId: "Configuration");
            AddMenu(context, "CleanUpDatabase", "Clean up database", MenuRegion.Configuration, 120, "DatabaseRemove", "core.system-settings.manage", parentId: "Configuration");
        }

        private static void AddPermission(
            IModuleRegistrationContext context,
            string key,
            string displayName,
            string group,
            string? legacyKey)
        {
            context.Permissions.Add(new PermissionDefinition(
                key,
                displayName,
                group,
                "Core",
                LegacyKey: legacyKey));
        }

        private static void AddRoute(
            IModuleRegistrationContext context,
            string routeId,
            string displayName,
            string? permission,
            MenuRegion region,
            int order,
            string? alternatePermission = null)
        {
            context.Routes.Add(new RouteMetadata(
                routeId,
                displayName,
                RequiredPermissions: BuildPermissions(permission, alternatePermission),
                ModuleId: "Core"));
        }

        private static void AddMenu(
            IModuleRegistrationContext context,
            string id,
            string title,
            MenuRegion region,
            int order,
            string iconKey,
            string? permission,
            string? alternatePermission = null,
            string? parentId = null,
            bool isAction = false)
        {
            context.Menus.Add(new MenuItemDefinition(
                id,
                title,
                ParentId: parentId,
                Region: region,
                Order: order,
                IconKey: iconKey,
                RouteId: id,
                RequiredPermissions: BuildPermissions(permission, alternatePermission),
                ModuleId: "Core",
                IsAction: isAction,
                RequiresAuthentication: true));
        }

        private static string[]? BuildPermissions(string? permission, string? alternatePermission)
        {
            if (permission is null)
                return null;

            return alternatePermission is null
                ? new[] { permission }
                : new[] { permission, alternatePermission };
        }
    }
}
