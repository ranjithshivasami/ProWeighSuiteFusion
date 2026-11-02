using Microsoft.Extensions.DependencyInjection;
using ProWeighSuitePlatforms.Core.Migrations;
using ProWeighSuitePlatforms.Core.Navigation;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Core.Settings;

namespace ProWeighSuitePlatforms.Core.Modules;

internal sealed class ModuleRegistrationContext : IModuleRegistrationContext
{
    public ModuleRegistrationContext(
        IServiceCollection services,
        IProductModuleRegistryBuilder productModules,
        IPermissionRegistryBuilder permissions,
        IMenuRegistryBuilder menus,
        IRouteRegistryBuilder routes,
        ISettingsRegistryBuilder settings,
        IMigrationRegistryBuilder migrations)
    {
        Services = services;
        ProductModules = productModules;
        Permissions = permissions;
        Menus = menus;
        Routes = routes;
        Settings = settings;
        Migrations = migrations;
    }

    public IServiceCollection Services { get; }

    public IProductModuleRegistryBuilder ProductModules { get; }

    public IPermissionRegistryBuilder Permissions { get; }

    public IMenuRegistryBuilder Menus { get; }

    public IRouteRegistryBuilder Routes { get; }

    public ISettingsRegistryBuilder Settings { get; }

    public IMigrationRegistryBuilder Migrations { get; }
}
