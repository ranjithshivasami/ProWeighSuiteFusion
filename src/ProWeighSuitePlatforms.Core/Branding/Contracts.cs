namespace ProWeighSuitePlatforms.Core.Branding;

public interface IBrandingProvider
{
    string ProductName { get; }

    string Title { get; }

    string ThemeName { get; }

    string PrimaryColor { get; }

    string SecondaryColor { get; }

    string LogoPath { get; }
}
