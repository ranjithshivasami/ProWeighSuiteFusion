using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProWeighSuitePlatforms.Configuration
{
    /// <summary>
    /// Host branding text and images supplied by branding/&lt;flavor&gt;/branding.json.
    /// Assembly metadata remains a fallback for design-time and failed-profile loads.
    /// </summary>
    public static class BrandingInfo
    {
        public static ProWeighSuitePlatforms.Licensing.IBrandingProvider? Provider { get; set; }

        public static string Flavor => Clean(Read("ProWeighFlavor", "Fusion"));

        public static string Name =>
            Clean(Provider?.Name ?? Read("BrandName", "ProWeigh"));

        public static string Suffix =>
            Clean(Provider?.Suffix ?? Read("BrandSuffix", "Fusion"));

        public static string Tagline =>
            Clean(Provider?.Tagline ?? Read("BrandTagline", "Intelligent Weighing Management System"));

        public static string Title =>
            Clean(Provider?.Title ?? $"{Read("BrandName", "ProWeigh")}{Read("BrandSuffix", "Suite")}");

        public static string LogoPath =>
            AssetPath(Provider?.LogoRelativePath, HostImagePath("Logo.png"));

        public static string BannerPath =>
            AssetPath(Provider?.BannerRelativePath, HostImagePath("Poster.png"));

        public static string IconPath =>
            AssetPath(Provider?.IconRelativePath, HostImagePath("app-icon.ico"));

        public static ImageSource? LogoSource => LogoImage.Value;

        public static ImageSource? BannerSource => BannerImage.Value;

        public static ImageSource? IconSource => IconImage.Value;

        private static readonly Lazy<ImageSource?> LogoImage =
            new(() => LoadImage(LogoPath));

        private static readonly Lazy<ImageSource?> BannerImage =
            new(() => LoadImage(BannerPath));

        private static readonly Lazy<ImageSource?> IconImage =
            new(() => LoadImage(IconPath));

        public static string AccountPrompt => $"Access your {Name} {Suffix} account";

        private static string Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        private static string HostImagePath(string fileName) =>
            $"branding/{Flavor}/Images/{fileName}";

        private static string AssetPath(string? path, string fallback)
        {
            var value = string.IsNullOrWhiteSpace(path) ? fallback : path.Trim();
            value = value.Replace('\\', '/');
            return value.StartsWith("/") ? value : "/" + value;
        }

        private static ImageSource? LoadImage(string path)
        {
            try
            {
                var resource = Application.GetResourceStream(new Uri(path, UriKind.Relative));
                if (resource == null) return null;

                using var stream = resource.Stream;
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = stream;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        private static string Read(string key, string fallback)
        {
            try
            {
                var value = (Assembly.GetEntryAssembly() ?? typeof(BrandingInfo).Assembly)
                    .GetCustomAttributes(typeof(AssemblyMetadataAttribute), false)
                    .Cast<AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase))
                    ?.Value;

                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
