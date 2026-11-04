// FUSION-PLAN.md Step 6 — Fusion Reports over fusion_transactions only.
// Columns are discovered dynamically from the fusion_transactions table schema.
// S.No is a dynamic 1-based row index. Date-filtered grid + CSV export.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Services;

using ProWeighSuitePlatforms.Models;
namespace ProWeighSuitePlatforms.ViewModels
{
    public sealed class FusionReportsViewModel : ViewModels.BaseViewModel
    {
        public string Title => "Reports";

        private readonly AuditService _audit = new();
        private readonly string _username;
        private readonly string _dbPath;

        private readonly DataTable _reportTable = new();
        public DataView ReportData { get; }

        public ObservableCollection<string> ReportColumns { get; } = new();
        public ObservableCollection<SelectableColumn> AvailableColumns { get; } = new();

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
                    ApplyColumnVisibility();
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
        public ICommand CopyTransactionIdCommand { get; }
        public ICommand CopyBatchNumberCommand { get; }
        public ICommand CopyTubIdCommand { get; }

        public FusionReportsViewModel(string username = "System")
        {
            _username = string.IsNullOrWhiteSpace(username) ? "System" : username;
            _dbPath = AppConfigurationManager.GetDatabasePath();
            ReportData = new DataView(_reportTable);

            RefreshCommand = new ViewModels.RelayCommand(async _ => await LoadAsync());
            ExportCsvCommand = new ViewModels.RelayCommand(async _ => await ExportCsvAsync());
            CopyTransactionIdCommand = new ViewModels.RelayCommand(param => CopyToClipboard(param as DataRowView, "id"));
            CopyBatchNumberCommand = new ViewModels.RelayCommand(param => CopyToClipboard(param as DataRowView, "batch_no"));
            CopyTubIdCommand = new ViewModels.RelayCommand(param => CopyToClipboard(param as DataRowView, "tub_id"));

            InitializeColumns();
            _ = LoadAsync();
        }

        private void InitializeColumns()
        {
            // S.No is always first
            AvailableColumns.Add(new SelectableColumn { Key = "SNo", DisplayName = "S.No", IsSelected = true });
        }

        public async Task LoadAsync()
        {
            try
            {
                await new FusionDatabaseInit(_dbPath).EnsureCreatedAsync();
                var repo = new FusionRepository(_dbPath);

                // Discover columns from fusion_transactions table schema
                var columns = await repo.GetTransactionColumnNamesAsync();

                ReportColumns.Clear();
                ReportColumns.Add("SNo");
                foreach (var col in columns)
                    ReportColumns.Add(col);

                // Rebuild AvailableColumns from schema (keep selection state)
                var selection = AvailableColumns.ToDictionary(c => c.Key, c => c.IsSelected);
                AvailableColumns.Clear();
                AvailableColumns.Add(new SelectableColumn { Key = "SNo", DisplayName = "S.No", IsSelected = selection.TryGetValue("SNo", out var s) ? s : true });
                foreach (var col in columns)
                    AvailableColumns.Add(new SelectableColumn { Key = col, DisplayName = col, IsSelected = selection.TryGetValue(col, out var v) ? v : true });

                // Load data into DataTable (reuse existing ReportData DataView)
                var from = _from.ToString("yyyy-MM-dd");
                var to = _to.ToString("yyyy-MM-dd");
                var table = await repo.LoadTransactionsReportAsync(from, to);

                // Reset filter before reload
                ReportData.RowFilter = null;

                _reportTable.Clear();
                _reportTable.Columns.Clear();
                foreach (DataColumn col in table.Columns)
                    _reportTable.Columns.Add(col.ColumnName, col.DataType);
                foreach (DataRow row in table.Rows)
                    _reportTable.Rows.Add(row.ItemArray);

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

        private void ApplyColumnVisibility()
        {
            // Column visibility is handled by the view via AvailableColumns;
            // this hook is kept for future column-show/hide logic.
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
