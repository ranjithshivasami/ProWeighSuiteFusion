using System;
using Microsoft.Extensions.DependencyInjection;
using ProWeighSuitePlatforms.Core.Migrations;
using ProWeighSuitePlatforms.Core.Modules;
using ProWeighSuitePlatforms.Core.Navigation;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Core.Settings;

namespace ProWeighSuitePlatforms.Core.Hosting;

public sealed class ProWeighSuitePlatformsHost : IDisposable
{
    private readonly ServiceProvider _provider;

    internal ProWeighSuitePlatformsHost(
        ServiceProvider provider,
        IModuleRegistry modules,
        IProductModuleRegistry productModules,
        IMenuRegistry menus,
        IRouteRegistry routes,
        IPermissionRegistry permissions,
        ISettingsRegistry settings,
        IMigrationRegistry migrations)
    {
        _provider = provider;
        Services = provider;
        Modules = modules;
        ProductModules = productModules;
        Menus = menus;
        Routes = routes;
        Permissions = permissions;
        Settings = settings;
        Migrations = migrations;
        MenuBuilder = provider.GetRequiredService<IMenuBuilder>();
        RouteGuard = provider.GetRequiredService<IRouteGuard>();
        NavigationChanges = provider.GetRequiredService<INavigationChangeNotifier>();
    }

    public IServiceProvider Services { get; }

    public IModuleRegistry Modules { get; }

    public IProductModuleRegistry ProductModules { get; }

    public IMenuRegistry Menus { get; }

    public IRouteRegistry Routes { get; }

    public IPermissionRegistry Permissions { get; }

    public ISettingsRegistry Settings { get; }

    public IMigrationRegistry Migrations { get; }

    public IMenuBuilder MenuBuilder { get; }

    public IRouteGuard RouteGuard { get; }

    public INavigationChangeNotifier NavigationChanges { get; }

    public void Dispose() => _provider.Dispose();
}
