// Effective = composed modules ∩ Entitled (license) ∩ DbEnabled(role)
//             ∩ WeighingSingleActive.
// The composed module set is fixed by the host composition; there is no
// runtime product selection. Unknown role (null) skips the DB gate so the
// startup probe never hides modules.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Modules.Contracts;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.Modules.Infrastructure
{
    public sealed class ModuleRegistry : IModuleRegistry
    {
        private readonly ILicenseService _license;

        public ModuleRegistry(ILicenseService license)
        {
            _license = license;
        }

        public IReadOnlyList<ITransactionModule> AllModules => ProductModuleCatalog.TransactionModules;

        public Task<IReadOnlyList<ITransactionModule>> GetEffectiveModulesAsync(CancellationToken ct = default)
            => GetEffectiveModulesAsync((UserRole?)null, ct);

        public Task<string> GetActiveWeighingModuleAsync(CancellationToken ct = default)
        {
            try
            {
                var repo = new ModuleAssignmentRepository(AppConfigurationManager.GetDatabasePath());
                return repo.GetActiveWeighingModuleAsync();
            }
            catch
            {
                return Task.FromResult(ModuleAssignmentRepository.DefaultActiveWeighing);
            }
        }

        public async Task<IReadOnlyList<ITransactionModule>> GetEffectiveModulesAsync(UserRole? role, CancellationToken ct = default)
        {
            var entitled = await _license.GetEntitledModulesAsync(ct).ConfigureAwait(false);
            var allModules = ProductModuleCatalog.TransactionModules;

            Dictionary<string, HashSet<UserRole>>? dbMap = null;
            string activeWeighing = ModuleAssignmentRepository.DefaultActiveWeighing;
            try
            {
                var repo = new ModuleAssignmentRepository(AppConfigurationManager.GetDatabasePath());
                await repo.InitializeAsync().ConfigureAwait(false);
                dbMap = await repo.GetEnabledMapAsync().ConfigureAwait(false);
                activeWeighing = await repo.GetActiveWeighingModuleAsync().ConfigureAwait(false);
            }
            catch { /* DB unavailable → skip DB gate, keep settings ∩ license behavior */ }

            return allModules
                .Where(m => entitled.Contains(m.ModuleId))
                .Where(m =>
                {
                    if (role.HasValue && dbMap != null)
                    {
                        if (dbMap.TryGetValue(m.ModuleId, out var roles) && !roles.Contains(role.Value))
                            return false;
                        // No row = seeded default enabled → show.
                    }
                    return true;
                })
                .Where(m =>
                {
                    // Weighing family: only the single active radio selection shows.
                    if (IsWeighingModule(m.ModuleId))
                        return string.Equals(m.ModuleId, activeWeighing, StringComparison.OrdinalIgnoreCase);
                    return true;
                })
                .ToList();
        }

        private static bool IsWeighingModule(string moduleId) =>
            string.Equals(
                ProductModuleCatalog.Find(moduleId)?.GroupName,
                ProWeighSuitePlatforms.Core.Modules.ProductModuleGroups.Weighing,
                StringComparison.OrdinalIgnoreCase);
    }
}
