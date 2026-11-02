using Microsoft.Extensions.DependencyInjection;
using ProWeighSuitePlatforms.Core.Migrations;
using ProWeighSuitePlatforms.Core.Navigation;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Core.Settings;

namespace ProWeighSuitePlatforms.Core.Modules;

public interface IModuleRegistrationContext
{
    IServiceCollection Services { get; }

    IProductModuleRegistryBuilder ProductModules { get; }

    IPermissionRegistryBuilder Permissions { get; }

    IMenuRegistryBuilder Menus { get; }

    IRouteRegistryBuilder Routes { get; }

    ISettingsRegistryBuilder Settings { get; }

    IMigrationRegistryBuilder Migrations { get; }
}
