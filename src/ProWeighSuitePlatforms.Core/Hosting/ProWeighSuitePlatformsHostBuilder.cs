using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProWeighSuitePlatforms.Core.Auditing;
using ProWeighSuitePlatforms.Core.Branding;
using ProWeighSuitePlatforms.Core.Licensing;
using ProWeighSuitePlatforms.Core.Migrations;
using ProWeighSuitePlatforms.Core.Modules;
using ProWeighSuitePlatforms.Core.Navigation;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Core.Settings;
using ProWeighSuitePlatforms.Core.Updates;

namespace ProWeighSuitePlatforms.Core.Hosting;

public sealed class ProWeighSuitePlatformsHostBuilder
{
    private readonly ServiceCollection _services = new();
    private readonly List<IModule> _modules = new();

    public ProWeighSuitePlatformsHostBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_services);
        return this;
    }

    public ProWeighSuitePlatformsHostBuilder AddModule(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        _modules.Add(module);
        return this;
    }

    public ProWeighSuitePlatformsHost Build()
    {
        var moduleRegistry = new ModuleRegistry(_modules);
        var productModules = new ProductModuleRegistry();
        var menus = new MenuRegistry();
        var routes = new RouteRegistry();
        var permissions = new PermissionRegistry();
        var settings = new SettingsRegistry();
        var migrations = new MigrationRegistry();
        var context = new ModuleRegistrationContext(
            _services,
            productModules,
            permissions,
            menus,
            routes,
            settings,
            migrations);

        foreach (var module in _modules)
            module.Register(context);

        var errors = ModuleRegistrationValidator.Validate(
            _modules,
            productModules,
            menus,
            routes,
            permissions,
            settings,
            migrations);

        if (errors.Count > 0)
            throw new ModuleValidationException(errors);

        _services.TryAddSingleton<ISettingsService, InMemorySettingsService>();
        _services.TryAddSingleton<IAuditService, NullAuditService>();
        _services.TryAddSingleton<ISessionContext, AnonymousSessionContext>();
        _services.TryAddSingleton<IAuthorizationService, DenyAllAuthorizationService>();
        _services.TryAddSingleton<IBrandingProvider, NeutralBrandingProvider>();
        _services.TryAddSingleton<ILicenseService, DenyAllLicenseService>();
        _services.TryAddSingleton<IUpdateService, NoOpUpdateService>();
        _services.TryAddSingleton<IMenuBuilder, MenuBuilder>();
        _services.TryAddSingleton<IRouteGuard, RouteGuard>();
        _services.TryAddSingleton<INavigationChangeNotifier, NavigationChangeNotifier>();

        _services.AddSingleton<IModuleRegistry>(moduleRegistry);
        _services.AddSingleton<IProductModuleRegistry>(productModules);
        _services.AddSingleton<IMenuRegistry>(menus);
        _services.AddSingleton<IRouteRegistry>(routes);
        _services.AddSingleton<IPermissionRegistry>(permissions);
        _services.AddSingleton<ISettingsRegistry>(settings);
        _services.AddSingleton<IMigrationRegistry>(migrations);

        var provider = _services.BuildServiceProvider();
        return new ProWeighSuitePlatformsHost(
            provider,
            moduleRegistry,
            productModules,
            menus,
            routes,
            permissions,
            settings,
            migrations);
    }
}
