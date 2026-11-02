using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ProWeighSuitePlatforms.Configuration;
using Microsoft.Data.Sqlite;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Generates Excel report files without any UI dependency, for use by the
    /// scheduled report service. Kept as a separate copy so the interactive
    /// Reports/SummaryReport screens are not touched.
    /// </summary>
    public class ReportExportService
    {
        private readonly string _dbPath;

        public ReportExportService()
        {
            _dbPath = AppConfigurationManager.GetDatabasePath();
        }

        public ReportExportService(string dbPath)
        {
            _dbPath = dbPath;
        }

        /// <summary>
        /// Generates a Transaction Report covering [from, to] (inclusive of full days).
        /// Returns the path of the generated .xlsx file.
        /// </summary>
        public async Task<string> GenerateTransactionReportAsync(DateTime from, DateTime to)
        {
            if (!File.Exists(_dbPath))
                throw new InvalidOperationException("Database not found.");

            var fromDate = from.Date;
            var toDate = to.Date;
            var filePath = Path.Combine(Path.GetTempPath(), $"DailyTransactionReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
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
                WHERE weighing_transactions.manufacturing_date >= $fromDate
                  AND weighing_transactions.manufacturing_date <= $toDate
                ORDER BY weighing_transactions.weighed_date DESC, weighing_transactions.id DESC";
            cmd.Parameters.AddWithValue("$fromDate", fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$toDate", toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            using var workbook = new XLWorkbook();
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
            using (var reader = cmd.ExecuteReader())
            {
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
            }

            worksheet.Columns().AdjustToContents();

            if (row > 2)
            {
                var dataRange = worksheet.Range(1, 1, row - 1, headers.Length);
                dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            }

            workbook.SaveAs(filePath);
            return filePath;
        }

        /// <summary>
        /// Generates a Summary Report (grouped) covering [from, to] (inclusive of full days).
        /// Returns the path of the generated .xlsx file.
        /// </summary>
        public async Task<string> GenerateSummaryReportAsync(string groupBy, DateTime from, DateTime to)
        {
            if (!File.Exists(_dbPath))
                throw new InvalidOperationException("Database not found.");

            var fromDate = from.Date;
            var toDate = to.Date;
            var filePath = Path.Combine(Path.GetTempPath(), $"DailySummaryReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

            string selectColumns;
            string groupByClause;
            string orderByClause;

            switch (groupBy)
            {
                case "Category Wise":
                    selectColumns = @"
                        '' AS material_code,
                        '' AS material_name,
                        COALESCE(c.name, 'Uncategorized') AS category_name,
                        '' AS variant_name";
                    groupByClause = "GROUP BY category_name";
                    orderByClause = "ORDER BY category_name";
                    break;

                case "Variant Wise":
                    selectColumns = @"
                        '' AS material_code,
                        '' AS material_name,
                        '' AS category_name,
                        COALESCE(v.name, 'No Variant') AS variant_name";
                    groupByClause = "GROUP BY variant_name";
                    orderByClause = "ORDER BY variant_name";
                    break;

                default: // Product Wise
                    selectColumns = @"
                        wt.material_code,
                        wt.material_name,
                        COALESCE(c.name, 'Uncategorized') AS category_name,
                        COALESCE(v.name, 'No Variant') AS variant_name";
                    groupByClause = "GROUP BY wt.material_code, wt.material_name, category_name, variant_name";
                    orderByClause = "ORDER BY category_name, variant_name, wt.material_name";
                    break;
            }

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();

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
                WHERE wt.manufacturing_date >= $fromDate
                  AND wt.manufacturing_date <= $toDate
                {groupByClause}
                {orderByClause}";
            cmd.Parameters.AddWithValue("$fromDate", fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$toDate", toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Summary Report");

            var showCategory = groupBy != "Variant Wise";
            var showVariant = groupBy != "Category Wise";
            var showMaterial = groupBy == "Product Wise";

            var headers = new List<string> { "Sl.No" };
            if (showCategory) headers.Add("Category");
            if (showVariant) headers.Add("Variant");
            if (showMaterial)
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
            int totalTx = 0;
            double totalWt = 0;

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    int col = 1;
                    worksheet.Cell(row, col++).Value = row - 1;
                    if (showCategory) worksheet.Cell(row, col++).Value = reader.GetString(2);
                    if (showVariant) worksheet.Cell(row, col++).Value = reader.GetString(3);
                    if (showMaterial)
                    {
                        worksheet.Cell(row, col++).Value = reader.GetString(0);
                        worksheet.Cell(row, col++).Value = reader.GetString(1);
                    }
                    var txCount = reader.GetInt32(4);
                    var totWt = reader.GetDouble(5);
                    worksheet.Cell(row, col++).Value = txCount;
                    worksheet.Cell(row, col++).Value = totWt;
                    worksheet.Cell(row, col++).Value = "kg";

                    totalTx += txCount;
                    totalWt += totWt;
                    row++;
                }
            }

            {
                int col = 1;
                int blankCols = headers.Count - 3;
                for (int i = 0; i < blankCols - 1; i++)
                    worksheet.Cell(row, col++).Value = "";
                worksheet.Cell(row, col).Value = "TOTAL";
                worksheet.Cell(row, col).Style.Font.Bold = true;
                col++;
                worksheet.Cell(row, col).Value = totalTx;
                worksheet.Cell(row, col).Style.Font.Bold = true;
                col++;
                worksheet.Cell(row, col).Value = totalWt;
                worksheet.Cell(row, col).Style.Font.Bold = true;
            }

            worksheet.Columns().AdjustToContents();

            if (row > 2)
            {
                var dataRange = worksheet.Range(1, 1, row, headers.Count);
                dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            }

            workbook.SaveAs(filePath);
            return filePath;
        }

        private static DateTime ParseWeighedDate(string weighedDateStr)
        {
            if (DateTime.TryParse(weighedDateStr.Replace(".", ":"), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt))
                return dt;

            if (DateTime.TryParseExact(weighedDateStr, "yyyy-MM-dd HH.mm.ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return dt;

            return DateTime.MinValue;
        }

        private static string FormatToDisplayDate(string? dateStr)
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
    }
}
