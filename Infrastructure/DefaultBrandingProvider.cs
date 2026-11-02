// Phase 1 - neutral fallback used when host branding is unavailable.
using ProWeighSuitePlatforms.Licensing;

namespace ProWeighSuitePlatforms.Infrastructure
{
    public sealed class DefaultBrandingProvider : IBrandingProvider
    {
        public string Brand => "Standard";
        public string Title => "ProWeigh Suite";
        public string Name => "ProWeigh";
        public string Suffix => "Suite";
        public string Tagline => "Intelligent Weighing Management System";
        public string PrimaryHex => "#FF9F00";
        public string SecondaryHex => "#CDDC39";
        public string LogoRelativePath => string.Empty;
        public string BannerRelativePath => string.Empty;
        public string IconRelativePath => string.Empty;
        public string UpdateFeedSubdir => "stable";
    }
}
