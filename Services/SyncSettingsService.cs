using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class SyncSettingsService
    {
        private readonly string _filePath;
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        public SyncSettingsService()
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProWeighSuitePlatforms");
            if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
            _filePath = Path.Combine(appData, "sync-settings.json");
        }

        /// <summary>
        /// Synchronous load for use on the UI thread (App Settings page). Mirrors
        /// LoadAsync but uses plain File.ReadAllText so there is no async
        /// continuation marshaling back to a blocked UI thread.
        /// </summary>
        public SyncSettings Load()
        {
            if (!File.Exists(_filePath))
            {
                // Default to localhost/api which matches the PHP API setup
                var defaults = new SyncSettings
                {
                    ApiBaseUrl = "http://localhost/api",
                    RetryCount = 3,
                    RetryDelaySeconds = 5
                };
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<SyncSettings>(json)
                ?? new SyncSettings { ApiBaseUrl = "http://localhost/api" };
        }

        public Task<SyncSettings> LoadAsync() => Task.FromResult(Load());

        public async Task SaveAsync(SyncSettings settings)
        {
            Save(settings);
            await Task.CompletedTask;
        }

        private void Save(SyncSettings settings)
        {
            EntityTextNormalizer.Trim(settings);
            var json = JsonSerializer.Serialize(settings, _opts);
            File.WriteAllText(_filePath, json);
        }

        /// <summary>
        /// Get the full path to the sync settings file for debugging/troubleshooting
        /// </summary>
        public string GetSettingsFilePath() => _filePath;
    }
}