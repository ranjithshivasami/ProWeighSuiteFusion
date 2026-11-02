using System;

namespace ProWeighSuitePlatforms.Models
{
    public class BackupSettings
    {
        public int Id { get; set; }
        public bool Enabled { get; set; } = false;
        public string BackupFolder { get; set; } = string.Empty; // full path
        public string ScheduleType { get; set; } = "Manual"; // Manual, Minutes, Hourly, Daily, Weekly
        public int Interval { get; set; } = 1; // minutes for Minutes, hours for Hourly, days for Daily/Weekly
        public string TimeOfDay { get; set; } = "02:00"; // HH:mm for Daily/Weekly
        public int RetentionCount { get; set; } = 7; // number of backups to keep
        public DateTime? LastBackupAt { get; set; }

        // Notification settings
        public bool SystemNotificationEnabled { get; set; } = true;

        // Cloud backup (OneDrive / network share / synced folder)
        public bool CloudBackupEnabled { get; set; } = false;
        public string CloudBackupFolder { get; set; } = string.Empty;
    }
}