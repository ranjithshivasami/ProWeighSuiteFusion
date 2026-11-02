using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Core.Auditing;

public sealed class NullAuditService : IAuditService
{
    public Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
