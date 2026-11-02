using System;
using System.Reflection;
using System.Windows;

namespace ProWeighSuitePlatforms.Helpers
{
    /// <summary>
    /// Single source of truth for the application version. Tries sources in priority:
    /// 1. Velopack currently installed version (when app is Velopack-deployed)
    /// 2. App.xaml AppVersion resource (design-time bump in App.xaml)
    /// 3. AssemblyInformationalVersion
    /// 4. AssemblyVersion
    /// </summary>
    public static class AppVersionHelper
    {
        public static string GetVersion()
        {
            // 1. Velopack installed version - most authoritative when deployed via Velopack
            try
            {
                if (Velopack.Locators.VelopackLocator.IsCurrentSet)
                {
                    var v = Velopack.Locators.VelopackLocator.Current?.CurrentlyInstalledVersion?.ToString();
                    if (!string.IsNullOrWhiteSpace(v))
                        return Normalize(v!);
                }
            }
            catch { }

            // 2. App.xaml AppVersion resource (e.g. "1.0.3")
            try
            {
                var app = Application.Current;
                if (app != null)
                {
                    var res = app.TryFindResource("AppVersion") as string;
                    if (!string.IsNullOrWhiteSpace(res))
                        return Normalize(res!);
                }
            }
            catch { }

            // 3. AssemblyInformationalVersion (e.g. "1.0.3+abc123")
            try
            {
                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info))
                {
                    var plus = info!.IndexOf('+');
                    if (plus > 0) info = info.Substring(0, plus);
                    if (!string.IsNullOrWhiteSpace(info))
                        return Normalize(info!);
                }
            }
            catch { }

            // 4. AssemblyVersion
            try
            {
                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var v = asm.GetName().Version?.ToString(3);
                if (!string.IsNullOrWhiteSpace(v))
                    return Normalize(v!);
            }
            catch { }

            return "1.0.0";
        }

        private static string Normalize(string v) => v.Trim().TrimStart('v', 'V');
    }
}
