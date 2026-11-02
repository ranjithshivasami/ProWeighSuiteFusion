using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Core.Wpf.Navigation;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class DashboardViewModel : BaseViewModel
    {
        private readonly IShellNavigator _shellNavigator;
        private readonly WeighingService _weighingService;

        public User? CurrentUser { get; }

        public string Title => "Dashboard";
        public ICommand FilterCommand { get; }

        public string WelcomeMessage => CurrentUser != null
            ? $"Welcome, {CurrentUser.Username}!"
            : "Welcome to ProWeigh Suite";

        // ?? Filters ???????????????????????????????????????????????????????????
        private DateTime? _fromDate;
        public DateTime? FromDate
        {
            get => _fromDate;
            set => SetProperty(ref _fromDate, value);
        }

        private DateTime? _toDate;
        public DateTime? ToDate
        {
            get => _toDate;
            set => SetProperty(ref _toDate, value);
        }

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

        // ?? Summary cards ?????????????????????????????????????????????????????
        private int _todayTransactions;
        public int TodayTransactions
        {
            get => _todayTransactions;
            set => SetProperty(ref _todayTransactions, value);
        }

        private double _todayWeight;
        public double TodayWeight
        {
            get => _todayWeight;
            set => SetProperty(ref _todayWeight, value);
        }

        private int _filteredTransactions;
        public int FilteredTransactions
        {
            get => _filteredTransactions;
            set => SetProperty(ref _filteredTransactions, value);
        }

        private double _filteredWeight;
        public double FilteredWeight
        {
            get => _filteredWeight;
            set => SetProperty(ref _filteredWeight, value);
        }

        private int _totalMaterials;
        public int TotalMaterials
        {
            get => _totalMaterials;
            set => SetProperty(ref _totalMaterials, value);
        }

        private double _avgWeight;
        public double AvgWeight
        {
            get => _avgWeight;
            set => SetProperty(ref _avgWeight, value);
        }

        private int _failedSyncCount;
        public int FailedSyncCount
        {
            get => _failedSyncCount;
            set
            {
                if (SetProperty(ref _failedSyncCount, value))
                    RaisePropertyChanged(nameof(IsFailedSyncBlink));
            }
        }

        public bool IsFailedSyncBlink => FailedSyncCount > 1;

        /// <summary>
        /// Hides the Failed Syncs card while sync is disabled in App Settings
        /// ("Enable Sync Setting" unchecked).
        /// </summary>
        public bool IsSyncUiVisible => ProWeighSuitePlatforms.Services.SyncGate.Enabled;

        public ICommand NavigateToFailedSyncsCommand { get; }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // ?? Chart series ??????????????????????????????????????????????????????
        private ISeries[] _dailyWeightSeries = Array.Empty<ISeries>();
        public ISeries[] DailyWeightSeries
        {
            get => _dailyWeightSeries;
            set => SetProperty(ref _dailyWeightSeries, value);
        }

        private Axis[] _dailyWeightXAxes = Array.Empty<Axis>();
        public Axis[] DailyWeightXAxes
        {
            get => _dailyWeightXAxes;
            set => SetProperty(ref _dailyWeightXAxes, value);
        }

        private Axis[] _dailyWeightYAxes = Array.Empty<Axis>();
        public Axis[] DailyWeightYAxes
        {
            get => _dailyWeightYAxes;
            set => SetProperty(ref _dailyWeightYAxes, value);
        }

        private ISeries[] _materialBarSeries = Array.Empty<ISeries>();
        public ISeries[] MaterialBarSeries
        {
            get => _materialBarSeries;
            set => SetProperty(ref _materialBarSeries, value);
        }

        private Axis[] _materialXAxes = Array.Empty<Axis>();
        public Axis[] MaterialXAxes
        {
            get => _materialXAxes;
            set => SetProperty(ref _materialXAxes, value);
        }

        private Axis[] _materialYAxes = Array.Empty<Axis>();
        public Axis[] MaterialYAxes
        {
            get => _materialYAxes;
            set => SetProperty(ref _materialYAxes, value);
        }

        private ISeries[] _materialPieSeries = Array.Empty<ISeries>();
        public ISeries[] MaterialPieSeries
        {
            get => _materialPieSeries;
            set => SetProperty(ref _materialPieSeries, value);
        }

        private ISeries[] _userSeries = Array.Empty<ISeries>();
        public ISeries[] UserSeries
        {
            get => _userSeries;
            set => SetProperty(ref _userSeries, value);
        }

        private Axis[] _userXAxes = Array.Empty<Axis>();
        public Axis[] UserXAxes
        {
            get => _userXAxes;
            set => SetProperty(ref _userXAxes, value);
        }

        private Axis[] _userYAxes = Array.Empty<Axis>();
        public Axis[] UserYAxes
        {
            get => _userYAxes;
            set => SetProperty(ref _userYAxes, value);
        }



        // ?? Constructor ???????????????????????????????????????????????????????
        public DashboardViewModel(ICurrentUserContext currentUserContext, User? user = null)
            : this(currentUserContext, NullShellNavigator.Instance, user)
        {
        }

        public DashboardViewModel(
            ICurrentUserContext currentUserContext,
            IShellNavigator shellNavigator,
            User? user = null)
        {
            _shellNavigator = shellNavigator;
            CurrentUser = user ?? currentUserContext.CurrentUser;

            var dbPath = AppConfigurationManager.GetDatabasePath();
            _weighingService = new WeighingService(new WeighingRepository(dbPath));
            FilterCommand = new RelayCommand(async _ => await LoadDashboardDataAsync());
            NavigateToFailedSyncsCommand = new RelayCommand(_ =>
            {
                if (!ProWeighSuitePlatforms.Services.SyncGate.Enabled) return;
                try
                {
                    _ = _shellNavigator.NavigateToRouteAsync("FailedSyncs");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Dashboard] Navigate to Failed Syncs failed: {ex.Message}");
                }
            });

            // Default filter: current date
            var now = DateTime.Now;
            FromDate = now.Date;
            ToDate = now.Date;

            // Auto-refresh when new transactions arrive
            WeighingRepository.TransactionInserted += OnTransactionInserted;

            _ = LoadDashboardDataAsync();
        }

        // ?? Data loading - now uses SQL aggregates to handle 5K/day (150K/month) without loading all rows
        private async Task LoadDashboardDataAsync()
        {
            try
            {
                StatusMessage = "Loading...";
                await _weighingService.InitializeAsync();

                var dbPath = AppConfigurationManager.GetDatabasePath();
                var repo = new WeighingRepository(dbPath);
                // For Mfg Date filter, aggregates need date field switch - fallback to old path for Mfg Date
                WeighingRepository.DashboardAggregates agg;
                if (SelectedDateType != "Trx Date")
                {
                    // Mfg Date: keep in-memory path but limit to 5K rows max to avoid OOM
                    var transactions = await GetTransactionsAsync(FromDate, ToDate);
                    if (transactions.Count > 5000)
                        transactions = transactions.Take(5000).ToList();
                    await LoadFailedSyncCountAsync();
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        BuildSummaryCards(transactions);
                        BuildDailyWeightChart(transactions);
                        BuildMaterialPieChart(transactions);
                        var from = FromDate?.ToString("dd-MM-yyyy") ?? "?";
                        var to = ToDate?.ToString("dd-MM-yyyy") ?? "?";
                        StatusMessage = $"Showing {transactions.Count} transaction(s) (Mfg filter, capped 5K) | {from} to {to}";
                    });
                    return;
                }

                agg = await repo.GetDashboardAggregatesAsync(FromDate, ToDate, SelectedStatusFilter);
                await LoadFailedSyncCountAsync();

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    TodayTransactions = agg.TodayTransactions;
                    TodayWeight = agg.TodayWeight;
                    FilteredTransactions = agg.FilteredTransactions;
                    FilteredWeight = agg.FilteredWeight;
                    TotalMaterials = agg.TotalMaterials;
                    AvgWeight = agg.AvgWeight;
                    BuildDailyWeightChartFromAggregates(agg.DailyTrend);
                    BuildMaterialPieChartFromAggregates(agg.TopMaterials);
                    var from = FromDate?.ToString("dd-MM-yyyy") ?? "?";
                    var to = ToDate?.ToString("dd-MM-yyyy") ?? "?";
                    StatusMessage = $"Showing {agg.FilteredTransactions} transaction(s) | {from} to {to}";
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Dashboard] Error: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private void BuildDailyWeightChartFromAggregates(List<WeighingRepository.DailyWeightPoint> trend)
        {
            var labels = trend.Select(g => g.Date.ToString("dd MMM")).ToArray();
            var weights = trend.Select(g => g.Weight).ToArray();
            var counts = trend.Select(g => (double)g.Count).ToArray();

            DailyWeightSeries = new ISeries[]
            {
                new ColumnSeries<double> { Name = "Weight (kg)", Values = weights, Fill = new SolidColorPaint(new SKColor(255, 159, 0)), MaxBarWidth = 28 },
                new ColumnSeries<double> { Name = "Transactions", Values = counts, Fill = new SolidColorPaint(new SKColor(66, 165, 245)), MaxBarWidth = 28 }
            };
            var maxVisible = Math.Min(labels.Length, 15);
            DailyWeightXAxes = new[] { new Axis { Labels = labels, LabelsRotation = labels.Length > 10 ? 45 : 0, TextSize = 11, MinLimit = labels.Length > maxVisible ? labels.Length - maxVisible - 0.5 : -0.5, MaxLimit = labels.Length - 0.5 } };
            DailyWeightYAxes = new[] { new Axis { TextSize = 11, MinLimit = 0 } };
        }

        private void BuildMaterialPieChartFromAggregates(List<WeighingRepository.MaterialWeightPoint> topMaterials)
        {
            var totalWeight = topMaterials.Sum(x => x.Weight);
            var colors = GenerateDistinctColors(topMaterials.Count);
            var series = new List<ISeries>();
            for (int i = 0; i < topMaterials.Count; i++)
            {
                var name = topMaterials[i].Name;
                var weight = topMaterials[i].Weight;
                var pct = totalWeight > 0 ? (weight / totalWeight * 100) : 0;
                series.Add(new PieSeries<double> { Name = name, Values = new[] { weight }, Fill = new SolidColorPaint(colors[i]), DataLabelsSize = 11, DataLabelsPaint = new SolidColorPaint(SKColors.White), DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle, DataLabelsFormatter = p => pct >= 5 ? $"{pct:F1}%" : string.Empty, ToolTipLabelFormatter = p => $"{name}: {weight:F1} kg ({pct:F1}%)" });
            }
            MaterialPieSeries = series.ToArray();
            MaterialBarSeries = Array.Empty<ISeries>();
            MaterialXAxes = Array.Empty<Axis>();
            MaterialYAxes = Array.Empty<Axis>();
        }

        // ?? Summary cards ?????????????????????????????????????????????????????
        private void BuildSummaryCards(List<WeighingTransaction> all)
        {
            var today = DateTime.Today;
            var todayTx = all.Where(t => t.WeighedDateValue.Date == today).ToList();

            TodayTransactions = todayTx.Count;
            TodayWeight = Math.Round(todayTx.Sum(t => t.ActualWeight), 3);

            FilteredTransactions = all.Count;
            FilteredWeight = Math.Round(all.Sum(t => t.ActualWeight), 3);

            TotalMaterials = all.Select(t => t.MaterialCode).Distinct().Count();
            AvgWeight = all.Count > 0 ? Math.Round(all.Average(t => t.ActualWeight), 3) : 0;
        }

        private async Task LoadFailedSyncCountAsync()
        {
            try
            {
                var dbPath = AppConfigurationManager.GetDatabasePath();
                if (!System.IO.File.Exists(dbPath)) { FailedSyncCount = 0; return; }
                var repo = new WeighingRepository(dbPath);
                var counts = await repo.GetSyncCountsAsync();
                FailedSyncCount = counts.Unsynced;
            }
            catch { FailedSyncCount = 0; }
        }

        // ?? Daily weight bar chart ???????????????????????????????????????????
        private void BuildDailyWeightChart(List<WeighingTransaction> all)
        {
            var grouped = all
                .GroupBy(t => t.WeighedDateValue.Date)
                .OrderBy(g => g.Key)
                .ToList();

            var labels = grouped.Select(g => g.Key.ToString("dd MMM")).ToArray();
            var weights = grouped.Select(g => Math.Round(g.Sum(t => t.ActualWeight), 2)).ToArray();
            var counts = grouped.Select(g => (double)g.Count()).ToArray();

            DailyWeightSeries = new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Name = "Weight (kg)",
                    Values = weights,
                    Fill = new SolidColorPaint(new SKColor(255, 159, 0)),   // #ff9f00
                    MaxBarWidth = 28
                },
                new ColumnSeries<double>
                {
                    Name = "Transactions",
                    Values = counts,
                    Fill = new SolidColorPaint(new SKColor(66, 165, 245)),  // blue
                    MaxBarWidth = 28
                }
            };

            // Show a visible window of ~15 days so the rest is reachable by scrolling
            var maxVisible = Math.Min(labels.Length, 15);

            DailyWeightXAxes = new Axis[]
            {
                new Axis
                {
                    Labels = labels,
                    LabelsRotation = labels.Length > 10 ? 45 : 0,
                    TextSize = 11,
                    MinLimit = labels.Length > maxVisible ? labels.Length - maxVisible - 0.5 : -0.5,
                    MaxLimit = labels.Length - 0.5
                }
            };

            DailyWeightYAxes = new Axis[]
            {
                new Axis
                {
                    TextSize = 11,
                    MinLimit = 0
                }
            };
        }

        // ?? Material horizontal bar chart ???????????????????????????????????
        /// <summary>
        /// Generates N visually distinct colors by evenly spacing hues around
        /// the HSL wheel. Works for any count � no fixed palette limit.
        /// </summary>
        private static SKColor[] GenerateDistinctColors(int count)
        {
            var colors = new SKColor[count];
            for (int i = 0; i < count; i++)
            {
                // Spread hues evenly; use golden-angle offset for better separation
                var hue = (i * 137.508f) % 360f;  // golden angle ? 137.508�
                var saturation = 70f + (i % 3) * 10f; // 70%, 80%, 90% cycling
                var lightness = 45f + (i % 2) * 10f;  // 45%, 55% alternating
                colors[i] = SKColor.FromHsl(hue, saturation, lightness);
            }
            return colors;
        }

        private void BuildMaterialPieChart(List<WeighingTransaction> all)
        {
            const int topN = 15;

            var allGrouped = all
                .GroupBy(t => string.IsNullOrWhiteSpace(t.MaterialName) ? t.MaterialCode : t.MaterialName)
                .Select(g => new { Name = g.Key, Weight = Math.Round(g.Sum(t => t.ActualWeight), 2) })
                .OrderByDescending(x => x.Weight)
                .ToList();

            var topItems = allGrouped.Take(topN).ToList();
            var othersWeight = allGrouped.Skip(topN).Sum(x => x.Weight);

            var items = new List<(string Name, double Weight)>();
            items.AddRange(topItems.Select(x => (x.Name, x.Weight)));
            if (othersWeight > 0)
                items.Add(($"Others ({allGrouped.Count - topN})", Math.Round(othersWeight, 2)));

            var totalWeight = items.Sum(x => x.Weight);
            var colors = GenerateDistinctColors(items.Count);

            var series = new List<ISeries>();
            for (int i = 0; i < items.Count; i++)
            {
                var name = items[i].Name;
                var weight = items[i].Weight;
                var pct = totalWeight > 0 ? (weight / totalWeight * 100) : 0;

                series.Add(new PieSeries<double>
                {
                    Name = name,
                    Values = new[] { weight },
                    Fill = new SolidColorPaint(colors[i]),
                    DataLabelsSize = 11,
                    DataLabelsPaint = new SolidColorPaint(SKColors.White),
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                    DataLabelsFormatter = p => pct >= 5 ? $"{pct:F1}%" : string.Empty,
                    ToolTipLabelFormatter = p => $"{name}: {weight:F1} kg ({pct:F1}%)"
                });
            }

            MaterialPieSeries = series.ToArray();

            // Clear bar series (no longer used)
            MaterialBarSeries = Array.Empty<ISeries>();
            MaterialXAxes = Array.Empty<Axis>();
            MaterialYAxes = Array.Empty<Axis>();
        }

        // ?? Transactions-by-user bar chart ???????????????????????????????????
        private static readonly SKColor[] UserColors =
        {
            new(255, 159, 0),    // amber
            new(66, 165, 245),   // blue
            new(102, 187, 106),  // green
            new(239, 83, 80),    // red
            new(171, 71, 188),   // purple
            new(38, 198, 218),   // cyan
            new(255, 183, 77),   // light orange
            new(141, 110, 99),   // brown
            new(255, 112, 67),   // deep orange
            new(92, 107, 192),   // indigo
        };

        private void BuildUserChart(List<WeighingTransaction> all)
        {
            var grouped = all
                .GroupBy(t => string.IsNullOrWhiteSpace(t.WeighedBy) ? "Unknown" : t.WeighedBy)
                .Select(g => new { User = g.Key, Count = g.Count(), Weight = Math.Round(g.Sum(t => t.ActualWeight), 2) })
                .OrderByDescending(x => x.Count)
                .ToList();

            var labels = grouped.Select(g => g.User).ToArray();

            // One series per user so each bar gets its own color
            var series = new List<ISeries>();
            for (int i = 0; i < grouped.Count; i++)
            {
                // Build a values array with nulls for other positions so bars don't overlap
                var values = new double?[grouped.Count];
                values[i] = grouped[i].Count;

                series.Add(new ColumnSeries<double?>
                {
                    Name = grouped[i].User,
                    Values = values,
                    Fill = new SolidColorPaint(UserColors[i % UserColors.Length]),
                    MaxBarWidth = 35
                });
            }

            UserSeries = series.ToArray();

            UserXAxes = new Axis[]
            {
                new Axis
                {
                    Labels = labels,
                    TextSize = 11
                }
            };

            UserYAxes = new Axis[]
            {
                new Axis
                {
                    TextSize = 11,
                    MinLimit = 0
                }
            };
        }

        // ?? DB query (capped at 5K for Mfg Date fallback, Trx Date uses aggregates)
        private async Task<List<WeighingTransaction>> GetTransactionsAsync(DateTime? fromDate, DateTime? toDate)
        {
            var dbPath = AppConfigurationManager.GetDatabasePath();
            if (!System.IO.File.Exists(dbPath))
                return new List<WeighingTransaction>();

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            var conditions = new List<string>();
            bool filterByManufacturingDate = SelectedDateType != "Trx Date";

            if (fromDate.HasValue)
            {
                if (filterByManufacturingDate)
                {
                    conditions.Add("manufacturing_date >= $from");
                    cmd.Parameters.AddWithValue("$from", fromDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }
                else
                {
                    conditions.Add("REPLACE(weighed_date, '.', ':') >= $from");
                    cmd.Parameters.AddWithValue("$from", fromDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 00:00:00");
                }
            }
            if (toDate.HasValue)
            {
                if (filterByManufacturingDate)
                {
                    conditions.Add("manufacturing_date <= $to");
                    cmd.Parameters.AddWithValue("$to", toDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }
                else
                {
                    conditions.Add("REPLACE(weighed_date, '.', ':') <= $to");
                    cmd.Parameters.AddWithValue("$to", toDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " 23:59:59");
                }
            }

            if (!string.IsNullOrWhiteSpace(SelectedStatusFilter) && SelectedStatusFilter != "ALL")
            {
                conditions.Add("status = $status");
                cmd.Parameters.AddWithValue("$status", SelectedStatusFilter.Trim());
            }

            var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : string.Empty;

            cmd.CommandText = $@"
                SELECT id, batch_id, batch_number, material_code, material_name,
                       sequence, target_weight, lower_weight, upper_weight, actual_weight, deviation,
                       status, weighed_by, weighed_date, printed_date, customer_name, manufacturing_date,
                       expiry_date, equipment_id, location, remarks, tare_weight
                FROM weighing_transactions
                {where}
                ORDER BY weighed_date DESC";

            var list = new List<WeighingTransaction>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var dateStr = reader.GetString(13);
                DateTime dt;
                try { dt = DateTime.Parse(dateStr.Replace(".", ":"), CultureInfo.InvariantCulture); }
                catch { try { dt = DateTime.ParseExact(dateStr, "yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture); } catch { dt = DateTime.MinValue; } }

                list.Add(new WeighingTransaction
                {
                    Id = reader.GetString(0),
                    BatchId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    BatchNumber = reader.GetString(2),
                    MaterialCode = reader.GetString(3),
                    MaterialName = reader.GetString(4),
                    Sequence = reader.GetInt32(5),
                    TargetWeight = reader.GetDouble(6),
                    LowerWeight = reader.GetDouble(7),
                    UpperWeight = reader.GetDouble(8),
                    ActualWeight = reader.GetDouble(9),
                    Deviation = reader.GetDouble(10),
                    Status = reader.GetString(11),
                    WeighedBy = reader.GetString(12),
                    WeighedDate = dateStr,
                    WeighedDateValue = dt,
                    PrintedDate = reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                    CustomerName = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                    ManufacturingDate = reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
                    ExpiryDate = reader.IsDBNull(17) ? string.Empty : reader.GetString(17),
                    EquipmentId = reader.IsDBNull(18) ? string.Empty : reader.GetString(18),
                    Location = reader.IsDBNull(19) ? string.Empty : reader.GetString(19),
                    Remarks = reader.IsDBNull(20) ? string.Empty : reader.GetString(20),
                    TareWeight = reader.IsDBNull(21) ? 0.0 : reader.GetDouble(21)
                });
            }
            return list;
        }

        private void OnTransactionInserted(string dbPath, string transactionId)
        {
            try
            {
                var currentDb = AppConfigurationManager.GetDatabasePath();
                if (string.Equals(System.IO.Path.GetFullPath(dbPath),
                    System.IO.Path.GetFullPath(currentDb), StringComparison.OrdinalIgnoreCase))
                {
                    _ = LoadDashboardDataAsync();
                }
            }
            catch { }
        }

        private sealed class NullShellNavigator : IShellNavigator
        {
            public static readonly NullShellNavigator Instance = new();

            public Task<bool> NavigateToRouteAsync(string routeId) => Task.FromResult(false);
        }
    }
}
