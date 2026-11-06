using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Idempotent migration for the email settings file
    /// (%APPDATA%\ProWeighSuiteFusion\email-settings.json).
    ///
    /// Ensures the file exists on first run with sensible defaults, and on
    /// subsequent runs fills in any missing/defaulted fields so older JSON
    /// files (missing newly-added properties) are always brought up to date.
    /// </summary>
    public class EmailSettingsMigration
    {
        private static readonly JsonSerializerOptions _writeOpts = new() { WriteIndented = true };
        private static readonly JsonSerializerOptions _readOpts = new() { PropertyNameCaseInsensitive = true };

        public string FilePath { get; }

        public EmailSettingsMigration()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuiteFusion");
            if (!Directory.Exists(appData))
                Directory.CreateDirectory(appData);
            FilePath = Path.Combine(appData, "email-settings.json");
        }

        /// <summary>
        /// Runs the email-settings migration. Safe to call on every startup.
        /// </summary>
        public async Task MigrateAsync()
        {
            EmailSettings settings;

            if (!File.Exists(FilePath))
            {
                // First run: create with defaults.
                settings = new EmailSettings();
                await SaveAsync(settings);
                System.Diagnostics.Debug.WriteLine(
                    $"[EmailSettingsMigration] Created {FilePath} with defaults");
                return;
            }

            // File exists: load and backfill any missing fields.
            try
            {
                var json = await File.ReadAllTextAsync(FilePath);
                settings = JsonSerializer.Deserialize<EmailSettings>(json, _readOpts)
                           ?? new EmailSettings();
            }
            catch
            {
                // Corrupt or unreadable JSON — reset to defaults.
                settings = new EmailSettings();
                System.Diagnostics.Debug.WriteLine(
                    $"[EmailSettingsMigration] Corrupt {FilePath}; reset to defaults");
            }

            bool changed = BackfillDefaults(settings);

            if (changed)
                await SaveAsync(settings);
        }

        /// <summary>
        /// Fills in default values for any unset/empty properties. Returns true
        /// if any value was changed.
        /// </summary>
        private static bool BackfillDefaults(EmailSettings s)
        {
            var defaults = new EmailSettings();
            bool changed = false;

            if (string.IsNullOrWhiteSpace(s.SmtpServer))
            { s.SmtpServer = defaults.SmtpServer; changed = true; }
            if (s.SmtpPort == 0)
            { s.SmtpPort = defaults.SmtpPort; changed = true; }
            if (string.IsNullOrWhiteSpace(s.SmtpUsername))
            { s.SmtpUsername = defaults.SmtpUsername; changed = true; }
            if (string.IsNullOrWhiteSpace(s.FromEmail))
            { s.FromEmail = defaults.FromEmail; changed = true; }
            if (string.IsNullOrWhiteSpace(s.FromName))
            { s.FromName = defaults.FromName; changed = true; }
            if (string.IsNullOrWhiteSpace(s.AppName))
            { s.AppName = defaults.AppName; changed = true; }

            return changed;
        }

        private async Task SaveAsync(EmailSettings settings)
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, _writeOpts);
            await File.WriteAllTextAsync(FilePath, json);
        }
    }
}
