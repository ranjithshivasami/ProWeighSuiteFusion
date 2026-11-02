using System;
using System.Reflection;

namespace ProWeighSuitePlatforms.Services
{
    public static class AppVersionProvider
    {
        private static Func<string?>? _resolver;

        public static void SetResolver(Func<string?>? resolver) => _resolver = resolver;

        public static string GetVersion()
        {
            try
            {
                var resolved = _resolver?.Invoke();
                if (!string.IsNullOrWhiteSpace(resolved))
                    return Normalize(resolved!);
            }
            catch
            {
            }

            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var informational = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;

                if (!string.IsNullOrWhiteSpace(informational))
                {
                    var plus = informational!.IndexOf('+');
                    if (plus > 0)
                        informational = informational.Substring(0, plus);

                    if (!string.IsNullOrWhiteSpace(informational))
                        return Normalize(informational!);
                }
            }
            catch
            {
            }

            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version?.ToString(3);
                if (!string.IsNullOrWhiteSpace(version))
                    return Normalize(version!);
            }
            catch
            {
            }

            return "1.0.0";
        }

        private static string Normalize(string version) =>
            version.Trim().TrimStart('v', 'V');
    }
}
