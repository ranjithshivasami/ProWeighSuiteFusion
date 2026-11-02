using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Core.Licensing;

public interface ILicenseService
{
    Task<bool> IsAllowedAsync(string moduleId, CancellationToken cancellationToken = default);

    Task<bool> IsExpiredAsync(CancellationToken cancellationToken = default);
}
