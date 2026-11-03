using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class EmailSettingsService
    {
        private readonly string _filePath;
        private static readonly JsonSerializerOptions _writeOpts = new() { WriteIndented = true };
        private static readonly JsonSerializerOptions _readOpts = new() { PropertyNameCaseInsensitive = true };

        public EmailSettingsService()
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProWeighSuiteFusion");
            if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
            _filePath = Path.Combine(appData, "email-settings.json");
        }

        public async Task<EmailSettings> LoadAsync()
        {
            if (!File.Exists(_filePath))
            {
                return new EmailSettings();
            }

            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<EmailSettings>(json, _readOpts) ?? new EmailSettings();
        }

        public async Task SaveAsync(EmailSettings settings)
        {
            EntityTextNormalizer.Trim(settings);
            var json = JsonSerializer.Serialize(settings, _writeOpts);
            await File.WriteAllTextAsync(_filePath, json);
        }

        public string GetSettingsFilePath() => _filePath;
    }
}
