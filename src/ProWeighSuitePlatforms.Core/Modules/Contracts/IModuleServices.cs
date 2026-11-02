// Phase 1 — additive only.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Modules.Contracts
{
    /// <summary>
    /// Entitlement gate. Phase 1 impl = AllowAll stub (upgradeplan.md §4).
    /// Real RSA-signed license.key verification lands last (pre-pilot).
    /// Formula (final): Effective = Requested ∩ Entitled ∩ NotExpired ∩ MachineMatch.
    /// </summary>
    public interface ILicenseService
    {
        Task<HashSet<string>> GetEntitledModulesAsync(CancellationToken ct = default);
        Task<bool> IsAllowedAsync(string moduleId, CancellationToken ct = default);
        Task<bool> IsExpiredAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Read-only view over the module catalog filtered by settings + license + permission.
    /// ShellViewModel consumes this in Phase 3 (not wired yet in Phase 1).
    /// Phase 6: role-aware overload applies DB role_modules + single-active weighing.
    /// </summary>
    public interface IModuleRegistry
    {
        IReadOnlyList<ITransactionModule> AllModules { get; }
        Task<IReadOnlyList<ITransactionModule>> GetEffectiveModulesAsync(CancellationToken ct = default);
        Task<IReadOnlyList<ITransactionModule>> GetEffectiveModulesAsync(UserRole? role, CancellationToken ct = default);
        Task<string> GetActiveWeighingModuleAsync(CancellationToken ct = default);
    }

    public interface IBrandingProvider
    {
        string Brand { get; }
        string Title { get; }
        string Name { get; }
        string Suffix { get; }
        string Tagline { get; }
        string PrimaryHex { get; }
        string SecondaryHex { get; }
        string LogoRelativePath { get; }
        string BannerRelativePath { get; }
        string IconRelativePath { get; }
        string UpdateFeedSubdir { get; }
    }
}
