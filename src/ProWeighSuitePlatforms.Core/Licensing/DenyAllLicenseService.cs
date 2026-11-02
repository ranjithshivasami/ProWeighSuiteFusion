using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Core.Licensing;

public sealed class DenyAllLicenseService : ILicenseService
{
    public Task<bool> IsAllowedAsync(string moduleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<bool> IsExpiredAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
