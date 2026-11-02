using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ProWeighSuitePlatforms.Core.Hosting;
using ProWeighSuitePlatforms.Core.Licensing;
using ProWeighSuitePlatforms.Core.Modules;
using ProWeighSuitePlatforms.Core.Navigation;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Core.Wpf.Navigation;
using ProWeighSuitePlatforms.Core.Wpf.Views;
using Xunit;

namespace ProWeighSuitePlatforms.Tests.Core;

public class NavigationTests
{
    [Fact]
    public async Task MenuBuilder_ShowsAllowedItemsAndHidesDeniedItems()
    {
        using var host = BuildHost(new[] { "test.view" }, authenticated: true);

        var model = await host.MenuBuilder.BuildAsync(new NavigationBuildContext(true, Array.Empty<string>()));
        var items = model.Primary.Concat(model.ModuleTop).ToList();

        Assert.Single(items);
        Assert.Equal("allowed", items[0].Id);
    }

    [Fact]
    public async Task MenuBuilder_HidesGroupWhenNoChildIsVisible()
    {
        using var host = BuildHost(Array.Empty<string>(), authenticated: true);

        var model = await host.MenuBuilder.BuildAsync(new NavigationBuildContext(true, Array.Empty<string>()));

        Assert.Empty(model.Primary);
    }

    [Fact]
    public async Task MenuBuilder_HidesEverythingWhenSignedOut()
    {
        using var host = BuildHost(new[] { "test.view" }, authenticated: false);

        var model = await host.MenuBuilder.BuildAsync(new NavigationBuildContext(false, Array.Empty<string>()));

        Assert.Empty(model.Primary);
        Assert.Empty(model.ModuleTop);
    }

    [Fact]
    public async Task MenuBuilder_RebuildsFromCurrentPermissionsWithoutRestart()
    {
        var authorization = new MutableAuthorization("test.view");
        using var host = new ProWeighSuitePlatformsHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ISessionContext>(new FakeSession(true));
                services.AddSingleton<IAuthorizationService>(authorization);
                services.AddSingleton<ILicenseService>(new FakeLicense("test"));
            })
            .AddModule(new NavigationModule())
            .Build();

        var before = await host.MenuBuilder.BuildAsync(new NavigationBuildContext(true, Array.Empty<string>()));
        Assert.Single(before.Primary);

        authorization.Revoke("test.view");
        var after = await host.MenuBuilder.BuildAsync(new NavigationBuildContext(true, Array.Empty<string>()));

        Assert.DoesNotContain(after.Primary, node => node.Id == "allowed");
    }

    [Fact]
    public async Task RouteGuard_DeniesUnknownAndUnauthorizedRoutes()
    {
        using var host = BuildHost(new[] { "test.view" }, authenticated: true);

        Assert.False((await host.RouteGuard.CanEnterAsync("missing")).IsAllowed);
        Assert.False((await host.RouteGuard.CanEnterAsync("test.denied")).IsAllowed);
        Assert.True((await host.RouteGuard.CanEnterAsync("test.allowed")).IsAllowed);
    }

    [Fact]
    public async Task RouteGuard_DeniesWhenLicenseIsMissing()
    {
        using var host = new ProWeighSuitePlatformsHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ISessionContext>(new FakeSession(true));
                services.AddSingleton<IAuthorizationService>(new FakeAuthorization(new[] { "test.view" }));
                services.AddSingleton<ILicenseService>(new FakeLicense("other"));
            })
            .AddModule(new NavigationModule())
            .Build();

        var result = await host.RouteGuard.CanEnterAsync("test.allowed");

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void NavigationChangeNotifier_RaisesRegisteredReason()
    {
        var notifier = new NavigationChangeNotifier();
        NavigationChangeReason? observed = null;
        notifier.Changed += (_, e) => observed = e.Reason;

        notifier.Notify(NavigationChangeReason.PermissionsChanged);

        Assert.Equal(NavigationChangeReason.PermissionsChanged, observed);
    }

    [Fact]
    public void WeakEventManager_SubscribesToNavigationNotifierInterface()
    {
        var notifier = new NavigationChangeNotifier();
        var observed = 0;

        void Handler(object? sender, NavigationChangedEventArgs args) => observed++;

        System.Windows.WeakEventManager<
            INavigationChangeNotifier,
            NavigationChangedEventArgs>.AddHandler(
                notifier,
                nameof(INavigationChangeNotifier.Changed),
                Handler);

        notifier.Notify(NavigationChangeReason.PermissionsChanged);

        Assert.Equal(1, observed);
    }

    [Fact]
    public void FusionModule_RegistersMenuRoutesPermissionsSettingsAndViews()
    {
        using var host = new ProWeighSuitePlatformsHostBuilder()
            .AddModule(new ProWeighSuitePlatforms.Modules.Fusion.FusionModule())
            .Build();

        Assert.Equal(13, host.Menus.All.Count);
        Assert.Equal(12, host.Routes.All.Count);
        Assert.Equal(12, host.Permissions.All.Count);
        Assert.Equal(5, host.Settings.All.Count);
        Assert.Single(host.Migrations.All);
        Assert.All(host.Menus.All, item => Assert.Equal("Fusion", item.ModuleId));
        Assert.All(host.Permissions.All, item => Assert.False(string.IsNullOrWhiteSpace(item.LegacyKey)));
        Assert.True(host.Permissions.Contains("fusion.dashboard.view"));
        Assert.Equal("ViewDashboard", host.Permissions.Find("fusion.dashboard.view")!.LegacyKey);

        // Sidebar "Master" group (next to Configuration) with the core
        // master-data screens.
        var master = host.Menus.All.Single(item => item.Id == "Master");
        Assert.Equal("Master", master.Title);
        Assert.Null(master.RouteId);
        Assert.Equal(MenuRegion.Primary, master.Region);
        Assert.Equal(80, master.Order);
        Assert.Equal("Master", host.Menus.All.Single(item => item.Id == "Category").ParentId);
        Assert.Equal("Master", host.Menus.All.Single(item => item.Id == "Variant").ParentId);
        Assert.Equal("Master", host.Menus.All.Single(item => item.Id == "Materials").ParentId);
        Assert.NotNull(host.Routes.Find("Category"));
        Assert.NotNull(host.Routes.Find("Variant"));
        Assert.NotNull(host.Routes.Find("Materials"));

        var views = new ViewFactoryRegistry();
        ProWeighSuitePlatforms.Modules.Fusion.FusionModule.RegisterViews(views);
        Assert.Equal(13, views.All.Count);
        Assert.NotNull(views.Find("FusionDashboard"));
        Assert.NotNull(views.Find("LiveView"));
        Assert.NotNull(views.Find("Category"));
        Assert.NotNull(views.Find("Variant"));
        Assert.NotNull(views.Find("Materials"));
    }

    [Fact]
    public void CoreNavigationModule_RegistersAdminAndConfigurationContributions()
    {
        using var host = new ProWeighSuitePlatformsHostBuilder()
            .AddModule(new ProWeighSuitePlatforms.Modules.Core.CoreNavigationModule())
            .Build();

        Assert.Equal(20, host.Menus.All.Count);
        Assert.Equal(19, host.Routes.All.Count);
        Assert.Equal(7, host.Permissions.All.Count);
        Assert.Equal(4, host.Menus.All.Count(item => item.Region == MenuRegion.Admin));
        Assert.Equal(13, host.Menus.All.Count(item => item.Region == MenuRegion.Configuration));
        Assert.Null(host.Menus.All.Single(item => item.Id == "Configuration").RouteId);
        Assert.Equal("Configuration",
            host.Menus.All.Single(item => item.Id == "ScaleSettings").ParentId);
        Assert.True(host.Permissions.Contains("core.users.view"));
        Assert.Equal("ViewUsers", host.Permissions.Find("core.users.view")!.LegacyKey);
        Assert.Null(host.Routes.Find("Info")!.RequiredPermissions);
        var audit = host.Routes.Find("AuditLogs")!;
        Assert.Equal(2, audit.RequiredPermissions!.Count);
        Assert.Equal(PermissionMatch.Any, audit.PermissionMatch);
    }

    [Fact]
    public void CoreAndFusionModules_RegisterWithoutConflicts()
    {
        using var host = new ProWeighSuitePlatformsHostBuilder()
            .AddModule(new ProWeighSuitePlatforms.Modules.Core.CoreNavigationModule())
            .AddModule(new ProWeighSuitePlatforms.Modules.Fusion.FusionModule())
            .Build();

        Assert.Equal(33, host.Menus.All.Count);
        Assert.Equal(31, host.Routes.All.Count);
        Assert.Equal(19, host.Permissions.All.Count);
        Assert.Equal(2, host.Modules.Modules.Count);
        Assert.Equal(9, host.Menus.All.Count(item => item.Region == MenuRegion.ModuleTop));
        Assert.All(
            host.Menus.All.Where(item => item.Region == MenuRegion.ModuleTop),
            item => Assert.Equal("Fusion", item.ModuleId));
    }

    [Fact]
    public void ViewFactoryRegistry_CachesKeepAliveRoutesOnly()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var registry = new ViewFactoryRegistry();
        var keepAliveCreated = 0;
        var transientCreated = 0;

        registry.Register(new ViewFactoryRegistration(
            "keep",
            _ => new object(),
            _ => { keepAliveCreated++; return new object(); },
            RouteCacheMode.KeepAlive));
        registry.Register(new ViewFactoryRegistration(
            "transient",
            _ => new object(),
            _ => { transientCreated++; return new object(); }));

        Assert.Same(registry.CreateView("keep", services), registry.CreateView("keep", services));
        Assert.NotSame(registry.CreateView("transient", services), registry.CreateView("transient", services));
        Assert.Equal(1, keepAliveCreated);
        Assert.Equal(2, transientCreated);
        Assert.Throws<InvalidOperationException>(() => registry.CreateView("missing", services));
    }

    [Fact]
    public async Task NavigationService_RevalidatesAndClearsRevokedRoute()
    {
        var authorization = new MutableAuthorization("test.view");
        using var host = new ProWeighSuitePlatformsHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ISessionContext>(new FakeSession(true));
                services.AddSingleton<IAuthorizationService>(authorization);
                services.AddSingleton<ILicenseService>(new FakeLicense("test"));
            })
            .AddModule(new NavigationModule())
            .Build();

        var views = new ViewFactoryRegistry();
        views.Register(new ViewFactoryRegistration(
            "test.allowed",
            _ => new object(),
            _ => new object()));

        var notifier = new NavigationChangeNotifier();
        using var navigation = new NavigationService(
            host.RouteGuard,
            views,
            host.Services,
            notifier);

        var invalidated = false;
        navigation.CurrentRouteInvalidated += (_, _) => invalidated = true;

        Assert.True(await navigation.NavigateAsync("test.allowed"));
        Assert.Equal("test.allowed", navigation.CurrentRouteId);
        Assert.NotNull(navigation.CurrentView);

        authorization.Revoke("test.view");
        await navigation.RevalidateCurrentRouteAsync(NavigationChangeReason.PermissionsChanged);

        Assert.True(invalidated);
        Assert.Null(navigation.CurrentRouteId);
        Assert.Null(navigation.CurrentView);
    }

    private static ProWeighSuitePlatformsHost BuildHost(IReadOnlyCollection<string> allowed, bool authenticated) =>
        new ProWeighSuitePlatformsHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<ISessionContext>(new FakeSession(authenticated));
                services.AddSingleton<IAuthorizationService>(new FakeAuthorization(allowed));
                services.AddSingleton<ILicenseService>(new FakeLicense("test"));
            })
            .AddModule(new NavigationModule())
            .Build();

    private sealed class NavigationModule : IModule
    {
        public ModuleMetadata Metadata { get; } = new(
            "test",
            "Test",
            new Version(1, 0),
            new Version(1, 0),
            RequiredLicense: "test");

        public void Register(IModuleRegistrationContext context)
        {
            context.Permissions.Add(new PermissionDefinition("test.view", "View test", "Test", "test"));

            context.Routes.Add(new RouteMetadata(
                "test.allowed",
                "Allowed",
                RequiredPermissions: new[] { "test.view" },
                RequiredLicense: "test",
                ModuleId: "test"));
            context.Routes.Add(new RouteMetadata(
                "test.denied",
                "Denied",
                RequiredPermissions: new[] { "test.other" },
                RequiredLicense: "test",
                ModuleId: "test"));
            context.Permissions.Add(new PermissionDefinition("test.other", "Other", "Test", "test"));

            context.Menus.Add(new MenuItemDefinition(
                "allowed",
                "Allowed",
                RouteId: "test.allowed",
                RequiredPermissions: new[] { "test.view" },
                RequiredLicense: "test",
                ModuleId: "test"));
            context.Menus.Add(new MenuItemDefinition(
                "denied",
                "Denied",
                RouteId: "test.denied",
                RequiredPermissions: new[] { "test.other" },
                RequiredLicense: "test",
                ModuleId: "test"));

            context.Menus.Add(new MenuItemDefinition("group", "Group", ModuleId: "test"));
            context.Menus.Add(new MenuItemDefinition(
                "group.child",
                "Child",
                ParentId: "group",
                RouteId: "test.denied",
                RequiredPermissions: new[] { "test.other" },
                RequiredLicense: "test",
                ModuleId: "test"));
        }
    }

    private sealed class FakeSession : ISessionContext
    {
        public FakeSession(bool isAuthenticated) => IsAuthenticated = isAuthenticated;

        public bool IsAuthenticated { get; }

        public string? UserName => IsAuthenticated ? "tester" : null;

        public IReadOnlyCollection<string> Roles => Array.Empty<string>();
    }

    private sealed class FakeAuthorization : IAuthorizationService
    {
        private readonly HashSet<string> _allowed;

        public FakeAuthorization(IEnumerable<string> allowed) => _allowed = new(allowed);

        public Task<bool> IsAllowedAsync(string permissionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_allowed.Contains(permissionKey));
    }

    private sealed class MutableAuthorization : IAuthorizationService
    {
        private readonly HashSet<string> _allowed;

        public MutableAuthorization(params string[] allowed) => _allowed = new(allowed);

        public void Revoke(string permission) => _allowed.Remove(permission);

        public Task<bool> IsAllowedAsync(string permissionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(_allowed.Contains(permissionKey));
    }

    private sealed class FakeLicense : ILicenseService
    {
        private readonly string _allowed;

        public FakeLicense(string allowed) => _allowed = allowed;

        public Task<bool> IsAllowedAsync(string moduleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Equals(moduleId, _allowed, StringComparison.OrdinalIgnoreCase));

        public Task<bool> IsExpiredAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

}
