using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Admin-switchable brand theme. Single primary hex drives all Brand* style variables.
    /// XAML must reference {DynamicResource BrandPrimaryBrush} (etc.) so runtime switches apply live.
    /// Legacy PrimaryOrange* keys are kept in sync for backwards compat.
    /// </summary>
    public static class ThemeService
    {
        public sealed record ThemeOption(string Name, string PrimaryHex, string Description);

        public static IReadOnlyList<ThemeOption> BuiltInThemes { get; } = new List<ThemeOption>
        {
            new("Orange", "#FF9F00", "Default ProWeigh orange"),
            new("Blue",   "#086BE8", "BridgeX blue"),
            new("Green",  "#0BB14D", "Success green"),
            new("Teal",   "#009688", "Teal"),
            new("Purple", "#673AB7", "Purple"),
            new("Red",    "#D32F2F", "Alert red"),
            new("Dark",   "#222222", "Dark graphite"),
        }.AsReadOnly();

        public static string CurrentHex { get; private set; } = "#FF9F00";
        public static string CurrentName { get; private set; } = "Orange";
        public static string CurrentSecondaryHex { get; private set; } = "#CDDC39";

        public static void ApplySavedTheme(string? hex, string? name = null)
        {
            var normalized = Models.AppSettings.NormalizeBrandHex(hex);
            var themeName = string.IsNullOrWhiteSpace(name) ? GuessName(normalized) : name.Trim();
            Apply(normalized, themeName);
        }

        public static void Apply(string hex, string name = "Custom")
        {
            var normalized = Models.AppSettings.NormalizeBrandHex(hex);
            CurrentHex = normalized;
            CurrentName = string.IsNullOrWhiteSpace(name) ? GuessName(normalized) : name.Trim();

            // NOTE: no silent swallow by design — failures are logged then rethrown.
            // Callers that must never crash (app startup) catch locally; UI callers
            // surface the error so a "saved but not applied" theme fails loudly.
            try
            {
                var app = Application.Current
                    ?? throw new InvalidOperationException("Application.Current is null; cannot apply theme.");

                var primary = (Color)ColorConverter.ConvertFromString(normalized);
                var primaryDark = Darken(primary, 0.18);
                var primaryLight = Lighten(primary, 0.85);
                var primarySoft = Lighten(primary, 0.92);
                var onPrimary = PickOnPrimary(primary);

                // Canonical Brand* style variables
                SetColor(app, "BrandPrimaryColor", primary);
                SetBrush(app, "BrandPrimaryBrush", primary);
                SetColor(app, "BrandPrimaryDarkColor", primaryDark);
                SetBrush(app, "BrandPrimaryDarkBrush", primaryDark);
                SetColor(app, "BrandPrimaryLightColor", primaryLight);
                SetBrush(app, "BrandPrimaryLightBrush", primaryLight);
                SetColor(app, "BrandPrimarySoftColor", primarySoft);
                SetBrush(app, "BrandPrimarySoftBrush", primarySoft);
                SetColor(app, "BrandOnPrimaryColor", onPrimary);
                SetBrush(app, "BrandOnPrimaryBrush", onPrimary);

                // Gradient stops for Sign In / accent gradients
                SetColor(app, "BrandGradientStartColor", Lighten(primary, 0.05));
                SetColor(app, "BrandGradientEndColor", Darken(primary, 0.08));

                // Legacy aliases — keeps old XAML (PrimaryOrange*) working after a theme switch
                SetColor(app, "PrimaryOrangeColor", primary);
                SetBrush(app, "PrimaryOrangeBrush", primary);
                SetColor(app, "AccentLightOrangeColor", primarySoft);
                SetBrush(app, "AccentLightOrangeBrush", primarySoft);
                SetColor(app, "AccentMediumOrangeColor", primaryLight);
                SetBrush(app, "AccentMediumOrangeBrush", primaryLight);

                LogApply(normalized, CurrentName, ok: true, error: null);
            }
            catch (Exception ex)
            {
                LogApply(normalized, CurrentName, ok: false, error: ex.ToString());
                throw;
            }
        }

        public static void ApplySecondary(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return;

            var normalized = Models.AppSettings.NormalizeBrandHex(hex);
            try
            {
                var app = Application.Current
                    ?? throw new InvalidOperationException("Application.Current is null; cannot apply secondary theme.");

                var secondary = (Color)ColorConverter.ConvertFromString(normalized);
                CurrentSecondaryHex = normalized;
                ApplySecondaryColors(app, secondary);
                LogApply(normalized, $"{CurrentName} secondary", ok: true, error: null);
            }
            catch (Exception ex)
            {
                LogApply(normalized, $"{CurrentName} secondary", ok: false, error: ex.ToString());
                throw;
            }
        }

        private static void ApplySecondaryColors(Application app, Color secondary)
        {
            SetColor(app, "BrandSecondaryColor", secondary);
            SetBrush(app, "BrandSecondaryBrush", secondary);
            SetColor(app, "BrandSecondaryDarkColor", Darken(secondary, 0.18));
            SetBrush(app, "BrandSecondaryDarkBrush", Darken(secondary, 0.18));
            SetColor(app, "BrandSecondaryLightColor", Lighten(secondary, 0.85));
            SetBrush(app, "BrandSecondaryLightBrush", Lighten(secondary, 0.85));
            SetColor(app, "BrandSecondarySoftColor", Lighten(secondary, 0.92));
            SetBrush(app, "BrandSecondarySoftBrush", Lighten(secondary, 0.92));
            SetColor(app, "BrandOnSecondaryColor", PickOnPrimary(secondary));
            SetBrush(app, "BrandOnSecondaryBrush", PickOnPrimary(secondary));
        }

        private static void LogApply(string hex, string name, bool ok, string? error)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ProWeighSuitePlatforms");
                System.IO.Directory.CreateDirectory(dir);
                var file = System.IO.Path.Combine(dir, "theme.log");
                System.IO.File.AppendAllText(file,
                    $"[{DateTime.Now:O}] Apply hex={hex} name={name} ok={ok}" +
                    (error == null ? string.Empty : $" error={error}") + Environment.NewLine);
            }
            catch { }
            System.Diagnostics.Debug.WriteLine($"[Theme] Apply hex={hex} name={name} ok={ok} {error}");
        }

        private static string GuessName(string hex)
        {
            foreach (var t in BuiltInThemes)
            {
                if (string.Equals(t.PrimaryHex, hex, StringComparison.OrdinalIgnoreCase))
                    return t.Name;
            }
            return "Custom";
        }

        private static void SetColor(Application app, string key, Color color)
        {
            app.Resources[key] = color;
        }

        private static void SetBrush(Application app, string key, Color color)
        {
            // Mutate the shared instance so every DynamicResource (and any
            // StaticResource holding the same instance) updates. A frozen brush
            // cannot be mutated — replace it instead of throwing.
            if (app.Resources[key] is SolidColorBrush existing && !existing.IsFrozen)
            {
                existing.Color = color;
            }
            else
            {
                app.Resources[key] = new SolidColorBrush(color);
            }
        }

        private static Color Darken(Color c, double amount)
        {
            double f = 1.0 - Math.Clamp(amount, 0, 1);
            return Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
        }

        private static Color Lighten(Color c, double amount)
        {
            // amount 0..1 blends toward white
            double a = Math.Clamp(amount, 0, 1);
            byte r = (byte)(c.R + (255 - c.R) * a);
            byte g = (byte)(c.G + (255 - c.G) * a);
            byte b = (byte)(c.B + (255 - c.B) * a);
            return Color.FromRgb(r, g, b);
        }

        private static Color PickOnPrimary(Color primary)
        {
            // Luminance heuristic: dark text on light brand, white on dark brand.
            double lum = (0.299 * primary.R + 0.587 * primary.G + 0.114 * primary.B) / 255.0;
            if (lum > 0.6) return Color.FromRgb(0x43, 0x42, 0x42); // #434242 readable on orange/yellow
            return Colors.White;
        }
    }
}
