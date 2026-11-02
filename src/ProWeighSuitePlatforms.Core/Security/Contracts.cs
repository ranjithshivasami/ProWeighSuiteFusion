using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Core.Navigation;

namespace ProWeighSuitePlatforms.Core.Security;

public interface ISessionContext
{
    bool IsAuthenticated { get; }

    string? UserName { get; }

    IReadOnlyCollection<string> Roles { get; }
}

public interface IAuthorizationService
{
    Task<bool> IsAllowedAsync(string permissionKey, CancellationToken cancellationToken = default);
}

public interface IPermissionRegistry
{
    IReadOnlyList<PermissionDefinition> All { get; }

    bool Contains(string key);

    PermissionDefinition? Find(string key);
}

public interface IPermissionRegistryBuilder
{
    void Add(PermissionDefinition definition);
}
