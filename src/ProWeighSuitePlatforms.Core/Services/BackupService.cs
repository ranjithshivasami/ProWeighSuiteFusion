using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public class BackupService : IDisposable
    {
        private readonly string _dbPath;
        private readonly BackupSettingsRepository _repo;
        private Timer? _timer;

        public BackupService()
        {
            _dbPath = AppConfigurationManager.GetDatabasePath();
            _repo = new BackupSettingsRepository(_dbPath);
        }

        public async Task InitializeAsync()
        {
            await _repo.InitializeAsync();
            var cfg = await _repo.GetAsync();

            if (cfg == null)
            {
                System.Diagnostics.Debug.WriteLine("[BackupService] No backup configuration found - backups not scheduled");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[BackupService] Loaded config: Enabled={cfg.Enabled}, Type={cfg.ScheduleType}, Folder={cfg.BackupFolder}");

            if (cfg != null && cfg.Enabled)
            {
                Schedule(cfg);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[BackupService] Backups are disabled - not scheduling");
            }
        }

        public void Schedule(BackupSettings cfg)
        {
            _timer?.Dispose();
            if (!cfg.Enabled)
            {
                System.Diagnostics.Debug.WriteLine("[BackupService] Backup scheduling disabled (Enabled=false)");
                return;
            }

            TimeSpan due;
            TimeSpan period;

            switch (cfg.ScheduleType)
            {
                case "Minutes":
                    // For sub-hour intervals (e.g., every 15 minutes)
                    due = TimeSpan.FromMinutes(cfg.Interval);
                    period = due;
                    break;
                case "Hourly":
                    due = TimeSpan.FromHours(cfg.Interval);
                    period = due;
                    break;
                case "Daily":
                    if (TimeSpan.TryParse(cfg.TimeOfDay, out var dailyTime))
                    {
                        var now = DateTime.Now;
                        var next = new DateTime(now.Year, now.Month, now.Day, dailyTime.Hours, dailyTime.Minutes, 0);
                        if (next <= now) next = next.AddDays(1);
                        due = next - now;
                    }
                    else
                    {
                        due = TimeSpan.FromHours(24);
                    }
                    period = TimeSpan.FromDays(cfg.Interval);
                    break;
                case "Weekly":
                    if (TimeSpan.TryParse(cfg.TimeOfDay, out var weeklyTime))
                    {
                        var now = DateTime.Now;
                        var next = new DateTime(now.Year, now.Month, now.Day, weeklyTime.Hours, weeklyTime.Minutes, 0);
                        if (next <= now) next = next.AddDays(7);
                        due = next - now;
                    }
                    else
                    {
                        due = TimeSpan.FromDays(7);
                    }
                    period = TimeSpan.FromDays(7 * cfg.Interval);
                    break;
                default: // Manual
                    System.Diagnostics.Debug.WriteLine("[BackupService] Backup scheduling set to Manual - no timer scheduled");
                    return;
            }

            System.Diagnostics.Debug.WriteLine($"[BackupService] Scheduling backup: Type={cfg.ScheduleType}, First backup in {due.TotalMinutes:F1} minutes, Then every {period.TotalMinutes:F1} minutes");
            _timer = new Timer(async _ => await DoBackupAsync(cfg), null, due, period);
            System.Diagnostics.Debug.WriteLine("[BackupService] Timer created and started successfully");
        }

        public async Task<bool> DoBackupAsync(BackupSettings cfg)
        {
            System.Diagnostics.Debug.WriteLine($"[BackupService] ======== BACKUP STARTED at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ========");
            try
            {
                var targetDir = cfg.BackupFolder;
                if (string.IsNullOrEmpty(targetDir))
                {
                    System.Diagnostics.Debug.WriteLine("[BackupService] ERROR: Backup folder is empty");
                    return false;
                }
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = Path.GetFileName(_dbPath);
                var baseName = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName);
                var backupFile = Path.Combine(targetDir, $"{baseName}_{timestamp}{ext}");

                File.Copy(_dbPath, backupFile, overwrite: false);

                // Cleanup old local backups
                CleanupBackups(targetDir, baseName, cfg.RetentionCount);

                // Write local log
                WriteLog(targetDir, $"SUCCESS: Local backup copied to {backupFile}");
                System.Diagnostics.Debug.WriteLine($"[BackupService] SUCCESS: Local backup copied to {backupFile}");

                // Cloud backup: copy to cloud folder with machine-name subfolder
                string? cloudBackupFile = null;
                if (cfg.CloudBackupEnabled && !string.IsNullOrWhiteSpace(cfg.CloudBackupFolder))
                {
                    try
                    {
                        var machineFolder = Path.Combine(cfg.CloudBackupFolder, Environment.MachineName);
                        if (!Directory.Exists(machineFolder)) Directory.CreateDirectory(machineFolder);

                        cloudBackupFile = Path.Combine(machineFolder, $"{baseName}_{timestamp}{ext}");
                        File.Copy(_dbPath, cloudBackupFile, overwrite: false);

                        // Cleanup old cloud backups for this machine
                        CleanupBackups(machineFolder, baseName, cfg.RetentionCount);

                        WriteLog(targetDir, $"SUCCESS: Cloud backup copied to {cloudBackupFile}");
                        WriteLog(machineFolder, $"SUCCESS: Backup copied to {cloudBackupFile}");
                        System.Diagnostics.Debug.WriteLine($"[BackupService] SUCCESS: Cloud backup copied to {cloudBackupFile}");
                    }
                    catch (Exception cloudEx)
                    {
                        WriteLog(targetDir, $"CLOUD FAIL: {cloudEx.Message}");
                        System.Diagnostics.Debug.WriteLine($"[BackupService] CLOUD FAIL: {cloudEx.Message}");

                        if (cfg.SystemNotificationEnabled)
                        {
                            try { NotificationService.ShowError("Cloud Backup Failed", cloudEx.Message); } catch { }
                        }
                    }
                }

                // Update last backup time
                cfg.LastBackupAt = DateTime.Now;
                await _repo.SaveAsync(cfg);

                if (cfg.SystemNotificationEnabled)
                {
                    var msg = $"Local: {backupFile}";
                    if (cloudBackupFile != null) msg += $"\nCloud: {cloudBackupFile}";
                    NotificationService.ShowInfo("Backup Completed", msg);
                }

                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    var targetDir = cfg?.BackupFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ProWeightSuiteBackups");
                    if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                    WriteLog(targetDir, $"FAIL: {ex.Message}");

                    if (cfg != null && cfg.SystemNotificationEnabled)
                    {
                        try { NotificationService.ShowError("Backup Failed", ex.Message); } catch { }
                    }
                }
                catch { }

                System.Diagnostics.Debug.WriteLine($"[BackupService] FAIL: {ex.Message}");
                return false;
            }
        }

        private void WriteLog(string folder, string message)
        {
            try
            {
                var logFile = Path.Combine(folder, "backup.log");
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(logFile, line);
            }
            catch { }
        }

        private void CleanupBackups(string folder, string baseName, int retention)
        {
            try
            {
                var files = Directory.GetFiles(folder)
                    .Where(f => Path.GetFileName(f).StartsWith(baseName + "_"))
                    .OrderByDescending(f => f)
                    .ToList();

                for (int i = retention; i < files.Count; i++)
                {
                    try { File.Delete(files[i]); } catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Detects the OneDrive folder path from environment variable.
        /// Returns null if OneDrive is not configured.
        /// </summary>
        public static string? DetectOneDrivePath()
        {
            var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrWhiteSpace(oneDrive) && Directory.Exists(oneDrive))
                return oneDrive;
            return null;
        }

        /// <summary>
        /// Returns a suggested cloud backup folder path using OneDrive with machine subfolder.
        /// </summary>
        public static string GetDefaultCloudBackupFolder()
        {
            var oneDrive = DetectOneDrivePath();
            if (oneDrive != null)
                return Path.Combine(oneDrive, "ProWeightSuiteBackups");

            return string.Empty;
        }

        public async Task<BackupSettings> GetOrCreateDefaultAsync()
        {
            var existing = await _repo.GetAsync();
            if (existing != null) return existing;

            var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ProWeightSuiteBackups");
            var cfg = new BackupSettings
            {
                Enabled = false,
                BackupFolder = defaultFolder,
                ScheduleType = "Daily",
                Interval = 1,
                TimeOfDay = "02:00",
                RetentionCount = 7,
                CloudBackupEnabled = false,
                CloudBackupFolder = GetDefaultCloudBackupFolder()
            };

            await _repo.SaveAsync(cfg);
            return cfg;
        }

        /// <summary>
        /// Get the current backup service status for troubleshooting
        /// </summary>
        public string GetBackupStatus()
        {
            var status = new System.Text.StringBuilder();
            status.AppendLine("=== Backup Service Status ===");
            status.AppendLine($"Timer Active: {(_timer != null ? "Yes" : "No")}");

            try
            {
                var cfg = _repo.GetAsync().GetAwaiter().GetResult();
                if (cfg != null)
                {
                    status.AppendLine($"Enabled: {cfg.Enabled}");
                    status.AppendLine($"Schedule Type: {cfg.ScheduleType}");
                    status.AppendLine($"Backup Folder: {cfg.BackupFolder}");
                    status.AppendLine($"Interval: {cfg.Interval}");
                    status.AppendLine($"Time of Day: {cfg.TimeOfDay}");
                    status.AppendLine($"Last Backup: {(cfg.LastBackupAt.HasValue ? cfg.LastBackupAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "Never")}");
                    status.AppendLine($"Cloud Backup: {(cfg.CloudBackupEnabled ? "Enabled" : "Disabled")}");
                    if (cfg.CloudBackupEnabled)
                    {
                        status.AppendLine($"Cloud Folder: {cfg.CloudBackupFolder}");
                    }
                }
                else
                {
                    status.AppendLine("Configuration: Not found");
                }
            }
            catch (Exception ex)
            {
                status.AppendLine($"Error loading config: {ex.Message}");
            }

            return status.ToString();
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
