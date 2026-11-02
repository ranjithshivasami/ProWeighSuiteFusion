// Host branding. The build embeds branding/<flavor>/branding.json and
// copies it to Branding/branding.json; PROWEIGH_BRAND or Branding/active-brand.txt
// can override the profile at runtime for white-label brands.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ProWeighSuitePlatforms.Licensing;

namespace ProWeighSuitePlatforms.Infrastructure
{
    public sealed class FileBrandingProvider : IBrandingProvider
    {
        private readonly BrandFile _brand;

        public FileBrandingProvider() => _brand = Load();

        public string Brand => _brand.Brand;
        public string Title => _brand.Title;
        public string Name => _brand.Name;
        public string Suffix => _brand.Suffix;
        public string Tagline => _brand.Tagline;
        public string PrimaryHex => _brand.Primary;
        public string SecondaryHex => _brand.Secondary;
        public string LogoRelativePath => _brand.Logo;
        public string BannerRelativePath => _brand.Banner;
        public string IconRelativePath => _brand.Icon;
        public string UpdateFeedSubdir => _brand.UpdateFeedSubdir;

        private sealed record BrandFile(string Brand, string Title, string Name,
            string Suffix, string Tagline, string Primary, string Secondary,
            string Logo, string Banner, string Icon, string IssAppId, string UpdateFeedSubdir);

        private static BrandFile Load()
        {
            var defaultBrand = ReadFlavor();
            var fallback = new BrandFile(defaultBrand, "ProWeigh Suite", "ProWeigh",
                "Suite", "Intelligent Weighing Management System", "#FF9F00",
                "#CDDC39", string.Empty, string.Empty, string.Empty,
                "{F7B8C9D0-1234-5678-9ABC-DEF012345678}", "stable");

            try
            {
                var name = Environment.GetEnvironmentVariable("PROWEIGH_BRAND");
                if (string.IsNullOrWhiteSpace(name))
                {
                    var marker = Path.Combine(AppContext.BaseDirectory, "Branding", "active-brand.txt");
                    if (File.Exists(marker)) name = File.ReadAllText(marker).Trim();
                }
                if (string.IsNullOrWhiteSpace(name)) name = defaultBrand;

                foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
                {
                    var flat = Path.Combine(root, "Branding", "branding.json");
                    var paths = new[]
                    {
                        Path.Combine(root, "Branding", name, "branding.json"),
                        flat
                    };

                    foreach (var path in paths)
                    {
                        if (!File.Exists(path)) continue;
                        var doc = JsonSerializer.Deserialize<BrandJson>(File.ReadAllText(path));
                        if (doc == null) continue;
                        if (string.Equals(path, flat, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(doc.brand)
                            && !string.Equals(doc.brand, name, StringComparison.OrdinalIgnoreCase))
                            continue;
                        return FromJson(name, doc, fallback);
                    }
                }

                using var stream = OpenEmbeddedBrand(name);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var doc = JsonSerializer.Deserialize<BrandJson>(reader.ReadToEnd());
                    if (doc != null) return FromJson(name, doc, fallback);
                }
            }
            catch { }
            return fallback;
        }

        private static BrandFile FromJson(string name, BrandJson doc, BrandFile fallback) =>
            new(string.IsNullOrWhiteSpace(doc.brand) ? name : doc.brand.Trim(),
                doc.title ?? fallback.Title,
                doc.name ?? fallback.Name, doc.suffix ?? fallback.Suffix,
                doc.tagline ?? fallback.Tagline, doc.primary ?? fallback.Primary,
                doc.secondary ?? fallback.Secondary, doc.logo ?? fallback.Logo,
                doc.banner ?? fallback.Banner, doc.icon ?? fallback.Icon,
                doc.issAppId ?? fallback.IssAppId,
                doc.updateFeedSubdir ?? "stable");

        private static Stream? OpenEmbeddedBrand(string name)
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(FileBrandingProvider).Assembly;
            var expected = $"ProWeighSuitePlatforms.Branding.{name}.branding.json";
            var match = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => string.Equals(n, expected, StringComparison.OrdinalIgnoreCase));
            return match == null ? null : assembly.GetManifestResourceStream(match);
        }

        private static string ReadFlavor()
        {
            try
            {
                var value = (Assembly.GetEntryAssembly() ?? typeof(FileBrandingProvider).Assembly)
                    .GetCustomAttributes(typeof(AssemblyMetadataAttribute), false)
                    .Cast<AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => string.Equals(a.Key, "ProWeighFlavor", StringComparison.OrdinalIgnoreCase))
                    ?.Value;
                return string.IsNullOrWhiteSpace(value) ? "Fusion" : value.Trim();
            }
            catch
            {
                return "Fusion";
            }
        }

        private sealed class BrandJson
        {
            public string? brand { get; set; }
            public string? title { get; set; }
            public string? name { get; set; }
            public string? suffix { get; set; }
            public string? tagline { get; set; }
            public string? primary { get; set; }
            public string? secondary { get; set; }
            public string? logo { get; set; }
            public string? banner { get; set; }
            public string? icon { get; set; }
            public string? issAppId { get; set; }
            public string? updateFeedSubdir { get; set; }
        }
    }
}
