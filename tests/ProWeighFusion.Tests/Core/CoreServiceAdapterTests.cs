using System.Threading.Tasks;
using ProWeighSuitePlatforms.Core.Auditing;
using ProWeighSuitePlatforms.Adapters;
using ProWeighSuitePlatforms.Modules.Infrastructure;
using Xunit;

namespace ProWeighSuitePlatforms.Tests.Core;

public class CoreServiceAdapterTests
{
    [Fact]
    public void FileBrandingProvider_ExposesActiveBrandDisplayValues()
    {
        var branding = new FileBrandingProvider();

        Assert.False(string.IsNullOrWhiteSpace(branding.Brand));
        Assert.False(string.IsNullOrWhiteSpace(branding.Name));
        Assert.False(string.IsNullOrWhiteSpace(branding.Suffix));
        Assert.False(string.IsNullOrWhiteSpace(branding.Tagline));
        Assert.False(string.IsNullOrWhiteSpace(branding.LogoRelativePath));
        Assert.False(string.IsNullOrWhiteSpace(branding.BannerRelativePath));
    }

    [Fact]
    public void LegacyBrandingAdapter_FallsBackToNeutralValues()
    {
        var adapter = new LegacyBrandingAdapter(null);

        Assert.Equal("ProWeigh", adapter.ProductName);
        Assert.Equal("ProWeigh Suite", adapter.Title);
        Assert.Equal("Standard", adapter.ThemeName);
        Assert.Equal("#FF9F00", adapter.PrimaryColor);
        Assert.Equal("#009688", adapter.SecondaryColor);
        Assert.Equal(string.Empty, adapter.LogoPath);
    }

    [Fact]
    public async Task LegacyLicenseAdapter_FallsBackToAllowAll()
    {
        var adapter = new LegacyLicenseAdapter(null);

        Assert.True(await adapter.IsAllowedAsync("Fusion"));
        Assert.False(await adapter.IsExpiredAsync());
    }

    [Fact]
    public async Task LegacyAuditAdapter_WithoutAttachedService_IsNoOp()
    {
        var adapter = new LegacyAuditAdapter();

        await adapter.LogAsync(new AuditEvent("Test", "System"));
    }
}
