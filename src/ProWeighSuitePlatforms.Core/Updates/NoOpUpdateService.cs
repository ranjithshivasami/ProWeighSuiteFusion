using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Core.Updates;

public sealed class NoOpUpdateService : IUpdateService
{
    public Task<bool> IsUpdateAvailableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task ApplyUpdateAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
