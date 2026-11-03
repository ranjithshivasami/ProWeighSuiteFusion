// Phase 5 — file gate (upgradeplan.md §4). Safe rollout:
// - No license.json → allow-all (dev / existing installs keep working), logged.
// - license.json present → Effective = Requested ∩ Entitled ∩ NotExpired.
// TODO(pre-pilot): RSA-SHA256 signature check with embedded public key + MachineHash
// lock + AuditService violation log on deny. AllowAllLicenseService stays for tests.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Licensing;

namespace ProWeighSuitePlatforms.Infrastructure
{
    public sealed class FileLicenseService : ILicenseService
    {
        private static readonly IReadOnlyList<string> All = new[] { "Fusion" };

        private static readonly DateTime HardcodedExpiry = new(2026, 11, 21);

        private string LicensePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ProWeighSuiteFusion", "license.json");

        public async Task<HashSet<string>> GetEntitledModulesAsync(CancellationToken ct = default)
        {
            if (DateTime.Today > HardcodedExpiry)
            {
                System.Diagnostics.Debug.WriteLine($"[License] hardcoded expiry {HardcodedExpiry:yyyy-MM-dd} passed — deny all.");
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                if (!File.Exists(LicensePath))
                {
                    System.Diagnostics.Debug.WriteLine("[License] no license.json — dev fallback allow-all.");
                    return new HashSet<string>(All, StringComparer.OrdinalIgnoreCase);
                }
                var json = await File.ReadAllTextAsync(LicensePath, ct).ConfigureAwait(false);
                var lic = JsonSerializer.Deserialize<LicenseFile>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (lic?.EntitledModules == null || lic.EntitledModules.Count == 0)
                    return new HashSet<string>(All, StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(lic.Expiry)
                    && DateTime.TryParse(lic.Expiry, out var exp)
                    && exp.Date < DateTime.Today)
                {
                    System.Diagnostics.Debug.WriteLine($"[License] expired {lic.Expiry} — deny all.");
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                return new HashSet<string>(lic.EntitledModules, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                // Corrupt file fails closed except in DEBUG (keeps dev loop unblocked).
                System.Diagnostics.Debug.WriteLine($"[License] read failed: {ex.Message}");
#if DEBUG
                return new HashSet<string>(All, StringComparer.OrdinalIgnoreCase);
#else
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
#endif
            }
        }

        public async Task<bool> IsAllowedAsync(string moduleId, CancellationToken ct = default)
            => (await GetEntitledModulesAsync(ct).ConfigureAwait(false)).Contains(moduleId);

        public async Task<bool> IsExpiredAsync(CancellationToken ct = default)
        {
            if (DateTime.Today > HardcodedExpiry)
                return true;

            try
            {
                if (!File.Exists(LicensePath))
                    return false;

                var json = await File.ReadAllTextAsync(LicensePath, ct).ConfigureAwait(false);
                var lic = JsonSerializer.Deserialize<LicenseFile>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return !string.IsNullOrWhiteSpace(lic?.Expiry)
                    && DateTime.TryParse(lic.Expiry, out var exp)
                    && exp.Date < DateTime.Today;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[License] expiry read failed: {ex.Message}");
#if DEBUG
                return false;
#else
                return true;
#endif
            }
        }

        private sealed class LicenseFile
        {
            public string? CustomerId { get; set; }
            public List<string>? EntitledModules { get; set; }
            public string? Expiry { get; set; }
            public string? Signature { get; set; }
        }
    }
}
