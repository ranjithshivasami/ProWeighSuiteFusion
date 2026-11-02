using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Core.Security;

public sealed class AnonymousSessionContext : ISessionContext
{
    public bool IsAuthenticated => false;

    public string? UserName => null;

    public IReadOnlyCollection<string> Roles { get; } = Array.Empty<string>();
}

public sealed class DenyAllAuthorizationService : IAuthorizationService
{
    public Task<bool> IsAllowedAsync(string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
