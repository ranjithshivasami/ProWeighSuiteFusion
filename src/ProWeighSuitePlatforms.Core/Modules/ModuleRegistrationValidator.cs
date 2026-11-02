using System;
using System.Collections.Generic;
using System.Linq;
using ProWeighSuitePlatforms.Core.Migrations;
using ProWeighSuitePlatforms.Core.Navigation;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Core.Settings;

namespace ProWeighSuitePlatforms.Core.Modules;

internal static class ModuleRegistrationValidator
{
    public static IReadOnlyList<string> Validate(
        IReadOnlyList<IModule> modules,
        IProductModuleRegistry productModules,
        IMenuRegistry menus,
        IRouteRegistry routes,
        IPermissionRegistry permissions,
        ISettingsRegistry settings,
        IMigrationRegistry migrations)
    {
        var errors = new List<string>();
        var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules)
        {
            var metadata = module.Metadata;
            if (string.IsNullOrWhiteSpace(metadata.ModuleId))
            {
                errors.Add("A module has an empty ModuleId.");
                continue;
            }

            if (!moduleIds.Add(metadata.ModuleId))
                errors.Add($"Duplicate module ID '{metadata.ModuleId}'.");

            if (!dependencies.ContainsKey(metadata.ModuleId))
                dependencies[metadata.ModuleId] = metadata.DependsOn ?? Array.Empty<string>();

            if (metadata.MinCoreVersion is not null &&
                metadata.MinCoreVersion.CompareTo(ProWeighSuitePlatformsCoreVersion.Current) > 0)
            {
                errors.Add(
                    $"Module '{metadata.ModuleId}' requires Core {metadata.MinCoreVersion} but Core is {ProWeighSuitePlatformsCoreVersion.Current}.");
            }

            if (metadata.MaxCoreVersion is not null &&
                metadata.MaxCoreVersion.CompareTo(ProWeighSuitePlatformsCoreVersion.Current) < 0)
            {
                errors.Add(
                    $"Module '{metadata.ModuleId}' requires Core no newer than {metadata.MaxCoreVersion} but Core is {ProWeighSuitePlatformsCoreVersion.Current}.");
            }
        }

        AddDuplicateErrors(productModules.All.Select(item => item.ModuleId), "product module ID", errors);
        AddDuplicateErrors(menus.All.Select(item => item.Id), "menu ID", errors);
        AddDuplicateErrors(routes.All.Select(item => item.RouteId), "route ID", errors);
        AddDuplicateErrors(permissions.All.Select(item => item.Key), "permission key", errors);
        AddDuplicateErrors(settings.All.Select(item => item.Key), "setting key", errors);
        AddDuplicateErrors(migrations.All.Select(item => item.Id), "migration ID", errors);

        var menuIds = new HashSet<string>(menus.All.Select(item => item.Id), StringComparer.OrdinalIgnoreCase);
        var routeIds = new HashSet<string>(routes.All.Select(item => item.RouteId), StringComparer.OrdinalIgnoreCase);
        var permissionKeys = new HashSet<string>(permissions.All.Select(item => item.Key), StringComparer.OrdinalIgnoreCase);

        foreach (var menu in menus.All)
        {
            if (string.IsNullOrWhiteSpace(menu.Id))
                errors.Add("A menu item has an empty ID.");

            if (menu.ParentId is not null && !menuIds.Contains(menu.ParentId))
                errors.Add($"Menu '{menu.Id}' references missing parent '{menu.ParentId}'.");

            if (menu.RouteId is not null && !routeIds.Contains(menu.RouteId))
                errors.Add($"Menu '{menu.Id}' references missing route '{menu.RouteId}'.");

            AddMissingPermissionErrors(menu.Id, "menu", menu.RequiredPermissions, permissionKeys, errors);
        }

        foreach (var route in routes.All)
        {
            if (string.IsNullOrWhiteSpace(route.RouteId))
                errors.Add("A route has an empty RouteId.");

            AddMissingPermissionErrors(route.RouteId, "route", route.RequiredPermissions, permissionKeys, errors);
        }

        foreach (var module in modules)
        {
            var defaultRouteId = module.Metadata.DefaultRouteId;
            if (defaultRouteId is not null && !routeIds.Contains(defaultRouteId))
                errors.Add($"Module '{module.Metadata.ModuleId}' references missing default route '{defaultRouteId}'.");
        }

        foreach (var pair in dependencies)
        {
            foreach (var dependency in pair.Value)
            {
                if (string.Equals(pair.Key, dependency, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Module '{pair.Key}' cannot depend on itself.");
                else if (!moduleIds.Contains(dependency))
                    errors.Add($"Module '{pair.Key}' depends on missing module '{dependency}'.");
            }
        }

        AddDependencyCycleErrors(dependencies, errors);

        var productModuleIds = new HashSet<string>(
            productModules.All.Select(item => item.ModuleId),
            StringComparer.OrdinalIgnoreCase);

        foreach (var definition in productModules.All)
        {
            if (string.IsNullOrWhiteSpace(definition.ModuleId))
                errors.Add("A product module has an empty ModuleId.");

            if (definition.ParentModuleId is not null &&
                !productModuleIds.Contains(definition.ParentModuleId))
            {
                errors.Add(
                    $"Product module '{definition.ModuleId}' references missing parent '{definition.ParentModuleId}'.");
            }
        }

        return errors;
    }

    private static void AddDuplicateErrors(
        IEnumerable<string> values,
        string label,
        List<string> errors)
    {
        foreach (var group in values
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"Duplicate {label} '{group.Key}'.");
        }
    }

    private static void AddMissingPermissionErrors(
        string definitionId,
        string definitionKind,
        IReadOnlyList<string>? requiredPermissions,
        HashSet<string> permissionKeys,
        List<string> errors)
    {
        if (requiredPermissions is null)
            return;

        foreach (var permissionKey in requiredPermissions)
        {
            if (!permissionKeys.Contains(permissionKey))
                errors.Add($"{definitionKind} '{definitionId}' references missing permission '{permissionKey}'.");
        }
    }

    private static void AddDependencyCycleErrors(
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies,
        List<string> errors)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var moduleId in dependencies.Keys)
        {
            var path = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!Visit(moduleId, dependencies, visited, path))
                errors.Add($"Module dependency cycle involving '{moduleId}'.");
        }
    }

    private static bool Visit(
        string moduleId,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies,
        HashSet<string> visited,
        HashSet<string> path)
    {
        if (visited.Contains(moduleId))
            return true;

        if (!path.Add(moduleId))
            return false;

        if (dependencies.TryGetValue(moduleId, out var moduleDependencies))
        {
            foreach (var dependency in moduleDependencies)
            {
                if (dependencies.ContainsKey(dependency) &&
                    !Visit(dependency, dependencies, visited, path))
                {
                    return false;
                }
            }
        }

        path.Remove(moduleId);
        visited.Add(moduleId);
        return true;
    }
}
