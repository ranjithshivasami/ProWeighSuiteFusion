using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Loads and saves centralized application settings to a local JSON file
    /// (%APPDATA%\ProWeighSuitePlatforms\appsettings.json).
    ///
    /// Missing or corrupt files fall back to default values so the app never
    /// fails to start because of a bad settings file.
    /// </summary>
    public class AppSettingsService
    {
        private readonly string _filePath;
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public AppSettingsService()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuitePlatforms");

            if (!Directory.Exists(appData))
                Directory.CreateDirectory(appData);

            _filePath = Path.Combine(appData, "appsettings.json");
        }

        /// <summary>
        /// Synchronous settings load for use on the UI thread (e.g. constructing
        /// the App Settings view model). Uses plain File.ReadAllText so there is
        /// no async continuation that has to marshal back to a blocked UI thread
        /// (async-over-sync deadlock risk when a view model is built during
        /// navigation).
        /// </summary>
        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new AppSettings();

                var json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                    return new AppSettings();

                var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                return settings ?? new AppSettings();
            }
            catch
            {
                // Missing or corrupt file: fall back to defaults.
                return new AppSettings();
            }
        }

        public Task<AppSettings> LoadAsync() => Task.FromResult(Load());

        public async Task SaveAsync(AppSettings settings)
        {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            // Atomic write: a crash/power-loss mid-save must never leave a
            // truncated appsettings.json (which would fall back to defaults
            // e.g. Orange theme on next startup). Write temp + replace.
            var tmp = _filePath + ".tmp";
            await File.WriteAllTextAsync(tmp, json);
            File.Move(tmp, _filePath, overwrite: true);
        }

        public string GetSettingsFilePath() => _filePath;
    }
}
