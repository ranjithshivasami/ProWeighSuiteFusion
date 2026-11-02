using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class PrinterConfigurationService
    {
        private static readonly string ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ProWeighSuitePlatforms");

        private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "printer_config.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public async Task<ZT231PrinterConfig> LoadAsync()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                    return GetDefaultConfig();

                var json = await File.ReadAllTextAsync(ConfigFilePath);
                return JsonSerializer.Deserialize<ZT231PrinterConfig>(json, JsonOptions) ?? GetDefaultConfig();
            }
            catch
            {
                return GetDefaultConfig();
            }
        }

        public async Task SaveAsync(ZT231PrinterConfig config)
        {
            if (!Directory.Exists(ConfigDirectory))
                Directory.CreateDirectory(ConfigDirectory);

            config.LastModified = DateTime.Now;
            EntityTextNormalizer.Trim(config);
            var json = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(ConfigFilePath, json);
        }

        public static ZT231PrinterConfig GetDefaultConfig()
        {
            return new ZT231PrinterConfig
            {
                ConnectionType = "USB/COM",
                ComPort = "COM1",
                BaudRate = 9600,
                DataBits = 8,
                Parity = "None",
                StopBits = "1",
                FlowControl = "None",
                IpAddress = "192.168.1.100",
                Port = 9100,
                SubnetMask = "255.255.255.0",
                Gateway = "192.168.1.1",
                LastModified = DateTime.Now
            };
        }
    }
}
