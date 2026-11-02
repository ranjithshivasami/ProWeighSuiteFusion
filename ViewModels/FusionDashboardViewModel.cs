// FUSION-PLAN.md Step 5 — Fusion dashboard over fusion_* only.
// 5 KPI cards + 6 charts (LiveCharts on-screen, same ISeries/Axis pattern as
// DashboardViewModel). Export renders off-screen with raw SkiaSharp primitives
// (the referenced LiveChartsCore rc4.5 has no SKChart headless export and no
// per-bar Fills), so no new NuGet packages and no visual-tree capture.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using SkiaSharp;

namespace ProWeighSuitePlatforms.ViewModels
{
    public sealed class FusionDashboardViewModel : ViewModels.BaseViewModel
    {
        public string Title => "Dashboard";

        // ===== KPIs =====
        private int _todayCount;
        public int TodayCount { get => _todayCount; private set => SetProperty(ref _todayCount, value); }
        private double _todayKg;
        public double TodayKg { get => _todayKg; private set => SetProperty(ref _todayKg, value); }
        private int _activeBatches;
        public int ActiveBatches { get => _activeBatches; private set => SetProperty(ref _activeBatches, value); }
        private int _totalTubs;
        public int TotalTubs { get => _totalTubs; private set => SetProperty(ref _totalTubs, value); }
        private int _activeTubs;
        public int ActiveTubs { get => _activeTubs; private set => SetProperty(ref _activeTubs, value); }
        private int _availableTubs;
        public int AvailableTubs { get => _availableTubs; private set => SetProperty(ref _availableTubs, value); }
        private int _completedToday;
        public int CompletedToday { get => _completedToday; private set => SetProperty(ref _completedToday, value); }
        private double _targetKg;
        public double TargetKg { get => _targetKg; private set => SetProperty(ref _targetKg, value); }
        public double TargetPct => TargetKg > 0 ? Math.Min(100, TodayKg / TargetKg * 100) : 0;
        public double RemainingKg => Math.Max(0, TargetKg - TodayKg);

        // ===== Subtitles (image parity) =====
        private string _supplierSubtitle = "no production today";
        public string SupplierSubtitle { get => _supplierSubtitle; private set => SetProperty(ref _supplierSubtitle, value); }
        private string _targetSubtitle = "no target set";
        public string TargetSubtitle { get => _targetSubtitle; private set => SetProperty(ref _targetSubtitle, value); }
        private string _hourlySubtitle = "no production today";
        public string HourlySubtitle { get => _hourlySubtitle; private set => SetProperty(ref _hourlySubtitle, value); }
        private string _trendSubtitle = string.Empty;
        public string TrendSubtitle { get => _trendSubtitle; private set => SetProperty(ref _trendSubtitle, value); }
        private string _batchSubtitle = "no batches today";
        public string BatchSubtitle { get => _batchSubtitle; private set => SetProperty(ref _batchSubtitle, value); }
        private string _baySubtitle = "no production today";
        public string BaySubtitle { get => _baySubtitle; private set => SetProperty(ref _baySubtitle, value); }

        private string _statusText = string.Empty;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        // ===== Chart series (WPF bindings) =====
        private ISeries[] _supplierSeries = Array.Empty<ISeries>();
        public ISeries[] SupplierSeries { get => _supplierSeries; private set => SetProperty(ref _supplierSeries, value); }
        private ISeries[] _targetSeries = Array.Empty<ISeries>();
        public ISeries[] TargetSeries { get => _targetSeries; private set => SetProperty(ref _targetSeries, value); }
        private ISeries[] _hourlySeries = Array.Empty<ISeries>();
        public ISeries[] HourlySeries { get => _hourlySeries; private set => SetProperty(ref _hourlySeries, value); }
        private Axis[] _hourlyX = Array.Empty<Axis>();
        public Axis[] HourlyX { get => _hourlyX; private set => SetProperty(ref _hourlyX, value); }
        private Axis[] _hourlyY = Array.Empty<Axis>();
        public Axis[] HourlyY { get => _hourlyY; private set => SetProperty(ref _hourlyY, value); }
        private ISeries[] _trendSeries = Array.Empty<ISeries>();
        public ISeries[] TrendSeries { get => _trendSeries; private set => SetProperty(ref _trendSeries, value); }
        private Axis[] _trendX = Array.Empty<Axis>();
        public Axis[] TrendX { get => _trendX; private set => SetProperty(ref _trendX, value); }
        private Axis[] _trendY = Array.Empty<Axis>();
        public Axis[] TrendY { get => _trendY; private set => SetProperty(ref _trendY, value); }
        private ISeries[] _batchSeries = Array.Empty<ISeries>();
        public ISeries[] BatchSeries { get => _batchSeries; private set => SetProperty(ref _batchSeries, value); }
        private Axis[] _batchX = Array.Empty<Axis>();
        public Axis[] BatchX { get => _batchX; private set => SetProperty(ref _batchX, value); }
        private Axis[] _batchY = Array.Empty<Axis>();
        public Axis[] BatchY { get => _batchY; private set => SetProperty(ref _batchY, value); }
        private ISeries[] _baySeries = Array.Empty<ISeries>();
        public ISeries[] BaySeries { get => _baySeries; private set => SetProperty(ref _baySeries, value); }
        private Axis[] _bayX = Array.Empty<Axis>();
        public Axis[] BayX { get => _bayX; private set => SetProperty(ref _bayX, value); }
        private Axis[] _bayY = Array.Empty<Axis>();
        public Axis[] BayY { get => _bayY; private set => SetProperty(ref _bayY, value); }

        // Raw data kept for off-screen export rendering.
        private List<(string Label, double Kg)> _supplier = new();
        private List<(string Label, double Kg)> _hourly = new();
        private List<(string Label, double Kg)> _trend = new();
        private List<(string Label, double Kg)> _batches = new();
        private List<(string Label, double Kg)> _bays = new();

        public ICommand RefreshCommand { get; }
        public ICommand ExportChartPngCommand { get; }
        public ICommand ExportChartPdfCommand { get; }
        public ICommand ExportDashboardImageCommand { get; }
        public ICommand ExportDashboardPdfCommand { get; }

        private static readonly SKColor[] Palette = new[]
        {
            new SKColor(99, 102, 241), new SKColor(16, 185, 129), new SKColor(245, 158, 11),
            new SKColor(236, 72, 153), new SKColor(14, 165, 233), new SKColor(139, 92, 246),
            new SKColor(34, 197, 94), new SKColor(249, 115, 22),
        };
        private static readonly SKColor Ink = new(28, 36, 48);
        private static readonly SKColor Muted = new(91, 100, 114);
        private static readonly SKColor GridLine = new(231, 234, 239);
        private static readonly SKColor BrandGreen = new(11, 177, 77);

        public FusionDashboardViewModel()
        {
            RefreshCommand = new ViewModels.RelayCommand(async _ => await LoadAsync());
            ExportChartPngCommand = new ViewModels.RelayCommand(async p => await ExportChartAsync(p as string, "png"));
            ExportChartPdfCommand = new ViewModels.RelayCommand(async p => await ExportChartAsync(p as string, "pdf"));
            ExportDashboardImageCommand = new ViewModels.RelayCommand(async _ => await ExportDashboardAsync("png"));
            ExportDashboardPdfCommand = new ViewModels.RelayCommand(async _ => await ExportDashboardAsync("pdf"));
            _ = LoadAsync();
        }

        public async Task LoadAsync()
        {
            try
            {
                await new FusionDatabaseInit(AppConfigurationManager.GetDatabasePath()).EnsureCreatedAsync();
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());

                var kpis = await repo.GetDashboardKpisAsync();
                TodayCount = kpis.TodayCount;
                TodayKg = kpis.TodayKg;
                ActiveBatches = kpis.ActiveBatches;
                TotalTubs = kpis.TotalTubs;
                ActiveTubs = kpis.ActiveTubs;
                AvailableTubs = kpis.AvailableTubs;
                CompletedToday = kpis.CompletedToday;
                TargetKg = kpis.TargetKg;
                RaisePropertyChanged(nameof(TargetPct));
                RaisePropertyChanged(nameof(RemainingKg));

                var share = await repo.GetSupplierShareAsync();
                var names = await repo.GetSupplierNamesAsync(share.Select(s => s.SupplierId));
                _supplier = share.Select(s => (
                    Label: names.TryGetValue(s.SupplierId, out var n) && !string.IsNullOrWhiteSpace(n)
                        ? n : (s.SupplierId == 0 ? "Direct" : $"SUP-{s.SupplierId}"),
                    Kg: s.Kg)).ToList();
                if (_supplier.Count > 0)
                {
                    var top = _supplier.OrderByDescending(s => s.Kg).First();
                    var total = _supplier.Sum(s => s.Kg);
                    SupplierSubtitle = $"Largest: {top.Label} {(total > 0 ? top.Kg / total * 100 : 0):F0}% of production";
                }
                else SupplierSubtitle = "no production today";
                BuildSupplierSeries();

                TargetSubtitle = TargetKg > 0 ? $"Remaining {RemainingKg:0,0} kg" : "no target set";
                BuildTargetSeries();

                _hourly = await repo.GetHourlyAsync();
                if (_hourly.Count > 0)
                {
                    var peak = _hourly.OrderByDescending(h => h.Kg).First();
                    HourlySubtitle = $"Peak hour {peak.Label} with {peak.Kg / 1000:F1}k kg";
                }
                else HourlySubtitle = "no production today";
                BuildHourlySeries();

                _trend = await repo.GetDailyAsync(7);
                if (_trend.Count >= 2)
                {
                    var last = _trend[^1].Kg;
                    var prevAvg = _trend.Take(_trend.Count - 1).Average(t => t.Kg);
                    var vs = prevAvg > 0 ? (last - prevAvg) / prevAvg * 100 : 0;
                    TrendSubtitle = $"Today is {vs:+0;-0}% vs the {(_trend.Count - 1)}-day average";
                }
                else TrendSubtitle = "needs more days of data";
                BuildTrendSeries();

                _batches = await repo.GetBatchTotalsAsync();
                BatchSubtitle = _batches.Count == 0 ? "no batches today" : $"{_batches.Count} batch(es) today";
                BuildBatchSeries();

                _bays = await repo.GetBayTotalsAsync();
                BaySubtitle = _bays.Count == 0 ? "no production today" : "Share of today's weight by store bay";
                BuildBaySeries();

                StatusText = $"Updated {DateTime.Now:T}: {TodayCount} transaction(s), {TodayKg:0.###} kg today.";
            }
            catch (System.Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        private void BuildSupplierSeries()
        {
            var list = new List<ISeries>();
            var total = _supplier.Sum(s => s.Kg);
            for (int i = 0; i < _supplier.Count; i++)
            {
                var (label, kg) = _supplier[i];
                var pct = total > 0 ? kg / total * 100 : 0;
                list.Add(new PieSeries<double>
                {
                    Name = label, Values = new[] { kg },
                    Fill = new SolidColorPaint(Palette[i % Palette.Length]),
                    InnerRadius = 62,
                    DataLabelsSize = 11,
                    DataLabelsPaint = new SolidColorPaint(SKColors.White),
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                    DataLabelsFormatter = p => pct >= 8 ? $"{pct:F0}%" : string.Empty,
                    ToolTipLabelFormatter = p => $"{label}: {kg:0.#} kg ({pct:F1}%)",
                });
            }
            SupplierSeries = list.ToArray();
        }

        private void BuildTargetSeries()
        {
            var achieved = Math.Min(TodayKg, TargetKg);
            var remaining = Math.Max(0, TargetKg - TodayKg);
            if (TargetKg <= 0)
            {
                TargetSeries = new ISeries[]
                {
                    new PieSeries<double> { Values = new[] { 1.0 }, Fill = new SolidColorPaint(GridLine), InnerRadius = 70, IsHoverable = false },
                };
                return;
            }
            TargetSeries = new ISeries[]
            {
                new PieSeries<double>
                {
                    Name = "Achieved", Values = new[] { achieved },
                    Fill = new SolidColorPaint(BrandGreen), InnerRadius = 70,
                    DataLabelsSize = 20, DataLabelsPaint = new SolidColorPaint(SKColors.Black),
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.ChartCenter,
                    DataLabelsFormatter = _ => $"{TargetPct:F0}%",
                    ToolTipLabelFormatter = _ => $"{achieved:0,#} kg of {TargetKg:0,#} kg",
                },
                new PieSeries<double>
                {
                    Name = "Remaining", Values = new[] { remaining },
                    Fill = new SolidColorPaint(GridLine), InnerRadius = 70,
                    DataLabelsSize = 0, IsHoverable = false,
                },
            };
        }

        private void BuildHourlySeries()
        {
            HourlySeries = new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Name = "kg", Values = _hourly.Select(h => h.Kg).ToArray(),
                    Fill = new SolidColorPaint(new SKColor(16, 185, 129)), MaxBarWidth = 34,
                    DataLabelsSize = 10, DataLabelsPaint = new SolidColorPaint(Ink),
                    DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top,
                    DataLabelsFormatter = p => FormatShort(p.Coordinate.PrimaryValue),
                },
            };
            HourlyX = new[] { new Axis { Labels = _hourly.Select(h => h.Label).ToArray(), TextSize = 11 } };
            HourlyY = new[] { new Axis { TextSize = 11, MinLimit = 0 } };
        }

        private void BuildTrendSeries()
        {
            TrendSeries = new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Name = "kg", Values = _trend.Select(t => t.Kg).ToArray(),
                    Fill = new SolidColorPaint(new SKColor(99, 102, 241)), MaxBarWidth = 40,
                },
            };
            TrendX = new[] { new Axis { Labels = _trend.Select(t => t.Label).ToArray(), TextSize = 11, LabelsRotation = _trend.Count > 5 ? 30 : 0 } };
            TrendY = new[] { new Axis { TextSize = 11, MinLimit = 0 } };
        }

        private void BuildBatchSeries()
        {
            BatchSeries = new ISeries[]
            {
                new RowSeries<double>
                {
                    Name = "kg", Values = _batches.Select(b => b.Kg).ToArray(),
                    Fill = new SolidColorPaint(new SKColor(99, 102, 241)), MaxBarWidth = 26,
                },
            };
            BatchX = new[] { new Axis { TextSize = 11, MinLimit = 0 } };
            BatchY = new[] { new Axis { Labels = _batches.Select(b => b.Label).ToArray(), TextSize = 11 } };
        }

        private void BuildBaySeries()
        {
            BaySeries = new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Name = "kg", Values = _bays.Select(b => b.Kg).ToArray(),
                    Fill = new SolidColorPaint(new SKColor(14, 165, 233)), MaxBarWidth = 40,
                },
            };
            BayX = new[] { new Axis { Labels = _bays.Select(b => b.Label).ToArray(), TextSize = 11 } };
            BayY = new[] { new Axis { TextSize = 11, MinLimit = 0 } };
        }

        private static string FormatShort(double v) => v >= 1000 ? $"{v / 1000:F1}k" : $"{v:0}";

        // ===== Export: raw-SkiaSharp rendering (no SKChart dependency) =====
        private const int TileW = 640;
        private const int TileH = 380;

        private static readonly string[] ChartKeys = new[] { "supplier", "target", "hourly", "trend", "batch", "bay" };
        private static readonly Dictionary<string, string> ChartTitles = new()
        {
            ["supplier"] = "Supplier share (kg)",
            ["target"] = "Production vs target",
            ["hourly"] = "Hourly production (kg)",
            ["trend"] = "Production trend, 7 days (kg)",
            ["batch"] = "Batch-wise production (kg)",
            ["bay"] = "Bay-wise production (kg)",
        };

        private SKImage RenderChartImage(string key)
        {
            using var bmp = new SKBitmap(TileW, TileH);
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(SKColors.White);
            switch (key.ToLowerInvariant())
            {
                case "supplier":
                    PaintDonut(canvas, new SKRect(0, 0, TileW, TileH),
                        _supplier.Select((s, i) => (s.Label, s.Kg, Palette[i % Palette.Length])).ToList(), null);
                    break;
                case "target":
                    PaintDonut(canvas, new SKRect(0, 0, TileW, TileH),
                        TargetKg > 0
                            ? new List<(string, double, SKColor)> { ("Achieved", Math.Min(TodayKg, TargetKg), BrandGreen), ("Remaining", RemainingKg, GridLine) }
                            : new List<(string, double, SKColor)> { ("No target", 1, GridLine) },
                        TargetKg > 0 ? $"{TargetPct:F0}%" : null);
                    break;
                case "hourly":
                    PaintBars(canvas, new SKRect(0, 0, TileW, TileH), _hourly, new SKColor(16, 185, 129), vertical: true);
                    break;
                case "trend":
                    PaintBars(canvas, new SKRect(0, 0, TileW, TileH), _trend, new SKColor(99, 102, 241), vertical: true);
                    break;
                case "batch":
                    PaintBars(canvas, new SKRect(0, 0, TileW, TileH), _batches, new SKColor(99, 102, 241), vertical: false);
                    break;
                case "bay":
                    PaintBars(canvas, new SKRect(0, 0, TileW, TileH), _bays, new SKColor(14, 165, 233), vertical: true);
                    break;
                default:
                    throw new ArgumentException($"Unknown chart '{key}'.", nameof(key));
            }
            return SKImage.FromBitmap(bmp);
        }

        private static void PaintBars(SKCanvas canvas, SKRect bounds, List<(string Label, double Kg)> data,
            SKColor color, bool vertical)
        {
            using var barPaint = new SKPaint { Color = color, IsAntialias = true };
            using var gridPaint = new SKPaint { Color = GridLine, IsAntialias = true, StrokeWidth = 1 };
            using var textPaint = new SKPaint { Color = Ink, IsAntialias = true };
            var labelFont = new SKFont(SKTypeface.Default, 12);
            var valueFont = new SKFont(SKTypeface.Default, 12);
            float padL = 58, padR = 14, padT = 26, padB = 46;
            var plot = new SKRect(bounds.Left + padL, bounds.Top + padT, bounds.Right - padR, bounds.Bottom - padB);
            double max = data.Count == 0 ? 1 : Math.Max(1, data.Max(d => d.Kg));
            for (int g = 1; g <= 3; g++)
            {
                float y = plot.Top + (plot.Height * g / 4);
                canvas.DrawLine(plot.Left, y, plot.Right, y, gridPaint);
            }
            if (data.Count == 0)
            {
                canvas.DrawText("no data", plot.Left + 8, plot.Top + 24, new SKFont(SKTypeface.Default, 14), textPaint);
                return;
            }
            if (vertical)
            {
                float slot = plot.Width / data.Count;
                float bw = Math.Min(slot * 0.62f, 44);
                for (int i = 0; i < data.Count; i++)
                {
                    float h = (float)(data[i].Kg / max) * plot.Height;
                    float x = plot.Left + slot * i + (slot - bw) / 2;
                    canvas.DrawRoundRect(new SKRect(x, plot.Bottom - h, x + bw, plot.Bottom), 4, 4, barPaint);
                    var val = FormatShort(data[i].Kg);
                    canvas.DrawText(val, x + bw / 2 - valueFont.MeasureText(val) / 2, plot.Bottom - h - 6, valueFont, textPaint);
                    DrawXLabel(canvas, data[i].Label, plot.Left + slot * i + slot / 2, plot.Bottom + 18, labelFont, textPaint, slot);
                }
            }
            else
            {
                float slot = plot.Height / data.Count;
                float bh = Math.Min(slot * 0.62f, 30);
                for (int i = 0; i < data.Count; i++)
                {
                    float w = (float)(data[i].Kg / max) * plot.Width;
                    float y = plot.Top + slot * i + (slot - bh) / 2;
                    canvas.DrawRoundRect(new SKRect(plot.Left, y, plot.Left + w, y + bh), 4, 4, barPaint);
                    canvas.DrawText(data[i].Label, plot.Left + 4, y + bh / 2 + 5, labelFont, textPaint);
                    var val = FormatShort(data[i].Kg);
                    canvas.DrawText(val, Math.Min(plot.Left + w + 6, plot.Right - valueFont.MeasureText(val)), y + bh / 2 + 5, valueFont, textPaint);
                }
            }
        }

        private static void DrawXLabel(SKCanvas canvas, string text, float cx, float y, SKFont font, SKPaint paint, float slot)
        {
            float w = font.MeasureText(text);
            if (w <= slot - 4)
            {
                canvas.DrawText(text, cx - w / 2, y, font, paint);
                return;
            }
            canvas.Save();
            canvas.Translate(cx, y);
            canvas.RotateDegrees(-32);
            canvas.DrawText(text, 0, 0, font, paint);
            canvas.Restore();
        }

        private static void PaintDonut(SKCanvas canvas, SKRect bounds, List<(string Label, double Kg, SKColor Color)> slices,
            string? centerText)
        {
            using var textPaint = new SKPaint { Color = Ink, IsAntialias = true };
            var labelFont = new SKFont(SKTypeface.Default, 12);
            double total = slices.Sum(s => s.Kg);
            float cx = bounds.Left + 200, cy = bounds.Top + bounds.Height / 2;
            float radius = 108, stroke = 58;
            if (total <= 0)
            {
                using var empty = new SKPaint { Color = GridLine, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = stroke };
                canvas.DrawCircle(cx, cy, radius, empty);
            }
            else
            {
                float start = -90;
                foreach (var s in slices)
                {
                    float sweep = (float)(s.Kg / total * 360);
                    if (sweep <= 0) continue;
                    using var p = new SKPaint { Color = s.Color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = stroke, StrokeCap = SKStrokeCap.Butt };
                    var oval = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
                    canvas.DrawArc(oval, start, Math.Max(sweep - 1.5f, 0.5f), false, p);
                    start += sweep;
                }
            }
            if (centerText != null)
                canvas.DrawText(centerText, cx - new SKFont(SKTypeface.Default, 30).MeasureText(centerText) / 2,
                    cy + 10, new SKFont(SKTypeface.Default, 30), textPaint);
            float lx = bounds.Left + 360, ly = bounds.Top + 60;
            foreach (var s in slices)
            {
                using var box = new SKPaint { Color = s.Color, IsAntialias = true };
                canvas.DrawRoundRect(new SKRect(lx, ly - 12, lx + 14, ly + 2), 3, 3, box);
                var pct = total > 0 ? s.Kg / total * 100 : 0;
                canvas.DrawText($"{s.Label} {FormatShort(s.Kg)} ({pct:F0}%)", lx + 20, ly, labelFont, textPaint);
                ly += 28;
                if (ly > bounds.Bottom - 20) break;
            }
        }

        public async Task ExportChartAsync(string? key, string format)
        {
            if (string.IsNullOrWhiteSpace(key) || !ChartTitles.ContainsKey(key.ToLowerInvariant()))
            {
                StatusText = "Unknown chart.";
                return;
            }
            key = key.ToLowerInvariant();
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"Fusion_{key}_{DateTime.Now:yyyyMMdd_HHmmss}.{format}",
                    Filter = format == "png" ? "PNG image (*.png)|*.png" : "PDF document (*.pdf)|*.pdf",
                };
                if (dlg.ShowDialog() != true) return;
                await Task.Run(() =>
                {
                    using var img = RenderChartImage(key);
                    if (format == "png")
                    {
                        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                        using var fs = File.OpenWrite(dlg.FileName);
                        data.SaveTo(fs);
                    }
                    else
                    {
                        using var fs = File.OpenWrite(dlg.FileName);
                        using var doc = SKDocument.CreatePdf(fs);
                        var canvas = doc.BeginPage(TileW + 80, TileH + 120);
                        DrawTitle(canvas, ChartTitles[key], 40, 56);
                        canvas.DrawImage(img, SKRect.Create(40, 80, TileW, TileH));
                        doc.EndPage();
                        doc.Close();
                    }
                });
                StatusText = $"Exported {ChartTitles[key]} to {dlg.FileName}.";
            }
            catch (System.Exception ex)
            {
                StatusText = $"Export failed: {ex.Message}";
            }
        }

        public async Task ExportDashboardAsync(string format)
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"FusionDashboard_{DateTime.Now:yyyyMMdd_HHmmss}.{format}",
                    Filter = format == "png" ? "PNG image (*.png)|*.png" : "PDF document (*.pdf)|*.pdf",
                };
                if (dlg.ShowDialog() != true) return;
                var title = $"Proweigh Fusion dashboard — {DateTime.Now:G}";
                var kpiLine = $"Transactions {TodayCount}   Weight {TodayKg:0,#} kg of {TargetKg:0,#} kg ({TargetPct:F0}%)   " +
                              $"Active batches {ActiveBatches}   Tubs {ActiveTubs} in use / {AvailableTubs} available of {TotalTubs}   Completed today {CompletedToday}";
                await Task.Run(() =>
                {
                    var tiles = ChartKeys.Select(k => (Title: ChartTitles[k], Img: RenderChartImage(k))).ToList();
                    try
                    {
                        const int pad = 24, cols = 2;
                        int pageW = cols * TileW + (cols + 1) * pad;
                        int pageH = 170 + 3 * (TileH + 44) + pad;
                        if (format == "png")
                        {
                            using var bmp = new SKBitmap(pageW, pageH);
                            using var canvas = new SKCanvas(bmp);
                            PaintDashboard(canvas, title, kpiLine, tiles, pad);
                            using var img = SKImage.FromBitmap(bmp);
                            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                            using var fs = File.OpenWrite(dlg.FileName);
                            data.SaveTo(fs);
                        }
                        else
                        {
                            using var fs = File.OpenWrite(dlg.FileName);
                            using var doc = SKDocument.CreatePdf(fs);
                            var canvas = doc.BeginPage(pageW, pageH);
                            PaintDashboard(canvas, title, kpiLine, tiles, pad);
                            doc.EndPage();
                            doc.Close();
                        }
                    }
                    finally
                    {
                        foreach (var t in tiles) t.Img.Dispose();
                    }
                });
                StatusText = $"Exported dashboard to {dlg.FileName}.";
            }
            catch (System.Exception ex)
            {
                StatusText = $"Export failed: {ex.Message}";
            }
        }

        private static void DrawTitle(SKCanvas canvas, string text, float x, float y)
        {
            using var paint = new SKPaint { Color = Ink, IsAntialias = true };
            canvas.DrawText(text, x, y, new SKFont(SKTypeface.Default, 20), paint);
        }

        private static void PaintDashboard(SKCanvas canvas, string title, string kpiLine,
            List<(string Title, SKImage Img)> tiles, int pad)
        {
            canvas.Clear(SKColors.White);
            DrawTitle(canvas, title, pad, 44);
            using (var paint = new SKPaint { Color = Muted, IsAntialias = true })
                canvas.DrawText(kpiLine, pad, 72, new SKFont(SKTypeface.Default, 13), paint);
            using (var paint = new SKPaint { Color = Ink, IsAntialias = true })
            {
                var titleFont = new SKFont(SKTypeface.Default, 15);
                float y = 100;
                for (int i = 0; i < tiles.Count; i++)
                {
                    int col = i % 2;
                    float x = pad + col * (TileW + pad);
                    if (col == 0 && i > 0) y += TileH + 44;
                    canvas.DrawText(tiles[i].Title, x, y + 18, titleFont, paint);
                    canvas.DrawImage(tiles[i].Img, SKRect.Create(x, y + 28, TileW, TileH));
                }
            }
        }
    }
}
