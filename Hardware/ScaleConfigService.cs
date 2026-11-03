using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Service to manage scale settings stored in a JSON configuration file
    /// </summary>
    public class ScaleConfigService
    {
        private readonly string _configFilePath;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };

        public ScaleConfigService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuiteFusion");

            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            _configFilePath = Path.Combine(appDataPath, "scale-settings.json");
        }

        /// <summary>
        /// Load scale settings from configuration file
        /// </summary>
        public async Task<ScaleSettings?> LoadSettingsAsync()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    // Return default settings if file doesn't exist
                    return new ScaleSettings
                    {
                        ScaleName = "Scale 1",
                        PortName = "COM1",
                        BaudRate = 9600,
                        DataBits = 8,
                        Parity = 0, // None
                        StopBits = 1, // One
                        Mode = "Continuous",
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    };
                }

                var json = await File.ReadAllTextAsync(_configFilePath);
                return JsonSerializer.Deserialize<ScaleSettings>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load scale settings: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Save scale settings to configuration file
        /// </summary>
        public async Task SaveSettingsAsync(ScaleSettings settings)
        {
            try
            {
                EntityTextNormalizer.Trim(settings);
                var json = JsonSerializer.Serialize(settings, _jsonOptions);
                await File.WriteAllTextAsync(_configFilePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save scale settings: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Check if configuration file exists
        /// </summary>
        public bool ConfigFileExists()
        {
            return File.Exists(_configFilePath);
        }

        /// <summary>
        /// Get the configuration file path
        /// </summary>
        public string GetConfigFilePath()
        {
            return _configFilePath;
        }
    }
}
