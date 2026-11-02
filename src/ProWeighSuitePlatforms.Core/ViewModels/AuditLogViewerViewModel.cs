using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Models;
using Microsoft.Win32;

namespace ProWeighSuitePlatforms.ViewModels
{
    /// <summary>
    /// ViewModel for viewing and searching audit logs
    /// </summary>
    public class AuditLogViewerViewModel : BaseViewModel
    {
        private readonly AuditService _auditService;

        public AuditLogViewerViewModel(AuditService auditService)
        {
            _auditService = auditService;

            // Initialize date range to today (not future dates)
            ToDate = DateTime.Now;
            FromDate = DateTime.Now.Date;

            // Initialize commands
            SearchCommand = new RelayCommand(async _ => await LoadAuditLogsAsync());
            ExportToCsvCommand = new RelayCommand(async _ => await ExportToCsvAsync(), _ => AuditEntries.Count > 0);
            ExportToExcelCommand = new RelayCommand(async _ => await ExportToExcelAsync(), _ => AuditEntries.Count > 0);
            ViewDetailsCommand = new RelayCommand(_ => ViewDetails(), _ => SelectedItem != null);
            RefreshCommand = new RelayCommand(async _ => await LoadAuditLogsAsync());
            ClearFiltersCommand = new RelayCommand(_ => ClearFilters());

            // Set default dropdown values
            SelectedCategory = "All";
            SelectedSeverity = "All";

            // Load initial data
            _ = LoadAuditLogsAsync();
        }

        public ObservableCollection<AuditEntryViewModel> AuditEntries { get; } = new();

        private AuditEntryViewModel? _selectedItem;
        public AuditEntryViewModel? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        private DateTime _fromDate;
        public DateTime FromDate
        {
            get => _fromDate;
            set => SetProperty(ref _fromDate, value);
        }

        private DateTime _toDate;
        public DateTime ToDate
        {
            get => _toDate;
            set => SetProperty(ref _toDate, value);
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        private string? _selectedCategory;
        public string? SelectedCategory
        {
            get => _selectedCategory;
            set => SetProperty(ref _selectedCategory, value);
        }

        private string? _selectedSeverity;
        public string? SelectedSeverity
        {
            get => _selectedSeverity;
            set => SetProperty(ref _selectedSeverity, value);
        }

        private string _statusMessage = "Loading...";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        // Category options for ComboBox
        public ObservableCollection<string> CategoryOptions { get; } = new()
        {
            "All",
            "Authentication",
            "Authorization",
            "UserManagement",
            "Material",
            "Transaction",
            "Configuration",
            "Security",
            "System",
            "Report",
            "Backup"
        };

        // Severity options for ComboBox
        public ObservableCollection<string> SeverityOptions { get; } = new()
        {
            "All",
            "Info",
            "Warning",
            "Error",
            "Critical"
        };

        // Commands
        public ICommand SearchCommand { get; }
        public ICommand ExportToCsvCommand { get; }
        public ICommand ExportToExcelCommand { get; }
        public ICommand ViewDetailsCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ClearFiltersCommand { get; }

        private async Task LoadAuditLogsAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading audit logs...";
                AuditEntries.Clear();

                // Read audit logs from files
                var entries = await _auditService.ReadAuditEntriesAsync(FromDate, ToDate);

                // Apply search filter
                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    var searchLower = SearchText.ToLowerInvariant();
                    entries = entries.Where(e =>
                        e.Action.ToLowerInvariant().Contains(searchLower) ||
                        e.Username.ToLowerInvariant().Contains(searchLower) ||
                        e.Details.ToLowerInvariant().Contains(searchLower) ||
                        (e.EntityType?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                        (e.EntityId?.ToLowerInvariant().Contains(searchLower) ?? false)
                    ).ToList();
                }

                // Apply category filter
                if (!string.IsNullOrWhiteSpace(SelectedCategory) && SelectedCategory != "All")
                {
                    if (Enum.TryParse<AuditCategory>(SelectedCategory, out var category))
                    {
                        entries = entries.Where(e => e.Category == category).ToList();
                    }
                }

                // Apply severity filter
                if (!string.IsNullOrWhiteSpace(SelectedSeverity) && SelectedSeverity != "All")
                {
                    if (Enum.TryParse<AuditSeverity>(SelectedSeverity, out var severity))
                    {
                        entries = entries.Where(e => e.Severity == severity).ToList();
                    }
                }

                // Update UI
                foreach (var entry in entries.OrderByDescending(e => e.Timestamp))
                {
                    AuditEntries.Add(new AuditEntryViewModel(entry));
                }

                if (AuditEntries.Count == 0)
                {
                    StatusMessage = $"No audit log entries found from {FromDate:yyyy-MM-dd HH:mm} to {ToDate:yyyy-MM-dd HH:mm}. Check if the date range is correct and if any actions have been logged.";
                }
                else
                {
                    StatusMessage = $"Loaded {AuditEntries.Count} audit log entrie(s) from {FromDate:yyyy-MM-dd HH:mm} to {ToDate:yyyy-MM-dd HH:mm}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Error loading audit logs: {ex.Message}\n\nDetails: {ex.StackTrace}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ExportToCsvAsync()
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "CSV Files (*.csv)|*.csv",
                    DefaultExt = ".csv",
                    FileName = $"audit_logs_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var csv = new StringBuilder();
                    csv.AppendLine("Timestamp,Action,Category,Username,Entity Type,Entity ID,Details,Old Value,New Value,IP Address,Machine Name,Severity");

                    foreach (var entry in AuditEntries)
                    {
                        csv.AppendLine($"\"{entry.TimestampFormatted}\",\"{EscapeCsv(entry.Action)}\",\"{entry.Category}\",\"{EscapeCsv(entry.Username)}\",\"{EscapeCsv(entry.EntityType)}\",\"{EscapeCsv(entry.EntityId)}\",\"{EscapeCsv(entry.Details)}\",\"{EscapeCsv(entry.OldValue)}\",\"{EscapeCsv(entry.NewValue)}\",\"{EscapeCsv(entry.IpAddress)}\",\"{EscapeCsv(entry.MachineName)}\",\"{entry.Severity}\"");
                    }

                    await File.WriteAllTextAsync(saveDialog.FileName, csv.ToString(), Encoding.UTF8);
                    
                    MessageBox.Show($"Exported {AuditEntries.Count} entries to CSV\n\n{saveDialog.FileName}", 
                        "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting to CSV: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ExportToExcelAsync()
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx|CSV Files (*.csv)|*.csv",
                    DefaultExt = ".xlsx",
                    FileName = $"audit_logs_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    // For simplicity, export as CSV (Excel can open it)
                    // For true .xlsx, you'd need a library like EPPlus or ClosedXML
                    var csv = new StringBuilder();
                    csv.AppendLine("Timestamp,Action,Category,Username,Entity Type,Entity ID,Details,Old Value,New Value,IP Address,Machine Name,Severity");

                    foreach (var entry in AuditEntries)
                    {
                        csv.AppendLine($"\"{entry.TimestampFormatted}\",\"{EscapeCsv(entry.Action)}\",\"{entry.Category}\",\"{EscapeCsv(entry.Username)}\",\"{EscapeCsv(entry.EntityType)}\",\"{EscapeCsv(entry.EntityId)}\",\"{EscapeCsv(entry.Details)}\",\"{EscapeCsv(entry.OldValue)}\",\"{EscapeCsv(entry.NewValue)}\",\"{EscapeCsv(entry.IpAddress)}\",\"{EscapeCsv(entry.MachineName)}\",\"{entry.Severity}\"");
                    }

                    await File.WriteAllTextAsync(saveDialog.FileName, csv.ToString(), Encoding.UTF8);
                    
                    MessageBox.Show($"Exported {AuditEntries.Count} entries\n\n{saveDialog.FileName}\n\nNote: File is in CSV format which Excel can open.", 
                        "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            // Escape double quotes by doubling them
            return value.Replace("\"", "\"\"");
        }

        private void ViewDetails()
        {
            if (SelectedItem == null) return;

            try
            {
                var detailsWindow = new Views.AuditLogDetailsWindow
                {
                    DataContext = SelectedItem,
                    Owner = Application.Current.MainWindow
                };
                detailsWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error viewing details: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearFilters()
        {
            SearchText = string.Empty;
            SelectedCategory = "All";
            SelectedSeverity = "All";
            // Reset to today instead of 7 days ago to find logs
            ToDate = DateTime.Now;
            FromDate = DateTime.Now.Date;
        }
    }

    /// <summary>
    /// View model for a single audit entry
    /// </summary>
    public class AuditEntryViewModel : BaseViewModel
    {
        private readonly AuditEntry _entry;

        public AuditEntryViewModel(AuditEntry entry)
        {
            _entry = entry;
        }

        // Convert UTC timestamp to local time for display
        public DateTime Timestamp => _entry.Timestamp.Kind == DateTimeKind.Utc 
            ? _entry.Timestamp.ToLocalTime() 
            : _entry.Timestamp;

        public string Action => _entry.Action;
        public string Category => _entry.Category.ToString();
        public string Username => _entry.Username;
        public string? EntityType => _entry.EntityType;
        public string? EntityId => _entry.EntityId;
        public string Details => _entry.Details;
        public string? OldValue => _entry.OldValue;
        public string? NewValue => _entry.NewValue;
        public string? IpAddress => _entry.IpAddress;
        public string MachineName => _entry.MachineName;
        public string Severity => _entry.Severity.ToString();

        // Use system's date/time format (respects regional settings)
        public string TimestampFormatted => Timestamp.ToString("G"); // General date/time pattern (short date + long time)
        public string TimeFormatted => Timestamp.ToString("T"); // Long time pattern
        
        public string SeverityColor => Severity switch
        {
            "Info" => "#4CAF50",        // Green
            "Warning" => "#FF9800",     // Orange
            "Error" => "#FF6B6B",       // Red
            "Critical" => "#D32F2F",    // Dark Red
            _ => "#9E9E9E"              // Gray
        };

        public string CategoryIcon => Category switch
        {
            "Authentication" => "Login",
            "Authorization" => "ShieldAccount",
            "UserManagement" => "AccountGroup",
            "Material" => "CubeOutline",
            "Transaction" => "ScaleBalance",
            "Configuration" => "Cog",
            "Security" => "Security",
            "System" => "Server",
            "Report" => "FileChart",
            "Backup" => "DatabaseImport",
            _ => "FileDocumentOutline"
        };

        public string DetailsTruncated
        {
            get
            {
                const int maxLength = 100;
                return Details.Length > maxLength 
                    ? Details.Substring(0, maxLength) + "..." 
                    : Details;
            }
        }
    }
}
