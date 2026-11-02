using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Central gateway for device integration configuration files (JSON) stored in the
    /// per-user application data folder. PLC settings today; scale/printer profiles can
    /// be added here later so all integrations resolve from one configuration source.
    /// </summary>
    public class ConfigManager
    {
        private static readonly string ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ProWeighSuitePlatforms");

        private static readonly string PlcConfigFilePath = Path.Combine(ConfigDirectory, "plc-config.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            // Serialise enums as names ("StopBits": "One") so the JSON stays hand-editable;
            // deserialisation still accepts legacy numeric values.
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Full path of the PLC configuration file (useful for diagnostics UI).</summary>
        public static string PlcConfigFile => PlcConfigFilePath;

        /// <summary>
        /// Loads the PLC configuration. Returns defaults when no file exists yet.
        /// A malformed file deliberately throws instead of silently reverting to defaults,
        /// because stale coil addresses could actuate the wrong hardware.
        /// </summary>
        public async Task<PlcConfig> LoadPlcConfigAsync()
        {
            try
            {
                if (!File.Exists(PlcConfigFilePath))
                    return PlcConfig.GetDefault();

                var json = await File.ReadAllTextAsync(PlcConfigFilePath);
                var config = JsonSerializer.Deserialize<PlcConfig>(json, JsonOptions);

                return config ?? throw new InvalidOperationException(
                    $"PLC configuration file '{PlcConfigFilePath}' is empty or has an invalid structure.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"PLC configuration file '{PlcConfigFilePath}' contains invalid JSON: {ex.Message}", ex);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to read PLC configuration from '{PlcConfigFilePath}': {ex.Message}", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException(
                    $"Access denied while reading '{PlcConfigFilePath}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Validates and saves the PLC configuration atomically:
        /// writes to a temp file first, then replaces the target so a crash mid-write
        /// can never leave a truncated/corrupt configuration on disk.
        /// </summary>
        public async Task SavePlcConfigAsync(PlcConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            var errors = config.Validate().ToList();
            if (errors.Count > 0)
                throw new InvalidOperationException(
                    "Cannot save invalid PLC configuration: " + string.Join(" ", errors));

            try
            {
                if (!Directory.Exists(ConfigDirectory))
                    Directory.CreateDirectory(ConfigDirectory);

                config.LastModified = DateTime.Now;
                config.PortName = config.PortName?.Trim() ?? string.Empty;

                var json = JsonSerializer.Serialize(config, JsonOptions);

                var tempPath = PlcConfigFilePath + ".tmp";
                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, PlcConfigFilePath, overwrite: true);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to save PLC configuration to '{PlcConfigFilePath}': {ex.Message}", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException(
                    $"Access denied while saving '{PlcConfigFilePath}': {ex.Message}", ex);
            }
        }
    }
}
