using System;
using System.Drawing;
using System.Windows;

namespace ProWeighSuitePlatforms.Services
{
    public static class NotificationService
    {
        private static System.Windows.Forms.NotifyIcon? _notifyIcon;

        private static void EnsureIcon()
        {
            if (_notifyIcon != null) return;

            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            try
            {
                var stream = typeof(NotificationService).Assembly.GetManifestResourceStream("ProWeighSuitePlatforms.Images.tray-icon.ico");
                if (stream != null) _notifyIcon.Icon = new Icon(stream);
            }
            catch
            {
                // ignore - no icon
            }
            _notifyIcon.Visible = true;
        }

        public static void ShowInfo(string title, string message, int timeoutMs = 5000)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                try
                {
                    EnsureIcon();
                    _notifyIcon?.ShowBalloonTip(timeoutMs / 1000, title, message, System.Windows.Forms.ToolTipIcon.Info);
                }
                catch
                {
                    MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
                }
            });
        }

        public static void ShowError(string title, string message, int timeoutMs = 5000)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                try
                {
                    EnsureIcon();
                    _notifyIcon?.ShowBalloonTip(timeoutMs / 1000, title, message, System.Windows.Forms.ToolTipIcon.Error);
                }
                catch
                {
                    MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }
    }
}
