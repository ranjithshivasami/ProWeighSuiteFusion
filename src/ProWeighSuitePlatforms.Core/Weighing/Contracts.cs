// Phase 1 — additive only.
// Shared workflow + validation abstractions. Implementations arrive Phase 2+.
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Modules.Contracts
{
    public sealed record TransactionContext
    {
        public string BatchNumber { get; init; } = string.Empty;
        public string MaterialCode { get; init; } = string.Empty;
        public double ActualWeight { get; init; }
        public string WeighedBy { get; init; } = string.Empty;
    }

    public sealed record CheckResult(bool IsAccepted, string Reason = "")
    {
        public static CheckResult Accept() => new(true, string.Empty);
        public static CheckResult Reject(string reason) => new(false, reason);
    }

    /// <summary>
    /// Standard weighing lifecycle. Hybrid modules decorate this (override interlock only).
    /// </summary>
    public interface IWeighingWorkflow
    {
        Task StartAsync(TransactionContext context, CancellationToken ct = default);
        Task<double> CaptureStableWeightAsync(CancellationToken ct = default);
        Task CompleteAsync(TransactionContext context, CancellationToken ct = default);
    }

    public interface IWeighingStrategy
    {
        string Mode { get; } // Manual | Auto
        Task<double> CaptureAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// upgradeplan.md §3b: LocalToleranceValidator vs ExternalApiValidator share this.
    /// VM calls this interface only — never branches on validator type.
    /// </summary>
    public interface ICheckValidator
    {
        string ValidatorId { get; } // LocalTolerance | ExternalApi
        Task<CheckResult> ValidateAsync(Material material, double weight, CancellationToken ct = default);
    }
}
