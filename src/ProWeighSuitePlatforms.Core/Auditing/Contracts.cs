using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Core.Auditing;

public sealed record AuditEvent(
    string Action,
    string Category,
    string? UserName = null,
    string? EntityType = null,
    string? EntityId = null,
    string? Details = null,
    string? OldValue = null,
    string? NewValue = null,
    string Severity = "Info",
    DateTimeOffset? Timestamp = null);

public interface IAuditService
{
    Task LogAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
