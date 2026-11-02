using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class AuditLogViewModel : BaseViewModel
    {
        private readonly AuditService _auditService;

        public ObservableCollection<AuditLogEntry> AuditLogs { get; } = new();
        public ObservableCollection<string> AvailableDates { get; } = new();

        private string _selectedDate = string.Empty;
        public string SelectedDate
        {
            get => _selectedDate;
            set
            {
                if (SetProperty(ref _selectedDate, value))
                {
                    _ = LoadAuditLogsAsync();
                }
            }
        }

        private string _filterText = string.Empty;
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    ApplyFilter();
                }
            }
        }

        private AuditCategory? _selectedCategory;
        public AuditCategory? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    ApplyFilter();
                }
            }
        }

        private AuditSeverity? _selectedSeverity;
        public AuditSeverity? SelectedSeverity
        {
            get => _selectedSeverity;
            set
            {
                if (SetProperty(ref _selectedSeverity, value))
                {
                    ApplyFilter();
                }
            }
        }

        private string _statusMessage = "Loading...";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private ObservableCollection<AuditLogEntry> _allLogs = new();

        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ClearFilterCommand { get; }
        public ICommand CloseCommand { get; }

        public AuditLogViewModel()
        {
            _auditService = new AuditService();

            RefreshCommand = new RelayCommand(async _ => await LoadAuditLogsAsync());
            ExportCommand = new RelayCommand(async _ => await ExportLogsAsync(), _ => AuditLogs.Count > 0);
            ClearFilterCommand = new RelayCommand(_ => ClearFilters());
            CloseCommand = new RelayCommand(_ => CloseWindow());

            _ = LoadAvailableDatesAsync();
        }

        private async Task LoadAvailableDatesAsync()
        {
            try
            {
                var files = _auditService.GetAllAuditLogFiles();
                
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    AvailableDates.Clear();
                    AvailableDates.Add("All Dates");
                    
                    foreach (var file in files.OrderByDescending(f => f))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var date = fileName.Replace("audit_", "");
                        
                        if (DateTime.TryParseExact(date, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var parsed))
                        {
                            AvailableDates.Add(parsed.ToString("yyyy-MM-dd"));
                        }
                    }

                    if (AvailableDates.Count > 1)
                    {
                        SelectedDate = AvailableDates[1]; // Select today
                    }
                });

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading dates: {ex.Message}";
            }
        }

        private async Task LoadAuditLogsAsync()
        {
            try
            {
                StatusMessage = "Loading audit logs...";

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    AuditLogs.Clear();
                    _allLogs.Clear();
                });

                var files = _auditService.GetAllAuditLogFiles();
                
                // Filter by selected date
                if (!string.IsNullOrEmpty(SelectedDate) && SelectedDate != "All Dates")
                {
                    var dateFilter = DateTime.Parse(SelectedDate).ToString("yyyyMMdd");
                    files = files.Where(f => f.Contains($"audit_{dateFilter}")).ToArray();
                }

                var entries = new System.Collections.Generic.List<AuditLogEntry>();

                foreach (var file in files)
                {
                    if (!File.Exists(file)) continue;

                    var lines = await File.ReadAllLinesAsync(file);
                    
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            var entry = JsonSerializer.Deserialize<AuditEntry>(line);
                            if (entry != null)
                            {
                                entries.Add(new AuditLogEntry
                                {
                                    Timestamp = entry.Timestamp.ToLocalTime(),
                                    Action = entry.Action,
                                    Category = entry.Category,
                                    Username = entry.Username,
                                    Details = entry.Details,
                                    EntityType = entry.EntityType ?? "",
                                    EntityId = entry.EntityId ?? "",
                                    Severity = entry.Severity,
                                    MachineName = entry.MachineName
                                });
                            }
                        }
                        catch
                        {
                            // Skip invalid JSON lines
                        }
                    }
                }

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    foreach (var entry in entries.OrderByDescending(e => e.Timestamp))
                    {
                        _allLogs.Add(entry);
                        AuditLogs.Add(entry);
                    }
                });

                StatusMessage = $"Loaded {AuditLogs.Count} audit log entries";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading logs: {ex.Message}";
                MessageBox.Show($"Error loading audit logs: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilter()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                AuditLogs.Clear();

                var filtered = _allLogs.AsEnumerable();

                // Filter by text
                if (!string.IsNullOrWhiteSpace(FilterText))
                {
                    var search = FilterText.ToLowerInvariant();
                    filtered = filtered.Where(e =>
                        e.Action.ToLowerInvariant().Contains(search) ||
                        e.Username.ToLowerInvariant().Contains(search) ||
                        e.Details.ToLowerInvariant().Contains(search) ||
                        e.EntityType.ToLowerInvariant().Contains(search));
                }

                // Filter by category
                if (SelectedCategory.HasValue)
                {
                    filtered = filtered.Where(e => e.Category == SelectedCategory.Value);
                }

                // Filter by severity
                if (SelectedSeverity.HasValue)
                {
                    filtered = filtered.Where(e => e.Severity == SelectedSeverity.Value);
                }

                foreach (var entry in filtered)
                {
                    AuditLogs.Add(entry);
                }

                StatusMessage = $"Showing {AuditLogs.Count} of {_allLogs.Count} audit log entries";
            });
        }

        private void ClearFilters()
        {
            FilterText = string.Empty;
            SelectedCategory = null;
            SelectedSeverity = null;
        }

        private async Task ExportLogsAsync()
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV Files|*.csv|JSON Files|*.json",
                    Title = "Export Audit Logs",
                    FileName = $"AuditLog_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    if (saveFileDialog.FilterIndex == 1) // CSV
                    {
                        await ExportToCsvAsync(saveFileDialog.FileName);
                    }
                    else // JSON
                    {
                        await ExportToJsonAsync(saveFileDialog.FileName);
                    }

                    MessageBox.Show($"Audit logs exported successfully to:\n{saveFileDialog.FileName}",
                        "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting logs: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ExportToCsvAsync(string filePath)
        {
            using var writer = new StreamWriter(filePath);
            
            // Header
            await writer.WriteLineAsync("Timestamp,Action,Category,Username,EntityType,EntityId,Details,Severity,MachineName");

            // Data
            foreach (var log in AuditLogs)
            {
                // Use TimeString to get properly formatted local time
                await writer.WriteLineAsync($"{log.TimeString},{EscapeCsv(log.Action)},{log.Category},{EscapeCsv(log.Username)},{EscapeCsv(log.EntityType)},{EscapeCsv(log.EntityId)},{EscapeCsv(log.Details)},{log.Severity},{EscapeCsv(log.MachineName)}");
            }
        }

        private string EscapeCsv(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }

        private async Task ExportToJsonAsync(string filePath)
        {
            var json = JsonSerializer.Serialize(AuditLogs.ToList(), new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            await File.WriteAllTextAsync(filePath, json);
        }

        private void CloseWindow()
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window.DataContext == this)
                {
                    window.Close();
                    break;
                }
            }
        }
    }

    public class AuditLogEntry : BaseViewModel
    {
        private DateTime _timestamp;

        public DateTime Timestamp 
        { 
            get => _timestamp;
            set => _timestamp = value;
        }

        public string Action { get; set; } = string.Empty;
        public AuditCategory Category { get; set; }
        public string Username { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public AuditSeverity Severity { get; set; }
        public string MachineName { get; set; } = string.Empty;

        // Convert UTC to local time and use system's date/time format
        public string TimeString 
        {
            get
            {
                var localTime = Timestamp.Kind == DateTimeKind.Utc 
                    ? Timestamp.ToLocalTime() 
                    : Timestamp;
                return localTime.ToString("G"); // General date/time pattern (respects regional settings)
            }
        }

        public string SeverityIcon => Severity switch
        {
            AuditSeverity.Info => "?",
            AuditSeverity.Warning => "?",
            AuditSeverity.Error => "?",
            AuditSeverity.Critical => "??",
            _ => "�"
        };
    }
}
