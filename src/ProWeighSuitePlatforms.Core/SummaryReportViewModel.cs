using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ClosedXML.Excel;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace ProWeighSuitePlatforms.ViewModels
{
    public enum SummaryGroupBy
    {
        ProductWise,
        CategoryWise,
        VariantWise
    }

    public class SummaryReportViewModel : BaseViewModel
    {
        private readonly ICurrentUserContext _main;
        private readonly AuditService _auditService;
        private readonly CompanySettingsService _companySettingsService;

        public string Title => "Summary Report";

        public bool CanExportReports => HasPermission(_main.CurrentUser, Permission.ExportReports);

        public ICommand FilterReportCommand { get; }
        public ICommand ExportToExcelCommand { get; }
        public ICommand EmailReportCommand { get; }

        public ObservableCollection<SummaryReportItem> SummaryItems { get; } = new();

        // Group-by options
        public List<string> GroupByOptions { get; } = new()
        {
            "Product Wise",
            "Category Wise",
            "Variant Wise"
        };

        private string _selectedGroupBy = "Product Wise";
        public string SelectedGroupBy
        {
            get => _selectedGroupBy;
            set
            {
                if (SetProperty(ref _selectedGroupBy, value))
                {
                    _ = LoadSummaryAsync();
                }
            }
        }

        private SummaryGroupBy CurrentGroupBy => SelectedGroupBy switch
        {
            "Category Wise" => SummaryGroupBy.CategoryWise,
            "Variant Wise" => SummaryGroupBy.VariantWise,
            _ => SummaryGroupBy.ProductWise
        };

        // Search
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    _searchDebounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    _searchDebounceTimer?.Change(300, Timeout.Infinite);
                }
            }
        }
        private string _searchText = string.Empty;
        private Timer? _searchDebounceTimer;

        public DateTime? FromDate
        {
            get => _fromDate;
            set => SetProperty(ref _fromDate, value);
        }
        private DateTime? _fromDate;

        public DateTime? ToDate
        {
            get => _toDate;
            set => SetProperty(ref _toDate, value);
        }
        private DateTime? _toDate;

        // Date type filter (which date field the From/To range applies to)
        public List<string> DateTypeOptions { get; } = new() { "Mfg Date", "Trx Date" };

        private string _selectedDateType = "Mfg Date";
        public string SelectedDateType
        {
            get => _selectedDateType;
            set => SetProperty(ref _selectedDateType, value);
        }

        // Status filter (ALL shows every result)
        public List<string> StatusFilterOptions { get; } = new() { "ALL", "OK", "OVER", "UNDER" };

        private string _selectedStatusFilter = "ALL";
        public string SelectedStatusFilter
        {
            get => _selectedStatusFilter;
            set => SetProperty(ref _selectedStatusFilter, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
        private string _statusMessage = string.Empty;

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

        // Totals
        public int TotalTransactions
        {
            get => _totalTransactions;
            set => SetProperty(ref _totalTransactions, value);
        }
        private int _totalTransactions;

        public double TotalWeight
        {
            get => _totalWeight;
            set => SetProperty(ref _totalWeight, value);
        }
        private double _totalWeight;

        // Column visibility based on group-by mode
        public bool ShowMaterialColumns => CurrentGroupBy == SummaryGroupBy.ProductWise;
        public bool ShowCategoryColumn => CurrentGroupBy == SummaryGroupBy.ProductWise || CurrentGroupBy == SummaryGroupBy.CategoryWise;
        public bool ShowVariantColumn => CurrentGroupBy == SummaryGroupBy.ProductWise || CurrentGroupBy == SummaryGroupBy.VariantWise;

        public SummaryReportViewModel(ICurrentUserContext main)
        {
            _main = main;
            _auditService = new AuditService();
            var dbPath = AppConfigurationManager.GetDatabasePath();
            _companySettingsService = new CompanySettingsService(new CompanySettingsRepository(dbPath));

            _searchDebounceTimer = new Timer(_ =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _ = LoadSummaryAsync();
                });
            });

            FilterReportCommand = new RelayCommand(async _ => await LoadSummaryAsync());
            ExportToExcelCommand = new RelayCommand(_ => ExportToExcel(), _ => SummaryItems.Count > 0 && CanExportReports && !IsLoading);
            EmailReportCommand = new RelayCommand(async _ => await EmailReportAsync(), _ => SummaryItems.Count > 0 && CanExportReports && !IsLoading);

            // Default filter: current date
            var now = DateTime.Now;
            FromDate = now.Date;
            ToDate = now.Date;

            _ = LoadSummaryAsync();
        }

        private async Task EmailReportAsync()
        {
            if (IsLoading) return;
            IsLoading = true;
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

                StatusMessage = "Generating summary report for email...";

                var groupBy = CurrentGroupBy;
                var tempPath = Path.Combine(Path.GetTempPath(), $"SummaryReport_{SelectedGroupBy.Replace(" ", "")}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("Summary Report");

                    var headers = new List<string> { "Sl.No" };
                    if (ShowCategoryColumn) headers.Add("Category");
                    if (ShowVariantColumn) headers.Add("Variant");
                    if (ShowMaterialColumns)
                    {
                        headers.Add("Material Code");
                        headers.Add("Material Name");
                    }
                    headers.AddRange(new[] { "Transactions", "Achieved Weight", "UOM" });

                    for (int i = 0; i < headers.Count; i++)
                        worksheet.Cell(1, i + 1).Value = headers[i];

                    var headerRange = worksheet.Range(1, 1, 1, headers.Count);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#555555");
                    headerRange.Style.Font.FontColor = XLColor.White;
                    headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                    int row = 2;
                    foreach (var item in SummaryItems)
                    {
                        int col = 1;
                        worksheet.Cell(row, col++).Value = item.SlNo;
                        if (ShowCategoryColumn) worksheet.Cell(row, col++).Value = item.CategoryName;
                        if (ShowVariantColumn) worksheet.Cell(row, col++).Value = item.VariantName;
                        if (ShowMaterialColumns)
                        {
                            worksheet.Cell(row, col++).Value = item.MaterialCode;
                            worksheet.Cell(row, col++).Value = item.MaterialName;
                        }
                        worksheet.Cell(row, col++).Value = item.TransactionCount;
                        worksheet.Cell(row, col++).Value = item.TotalWeight;
                        worksheet.Cell(row, col++).Value = item.UOM;
                        row++;
                    }

                    {
                        int col = 1;
                        int blankCols = headers.Count - 3;
                        for (int i = 0; i < blankCols - 1; i++)
                            worksheet.Cell(row, col++).Value = "";
                        worksheet.Cell(row, col).Value = "TOTAL";
                        worksheet.Cell(row, col).Style.Font.Bold = true;
                        col++;
                        worksheet.Cell(row, col).Value = TotalTransactions;
                        worksheet.Cell(row, col).Style.Font.Bold = true;
                        col++;
                        worksheet.Cell(row, col).Value = TotalWeight;
                        worksheet.Cell(row, col).Style.Font.Bold = true;
                        row++;
                    }

                    worksheet.Columns().AdjustToContents();

                    if (row > 2)
                    {
                        var dataRange = worksheet.Range(1, 1, row - 1, headers.Count);
                        dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    }

                    workbook.SaveAs(tempPath);
                }

                var attachmentName = $"SummaryReport_{SelectedGroupBy.Replace(" ", "")}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                var fromStr = FromDate?.ToString("dd-MM-yyyy") ?? "-";
                var toStr = ToDate?.ToString("dd-MM-yyyy") ?? "-";
                var subject = $"Summary Report ({SelectedGroupBy}) - {fromStr} to {toStr}";
                var body = $"Please find attached the Summary Report.\n\nGroup By: {SelectedGroupBy}\nGenerated on: {DateTime.Now:dd-MM-yyyy HH:mm:ss}\nPeriod: {fromStr} to {toStr}\nTotal Transactions: {TotalTransactions}\nTotal Weight: {TotalWeight:0.000} kg";

                var emailService = new EmailService();
                await emailService.SendEmailWithAttachmentAsync(reportEmail, subject, body, tempPath, attachmentName);

                try { File.Delete(tempPath); } catch { }

                var username = _main.CurrentUser?.Username ?? "Unknown";
                _ = _auditService.LogReportExportedAsync(username, $"Summary Report - {SelectedGroupBy} (Email)", SummaryItems.Count);

                var recipientCount = reportEmail.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
                var displayMsg = recipientCount > 1
                    ? $"Summary report sent successfully to {recipientCount} recipients!"
                    : $"Summary report sent successfully to {reportEmail}!";
                MessageBox.Show(displayMsg, "Email Sent", MessageBoxButton.OK, MessageBoxImage.Information);

                StatusMessage = recipientCount > 1
                    ? $"Summary report emailed to {recipientCount} recipients"
                    : $"Summary report emailed to {reportEmail}";
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Email Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusMessage = $"Email error: {ex.Message}";
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("You do not have permission to export reports.\n\nPlease contact your administrator.",
                    "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending summary report via email: {ex.Message}",
                    "Email Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"Error sending email: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void RaiseColumnVisibilityChanged()
        {
            RaisePropertyChanged(nameof(ShowMaterialColumns));
            RaisePropertyChanged(nameof(ShowCategoryColumn));
            RaisePropertyChanged(nameof(ShowVariantColumn));
        }

        private async Task LoadSummaryAsync()
        {
            if (IsLoading) return;
            IsLoading = true;

            try
            {
                RaiseColumnVisibilityChanged();
                StatusMessage = "Loading summary...";

                var dbPath = AppConfigurationManager.GetDatabasePath();
                if (!File.Exists(dbPath))
                {
                    StatusMessage = "Database not found";
                    return;
                }

                var groupBy = CurrentGroupBy;
                var search = SearchText?.Trim() ?? string.Empty;

                await Task.Run(async () =>
                {
                    using var conn = new SqliteConnection($"Data Source={dbPath}");
                    await conn.OpenAsync();

                    // Build WHERE clause for date filtering and search
                    var conditions = new List<string>();
                    var parameters = new List<(string name, object value)>();

                    bool filterByManufacturingDate = SelectedDateType != "Trx Date";

                    if (FromDate.HasValue)
                    {
                        if (filterByManufacturingDate)
                        {
                            conditions.Add("wt.manufacturing_date >= $fromDate");
                            parameters.Add(("$fromDate", FromDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                        }
                        else
                        {
                            conditions.Add("wt.weighed_date >= $fromDate");
                            parameters.Add(("$fromDate", FromDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 00:00:00"));
                        }
                    }
                    if (ToDate.HasValue)
                    {
                        if (filterByManufacturingDate)
                        {
                            conditions.Add("wt.manufacturing_date <= $toDate");
                            parameters.Add(("$toDate", ToDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                        }
                        else
                        {
                            conditions.Add("wt.weighed_date <= $toDate");
                            parameters.Add(("$toDate", ToDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 23:59:59"));
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        conditions.Add(@"(
                            wt.material_code LIKE $search OR
                            wt.material_name LIKE $search OR
                            COALESCE(c.name, '') LIKE $search OR
                            COALESCE(v.name, '') LIKE $search
                        )");
                        parameters.Add(("$search", $"%{search}%"));
                    }
                    if (!string.IsNullOrWhiteSpace(SelectedStatusFilter) && SelectedStatusFilter != "ALL")
                    {
                        conditions.Add("wt.status = $status");
                        parameters.Add(("$status", SelectedStatusFilter.Trim()));
                    }

                    var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

                    // Build SELECT / GROUP BY based on group-by mode
                    string selectColumns;
                    string groupByClause;
                    string orderByClause;

                    switch (groupBy)
                    {
                        case SummaryGroupBy.CategoryWise:
                            selectColumns = @"
                                '' AS material_code,
                                '' AS material_name,
                                COALESCE(c.name, 'Uncategorized') AS category_name,
                                '' AS variant_name";
                            groupByClause = "GROUP BY category_name";
                            orderByClause = "ORDER BY category_name";
                            break;

                        case SummaryGroupBy.VariantWise:
                            selectColumns = @"
                                '' AS material_code,
                                '' AS material_name,
                                '' AS category_name,
                                COALESCE(v.name, 'No Variant') AS variant_name";
                            groupByClause = "GROUP BY variant_name";
                            orderByClause = "ORDER BY variant_name";
                            break;

                        default: // ProductWise
                            selectColumns = @"
                                wt.material_code,
                                wt.material_name,
                                COALESCE(c.name, 'Uncategorized') AS category_name,
                                COALESCE(v.name, 'No Variant') AS variant_name";
                            groupByClause = "GROUP BY wt.material_code, wt.material_name, category_name, variant_name";
                            orderByClause = "ORDER BY category_name, variant_name, wt.material_name";
                            break;
                    }

                    var cmd = conn.CreateCommand();
                    cmd.CommandText = $@"
                        SELECT 
                            {selectColumns},
                            COUNT(*) AS transaction_count,
                            SUM(wt.actual_weight) AS total_weight
                        FROM weighing_transactions wt
                        LEFT JOIN materials m ON LOWER(TRIM(wt.material_code)) = LOWER(TRIM(m.product_code))
                        LEFT JOIN categories c ON m.category_id = c.id
                        LEFT JOIN variants v ON m.variant_id = v.id
                        {whereClause}
                        {groupByClause}
                        {orderByClause}";

                    foreach (var (name, value) in parameters)
                        cmd.Parameters.AddWithValue(name, value);

                    var items = new List<SummaryReportItem>();
                    int slNo = 1;
                    int totalTx = 0;
                    double totalWt = 0;

                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var txCount = reader.GetInt32(4);
                        var totWt = reader.GetDouble(5);

                        items.Add(new SummaryReportItem
                        {
                            SlNo = slNo++,
                            MaterialCode = reader.GetString(0),
                            MaterialName = reader.GetString(1),
                            CategoryName = reader.GetString(2),
                            VariantName = reader.GetString(3),
                            TransactionCount = txCount,
                            TotalWeight = totWt
                        });

                        totalTx += txCount;
                        totalWt += totWt;
                    }

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        SummaryItems.Clear();
                        foreach (var item in items)
                            SummaryItems.Add(item);

                        TotalTransactions = totalTx;
                        TotalWeight = totalWt;
                    });
                });

                var fromStr = FromDate?.ToString("dd-MM-yyyy") ?? "-";
                var toStr = ToDate?.ToString("dd-MM-yyyy") ?? "-";
                var searchInfo = string.IsNullOrEmpty(search) ? "" : $"  |  search: \"{search}\"";
                var groupLabel = SelectedGroupBy;
                StatusMessage = $"Loaded {SummaryItems.Count} summary row(s) [{groupLabel}] ({TotalTransactions} transactions, {TotalWeight:0.000} kg total) from {fromStr} to {toStr}{searchInfo}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                Debug.WriteLine($"[SummaryReportViewModel] ERROR: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ExportToExcel()
        {
            try
            {
                CheckPermissionOrThrow(_main.CurrentUser, Permission.ExportReports);

                var groupBy = CurrentGroupBy;

                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "Excel Files|*.xlsx",
                    Title = "Export Summary Report to Excel",
                    FileName = $"SummaryReport_{SelectedGroupBy.Replace(" ", "")}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };

                if (saveFileDialog.ShowDialog() != true) return;

                StatusMessage = "Exporting...";

                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Summary Report");

                // Build dynamic headers based on group-by mode
                var headers = new List<string> { "Sl.No" };

                if (ShowCategoryColumn) headers.Add("Category");
                if (ShowVariantColumn) headers.Add("Variant");
                if (ShowMaterialColumns)
                {
                    headers.Add("Material Code");
                    headers.Add("Material Name");
                }
                headers.AddRange(new[] { "Transactions", "Achieved Weight", "UOM" });

                for (int i = 0; i < headers.Count; i++)
                    worksheet.Cell(1, i + 1).Value = headers[i];

                var headerRange = worksheet.Range(1, 1, 1, headers.Count);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#555555");
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                int row = 2;
                foreach (var item in SummaryItems)
                {
                    int col = 1;
                    worksheet.Cell(row, col++).Value = item.SlNo;
                    if (ShowCategoryColumn) worksheet.Cell(row, col++).Value = item.CategoryName;
                    if (ShowVariantColumn) worksheet.Cell(row, col++).Value = item.VariantName;
                    if (ShowMaterialColumns)
                    {
                        worksheet.Cell(row, col++).Value = item.MaterialCode;
                        worksheet.Cell(row, col++).Value = item.MaterialName;
                    }
                    worksheet.Cell(row, col++).Value = item.TransactionCount;
                    worksheet.Cell(row, col++).Value = item.TotalWeight;
                    worksheet.Cell(row, col++).Value = item.UOM;
                    row++;
                }

                // Totals row
                {
                    int col = 1;
                    // Fill blank columns up to the Transactions column
                    int blankCols = headers.Count - 3; // 3 = Transactions + 2 weight columns
                    for (int i = 0; i < blankCols - 1; i++)
                        worksheet.Cell(row, col++).Value = "";
                    worksheet.Cell(row, col).Value = "TOTAL";
                    worksheet.Cell(row, col).Style.Font.Bold = true;
                    col++;
                    worksheet.Cell(row, col).Value = TotalTransactions;
                    worksheet.Cell(row, col).Style.Font.Bold = true;
                    col++;
                    worksheet.Cell(row, col).Value = TotalWeight;
                    worksheet.Cell(row, col).Style.Font.Bold = true;
                    row++;
                }

                worksheet.Columns().AdjustToContents();

                if (row > 2)
                {
                    var dataRange = worksheet.Range(1, 1, row - 1, headers.Count);
                    dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                }

                workbook.SaveAs(saveFileDialog.FileName);

                var exported = SummaryItems.Count;
                var username = _main.CurrentUser?.Username ?? "Unknown";
                _ = _auditService.LogReportExportedAsync(username, $"Summary Report - {SelectedGroupBy} (Excel)", exported);

                MessageBox.Show($"Summary report exported successfully!\n\n{exported} rows exported to:\n{saveFileDialog.FileName}",
                    "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);

                StatusMessage = $"Exported {exported} summary row(s) to Excel";
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("You do not have permission to export reports.\n\nPlease contact your administrator.",
                    "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting to Excel: {ex.Message}",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"Error exporting: {ex.Message}";
            }
        }
    }

    public class SummaryReportItem : BaseViewModel
    {
        public int SlNo { get; set; }
        public string MaterialCode { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string VariantName { get; set; } = string.Empty;
        public int TransactionCount { get; set; }
        public double TotalWeight { get; set; }
        public string UOM { get; set; } = "kg";
    }
}
