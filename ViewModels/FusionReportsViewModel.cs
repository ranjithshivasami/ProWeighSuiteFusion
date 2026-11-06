// FUSION-PLAN.md Step 6 — Fusion Reports over fusion_transactions only.
// Columns are discovered dynamically from the fusion_transactions table schema.
// S.No is a dynamic 1-based row index. Date-filtered grid + CSV export.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Security;
using ProWeighSuitePlatforms.Services;
namespace ProWeighSuitePlatforms.ViewModels
{
    public sealed class FusionReportsViewModel : ViewModels.BaseViewModel
    {
        public string Title => "Reports";

        private readonly AuditService _audit = new();
        private readonly ICurrentUserContext _main;
        private readonly CompanySettingsService _companySettingsService;
        private readonly string _username;
        private readonly string _dbPath;

        private readonly DataTable _reportTable = new();
        public DataView ReportData { get; }

        public ObservableCollection<string> ReportColumns { get; } = new();
        public ObservableCollection<SelectableColumn> AvailableColumns { get; } = new();

        /// <summary>
        /// Friendly UI names for fusion_transactions schema columns.
        /// The DataGrid binds to the DB column name (Key) but displays the friendly name.
        /// "is_synced" is intentionally omitted — it is removed from the report UI.
        /// </summary>
        private static readonly Dictionary<string, string> ColumnDisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "Id",
            ["batch_no"] = "Batch No",
            ["tub_id"] = "Tub",
            ["supplier_name"] = "Supplier Name",
            ["weight"] = "Weight",
            ["uom"] = "UOM",
            ["recorded_at"] = "Recorded At",
            ["epc"] = "EPC",
            ["tub_cycles"] = "Tub Cycles",
            ["grn_no"] = "GRN No",
            ["bay_id"] = "Bay",
            ["lot_no"] = "LOT No",
            ["nuts"] = "Nuts",
            ["operator_name"] = "Operator Name",
        };

        public static string GetDisplayName(string dbColumnName)
        {
            if (ColumnDisplayNames.TryGetValue(dbColumnName, out var display))
                return display;
            return dbColumnName;
        }

        public event EventHandler? ColumnSelectionChanged;

        private void NotifyColumnSelectionChanged()
        {
            ColumnSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public SelectableColumn? GetColumnByKey(string key)
        {
            foreach (var col in AvailableColumns)
                if (string.Equals(col.Key, key, StringComparison.OrdinalIgnoreCase))
                    return col;
            return null;
        }

        private bool _selectAllColumns = true;
        public bool SelectAllColumns
        {
            get => _selectAllColumns;
            set
            {
                if (SetProperty(ref _selectAllColumns, value))
                {
                    foreach (var col in AvailableColumns)
                        col.IsSelected = value;
                    NotifyColumnSelectionChanged();
                }
            }
        }

        private DateTime _from = DateTime.Today;
        public DateTime From { get => _from; set => SetProperty(ref _from, value); }
        private DateTime _to = DateTime.Today;
        public DateTime To { get => _to; set => SetProperty(ref _to, value); }

        private string _statusText = string.Empty;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ApplySearchFilter();
            }
        }
        private string _searchText = string.Empty;

        public ICommand RefreshCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand EmailReportCommand { get; }
        public ICommand CopyTransactionIdCommand { get; }
        public ICommand CopyBatchNumberCommand { get; }
        public ICommand CopyTubIdCommand { get; }

        public bool CanExportReports => HasPermission(_main.CurrentUser, Permission.ExportReports);

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        private bool _isEmailSending;
        public bool IsEmailSending
        {
            get => _isEmailSending;
            set
            {
                if (SetProperty(ref _isEmailSending, value))
                {
                    RaisePropertyChanged(nameof(EmailButtonText));
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string EmailButtonText => IsEmailSending ? "Sending..." : "Email";

        public FusionReportsViewModel(ICurrentUserContext main)
        {
            _main = main;
            _username = string.IsNullOrWhiteSpace(main.CurrentUser?.Username) ? "System" : main.CurrentUser.Username;
            _dbPath = AppConfigurationManager.GetDatabasePath();
            var companyDbPath = AppConfigurationManager.GetDatabasePath();
            _companySettingsService = new CompanySettingsService(new CompanySettingsRepository(companyDbPath));
            ReportData = new DataView(_reportTable);

            RefreshCommand = new ViewModels.RelayCommand(async _ => await LoadAsync());
            ExportCsvCommand = new ViewModels.RelayCommand(async _ => await ExportCsvAsync());
            EmailReportCommand = new ViewModels.RelayCommand(async _ => await EmailReportAsync(),
                _ => ReportData.Count > 0 && CanExportReports && !IsLoading);
            CopyTransactionIdCommand = new ViewModels.RelayCommand(param => CopyToClipboard(param as DataRowView, "id"));
            CopyBatchNumberCommand = new ViewModels.RelayCommand(param => CopyToClipboard(param as DataRowView, "batch_no"));
            CopyTubIdCommand = new ViewModels.RelayCommand(param => CopyToClipboard(param as DataRowView, "tub_id"));

            InitializeColumns();
            _ = LoadAsync();
        }

        private void InitializeColumns()
        {
            // S.No is always first
            var sno = new SelectableColumn { Key = "SNo", DisplayName = "S.No", IsSelected = true };
            sno.SelectionChanged += (_, _) => NotifyColumnSelectionChanged();
            AvailableColumns.Add(sno);
        }

        public async Task LoadAsync()
        {
            try
            {
                await new FusionDatabaseInit(_dbPath).EnsureCreatedAsync();
                var repo = new FusionRepository(_dbPath);

                // Discover columns from fusion_transactions table schema
                var columns = await repo.GetTransactionColumnNamesAsync();

                // Remove internal/sync columns from the report UI entirely.
                columns = columns.Where(c =>
                    !string.Equals(c, "is_synced", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(c, "supplier_id", StringComparison.OrdinalIgnoreCase)
                ).ToList();

                ReportColumns.Clear();
                ReportColumns.Add("SNo");
                foreach (var col in columns)
                    ReportColumns.Add(col);

                // Rebuild AvailableColumns from schema (keep selection state)
                var selection = AvailableColumns.ToDictionary(c => c.Key, c => c.IsSelected);
                AvailableColumns.Clear();
                var sno = new SelectableColumn { Key = "SNo", DisplayName = "S.No", IsSelected = selection.TryGetValue("SNo", out var s) ? s : true };
                sno.SelectionChanged += (_, _) => NotifyColumnSelectionChanged();
                AvailableColumns.Add(sno);
                foreach (var col in columns)
                {
                    var selectable = new SelectableColumn
                    {
                        Key = col,                          // DB column name — used for binding + visibility key
                        DisplayName = GetDisplayName(col),  // friendly UI name shown in header & column picker
                        IsSelected = selection.TryGetValue(col, out var v) ? v : true,
                    };
                    selectable.SelectionChanged += (_, _) => NotifyColumnSelectionChanged();
                    AvailableColumns.Add(selectable);
                }

                // Load data into DataTable (reuse existing ReportData DataView)
                var from = _from.ToString("yyyy-MM-dd");
                var to = _to.ToString("yyyy-MM-dd");
                var table = await repo.LoadTransactionsReportAsync(from, to);

                // Reset filter before reload
                ReportData.RowFilter = null;

                _reportTable.Clear();
                _reportTable.Columns.Clear();
                foreach (DataColumn col in table.Columns)
                {
                    if (string.Equals(col.ColumnName, "supplier_id", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(col.ColumnName, "is_synced", StringComparison.OrdinalIgnoreCase))
                        continue;
                    _reportTable.Columns.Add(col.ColumnName, col.DataType);
                }
                foreach (DataRow row in table.Rows)
                {
                    var newRow = _reportTable.NewRow();
                    foreach (DataColumn col in _reportTable.Columns)
                        newRow[col.ColumnName] = row[col.ColumnName];
                    _reportTable.Rows.Add(newRow);
                }

                ReIndexSNo();
                ApplySearchFilter();

                StatusText = $"{ReportData.Count} transaction(s) from {From:dd-MM-yyyy} to {To:dd-MM-yyyy}.";
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        private void ApplySearchFilter()
        {
            if (ReportData == null || ReportData.Table == null) return;

            if (string.IsNullOrWhiteSpace(_searchText))
                ReportData.RowFilter = null;
            else
            {
                var search = _searchText.Replace("'", "''");
                var conditions = _reportTable.Columns
                    .Cast<DataColumn>()
                    .Where(c => c.DataType == typeof(string))
                    .Select(c => $"[{c.ColumnName}] LIKE '%{search}%'");
                ReportData.RowFilter = string.Join(" OR ", conditions);
            }
            ReIndexSNo();
        }

        private void ReIndexSNo()
        {
            int i = 1;
            foreach (DataRowView row in ReportData)
                row["SNo"] = i++;
        }


        private async Task ExportCsvAsync()
        {
            if (ReportData.Count == 0)
            {
                StatusText = "Nothing to export — load a range first.";
                return;
            }
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"FusionReport_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                    Filter = "CSV file (*.csv)|*.csv",
                };
                if (dlg.ShowDialog() != true) return;
                var sb = new StringBuilder();
                // Header
                sb.Append("SNo,");
                foreach (DataColumn col in _reportTable.Columns)
                    if (col.ColumnName != "SNo")
                        sb.Append($"\"{col.ColumnName}\",");
                sb.Length--;
                sb.Append("\r\n");

                foreach (DataRowView row in ReportData)
                {
                    sb.Append(row["SNo"] + ",");
                    for (int i = 1; i < row.Row.ItemArray.Length; i++)
                        sb.Append($"\"{row.Row.ItemArray[i]}\",");
                    sb.Length--;
                    sb.Append("\r\n");
                }
                await System.IO.File.WriteAllTextAsync(dlg.FileName, sb.ToString(), Encoding.UTF8);
                await _audit.LogReportExportedAsync(_username,
                    $"Fusion Transactions ({From:dd-MM-yyyy} to {To:dd-MM-yyyy}, CSV)", ReportData.Count);
                StatusText = $"Exported {ReportData.Count} row(s) to {dlg.FileName}.";
            }
            catch (Exception ex)
            {
                StatusText = $"Export failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Export Fusion transactions CSV", ex);
            }
        }

        private async Task EmailReportAsync()
        {
            if (ReportData.Count == 0) return;
            IsLoading = true;
            IsEmailSending = true;
            try
            {
                CheckPermissionOrThrow(_main.CurrentUser, Permission.ExportReports);

                var companySettings = await _companySettingsService.GetAsync();
                var reportEmail = companySettings?.ReportEmail?.Trim();

                if (string.IsNullOrWhiteSpace(reportEmail))
                {
                    MessageBox.Show("Report email is not configured.\n\nPlease set the Report Email in Company Settings first.",
                        "Email Not Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusText = "Generating report for email...";

                var tempPath = Path.Combine(Path.GetTempPath(), $"FusionReport_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                int recordCount = 0;

                var sb = new StringBuilder();
                // Header
                sb.Append("SNo,");
                foreach (DataColumn col in _reportTable.Columns)
                    if (col.ColumnName != "SNo")
                        sb.Append($"\"{col.ColumnName}\",");
                sb.Length--;
                sb.Append("\r\n");

                foreach (DataRowView row in ReportData)
                {
                    sb.Append(row["SNo"] + ",");
                    for (int i = 1; i < row.Row.ItemArray.Length; i++)
                        sb.Append($"\"{row.Row.ItemArray[i]}\",");
                    sb.Length--;
                    sb.Append("\r\n");
                    recordCount++;
                }

                await System.IO.File.WriteAllTextAsync(tempPath, sb.ToString(), Encoding.UTF8);

                var attachmentName = $"FusionReport_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var subject = $"Fusion Transactions Report - {From:dd-MM-yyyy} to {To:dd-MM-yyyy}";
                var body = $"Please find attached the Fusion Transactions Report.\n\nGenerated on: {DateTime.Now:dd-MM-yyyy HH:mm:ss}\nPeriod: {From:dd-MM-yyyy} to {To:dd-MM-yyyy}\nRecords: {recordCount}";

                var emailService = new EmailService();
                await emailService.SendEmailWithAttachmentAsync(reportEmail, subject, body, tempPath, attachmentName);

                try { File.Delete(tempPath); } catch { }

                var username = _main.CurrentUser?.Username ?? "Unknown";
                _ = _audit.LogReportExportedAsync(username, $"Fusion Transactions Report (Email)", recordCount);

                var recipientCount = reportEmail.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
                var displayMsg = recipientCount > 1
                    ? $"Report sent successfully to {recipientCount} recipients!"
                    : $"Report sent successfully to {reportEmail}!";
                MessageBox.Show(displayMsg, "Email Sent", MessageBoxButton.OK, MessageBoxImage.Information);

                StatusText = recipientCount > 1
                    ? $"Report emailed to {recipientCount} recipients"
                    : $"Report emailed to {reportEmail}";
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Email Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText = $"Email error: {ex.Message}";
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("You do not have permission to export reports.\n\nPlease contact your administrator.",
                    "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending report via email: {ex.Message}",
                    "Email Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText = $"Error sending email: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                IsEmailSending = false;
            }
        }

        private void CopyToClipboard(DataRowView? row, string field)
        {
            if (row == null) return;
            string? value = row[field] as string ?? row[field]?.ToString();
            if (string.IsNullOrEmpty(value)) return;
            try
            {
                Clipboard.SetText(value);
                StatusText = $"Copied {field}: {value}";
            }
            catch (Exception ex)
            {
                StatusText = $"Copy failed: {ex.Message}";
            }
        }
    }
}
