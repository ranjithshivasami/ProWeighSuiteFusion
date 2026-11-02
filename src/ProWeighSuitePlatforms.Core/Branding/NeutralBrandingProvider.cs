namespace ProWeighSuitePlatforms.Core.Branding;

public sealed class NeutralBrandingProvider : IBrandingProvider
{
    public string ProductName => "ProWeigh";

    public string Title => "ProWeigh Suite";

    public string ThemeName => "Standard";

    public string PrimaryColor => "#FF9F00";

    public string SecondaryColor => "#009688";

    public string LogoPath => string.Empty;
}
