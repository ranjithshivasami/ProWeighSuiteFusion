using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class BackupSettingsViewModel : BaseViewModel
    {
        public string Title => "Backup Settings";

        private readonly BackupSettingsRepository _repo;
        private readonly BackupService _service;
        private readonly AuditService _auditService;

        public BackupSettings Settings { get; private set; }

        public ICommand SaveCommand { get; }
        public ICommand RunBackupNowCommand { get; }
        public ICommand BrowseFolderCommand { get; }
        public ICommand BrowseCloudFolderCommand { get; }
        public ICommand ViewStatusCommand { get; }

        // Inline validation message properties
        public string BackupFolderError
        {
            get => _backupFolderError;
            private set => SetProperty(ref _backupFolderError, value);
        }
        private string _backupFolderError = string.Empty;

        public string CloudFolderError
        {
            get => _cloudFolderError;
            private set => SetProperty(ref _cloudFolderError, value);
        }
        private string _cloudFolderError = string.Empty;

        // String-backed fields to avoid binding exceptions when the view uses textboxes
        public string IntervalText
        {
            get => _intervalText;
            set => SetProperty(ref _intervalText, value);
        }
        private string _intervalText = "1";

        public string RetentionText
        {
            get => _retentionText;
            set => SetProperty(ref _retentionText, value);
        }
        private string _retentionText = "7";

        // Errors for the string-backed fields
        public string IntervalError
        {
            get => _intervalError;
            private set => SetProperty(ref _intervalError, value);
        }
        private string _intervalError = string.Empty;

        public string RetentionError
        {
            get => _retentionError;
            private set => SetProperty(ref _retentionError, value);
        }
        private string _retentionError = string.Empty;

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }
        private string _statusMessage = string.Empty;

        public string OneDriveStatus
        {
            get => _oneDriveStatus;
            private set => SetProperty(ref _oneDriveStatus, value);
        }
        private string _oneDriveStatus = string.Empty;

        public bool IsInitialized
        {
            get => _isInitialized;
            private set
            {
                if (SetProperty(ref _isInitialized, value))
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }
        private bool _isInitialized = false;

        public BackupSettingsViewModel(BackupService service)
        {
            var dbPath = AppConfigurationManager.GetDatabasePath();
            _repo = new BackupSettingsRepository(dbPath);
            _service = service;
            _auditService = new AuditService();

            Settings = new BackupSettings();

            SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => true);
            RunBackupNowCommand = new RelayCommand(async _ => await RunNowAsync(), _ => true);
            BrowseFolderCommand = new RelayCommand(_ => BrowseFolder(isCloud: false));
            BrowseCloudFolderCommand = new RelayCommand(_ => BrowseFolder(isCloud: true));
            ViewStatusCommand = new RelayCommand(_ => ViewStatus());

            DetectOneDrive();
            _ = LoadAsync();
        }

        private void DetectOneDrive()
        {
            var path = BackupService.DetectOneDrivePath();
            if (path != null)
                OneDriveStatus = $"OneDrive detected: {path}";
            else
                OneDriveStatus = "OneDrive not detected. You can use any network share or synced folder.";
        }

        private async Task LoadAsync()
        {
            await _repo.InitializeAsync();
            var cfg = await _repo.GetAsync();
            if (cfg != null) Settings = cfg;
            else Settings = await _service.GetOrCreateDefaultAsync();

            IntervalText = Settings.Interval.ToString();
            RetentionText = Settings.RetentionCount.ToString();

            RaisePropertyChanged(string.Empty);
            IsInitialized = true;
        }

        private void BrowseFolder(bool isCloud)
        {
            var currentPath = isCloud ? Settings.CloudBackupFolder : Settings.BackupFolder;

            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    global::System.Windows.Forms.Application.EnableVisualStyles();
                    using (var dlg = new global::System.Windows.Forms.FolderBrowserDialog()
                    {
                        Description = isCloud ? "Select cloud/network backup folder" : "Select local backup folder",
                        UseDescriptionForTitle = true,
                        SelectedPath = string.IsNullOrEmpty(currentPath)
                            ? (isCloud ? (BackupService.DetectOneDrivePath() ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
                                       : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
                            : currentPath
                    })
                    {
                        var res = dlg.ShowDialog();
                        if (res == global::System.Windows.Forms.DialogResult.OK)
                        {
                            var selected = dlg.SelectedPath;
                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                if (isCloud)
                                {
                                    Settings.CloudBackupFolder = selected;
                                    CloudFolderError = string.Empty;
                                }
                                else
                                {
                                    Settings.BackupFolder = selected;
                                    BackupFolderError = string.Empty;
                                }
                                RaisePropertyChanged(string.Empty);
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                        MessageBox.Show($"Failed to open folder browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
            });

            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        private void ClearValidationErrors()
        {
            BackupFolderError = string.Empty;
            CloudFolderError = string.Empty;
            StatusMessage = string.Empty;
            IntervalError = string.Empty;
            RetentionError = string.Empty;
        }

        private bool Validate()
        {
            ClearValidationErrors();
            bool ok = true;

            if (Settings.Enabled)
            {
                if (string.IsNullOrWhiteSpace(Settings.BackupFolder))
                {
                    BackupFolderError = "Backup folder cannot be empty when backups are enabled.";
                    ok = false;
                }
                else
                {
                    try
                    {
                        if (!Directory.Exists(Settings.BackupFolder))
                        {
                            Directory.CreateDirectory(Settings.BackupFolder);
                        }
                        var testFile = Path.Combine(Settings.BackupFolder, ".test" + Guid.NewGuid().ToString("N"));
                        File.WriteAllText(testFile, "test");
                        File.Delete(testFile);
                    }
                    catch (Exception ex)
                    {
                        BackupFolderError = "Cannot write to backup folder: " + ex.Message;
                        ok = false;
                    }
                }
            }

            // Validate cloud folder if cloud backup is enabled
            if (Settings.CloudBackupEnabled)
            {
                if (string.IsNullOrWhiteSpace(Settings.CloudBackupFolder))
                {
                    CloudFolderError = "Cloud backup folder cannot be empty when cloud backup is enabled.";
                    ok = false;
                }
                else
                {
                    try
                    {
                        var machineFolder = Path.Combine(Settings.CloudBackupFolder, Environment.MachineName);
                        if (!Directory.Exists(machineFolder))
                        {
                            Directory.CreateDirectory(machineFolder);
                        }
                        var testFile = Path.Combine(machineFolder, ".test" + Guid.NewGuid().ToString("N"));
                        File.WriteAllText(testFile, "test");
                        File.Delete(testFile);
                    }
                    catch (Exception ex)
                    {
                        CloudFolderError = "Cannot write to cloud folder: " + ex.Message;
                        ok = false;
                    }
                }
            }

            // Validate interval
            if (!int.TryParse(IntervalText, out var intervalVal) || intervalVal <= 0)
            {
                IntervalError = "Interval must be a positive whole number.";
                ok = false;
            }
            else
            {
                Settings.Interval = intervalVal;
            }

            // Validate retention
            if (!int.TryParse(RetentionText, out var retentionVal) || retentionVal < 0)
            {
                RetentionError = "Retention count must be a non-negative whole number.";
                ok = false;
            }
            else
            {
                Settings.RetentionCount = retentionVal;
            }

            if (!ok)
            {
                StatusMessage = "Please correct the fields marked with errors.";
            }

            return ok;
        }

        private async Task SaveAsync()
        {
            if (!Validate()) return;

            StatusMessage = "Saving backup settings...";
            try { NotificationService.ShowInfo("Backup settings", "Saving backup settings..."); } catch { }

            try
            {
                await _repo.SaveAsync(Settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackupSettingsViewModel] Error saving settings: {ex.Message}");
                try { NotificationService.ShowError("Backup settings", $"Failed to save: {ex.Message}"); } catch { }
                MessageBox.Show($"Failed to save backup settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Failed to save settings.";
                return;
            }

            try
            {
                _service.Schedule(Settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackupSettingsViewModel] Error scheduling backup: {ex.Message}");
                MessageBox.Show($"Settings saved but failed to schedule backups: {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // Log settings change
            await _auditService.LogSettingsChangedAsync(
                "System",
                "Backup Settings",
                null,
                $"Enabled={Settings.Enabled}, Folder={Settings.BackupFolder}, Interval={Settings.Interval}, Retention={Settings.RetentionCount}, CloudEnabled={Settings.CloudBackupEnabled}");

            try { NotificationService.ShowInfo("Backup settings", "Backup settings saved"); } catch { }

            StatusMessage = "Settings saved.";
            RaisePropertyChanged(string.Empty);
        }

        private async Task RunNowAsync()
        {
            if (!Settings.Enabled)
            {
                MessageBox.Show("Backups are disabled. Enable backups to run now.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(Settings.BackupFolder))
            {
                MessageBox.Show("Please select a backup folder before running backup.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusMessage = "Running backup...";
            try { NotificationService.ShowInfo("Backup", "Backup started"); } catch { }

            try
            {
                var success = await _service.DoBackupAsync(Settings);
                if (success)
                {
                    await _auditService.LogDatabaseBackupAsync("System", Settings.BackupFolder, true);
                    NotificationService.ShowInfo("Backup", "Backup completed successfully.");
                    StatusMessage = "Backup completed successfully.";
                }
                else
                {
                    await _auditService.LogDatabaseBackupAsync("System", Settings.BackupFolder, false);
                    NotificationService.ShowError("Backup", "Backup failed. Check log for details.");
                    StatusMessage = "Backup failed. Check log for details.";
                }
            }
            catch (Exception ex)
            {
                await _auditService.LogDatabaseBackupAsync("System", Settings.BackupFolder, false);
                System.Diagnostics.Debug.WriteLine($"[BackupSettingsViewModel] Error running backup: {ex.Message}");
                try { NotificationService.ShowError("Backup", $"Failed to run backup: {ex.Message}"); } catch { }
                MessageBox.Show($"Failed to run backup: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Failed to run backup.";
            }
            RaisePropertyChanged(string.Empty);
        }

        private void ViewStatus()
        {
            try
            {
                var status = _service.GetBackupStatus();
                var statusWindow = new Window
                {
                    Title = "Backup Service Status",
                    Width = 600,
                    Height = 450,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Application.Current.MainWindow,
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            Text = status,
                            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                            FontSize = 12,
                            Margin = new Thickness(15),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                };
                statusWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error viewing status: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
