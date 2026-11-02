using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Core.Licensing;
using ProWeighSuitePlatforms.Core.Security;

namespace ProWeighSuitePlatforms.Core.Navigation;

public sealed class MenuBuilder : IMenuBuilder
{
    private readonly IMenuRegistry _menus;
    private readonly IRouteRegistry _routes;
    private readonly IAuthorizationService _authorization;
    private readonly ILicenseService _license;
    private readonly ISessionContext _session;

    public MenuBuilder(
        IMenuRegistry menus,
        IRouteRegistry routes,
        IAuthorizationService authorization,
        ILicenseService license,
        ISessionContext session)
    {
        _menus = menus;
        _routes = routes;
        _authorization = authorization;
        _license = license;
        _session = session;
    }

    public async Task<NavigationModel> BuildAsync(
        NavigationBuildContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var visible = new List<VisibleItem>();
        foreach (var definition in _menus.All)
        {
            var route = definition.RouteId is null ? null : _routes.Find(definition.RouteId);
            if (definition.RouteId is not null && route is null)
                continue;

            if (!await IsVisibleAsync(definition, route, context, cancellationToken).ConfigureAwait(false))
                continue;

            visible.Add(new VisibleItem(definition, route));
        }

        var nodes = BuildTree(visible);
        return new NavigationModel
        {
            Primary = nodes.Where(node => node.Region == MenuRegion.Primary).ToList(),
            ModuleTop = nodes.Where(node => node.Region == MenuRegion.ModuleTop).ToList(),
            Configuration = nodes.Where(node => node.Region == MenuRegion.Configuration).ToList(),
            Admin = nodes.Where(node => node.Region == MenuRegion.Admin).ToList(),
            Footer = nodes.Where(node => node.Region == MenuRegion.Footer).ToList()
        };
    }

    private async Task<bool> IsVisibleAsync(
        MenuItemDefinition definition,
        RouteMetadata? route,
        NavigationBuildContext context,
        CancellationToken cancellationToken)
    {
        if (!definition.IsVisibleByDefault)
            return false;

        var isAuthenticated = context.IsAuthenticated || _session.IsAuthenticated;
        if (definition.RequiresAuthentication && !isAuthenticated)
            return false;

        if (definition.ModuleId is not null &&
            context.EnabledModuleIds is not null &&
            !Contains(context.EnabledModuleIds, definition.ModuleId))
        {
            return false;
        }

        var moduleId = definition.ModuleId ?? route?.ModuleId;
        if (moduleId is not null && context.EnabledModuleIds is not null &&
            !Contains(context.EnabledModuleIds, moduleId))
        {
            return false;
        }

        if (definition.Region == MenuRegion.ModuleTop &&
            !string.IsNullOrWhiteSpace(context.ActiveModuleId) &&
            moduleId is not null &&
            !string.Equals(moduleId, context.ActiveModuleId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!await IsAllowedAsync(definition.RequiredPermissions, definition.PermissionMatch, cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        if (!await IsLicenseAllowedAsync(definition.RequiredLicense, cancellationToken).ConfigureAwait(false))
            return false;

        if (route is null)
            return true;

        if (!await IsAllowedAsync(route.RequiredPermissions, route.PermissionMatch, cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        return await IsLicenseAllowedAsync(route.RequiredLicense, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsAllowedAsync(
        IReadOnlyList<string>? permissions,
        PermissionMatch match,
        CancellationToken cancellationToken)
    {
        if (permissions is null || permissions.Count == 0)
            return true;

        if (match == PermissionMatch.All)
        {
            foreach (var permission in permissions)
            {
                if (!await IsPermissionAllowedAsync(permission, cancellationToken).ConfigureAwait(false))
                    return false;
            }

            return true;
        }

        foreach (var permission in permissions)
        {
            if (await IsPermissionAllowedAsync(permission, cancellationToken).ConfigureAwait(false))
                return true;
        }

        return false;
    }

    private async Task<bool> IsPermissionAllowedAsync(string permission, CancellationToken cancellationToken)
    {
        try
        {
            return await _authorization.IsAllowedAsync(permission, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> IsLicenseAllowedAsync(string? licenseKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
            return true;

        try
        {
            return await _license.IsAllowedAsync(licenseKey, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private static bool Contains(IReadOnlyCollection<string> values, string value) =>
        values.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<NavigationNode> BuildTree(IReadOnlyList<VisibleItem> visible)
    {
        var byId = visible.ToDictionary(
            item => item.Definition.Id,
            item => item,
            StringComparer.OrdinalIgnoreCase);

        var children = new Dictionary<string, List<VisibleItem>>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<VisibleItem>();

        foreach (var item in visible)
        {
            var parentId = item.Definition.ParentId;
            if (parentId is not null && byId.ContainsKey(parentId))
            {
                if (!children.TryGetValue(parentId, out var list))
                {
                    list = new List<VisibleItem>();
                    children[parentId] = list;
                }

                list.Add(item);
            }
            else
            {
                roots.Add(item);
            }
        }

        var nodes = new List<NavigationNode>();
        foreach (var root in roots)
        {
            var node = BuildNode(root, children, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (node is not null)
                nodes.Add(node);
        }

        return nodes
            .OrderBy(node => node.Order)
            .ThenBy(node => node.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static NavigationNode? BuildNode(
        VisibleItem item,
        IReadOnlyDictionary<string, List<VisibleItem>> children,
        HashSet<string> path)
    {
        if (!path.Add(item.Definition.Id))
            return null;

        var childNodes = new List<NavigationNode>();
        if (children.TryGetValue(item.Definition.Id, out var childItems))
        {
            foreach (var child in childItems)
            {
                var node = BuildNode(child, children, path);
                if (node is not null)
                    childNodes.Add(node);
            }
        }

        path.Remove(item.Definition.Id);

        var isGroup = item.Definition.RouteId is null && !item.Definition.IsAction;
        if (isGroup && childNodes.Count == 0)
            return null;

        return new NavigationNode(
            item.Definition.Id,
            item.Definition.Title,
            item.Definition.Region,
            item.Definition.Order,
            item.Definition.IconKey,
            item.Definition.RouteId,
            childNodes
                .OrderBy(node => node.Order)
                .ThenBy(node => node.Title, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            item.Definition.ModuleId ?? item.Route?.ModuleId,
            IsEnabled: true);
    }

    private sealed record VisibleItem(MenuItemDefinition Definition, RouteMetadata? Route);
}
