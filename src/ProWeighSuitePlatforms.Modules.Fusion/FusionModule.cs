using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ProWeighSuitePlatforms.Core.Data;
using ProWeighSuitePlatforms.Core.Migrations;
using ProWeighSuitePlatforms.Core.Modules;
using ProWeighSuitePlatforms.Core.Navigation;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Core.Settings;
using ProWeighSuitePlatforms.Core.Wpf.Views;
using ProWeighSuitePlatforms.Core.Wpf.Navigation;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data.Fusion;

namespace ProWeighSuitePlatforms.Modules.Fusion
{
    public sealed class FusionModule : IModule
    {
        public const string Id = "Fusion";

        public ModuleMetadata Metadata { get; } = new(
            ModuleId: Id,
            DisplayName: "Fusion",
            Version: new Version(1, 0, 0),
            MinCoreVersion: new Version(1, 0, 0),
            RequiredLicense: Id,
            DefaultRouteId: FusionRoutes.Dashboard,
            Order: 20);

        public void Register(IModuleRegistrationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            RegisterPermissions(context);
            RegisterRoutes(context);
            RegisterMenus(context);
            RegisterSettings(context);
            RegisterMigrations(context);
            RegisterServices(context);
        }

        public static void RegisterViews(IViewFactoryRegistryBuilder registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            registry.Register(new ViewFactoryRegistration(
                FusionRoutes.Dashboard,
                _ => new FusionDashboardViewModel(),
                viewModel => new FusionDashboardView { DataContext = viewModel },
                RouteCacheMode.KeepAlive));

            registry.Register(new ViewFactoryRegistration(
                FusionRoutes.Settings,
                _ => new FusionSettingsViewModel(),
                viewModel => new FusionSettingsView { DataContext = viewModel }));

            registry.Register(new ViewFactoryRegistration(
                FusionRoutes.BayStatus,
                _ => new BayStatusViewModel(),
                viewModel => new BayStatusView { DataContext = viewModel }));

            registry.Register(new ViewFactoryRegistration(
                FusionRoutes.SupplierMaster,
                _ => new SupplierMasterViewModel(),
                viewModel => new SupplierMasterView { DataContext = viewModel }));

            registry.Register(new ViewFactoryRegistration(
                FusionRoutes.TubMaster,
                _ => new TubMasterViewModel(),
                viewModel => new TubMasterView { DataContext = viewModel }));

            registry.Register(new ViewFactoryRegistration(
                FusionRoutes.InwardStock,
                _ => new InwardStockViewModel(),
                viewModel => new InwardStockView { DataContext = viewModel }));

            registry.Register(new ViewFactoryRegistration(
                FusionRoutes.BatchLots,
                _ => new FusionBatchViewModel(),
                viewModel => new FusionBatchView { DataContext = viewModel }));

            registry.Register(new ViewFactoryRegistration(
                FusionRoutes.LiveView,
                _ => new FusionLiveViewModel(),
                viewModel => new FusionLiveView { DataContext = viewModel },
                RouteCacheMode.KeepAlive));

            registry.Register(new ViewFactoryRegistration(
                FusionRoutes.Reports,
                _ => new FusionReportsViewModel(),
                viewModel => new FusionReportsView { DataContext = viewModel }));

            // Core master-data screens (Category/Variant/Materials) reachable
            // from the sidebar "Master" menu next to Configuration. Supplier is
            // registered too so the IDs stay identical to the Standard host.
            CoreMasterDataViewRegistration.RegisterViews(registry);
        }

        private static void RegisterPermissions(IModuleRegistrationContext context)
        {
            AddPermission(context, FusionPermissions.Dashboard,
                "View Fusion dashboard", "Fusion", "ViewDashboard");
            AddPermission(context, FusionPermissions.Settings,
                "View Fusion settings", "Fusion", "ViewCompanySettings");
            AddPermission(context, FusionPermissions.BayStatus,
                "View Fusion bay status", "Fusion", "ViewDashboard");
            AddPermission(context, FusionPermissions.SupplierMaster,
                "View Fusion supplier master", "Fusion", "ViewSupplier");
            AddPermission(context, FusionPermissions.TubMaster,
                "View Fusion tub master", "Fusion", "ViewMaterials");
            AddPermission(context, FusionPermissions.InwardStock,
                "View Fusion inward and supplier stock", "Fusion", "ViewMaterials");
            AddPermission(context, FusionPermissions.BatchLots,
                "View and create Fusion batches and lots", "Fusion", "ViewMaterials");
            AddPermission(context, FusionPermissions.LiveView,
                "View the Fusion live weigh screen", "Fusion", "ViewDashboard");
            AddPermission(context, FusionPermissions.Reports,
                "View Fusion reports", "Fusion", "ViewReports");
            AddPermission(context, FusionPermissions.Category,
                "View categories", "Master", "ViewCategory");
            AddPermission(context, FusionPermissions.Variant,
                "View variants", "Master", "ViewVariant");
            AddPermission(context, FusionPermissions.Materials,
                "View materials", "Master", "ViewMaterials");
        }

        private static void RegisterRoutes(IModuleRegistrationContext context)
        {
            AddRoute(context, FusionRoutes.Dashboard, "Fusion Dashboard",
                FusionPermissions.Dashboard, RouteCacheMode.KeepAlive);
            AddRoute(context, FusionRoutes.Settings, "Fusion settings",
                FusionPermissions.Settings);
            AddRoute(context, FusionRoutes.BayStatus, "Bay status",
                FusionPermissions.BayStatus);
            AddRoute(context, FusionRoutes.SupplierMaster, "Supplier master",
                FusionPermissions.SupplierMaster);
            AddRoute(context, FusionRoutes.TubMaster, "Tub master",
                FusionPermissions.TubMaster);
            AddRoute(context, FusionRoutes.InwardStock, "Inward and supplier stock",
                FusionPermissions.InwardStock);
            AddRoute(context, FusionRoutes.BatchLots, "Batch creation and lots",
                FusionPermissions.BatchLots);
            AddRoute(context, FusionRoutes.LiveView, "Live view",
                FusionPermissions.LiveView, RouteCacheMode.KeepAlive);
            AddRoute(context, FusionRoutes.Reports, "Reports",
                FusionPermissions.Reports);

            // Core master-data routes shared with the Standard host.
            AddRoute(context, FusionRoutes.Category, "Categories",
                FusionPermissions.Category);
            AddRoute(context, FusionRoutes.Variant, "Variants",
                FusionPermissions.Variant);
            AddRoute(context, FusionRoutes.Materials, "Materials",
                FusionPermissions.Materials);
        }

        private static void RegisterMenus(IModuleRegistrationContext context)
        {
            AddMenu(context, FusionRoutes.Dashboard, "Dashboard", 10,
                "ViewDashboard", FusionPermissions.Dashboard);
            AddMenu(context, FusionRoutes.Settings, "Fusion settings", 20,
                "Cog", FusionPermissions.Settings);
            AddMenu(context, FusionRoutes.BayStatus, "Bay status", 30,
                "MapMarkerRadius", FusionPermissions.BayStatus);
            AddMenu(context, FusionRoutes.SupplierMaster, "Supplier master", 40,
                "TruckDelivery", FusionPermissions.SupplierMaster);
            AddMenu(context, FusionRoutes.TubMaster, "Tub master", 50,
                "PackageVariantClosed", FusionPermissions.TubMaster);
            AddMenu(context, FusionRoutes.InwardStock, "Inward and supplier stock", 60,
                "PackageVariantClosedPlus", FusionPermissions.InwardStock);
            AddMenu(context, FusionRoutes.BatchLots, "Batch creation and lots", 70,
                "LayersTriple", FusionPermissions.BatchLots);
            AddMenu(context, FusionRoutes.LiveView, "Live view", 80,
                "MonitorDashboard", FusionPermissions.LiveView);
            AddMenu(context, FusionRoutes.Reports, "Reports", 90,
                "FileChart", FusionPermissions.Reports);

            RegisterMasterMenus(context);
        }

        /// <summary>
        /// Sidebar "Master" group placed next to Configuration (order 80 vs 90)
        /// with the core master-data screens. Same IDs as the Standard host so
        /// persisted navigation state stays valid across flavors.
        /// </summary>
        private static void RegisterMasterMenus(IModuleRegistrationContext context)
        {
            context.Menus.Add(new MenuItemDefinition(
                "Master",
                "Master",
                ParentId: null,
                Region: MenuRegion.Primary,
                Order: 80,
                IconKey: "Database",
                RouteId: null,
                RequiredLicense: Id,
                ModuleId: Id,
                RequiresAuthentication: true));

            AddMasterChildMenu(context, FusionRoutes.Category, "Category", 10,
                "Tag", FusionPermissions.Category);
            AddMasterChildMenu(context, FusionRoutes.Variant, "Variant", 20,
                "Shape", FusionPermissions.Variant);
            AddMasterChildMenu(context, FusionRoutes.Materials, "Materials", 30,
                "CubeOutline", FusionPermissions.Materials);
        }

        private static void AddMasterChildMenu(
            IModuleRegistrationContext context,
            string id,
            string title,
            int order,
            string iconKey,
            string permission)
        {
            context.Menus.Add(new MenuItemDefinition(
                id,
                title,
                ParentId: "Master",
                Region: MenuRegion.Primary,
                Order: order,
                IconKey: iconKey,
                RouteId: id,
                RequiredPermissions: new[] { permission },
                PermissionMatch: PermissionMatch.Any,
                RequiredLicense: Id,
                IsVisibleByDefault: true,
                ModuleId: Id,
                RequiresAuthentication: true));
        }

        private static void RegisterSettings(IModuleRegistrationContext context)
        {
            context.Settings.Add(new SettingDefinition(
                "fusion.near_expiry_days",
                "Near-expiry days",
                FusionSettingsDefaults.NearExpiryDays.ToString(),
                "Fusion",
                Id));

            context.Settings.Add(new SettingDefinition(
                "fusion.expired_below_days",
                "Expired-below days",
                FusionSettingsDefaults.ExpiredBelowDays.ToString(),
                "Fusion",
                Id));

            context.Settings.Add(new SettingDefinition(
                "fusion.tub_replace_at_cycles",
                "Tub replace-at cycles",
                FusionSettingsDefaults.TubReplaceAtCycles.ToString(),
                "Fusion",
                Id));

            context.Settings.Add(new SettingDefinition(
                "fusion.tub_warn_percent",
                "Tub warning percent",
                FusionSettingsDefaults.TubWarnPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Fusion",
                Id));

            context.Settings.Add(new SettingDefinition(
                "fusion.zero_return",
                "Require zero return after each weigh",
                FusionSettingsDefaults.ZeroReturn ? "1" : "0",
                "Fusion",
                Id));
        }

        private static void RegisterMigrations(IModuleRegistrationContext context)
        {
            context.Migrations.Add(new FusionDatabaseMigration());
        }

        private static void RegisterServices(IModuleRegistrationContext context)
        {
            context.Services.AddTransient<FusionDashboardViewModel>();
            context.Services.AddTransient<FusionSettingsViewModel>();
            context.Services.AddTransient<BayStatusViewModel>();
            context.Services.AddTransient<SupplierMasterViewModel>();
            context.Services.AddTransient<TubMasterViewModel>();
            context.Services.AddTransient<InwardStockViewModel>();
            context.Services.AddTransient<FusionBatchViewModel>();
            context.Services.AddTransient<FusionLiveViewModel>();
            context.Services.AddTransient<FusionReportsViewModel>();
            context.Services.AddSingleton<ProWeighSuitePlatforms.Core.Data.IProductDatabaseInitializer, FusionProductDatabaseInitializer>();
        }

        private static void AddPermission(
            IModuleRegistrationContext context,
            string key,
            string displayName,
            string group,
            string legacyKey)
        {
            context.Permissions.Add(new PermissionDefinition(
                key,
                displayName,
                group,
                Id,
                LegacyKey: legacyKey));
        }

        private static void AddRoute(
            IModuleRegistrationContext context,
            string routeId,
            string displayName,
            string permission,
            RouteCacheMode cacheMode = RouteCacheMode.Transient)
        {
            context.Routes.Add(new RouteMetadata(
                routeId,
                displayName,
                RequiredPermissions: new[] { permission },
                PermissionMatch: PermissionMatch.Any,
                RequiredLicense: Id,
                ModuleId: Id,
                CacheMode: cacheMode));
        }

        private static void AddMenu(
            IModuleRegistrationContext context,
            string id,
            string title,
            int order,
            string iconKey,
            string permission)
        {
            context.Menus.Add(new MenuItemDefinition(
                id,
                title,
                ParentId: null,
                Region: MenuRegion.ModuleTop,
                Order: order,
                IconKey: iconKey,
                RouteId: id,
                RequiredPermissions: new[] { permission },
                PermissionMatch: PermissionMatch.Any,
                RequiredLicense: Id,
                IsVisibleByDefault: true,
                ModuleId: Id,
                RequiresAuthentication: true));
        }

        private sealed class FusionDatabaseMigration : IModuleMigration
        {
            public string Id => "fusion.database";

            public string ModuleId => FusionModule.Id;

            public int Order => 20;

            public async Task ApplyAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var database = new FusionDatabaseInit(AppConfigurationManager.GetDatabasePath());
                await database.EnsureCreatedAsync().ConfigureAwait(false);
            }
        }
    }

    internal sealed class FusionProductDatabaseInitializer : ProWeighSuitePlatforms.Core.Data.IProductDatabaseInitializer
    {
        public string Name => "Fusion";

        public async Task InitializeAsync(string dbPath)
        {
            var initializer = new FusionDatabaseInit(dbPath);
            await initializer.EnsureCreatedAsync();
        }
    }

    public static class FusionRoutes
    {
        public const string Dashboard = "FusionDashboard";
        public const string Settings = "FusionSettings";
        public const string BayStatus = "BayStatus";
        public const string SupplierMaster = "SupplierMaster";
        public const string TubMaster = "TubMaster";
        public const string InwardStock = "InwardStock";
        public const string BatchLots = "BatchLots";
        public const string LiveView = "LiveView";
        public const string Reports = "FusionReports";
        public const string Category = "Category";
        public const string Variant = "Variant";
        public const string Materials = "Materials";
    }

    public static class FusionPermissions
    {
        public const string Dashboard = "fusion.dashboard.view";
        public const string Settings = "fusion.settings.view";
        public const string BayStatus = "fusion.bay-status.view";
        public const string SupplierMaster = "fusion.supplier.view";
        public const string TubMaster = "fusion.tub.view";
        public const string InwardStock = "fusion.stock.view";
        public const string BatchLots = "fusion.batch.view";
        public const string LiveView = "fusion.live.view";
        public const string Reports = "fusion.reports.view";
        public const string Category = "fusion.category.view";
        public const string Variant = "fusion.variant.view";
        public const string Materials = "fusion.materials.view";
    }
}
