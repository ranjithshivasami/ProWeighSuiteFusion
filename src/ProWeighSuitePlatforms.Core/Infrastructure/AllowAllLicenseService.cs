// Phase 1 — additive only. Allow-all stub so module extraction (Phases 2-4)
// is never blocked by crypto. Real RSA license.key check lands last (pre-pilot).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Modules.Contracts;

namespace ProWeighSuitePlatforms.Modules.Infrastructure
{
    public sealed class AllowAllLicenseService : ILicenseService
    {
        public Task<HashSet<string>> GetEntitledModulesAsync(CancellationToken ct = default)
            => Task.FromResult(new HashSet<string>(
                ProductModuleCatalog.LicenseGatedModuleIds,
                StringComparer.OrdinalIgnoreCase));

        public Task<bool> IsAllowedAsync(string moduleId, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> IsExpiredAsync(CancellationToken ct = default)
            => Task.FromResult(false);
    }
}
