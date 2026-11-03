// Licensing and branding contracts. Implementations live in Infrastructure\
// (FileLicenseService, FileBrandingProvider).
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Licensing
{
    /// <summary>
    /// Entitlement gate. FileLicenseService reads %APPDATA%\ProWeighSuiteFusion\license.json;
    /// a missing file means dev fallback allow-all, a present file gates by entitled modules
    /// and expiry.
    /// </summary>
    public interface ILicenseService
    {
        Task<HashSet<string>> GetEntitledModulesAsync(CancellationToken ct = default);
        Task<bool> IsAllowedAsync(string moduleId, CancellationToken ct = default);
        Task<bool> IsExpiredAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Brand text, colors and image paths for the running product (Fusion).
    /// </summary>
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
