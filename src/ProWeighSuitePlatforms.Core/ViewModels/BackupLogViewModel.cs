using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Collections.ObjectModel;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class BackupLogViewModel : BaseViewModel
    {
        public ObservableCollection<string> Lines { get; } = new ObservableCollection<string>();

        public ICommand RefreshCommand { get; }

        public BackupLogViewModel()
        {
            RefreshCommand = new RelayCommand(async _ => await LoadAsync());
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            Lines.Clear();
            try
            {
                var dbPath = AppConfigurationManager.GetDatabasePath();

                // Try to find configured backup folder from settings
                string? configuredFolder = null;
                try
                {
                    var repo = new BackupSettingsRepository(dbPath);
                    await repo.InitializeAsync();
                    var settings = await repo.GetAsync();
                    if (settings != null && !string.IsNullOrWhiteSpace(settings.BackupFolder))
                        configuredFolder = settings.BackupFolder;
                }
                catch
                {
                    // ignore failures reading settings
                }

                var candidates = new[]
                {
                    configuredFolder,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ProWeightSuiteBackups"),
                    Path.GetDirectoryName(dbPath),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                string? foundLog = null;
                foreach (var c in candidates)
                {
                    if (string.IsNullOrWhiteSpace(c)) continue;
                    var p = Path.Combine(c, "backup.log");
                    if (File.Exists(p))
                    {
                        foundLog = p;
                        break;
                    }
                }

                // As a last resort, try to find any backup.log under user's documents (non-recursive) or DB folder
                if (foundLog == null)
                {
                    foreach (var c in new[] { Path.GetDirectoryName(dbPath), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
                    {
                        if (string.IsNullOrWhiteSpace(c) || !Directory.Exists(c)) continue;
                        try
                        {
                            var files = Directory.GetFiles(c, "backup.log", SearchOption.AllDirectories);
                            if (files.Length > 0)
                            {
                                foundLog = files[0];
                                break;
                            }
                        }
                        catch { }
                    }
                }

                if (foundLog == null)
                {
                    Lines.Add("No backup.log found. Looked in:");
                    foreach (var c in candidates)
                        if (!string.IsNullOrWhiteSpace(c)) Lines.Add(" - " + c);
                    return;
                }

                var lines = await File.ReadAllLinesAsync(foundLog);
                // show most recent entries at bottom (keep original order)
                foreach (var l in lines)
                    Lines.Add(l);
            }
            catch (Exception ex)
            {
                Lines.Add("Error loading backup log: " + ex.Message);
            }
        }
    }
}