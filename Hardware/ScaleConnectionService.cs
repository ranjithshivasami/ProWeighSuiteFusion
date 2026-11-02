using System;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Single entry point for opening the saved scale connection (scale-settings.json)
    /// and keeping the shared ScaleReaderService polling. All callers share one gate so
    /// startup, Live view and batch creation never race on the same port.
    /// </summary>
    public static class ScaleConnectionService
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);

        public static async Task<bool> EnsureConnectedAsync()
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var reader = ScaleReaderService.GetShared(
                    new ScaleSettingsRepository(AppConfigurationManager.GetDatabasePath()));
                if (reader.IsPolling) return true;

                var settings = await new ScaleConfigService().LoadSettingsAsync().ConfigureAwait(false);
                if (settings == null)
                {
                    Log("No scale settings found.");
                    return false;
                }

                if (!await reader.StartReadingAsync(settings).ConfigureAwait(false))
                {
                    Log($"Connection refused/failed for {settings.ConnectionInfo}.");
                    return false;
                }

                reader.StartPolling();
                Log($"Connected: {settings.ScaleName} ({settings.ConnectionInfo}) [{settings.Mode} mode]");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Connect failed: {ex.Message}");
                return false;
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// Retries because USB/TCP scales often become available a few seconds after
        /// the app starts (driver warm-up, emulator launch, network link).
        /// </summary>
        public static async Task<bool> EnsureConnectedWithRetryAsync(int attempts = 15, int delayMs = 2000)
        {
            for (int i = 1; i <= attempts; i++)
            {
                if (await EnsureConnectedAsync().ConfigureAwait(false)) return true;
                if (i < attempts) await Task.Delay(delayMs).ConfigureAwait(false);
            }

            Log($"Gave up after {attempts} attempt(s).");
            return false;
        }

        private static void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[ScaleConnection] {message}");
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ProWeighSuitePlatforms", "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(dir, "scale.log"),
                    $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
