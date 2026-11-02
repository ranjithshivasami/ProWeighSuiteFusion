using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Linq;
using System.Windows;

namespace ProWeighSuitePlatforms.Data
{
    public class BackupSettingsRepository
    {
        private readonly string _connectionString;

        public BackupSettingsRepository(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        public async Task InitializeAsync()
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS backup_settings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                enabled INTEGER NOT NULL DEFAULT 0,
                backup_folder TEXT NOT NULL DEFAULT '',
                schedule_type TEXT NOT NULL DEFAULT 'Manual',
                interval INTEGER NOT NULL DEFAULT 1,
                time_of_day TEXT NOT NULL DEFAULT '02:00',
                retention_count INTEGER NOT NULL DEFAULT 7,
                last_backup_at TEXT,
                system_notification_enabled INTEGER NOT NULL DEFAULT 1,
                cloud_backup_enabled INTEGER NOT NULL DEFAULT 0,
                cloud_backup_folder TEXT NOT NULL DEFAULT ''
            );";
            await cmd.ExecuteNonQueryAsync();

            // Determine existing columns once
            var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            cmd.CommandText = "PRAGMA table_info(backup_settings)";
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    existingCols.Add(reader.GetString(1));
                }
            }

            // Columns we expect (new ones to migrate)
            var desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "system_notification_enabled", "INTEGER NOT NULL DEFAULT 1" },
                { "cloud_backup_enabled", "INTEGER NOT NULL DEFAULT 0" },
                { "cloud_backup_folder", "TEXT NOT NULL DEFAULT ''" }
            };

            var missing = desired.Keys.Where(k => !existingCols.Contains(k)).ToList();
            List<string> addedColumns = new List<string>();

            if (missing.Count > 0)
            {
                // Try to read configured backup folder for log location
                string configuredBackupFolder = string.Empty;
                try
                {
                    var readCmd = conn.CreateCommand();
                    readCmd.CommandText = "SELECT backup_folder FROM backup_settings LIMIT 1";
                    var res = await readCmd.ExecuteScalarAsync();
                    if (res != null)
                        configuredBackupFolder = res.ToString() ?? string.Empty;
                }
                catch { }

                var targetDir = !string.IsNullOrWhiteSpace(configuredBackupFolder)
                    ? configuredBackupFolder
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ProWeightSuiteBackups");

                try
                {
                    if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                }
                catch { targetDir = Path.GetDirectoryName(GetDataSourcePath(_connectionString)) ?? "."; }

                // Create a safety backup of the DB before altering schema
                string createdBackupPath = string.Empty;
                try
                {
                    var dbPath = GetDataSourcePath(_connectionString);
                    if (!string.IsNullOrEmpty(dbPath) && File.Exists(dbPath))
                    {
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        var backupName = Path.Combine(targetDir, Path.GetFileName(dbPath) + $".pre_migration_{timestamp}.bak");
                        File.Copy(dbPath, backupName, overwrite: false);
                        createdBackupPath = backupName;

                        try
                        {
                            var logFile = Path.Combine(targetDir, "backup.log");
                            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Migration backup created: {backupName}{Environment.NewLine}");
                        }
                        catch { }
                    }
                }
                catch { }

                // Add missing columns
                foreach (var col in missing)
                {
                    try
                    {
                        var def = desired[col];
                        cmd.CommandText = $"ALTER TABLE backup_settings ADD COLUMN {col} {def}";
                        await cmd.ExecuteNonQueryAsync();
                        addedColumns.Add(col);

                        try
                        {
                            var logFile = Path.Combine(targetDir, "backup.log");
                            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Added column: {col}{Environment.NewLine}");
                        }
                        catch { }
                    }
                    catch { }
                }

                // Show modal dialog with migration details
                if (addedColumns.Count > 0)
                {
                    try
                    {
                        var message = $"Database migration completed. Added {addedColumns.Count} column(s):\n\n{string.Join(", ", addedColumns)}\n\n" +
                                      (string.IsNullOrEmpty(createdBackupPath) ? string.Empty : $"A pre-migration DB backup was created at:\n{createdBackupPath}\n\n") +
                                      "You can view backup.log in the backup folder for details.";

                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            MessageBox.Show(message, "Database Migration", MessageBoxButton.OK, MessageBoxImage.Information);
                        });
                    }
                    catch { }
                }
            }
        }

        private string GetDataSourcePath(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return string.Empty;
            var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                var kv = p.Split('=', 2);
                if (kv.Length == 2 && kv[0].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase))
                {
                    return kv[1].Trim();
                }
            }
            const string prefix = "Data Source=";
            if (connectionString.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var v = connectionString.Substring(prefix.Length);
                var idx = v.IndexOf(';');
                return idx >= 0 ? v.Substring(0, idx) : v;
            }
            return string.Empty;
        }

        public async Task<BackupSettings?> GetAsync()
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT id, enabled, backup_folder, schedule_type, interval, time_of_day, 
                                       retention_count, last_backup_at, system_notification_enabled, 
                                       cloud_backup_enabled, cloud_backup_folder 
                                FROM backup_settings LIMIT 1";
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new BackupSettings
                {
                    Id = reader.GetInt32(0),
                    Enabled = reader.GetInt32(1) == 1,
                    BackupFolder = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    ScheduleType = reader.IsDBNull(3) ? "Manual" : reader.GetString(3),
                    Interval = reader.IsDBNull(4) ? 1 : reader.GetInt32(4),
                    TimeOfDay = reader.IsDBNull(5) ? "02:00" : reader.GetString(5),
                    RetentionCount = reader.IsDBNull(6) ? 7 : reader.GetInt32(6),
                    LastBackupAt = reader.IsDBNull(7) ? (DateTime?)null : DateTime.Parse(reader.GetString(7)),
                    SystemNotificationEnabled = reader.IsDBNull(8) ? true : reader.GetInt32(8) == 1,
                    CloudBackupEnabled = reader.IsDBNull(9) ? false : reader.GetInt32(9) == 1,
                    CloudBackupFolder = reader.IsDBNull(10) ? string.Empty : reader.GetString(10)
                };
            }
            return null;
        }

        public async Task<int> SaveAsync(BackupSettings settings)
        {
            EntityTextNormalizer.Trim(settings);
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var existing = await GetAsync();
            var cmd = conn.CreateCommand();

            var lastBackupValue = settings.LastBackupAt.HasValue
                ? (object)settings.LastBackupAt.Value.ToString("yyyy-MM-dd HH:mm:ss")
                : DBNull.Value;

            if (existing == null)
            {
                cmd.CommandText = @"
                INSERT INTO backup_settings (enabled, backup_folder, schedule_type, interval, time_of_day, 
                                             retention_count, last_backup_at, system_notification_enabled,
                                             cloud_backup_enabled, cloud_backup_folder)
                VALUES ($enabled, $folder, $schedule, $interval, $timeOfDay, $retain, $last, 
                        $sysNotify, $cloudEnabled, $cloudFolder);
                SELECT last_insert_rowid();";

                cmd.Parameters.AddWithValue("$enabled", settings.Enabled ? 1 : 0);
                cmd.Parameters.AddWithValue("$folder", settings.BackupFolder ?? string.Empty);
                cmd.Parameters.AddWithValue("$schedule", settings.ScheduleType ?? "Manual");
                cmd.Parameters.AddWithValue("$interval", settings.Interval);
                cmd.Parameters.AddWithValue("$timeOfDay", settings.TimeOfDay ?? "02:00");
                cmd.Parameters.AddWithValue("$retain", settings.RetentionCount);
                cmd.Parameters.AddWithValue("$last", lastBackupValue);
                cmd.Parameters.AddWithValue("$sysNotify", settings.SystemNotificationEnabled ? 1 : 0);
                cmd.Parameters.AddWithValue("$cloudEnabled", settings.CloudBackupEnabled ? 1 : 0);
                cmd.Parameters.AddWithValue("$cloudFolder", settings.CloudBackupFolder ?? string.Empty);

                var res = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(res);
            }
            else
            {
                cmd.CommandText = @"
                UPDATE backup_settings SET
                    enabled = $enabled,
                    backup_folder = $folder,
                    schedule_type = $schedule,
                    interval = $interval,
                    time_of_day = $timeOfDay,
                    retention_count = $retain,
                    last_backup_at = $last,
                    system_notification_enabled = $sysNotify,
                    cloud_backup_enabled = $cloudEnabled,
                    cloud_backup_folder = $cloudFolder
                WHERE id = $id";

                cmd.Parameters.AddWithValue("$id", existing.Id);
                cmd.Parameters.AddWithValue("$enabled", settings.Enabled ? 1 : 0);
                cmd.Parameters.AddWithValue("$folder", settings.BackupFolder ?? string.Empty);
                cmd.Parameters.AddWithValue("$schedule", settings.ScheduleType ?? "Manual");
                cmd.Parameters.AddWithValue("$interval", settings.Interval);
                cmd.Parameters.AddWithValue("$timeOfDay", settings.TimeOfDay ?? "02:00");
                cmd.Parameters.AddWithValue("$retain", settings.RetentionCount);
                cmd.Parameters.AddWithValue("$last", lastBackupValue);
                cmd.Parameters.AddWithValue("$sysNotify", settings.SystemNotificationEnabled ? 1 : 0);
                cmd.Parameters.AddWithValue("$cloudEnabled", settings.CloudBackupEnabled ? 1 : 0);
                cmd.Parameters.AddWithValue("$cloudFolder", settings.CloudBackupFolder ?? string.Empty);

                return await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}