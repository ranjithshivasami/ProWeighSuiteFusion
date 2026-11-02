using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Core.Licensing;
using ProWeighSuitePlatforms.Core.Security;

namespace ProWeighSuitePlatforms.Core.Navigation;

public sealed class RouteGuard : IRouteGuard
{
    private readonly IRouteRegistry _routes;
    private readonly IAuthorizationService _authorization;
    private readonly ILicenseService _license;
    private readonly ISessionContext _session;

    public RouteGuard(
        IRouteRegistry routes,
        IAuthorizationService authorization,
        ILicenseService license,
        ISessionContext session)
    {
        _routes = routes;
        _authorization = authorization;
        _license = license;
        _session = session;
    }

    public async Task<NavigationGuardResult> CanEnterAsync(
        string routeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(routeId))
            return NavigationGuardResult.Deny("Route ID is required.");

        var route = _routes.Find(routeId);
        if (route is null)
            return NavigationGuardResult.Deny($"Unknown route '{routeId}'.");

        if (!_session.IsAuthenticated)
            return NavigationGuardResult.Deny("Sign in is required.");

        if (!string.IsNullOrWhiteSpace(route.RequiredLicense))
        {
            var licensed = await _license
                .IsAllowedAsync(route.RequiredLicense, cancellationToken)
                .ConfigureAwait(false);
            if (!licensed)
                return NavigationGuardResult.Deny(
                    $"The '{route.RequiredLicense}' license is required.");
        }

        if (!await ArePermissionsAllowedAsync(
                route.RequiredPermissions,
                route.PermissionMatch,
                cancellationToken)
                .ConfigureAwait(false))
        {
            return NavigationGuardResult.Deny("You do not have permission to open this page.");
        }

        return NavigationGuardResult.Allow;
    }

    private async Task<bool> ArePermissionsAllowedAsync(
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
                if (!await _authorization
                        .IsAllowedAsync(permission, cancellationToken)
                        .ConfigureAwait(false))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (var permission in permissions)
        {
            if (await _authorization
                    .IsAllowedAsync(permission, cancellationToken)
                    .ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }
}
