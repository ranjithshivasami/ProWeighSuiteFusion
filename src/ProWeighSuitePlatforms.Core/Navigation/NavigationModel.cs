using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Core.Navigation;

public sealed record NavigationNode(
    string Id,
    string Title,
    MenuRegion Region,
    int Order,
    string? IconKey,
    string? RouteId,
    IReadOnlyList<NavigationNode> Children,
    string? ModuleId = null,
    bool IsEnabled = true);

public sealed class NavigationModel
{
    public static NavigationModel Empty { get; } = new();

    public IReadOnlyList<NavigationNode> Primary { get; init; } = Array.Empty<NavigationNode>();

    public IReadOnlyList<NavigationNode> ModuleTop { get; init; } = Array.Empty<NavigationNode>();

    public IReadOnlyList<NavigationNode> Configuration { get; init; } = Array.Empty<NavigationNode>();

    public IReadOnlyList<NavigationNode> Admin { get; init; } = Array.Empty<NavigationNode>();

    public IReadOnlyList<NavigationNode> Footer { get; init; } = Array.Empty<NavigationNode>();

    public IReadOnlyList<NavigationNode> GetRegion(MenuRegion region) => region switch
    {
        MenuRegion.Primary => Primary,
        MenuRegion.ModuleTop => ModuleTop,
        MenuRegion.Configuration => Configuration,
        MenuRegion.Admin => Admin,
        MenuRegion.Footer => Footer,
        _ => Array.Empty<NavigationNode>()
    };
}

public sealed record NavigationBuildContext(
    bool IsAuthenticated,
    IReadOnlyCollection<string> Roles,
    string? ActiveModuleId = null,
    IReadOnlyCollection<string>? EnabledModuleIds = null);

public interface IMenuBuilder
{
    Task<NavigationModel> BuildAsync(
        NavigationBuildContext context,
        CancellationToken cancellationToken = default);
}

public enum NavigationChangeReason
{
    Login,
    Logout,
    PermissionsChanged,
    RolesChanged,
    ModuleStateChanged,
    LicenseChanged,
    SettingsChanged,
    Manual
}

public sealed class NavigationChangedEventArgs : EventArgs
{
    public NavigationChangedEventArgs(NavigationChangeReason reason)
    {
        Reason = reason;
    }

    public NavigationChangeReason Reason { get; }
}

public interface INavigationChangeNotifier
{
    event EventHandler<NavigationChangedEventArgs>? Changed;

    void Notify(NavigationChangeReason reason);
}

public sealed record NavigationGuardResult(bool IsAllowed, string? Reason = null)
{
    public static NavigationGuardResult Allow { get; } = new(true);

    public static NavigationGuardResult Deny(string reason) => new(false, reason);
}

public interface IRouteGuard
{
    Task<NavigationGuardResult> CanEnterAsync(
        string routeId,
        CancellationToken cancellationToken = default);
}
