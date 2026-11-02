using System.Collections.Generic;

namespace ProWeighSuitePlatforms.Core.Navigation;

public enum MenuRegion
{
    Primary,
    ModuleTop,
    Configuration,
    Admin,
    Footer
}

public enum PermissionMatch
{
    Any,
    All
}

public enum RouteCacheMode
{
    Transient,
    KeepAlive,
    Singleton
}

public sealed record MenuItemDefinition(
    string Id,
    string Title,
    string? ParentId = null,
    MenuRegion Region = MenuRegion.Primary,
    int Order = 0,
    string? IconKey = null,
    string? RouteId = null,
    IReadOnlyList<string>? RequiredPermissions = null,
    PermissionMatch PermissionMatch = PermissionMatch.Any,
    string? RequiredLicense = null,
    bool IsVisibleByDefault = true,
    string? ModuleId = null,
    bool RequiresAuthentication = true,
    bool IsAction = false);

public sealed record RouteMetadata(
    string RouteId,
    string DisplayName,
    IReadOnlyList<string>? RequiredPermissions = null,
    PermissionMatch PermissionMatch = PermissionMatch.Any,
    string? RequiredLicense = null,
    string? ModuleId = null,
    RouteCacheMode CacheMode = RouteCacheMode.Transient);

public sealed record PermissionDefinition(
    string Key,
    string DisplayName,
    string Group,
    string? ModuleId = null,
    string? Description = null,
    bool IsCore = false,
    string? LegacyKey = null);
