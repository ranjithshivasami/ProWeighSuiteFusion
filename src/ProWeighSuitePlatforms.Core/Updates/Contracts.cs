using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Core.Updates;

public interface IUpdateService
{
    Task<bool> IsUpdateAvailableAsync(CancellationToken cancellationToken = default);

    Task ApplyUpdateAsync(CancellationToken cancellationToken = default);
}
