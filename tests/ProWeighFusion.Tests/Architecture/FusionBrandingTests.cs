using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ProWeighSuitePlatforms.Tests.Architecture
{
    public sealed class FusionBrandingTests
    {
        private static readonly string Root = FindRepositoryRoot();
        private static readonly string BrandDir = Path.Combine(Root, "branding", "fusion");
        private static readonly string ImagesDir = Path.Combine(BrandDir, "Images");

        private sealed record BrandingProfile(
            string Brand,
            string Title,
            string Name,
            string Suffix,
            string Tagline,
            string Primary,
            string Secondary,
            string Logo,
            string Banner,
            string Icon,
            string Flavor,
            string InstallerName,
            string IssAppId,
            string UpdateFeedSubdir);

        [Fact]
        public void BrandingProfileIsCompleteAndPointsAtFusionAssets()
        {
            var profilePath = Path.Combine(BrandDir, "branding.json");
            Assert.True(File.Exists(profilePath), $"Missing branding profile: {profilePath}");

            var profile = JsonSerializer.Deserialize<BrandingProfile>(
                File.ReadAllText(profilePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            Assert.False(string.IsNullOrWhiteSpace(profile.Brand));
            Assert.False(string.IsNullOrWhiteSpace(profile.Title));
            Assert.False(string.IsNullOrWhiteSpace(profile.Name));
            Assert.False(string.IsNullOrWhiteSpace(profile.Suffix));
            Assert.False(string.IsNullOrWhiteSpace(profile.Tagline));
            Assert.False(string.IsNullOrWhiteSpace(profile.Flavor));
            Assert.False(string.IsNullOrWhiteSpace(profile.InstallerName));
            Assert.False(string.IsNullOrWhiteSpace(profile.IssAppId));
            Assert.False(string.IsNullOrWhiteSpace(profile.UpdateFeedSubdir));
            Assert.Matches("^#[0-9A-Fa-f]{6}$", profile.Primary);
            Assert.Matches("^#[0-9A-Fa-f]{6}$", profile.Secondary);

            Assert.Equal("Fusion", profile.Flavor);
            Assert.Equal("Fusion", profile.Brand);

            Assert.StartsWith("branding/fusion/Images/", profile.Logo, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("branding/fusion/Images/", profile.Banner, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("branding/fusion/Images/", profile.Icon, StringComparison.OrdinalIgnoreCase);

            AssertAssetExists(profile.Logo);
            AssertAssetExists(profile.Banner);
            AssertAssetExists(profile.Icon);

            Assert.True(File.Exists(Path.Combine(ImagesDir, "Logo.png")));
            Assert.True(File.Exists(Path.Combine(ImagesDir, "Poster.png")));
            Assert.True(File.Exists(Path.Combine(ImagesDir, "app-icon.ico")));
            Assert.True(File.Exists(Path.Combine(BrandDir, "active-brand.txt")));
            Assert.True(File.Exists(Path.Combine(BrandDir, "Version.props")));

            Assert.Equal("Fusion", File.ReadAllText(Path.Combine(BrandDir, "active-brand.txt")).Trim());
        }

        private static void AssertAssetExists(string relativePath)
        {
            var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full), $"Missing branding asset: {full}");
        }

        private static string FindRepositoryRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Repository root not found.");
        }
    }
}
