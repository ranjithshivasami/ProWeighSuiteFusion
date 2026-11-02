// In-process master switch for background sync. Seeded from SyncSettings.Enabled
// at startup (App.xaml.cs) and updated live when the App Settings page toggles
// "Enable Sync Setting". Both LiteQueueSyncService (transactions) and
// OfflineAuditSyncService (audit) check this before uploading.
using System;

namespace ProWeighSuitePlatforms.Services
{
    public static class SyncGate
    {
        private static volatile bool _enabled = true;

        /// <summary>
        /// True when background sync is allowed. When false, sync services pause
        /// uploads but keep queueing locally (items stay Pending).
        /// </summary>
        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                Changed?.Invoke(null, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Raised on the thread that changes <see cref="Enabled"/> so shell UI
        /// (sync counters, sync menus) can show/hide live.
        /// </summary>
        public static event EventHandler? Changed;
    }
}
