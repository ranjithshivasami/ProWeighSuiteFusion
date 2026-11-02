using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Security;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;
using ClosedXML.Excel;
using Microsoft.Win32;
using ProWeighSuitePlatforms.Configuration;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class ReportsViewModel : BaseViewModel
    {
        private readonly ICurrentUserContext _main;
        private readonly AuditService _auditService;
        private readonly CompanySettingsService _companySettingsService;

        public string Title => "Transaction Reports";

        // Permission-based properties
        public bool CanExportReports => HasPermission(_main.CurrentUser, Permission.ExportReports);
        public ICommand RefreshReportCommand { get; }
        public ICommand FilterReportCommand { get; }
        public ICommand ExportToExcelCommand { get; }
        public ICommand EmailReportCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand LastPageCommand { get; }
        public ICommand CopyTransactionIdCommand { get; }
        public ICommand CopyBatchNumberCommand { get; }
        public ICommand CopyMaterialCodeCommand { get; }

        public ObservableCollection<TransactionReportItem> Transactions { get; } = new();

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    // Debounce: cancel previous timer, start a new 300ms delay
                    _searchDebounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    _searchDebounceTimer?.Change(300, Timeout.Infinite);
                }
            }
        }
        private string _searchText = string.Empty;
        private Timer? _searchDebounceTimer;

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
                    NotifyColumnSelectionChanged();
                }
            }
        }

        public event EventHandler? ColumnSelectionChanged;

        private void NotifyColumnSelectionChanged()
        {
            ColumnSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public SelectableColumn? GetColumnByKey(string key)
        {
            foreach (var col in AvailableColumns)
                if (col.Key == key) return col;
            return null;
        }

        private void InitializeColumns()
        {
            var columns = new[]
            {
                "Sl.No", "Date", "Time", "Transaction UUID", "Batch No",
                "Material Code", "Material Name", "Category", "Variant", "Target Weight", "Weight", "Preset Tare", "UOM", "Status", "Result",
                "Equipment ID", "Location", "Weighed By", "Manufacturing Date",
                "Expiry Date", "Customer Name", "Remarks"
            };
            foreach (var col in columns)
            {
                var selectable = new SelectableColumn { Key = col, DisplayName = col, IsSelected = true };
                selectable.SelectionChanged += (_, _) => NotifyColumnSelectionChanged();
                AvailableColumns.Add(selectable);
            }
        }

        // Date filters
        public DateTime? FromDate
        {
            get => _fromDate;
            set => SetProperty(ref _fromDate, value);
        }
        private DateTime? _fromDate = null;

        public DateTime? ToDate
        {
            get => _toDate;
            set => SetProperty(ref _toDate, value);
        }
        private DateTime? _toDate = null;

        public string FromDateText
        {
            get => _fromDateText;
            set => SetProperty(ref _fromDateText, value);
        }
        private string _fromDateText = string.Empty;

        public string ToDateText
        {
            get => _toDateText;
            set => SetProperty(ref _toDateText, value);
        }
        private string _toDateText = string.Empty;

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

        // Pagination
        private int _pageSize = 50;
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (SetProperty(ref _pageSize, value))
                {
                    CurrentPage = 1;
                    _ = LoadPageAsync();
                }
            }
        }

        private int _currentPage = 1;
        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    RaisePropertyChanged(nameof(PageInfo));
                }
            }
        }

        private int _totalPages = 1;
        public int TotalPages
        {
            get => _totalPages;
            set
            {
                if (SetProperty(ref _totalPages, value))
                {
                    RaisePropertyChanged(nameof(PageInfo));
                }
            }
        }

        private int _totalRecords;
        public int TotalRecords
        {
            get => _totalRecords;
            set
            {
                if (SetProperty(ref _totalRecords, value))
                {
                    RaisePropertyChanged(nameof(PageInfo));
                }
            }
        }

        public string PageInfo => $"Page {CurrentPage} of {TotalPages}  ({TotalRecords} records)";

        public ReportsViewModel(ICurrentUserContext main)
        {
            _main = main;

            var dbPath = AppConfigurationManager.GetDatabasePath();
            _auditService = new AuditService();
            _companySettingsService = new CompanySettingsService(new CompanySettingsRepository(dbPath));

            // Initialize column selector
            InitializeColumns();

            // Debounce timer for search
            _searchDebounceTimer = new Timer(_ =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    CurrentPage = 1;
                    _ = LoadPageAsync();
                });
            });

            // Subscribe to repository notifications
            WeighingRepository.TransactionInserted += OnTransactionInserted;

            RefreshReportCommand = new RelayCommand(async _ => { CurrentPage = 1; await LoadPageAsync(); });
            FilterReportCommand = new RelayCommand(async _ => { CurrentPage = 1; await LoadPageAsync(); });
            ExportToExcelCommand = new RelayCommand(_ => ExportToExcel(), _ => TotalRecords > 0 && CanExportReports && !IsLoading);
            EmailReportCommand = new RelayCommand(async _ => await EmailReportAsync(), _ => TotalRecords > 0 && CanExportReports && !IsLoading);

            FirstPageCommand = new RelayCommand(async _ => { if (CurrentPage != 1) { CurrentPage = 1; await LoadPageAsync(); } });
            PrevPageCommand = new RelayCommand(async _ => { if (CurrentPage > 1) { CurrentPage--; await LoadPageAsync(); } });
            NextPageCommand = new RelayCommand(async _ => { if (CurrentPage < TotalPages) { CurrentPage++; await LoadPageAsync(); } });
            LastPageCommand = new RelayCommand(async _ => { if (CurrentPage != TotalPages) { CurrentPage = TotalPages; await LoadPageAsync(); } });
            CopyTransactionIdCommand = new RelayCommand(param => CopyToClipboard(param as TransactionReportItem, "TransactionId"));
            CopyBatchNumberCommand = new RelayCommand(param => CopyToClipboard(param as TransactionReportItem, "BatchNumber"));
            CopyMaterialCodeCommand = new RelayCommand(param => CopyToClipboard(param as TransactionReportItem, "MaterialCode"));

            // Apply default filter: current date
            if (!FromDate.HasValue && !ToDate.HasValue && string.IsNullOrWhiteSpace(FromDateText) && string.IsNullOrWhiteSpace(ToDateText))
            {
                var now = DateTime.Now;
                FromDate = now.Date;
                ToDate = now.Date;
            }

            _ = LoadPageAsync();
        }

        private void CopyToClipboard(TransactionReportItem? item, string fieldName)
        {
            if (item == null) return;

            try
            {
                string valueToCopy = fieldName switch
                {
                    "TransactionId" => item.TransactionId.ToUpperInvariant(),
                    "BatchNumber" => item.BatchNumber,
                    "MaterialCode" => item.MaterialCode,
                    _ => string.Empty
                };

                string displayName = fieldName switch
                {
                    "TransactionId" => "Transaction UUID",
                    "BatchNumber" => "Batch Number",
                    "MaterialCode" => "Material Code",
                    _ => fieldName
                };

                if (!string.IsNullOrEmpty(valueToCopy))
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        Clipboard.SetText(valueToCopy);
                        StatusMessage = $"{displayName} copied to clipboard: {valueToCopy}";
                    });
                }
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error copying to clipboard: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        /// <summary>
        /// Resolves effective from/to dates from both DatePicker and text-entry inputs.
        /// </summary>
        private (DateTime? from, DateTime? to) ResolveFilterDates()
        {
            DateTime? parsedFrom = null;
            DateTime? parsedTo = null;
            if (!string.IsNullOrWhiteSpace(FromDateText))
            {
                if (DateTime.TryParseExact(FromDateText.Trim(), new[] { "dd-MM-yyyy", "yyyy-MM-dd", "dd/MM/yyyy" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    parsedFrom = dt.Date;
            }
            if (!string.IsNullOrWhiteSpace(ToDateText))
            {
                if (DateTime.TryParseExact(ToDateText.Trim(), new[] { "dd-MM-yyyy", "yyyy-MM-dd", "dd/MM/yyyy" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
                    parsedTo = dt2.Date;
            }
            return (parsedFrom ?? FromDate, parsedTo ?? ToDate);
        }

        /// <summary>
        /// Loads a single page of data from SQLite using LIMIT/OFFSET � the core optimization.
        /// </summary>
        private async Task LoadPageAsync()
        {
            if (IsLoading) return;
            IsLoading = true;

            try
            {
                StatusMessage = "Loading...";
                var (fromDate, toDate) = ResolveFilterDates();
                var search = SearchText?.Trim() ?? string.Empty;

                var dbPath = AppConfigurationManager.GetDatabasePath();
                if (!File.Exists(dbPath))
                {
                    StatusMessage = "Database not found";
                    return;
                }

                await Task.Run(async () =>
                {
                    using var conn = new SqliteConnection($"Data Source={dbPath}");
                    await conn.OpenAsync();

                    // Build shared WHERE clause
                    var (whereClause, addParams) = BuildWhereClause(fromDate, toDate, search, SelectedStatusFilter);

                    // 1. Get total count
                    var countCmd = conn.CreateCommand();
                    countCmd.CommandText = $"SELECT COUNT(*) FROM weighing_transactions {whereClause}";
                    addParams(countCmd);
                    var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

                    // 2. Calculate paging
                    var totalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)PageSize);
                    var currentPage = Math.Clamp(CurrentPage, 1, totalPages);
                    var offset = (currentPage - 1) * PageSize;

                    // 3. Fetch only the current page
                    var dataCmd = conn.CreateCommand();
                    dataCmd.CommandText = $@"
                        SELECT weighing_transactions.id, weighing_transactions.batch_id, weighing_transactions.batch_number, weighing_transactions.material_code, weighing_transactions.material_name,
                               weighing_transactions.sequence, weighing_transactions.target_weight, weighing_transactions.lower_weight, weighing_transactions.upper_weight, weighing_transactions.actual_weight, weighing_transactions.deviation,
                               weighing_transactions.status, weighing_transactions.weighed_by, weighing_transactions.weighed_date, weighing_transactions.printed_date, weighing_transactions.customer_name, weighing_transactions.manufacturing_date,
                               weighing_transactions.expiry_date, weighing_transactions.equipment_id, weighing_transactions.location, weighing_transactions.remarks, weighing_transactions.tare_weight,
                               COALESCE(c.name, '') as category_name, COALESCE(v.name, '') as variant_name
                        FROM weighing_transactions
                        LEFT JOIN materials m ON LOWER(TRIM(m.product_code)) = LOWER(TRIM(weighing_transactions.material_code))
                        LEFT JOIN categories c ON m.category_id = c.id
                        LEFT JOIN variants v ON m.variant_id = v.id
                        {whereClause}
                        ORDER BY weighing_transactions.weighed_date DESC, weighing_transactions.id DESC
                        LIMIT $limit OFFSET $offset";
                    addParams(dataCmd);
                    dataCmd.Parameters.AddWithValue("$limit", PageSize);
                    dataCmd.Parameters.AddWithValue("$offset", offset);

                    var items = new List<TransactionReportItem>();
                    int slNo = offset + 1;

                    using var reader = await dataCmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var weighedDateStr = reader.GetString(13);
                        var weighedDate = ParseWeighedDate(weighedDateStr);

                        items.Add(new TransactionReportItem
                        {
                            SlNo = slNo++,
                            WeighedDate = weighedDate,
                            Date = weighedDate.ToString("dd-MM-yyyy"),
                            Time = weighedDate.ToString("HH:mm"),
                            TransactionId = reader.GetString(0),
                            BatchNumber = reader.GetString(2),
                            MaterialCode = reader.GetString(3),
                            MaterialName = reader.GetString(4),
                            Weight = $"{reader.GetDouble(9):0.000}",
                            TargetWeight = $"{reader.GetDouble(6):0.000}",
                            Deviation = $"{reader.GetDouble(10):+0.00;-0.00;0.00}",
                            TareWeight = $"{(reader.IsDBNull(21) ? 0.0 : reader.GetDouble(21)):0.000}",
                            EquipmentId = reader.IsDBNull(18) ? string.Empty : reader.GetString(18),
                            Location = reader.IsDBNull(19) ? string.Empty : reader.GetString(19),
                            Login = reader.GetString(12),
                            Status = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                            ManufacturingDate = FormatToDisplayDate(reader.IsDBNull(16) ? null : reader.GetString(16)),
                            ExpiryDate = FormatToDisplayDate(reader.IsDBNull(17) ? null : reader.GetString(17)),
                            CustomerName = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                            Remarks = reader.IsDBNull(20) ? string.Empty : reader.GetString(20),
                            Category = reader.IsDBNull(22) ? string.Empty : reader.GetString(22),
                            Variant = reader.IsDBNull(23) ? string.Empty : reader.GetString(23)
                        });
                    }

                    // 4. Update UI in one batch
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        Transactions.Clear();
                        foreach (var item in items)
                            Transactions.Add(item);

                        TotalRecords = totalCount;
                        TotalPages = totalPages;
                        CurrentPage = currentPage;
                    });
                });

                var fromStr = fromDate?.ToString("dd-MM-yyyy") ?? "-";
                var toStr = toDate?.ToString("dd-MM-yyyy") ?? "-";
                var searchInfo = string.IsNullOrEmpty(search) ? "" : $"  |  search: \"{search}\"";
                StatusMessage = $"Loaded {Transactions.Count} of {TotalRecords} transaction(s) from {fromStr} to {toStr}{searchInfo}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                Debug.WriteLine($"[ReportsViewModel] ERROR: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Builds a SQL WHERE clause for date filtering and optional text search.
        /// Returns the clause string and an Action to add parameters to a command.
        /// </summary>
        private (string whereClause, Action<SqliteCommand> addParams) BuildWhereClause(
            DateTime? fromDate, DateTime? toDate, string search, string statusFilter)
        {
            var conditions = new List<string>();
            var parameters = new List<(string name, object value)>();

            bool filterByManufacturingDate = SelectedDateType != "Trx Date";

            if (fromDate.HasValue)
            {
                if (filterByManufacturingDate)
                {
                    conditions.Add("weighing_transactions.manufacturing_date >= $fromDate");
                    parameters.Add(("$fromDate", fromDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                }
                else
                {
                    conditions.Add("weighing_transactions.weighed_date >= $fromDate");
                    parameters.Add(("$fromDate", fromDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 00:00:00"));
                }
            }
            if (toDate.HasValue)
            {
                if (filterByManufacturingDate)
                {
                    conditions.Add("weighing_transactions.manufacturing_date <= $toDate");
                    parameters.Add(("$toDate", toDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                }
                else
                {
                    conditions.Add("weighing_transactions.weighed_date <= $toDate");
                    parameters.Add(("$toDate", toDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 23:59:59"));
                }
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                conditions.Add(@"(
                    weighing_transactions.batch_number LIKE $search OR
                    weighing_transactions.material_code LIKE $search OR
                    weighing_transactions.material_name LIKE $search OR
                    weighing_transactions.weighed_by LIKE $search OR
                    weighing_transactions.customer_name LIKE $search OR
                    weighing_transactions.equipment_id LIKE $search OR
                    weighing_transactions.location LIKE $search OR
                    weighing_transactions.remarks LIKE $search OR
                    CAST(weighing_transactions.id AS TEXT) LIKE $search
                )");
                parameters.Add(("$search", $"%{search}%"));
            }

            if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "ALL")
            {
                conditions.Add("weighing_transactions.status = $status");
                parameters.Add(("$status", statusFilter.Trim()));
            }

            var clause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
            void AddParams(SqliteCommand cmd)
            {
                foreach (var (name, value) in parameters)
                    cmd.Parameters.AddWithValue(name, value);
            }

            return (clause, AddParams);
        }

        private static DateTime ParseWeighedDate(string weighedDateStr)
        {
            // Fast path: standard format stored by the app
            if (DateTime.TryParse(weighedDateStr.Replace(".", ":"), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt))
                return dt;

            if (DateTime.TryParseExact(weighedDateStr, "yyyy-MM-dd HH.mm.ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return dt;

            return DateTime.MinValue;
        }

        private void ExportToExcel()
        {
            try
            {
                CheckPermissionOrThrow(_main.CurrentUser, Permission.ExportReports);

                var (fromDate, toDate) = ResolveFilterDates();
                var search = SearchText?.Trim() ?? string.Empty;

                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "Excel Files|*.xlsx",
                    Title = "Export Transactions to Excel",
                    FileName = $"TransactionReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };

                if (saveFileDialog.ShowDialog() != true) return;

                StatusMessage = "Exporting...";

                // Fetch ALL matching records for export (not just current page)
                var dbPath = AppConfigurationManager.GetDatabasePath();
                using var conn = new SqliteConnection($"Data Source={dbPath}");
                conn.Open();

                var (whereClause, addParams) = BuildWhereClause(fromDate, toDate, search, SelectedStatusFilter);
                var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
                    SELECT weighing_transactions.id, weighing_transactions.batch_number, weighing_transactions.material_code, weighing_transactions.material_name,
                           weighing_transactions.actual_weight, weighing_transactions.tare_weight, weighing_transactions.weighed_by, weighing_transactions.weighed_date,
                           weighing_transactions.customer_name, weighing_transactions.manufacturing_date, weighing_transactions.expiry_date,
                           weighing_transactions.equipment_id, weighing_transactions.location, weighing_transactions.remarks,
                           COALESCE(c.name, '') as category_name, COALESCE(v.name, '') as variant_name,
                           weighing_transactions.status, weighing_transactions.deviation, weighing_transactions.target_weight
                    FROM weighing_transactions
                    LEFT JOIN materials m ON LOWER(TRIM(m.product_code)) = LOWER(TRIM(weighing_transactions.material_code))
                    LEFT JOIN categories c ON m.category_id = c.id
                    LEFT JOIN variants v ON m.variant_id = v.id
                    {whereClause}
                    ORDER BY weighing_transactions.weighed_date DESC, weighing_transactions.id DESC";
                addParams(cmd);

                using var workbook = new XLWorkbook();
                var worksheet = workbook.Worksheets.Add("Transaction Report");

                // Headers
                var headers = new[] { "Sl.No", "Date", "Time", "Transaction Id", "Batch No",
                    "Material Code", "Material Name", "Category", "Variant", "Target Weight", "Weight", "Tare Weight", "UOM", "Status", "Result",
                    "Equipment ID", "Location", "Weighed By", "Manufacturing Date",
                    "Expiry Date", "Customer Name", "Remarks" };
                for (int i = 0; i < headers.Length; i++)
                    worksheet.Cell(1, i + 1).Value = headers[i];

                var headerRange = worksheet.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#555555");
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                int row = 2;
                int slNo = 1;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var dateStr = reader.GetString(7);
                    var dt = ParseWeighedDate(dateStr);

                    worksheet.Cell(row, 1).Value = slNo++;
                    worksheet.Cell(row, 2).Value = dt.ToString("dd-MM-yyyy");
                    worksheet.Cell(row, 3).Value = dt.ToString("HH:mm");
                    worksheet.Cell(row, 4).Value = reader.GetString(0);
                    worksheet.Cell(row, 5).Value = reader.GetString(1);
                    worksheet.Cell(row, 6).Value = reader.GetString(2);
                    worksheet.Cell(row, 7).Value = reader.GetString(3);
                    worksheet.Cell(row, 8).Value = reader.IsDBNull(14) ? "" : reader.GetString(14);
                    worksheet.Cell(row, 9).Value = reader.IsDBNull(15) ? "" : reader.GetString(15);
                    worksheet.Cell(row, 10).Value = reader.GetDouble(18);
                    worksheet.Cell(row, 11).Value = reader.GetDouble(4);
                    worksheet.Cell(row, 12).Value = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5);
                    worksheet.Cell(row, 13).Value = "kg";
                    worksheet.Cell(row, 14).Value = reader.IsDBNull(16) ? "" : reader.GetString(16);
                    worksheet.Cell(row, 15).Value = reader.IsDBNull(16) ? "" : reader.GetString(16);
                    worksheet.Cell(row, 16).Value = reader.IsDBNull(11) ? "" : reader.GetString(11);
                    worksheet.Cell(row, 17).Value = reader.IsDBNull(12) ? "" : reader.GetString(12);
                    worksheet.Cell(row, 18).Value = reader.GetString(6);
                    worksheet.Cell(row, 19).Value = FormatToDisplayDate(reader.IsDBNull(9) ? null : reader.GetString(9));
                    worksheet.Cell(row, 20).Value = FormatToDisplayDate(reader.IsDBNull(10) ? null : reader.GetString(10));
                    worksheet.Cell(row, 21).Value = reader.IsDBNull(8) ? "" : reader.GetString(8);
                    worksheet.Cell(row, 22).Value = reader.IsDBNull(13) ? "" : reader.GetString(13);
                    row++;
                }

                worksheet.Columns().AdjustToContents();

                if (row > 2)
                {
                    var dataRange = worksheet.Range(1, 1, row - 1, headers.Length);
                    dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                }

                workbook.SaveAs(saveFileDialog.FileName);

                var exported = row - 2;
                var username = _main.CurrentUser?.Username ?? "Unknown";
                _ = _auditService.LogReportExportedAsync(username, "Transaction Report (Excel)", exported);

                MessageBox.Show($"Report exported successfully!\n\n{exported} records exported to:\n{saveFileDialog.FileName}",
                    "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);

                StatusMessage = $"Exported {exported} transaction(s) to Excel";
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

        private async Task EmailReportAsync()
        {
            if (IsLoading) return;
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

                StatusMessage = "Generating report for email...";

                var (fromDate, toDate) = ResolveFilterDates();
                var search = SearchText?.Trim() ?? string.Empty;

                var dbPath = AppConfigurationManager.GetDatabasePath();
                using var conn = new SqliteConnection($"Data Source={dbPath}");
                conn.Open();

                var (whereClause, addParams) = BuildWhereClause(fromDate, toDate, search, SelectedStatusFilter);
                var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
                    SELECT weighing_transactions.id, weighing_transactions.batch_number, weighing_transactions.material_code, weighing_transactions.material_name,
                           weighing_transactions.actual_weight, weighing_transactions.tare_weight, weighing_transactions.weighed_by, weighing_transactions.weighed_date,
                           weighing_transactions.customer_name, weighing_transactions.manufacturing_date, weighing_transactions.expiry_date,
                           weighing_transactions.equipment_id, weighing_transactions.location, weighing_transactions.remarks,
                           COALESCE(c.name, '') as category_name, COALESCE(v.name, '') as variant_name,
                           weighing_transactions.status, weighing_transactions.deviation, weighing_transactions.target_weight
                    FROM weighing_transactions
                    LEFT JOIN materials m ON LOWER(TRIM(m.product_code)) = LOWER(TRIM(weighing_transactions.material_code))
                    LEFT JOIN categories c ON m.category_id = c.id
                    LEFT JOIN variants v ON m.variant_id = v.id
                    {whereClause}
                    ORDER BY weighing_transactions.weighed_date DESC, weighing_transactions.id DESC";
                addParams(cmd);

                var tempPath = Path.Combine(Path.GetTempPath(), $"TransactionReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                int recordCount = 0;

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("Transaction Report");

                    var headers = new[] { "Sl.No", "Date", "Time", "Transaction Id", "Batch No",
                        "Material Code", "Material Name", "Category", "Variant", "Target Weight", "Weight", "Tare Weight", "UOM", "Status", "Result",
                        "Equipment ID", "Location", "Weighed By", "Manufacturing Date",
                        "Expiry Date", "Customer Name", "Remarks" };
                    for (int i = 0; i < headers.Length; i++)
                        worksheet.Cell(1, i + 1).Value = headers[i];

                    var headerRange = worksheet.Range(1, 1, 1, headers.Length);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#555555");
                    headerRange.Style.Font.FontColor = XLColor.White;
                    headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                    int row = 2;
                    int slNo = 1;
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var dateStr = reader.GetString(7);
                        var dt = ParseWeighedDate(dateStr);

                        worksheet.Cell(row, 1).Value = slNo++;
                        worksheet.Cell(row, 2).Value = dt.ToString("dd-MM-yyyy");
                        worksheet.Cell(row, 3).Value = dt.ToString("HH:mm");
                        worksheet.Cell(row, 4).Value = reader.GetString(0);
                        worksheet.Cell(row, 5).Value = reader.GetString(1);
                        worksheet.Cell(row, 6).Value = reader.GetString(2);
                        worksheet.Cell(row, 7).Value = reader.GetString(3);
                        worksheet.Cell(row, 8).Value = reader.IsDBNull(14) ? "" : reader.GetString(14);
                        worksheet.Cell(row, 9).Value = reader.IsDBNull(15) ? "" : reader.GetString(15);
                        worksheet.Cell(row, 10).Value = reader.GetDouble(18);
                        worksheet.Cell(row, 11).Value = reader.GetDouble(4);
                        worksheet.Cell(row, 12).Value = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5);
                        worksheet.Cell(row, 13).Value = "kg";
                        worksheet.Cell(row, 14).Value = reader.IsDBNull(16) ? "" : reader.GetString(16);
                        worksheet.Cell(row, 15).Value = reader.IsDBNull(16) ? "" : reader.GetString(16);
                        worksheet.Cell(row, 16).Value = reader.IsDBNull(11) ? "" : reader.GetString(11);
                        worksheet.Cell(row, 17).Value = reader.IsDBNull(12) ? "" : reader.GetString(12);
                        worksheet.Cell(row, 18).Value = reader.GetString(6);
                        worksheet.Cell(row, 19).Value = FormatToDisplayDate(reader.IsDBNull(9) ? null : reader.GetString(9));
                        worksheet.Cell(row, 20).Value = FormatToDisplayDate(reader.IsDBNull(10) ? null : reader.GetString(10));
                        worksheet.Cell(row, 21).Value = reader.IsDBNull(8) ? "" : reader.GetString(8);
                        worksheet.Cell(row, 22).Value = reader.IsDBNull(13) ? "" : reader.GetString(13);
                        row++;
                    }

                    worksheet.Columns().AdjustToContents();

                    if (row > 2)
                    {
                        var dataRange = worksheet.Range(1, 1, row - 1, headers.Length);
                        dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    }

                    recordCount = slNo - 1;
                    workbook.SaveAs(tempPath);
                }

                var attachmentName = $"TransactionReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                var subject = $"Transaction Report - {fromDate?.ToString("dd-MM-yyyy") ?? "-"} to {toDate?.ToString("dd-MM-yyyy") ?? "-"}";
                var body = $"Please find attached the Transaction Report.\n\nGenerated on: {DateTime.Now:dd-MM-yyyy HH:mm:ss}\nPeriod: {fromDate?.ToString("dd-MM-yyyy") ?? "-"} to {toDate?.ToString("dd-MM-yyyy") ?? "-"}";

                var emailService = new EmailService();
                await emailService.SendEmailWithAttachmentAsync(reportEmail, subject, body, tempPath, attachmentName);

                try { File.Delete(tempPath); } catch { }

                var username = _main.CurrentUser?.Username ?? "Unknown";
                _ = _auditService.LogReportExportedAsync(username, "Transaction Report (Email)", recordCount);

                var recipientCount = reportEmail.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
                var displayMsg = recipientCount > 1
                    ? $"Report sent successfully to {recipientCount} recipients!"
                    : $"Report sent successfully to {reportEmail}!";
                MessageBox.Show(displayMsg, "Email Sent", MessageBoxButton.OK, MessageBoxImage.Information);

                StatusMessage = recipientCount > 1
                    ? $"Report emailed to {recipientCount} recipients"
                    : $"Report emailed to {reportEmail}";
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
                MessageBox.Show($"Error sending report via email: {ex.Message}",
                    "Email Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"Error sending email: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                IsEmailSending = false;
            }
        }

        private string FormatToDisplayDate(string? dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr))
                return string.Empty;

            dateStr = dateStr.Trim();

            var formats = new[]
            {
                "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH.mm.ss",
                "yyyy-MM-ddTHH:mm:ss", "dd-MM-yyyy", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy/MM/dd"
            };

            if (DateTime.TryParseExact(dateStr, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt.ToString("dd-MM-yyyy");

            if (DateTime.TryParse(dateStr, out dt))
                return dt.ToString("dd-MM-yyyy");

            return dateStr;
        }

        private void OnTransactionInserted(string dbPath, string id)
        {
            try
            {
                var currentDb = AppConfigurationManager.GetDatabasePath();
                if (string.Equals(Path.GetFullPath(dbPath), Path.GetFullPath(currentDb), StringComparison.OrdinalIgnoreCase))
                {
                    // Only update total count and status, don't reload the entire page
                    Application.Current?.Dispatcher.InvokeAsync(async () =>
                    {
                        // Refresh current page data
                        await LoadPageAsync();
                    });
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    public class TransactionReportItem : BaseViewModel
    {
        public int SlNo { get; set; }
        public string Date { get; set; } = string.Empty;
        public DateTime WeighedDate { get; set; }
        public string Time { get; set; } = string.Empty;
        public string TransactionId { get; set; } = string.Empty;
        public string TransactionIdUpperCase => TransactionId.ToUpperInvariant();
        public string BatchNumber { get; set; } = string.Empty;
        public string MaterialCode { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Variant { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Weight { get; set; } = string.Empty;
        public string TargetWeight { get; set; } = string.Empty;
        public string Deviation { get; set; } = string.Empty;
        public string TareWeight { get; set; } = string.Empty;
        public string Unit { get; } = "kg";
        public string EquipmentId { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Login { get; set; } = string.Empty;
        public string ManufacturingDate { get; set; } = string.Empty;
        public string ExpiryDate { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string Remarks { get; set; } = string.Empty;
    }

}
