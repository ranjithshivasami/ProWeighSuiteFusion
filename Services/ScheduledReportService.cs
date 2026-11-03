using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Lightweight daily scheduler for emailing the Transaction Report and the
    /// Summary Report. Uses a single 1-minute tick timer and checks whether each
    /// enabled report is due for today. Runs only while the app is running;
    /// performs catch-up on startup for any report whose time already passed.
    /// </summary>
    public class ScheduledReportService : IDisposable
    {
        private static readonly string _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ProWeighSuiteFusion", "scheduled-report.log");

        private readonly ReportScheduleService _scheduleService;
        private readonly ReportExportService _exportService;
        private readonly EmailService _emailService;
        private readonly CompanySettingsService _companySettingsService;
        private readonly AuditService _auditService;

        private Timer? _timer;
        private int _running;

        public ScheduledReportService()
        {
            _scheduleService = new ReportScheduleService();
            _exportService = new ReportExportService();
            _emailService = new EmailService();
            _companySettingsService = new CompanySettingsService(
                new Data.CompanySettingsRepository(Configuration.AppConfigurationManager.GetDatabasePath()));
            _auditService = new AuditService();
        }

        public async Task InitializeAsync()
        {
            try
            {
                await _scheduleService.InitializeAsync();
                var cfg = await _scheduleService.GetAsync();
                if (cfg == null)
                {
                    Log("No report schedule configuration found - scheduler idle.");
                    return;
                }

                Log($"Loaded schedule: Report={(cfg.ReportEnabled ? cfg.ReportTimeOfDay : "off")}, " +
                    $"Summary={(cfg.SummaryEnabled ? cfg.SummaryTimeOfDay : "off")}");

                // Catch-up: if enabled and the time already passed today without being sent, send now.
                await SendIfDueAsync(cfg, catchUp: true);

                // Start the minute tick
                _timer?.Dispose();
                _timer = new Timer(async _ => await OnTickAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1));
                Log("Scheduler timer started (ticks every minute).");
            }
            catch (Exception ex)
            {
                Log($"InitializeAsync error: {ex}");
            }
        }

        public async Task RescheduleAsync()
        {
            try
            {
                await _scheduleService.InitializeAsync();
                var cfg = await _scheduleService.GetAsync();

                _timer?.Dispose();
                _timer = null;

                if (cfg == null || (!cfg.ReportEnabled && !cfg.SummaryEnabled))
                {
                    Log("Reschedule: nothing enabled - scheduler stopped.");
                    return;
                }

                Log($"Reschedule: Report={(cfg.ReportEnabled ? cfg.ReportTimeOfDay : "off")}, " +
                    $"Summary={(cfg.SummaryEnabled ? cfg.SummaryTimeOfDay : "off")}");

                _timer = new Timer(async _ => await OnTickAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1));
            }
            catch (Exception ex)
            {
                Log($"RescheduleAsync error: {ex}");
            }
        }

        private async Task OnTickAsync()
        {
            try
            {
                var cfg = await _scheduleService.GetAsync();
                if (cfg == null) return;
                await SendIfDueAsync(cfg, catchUp: false);
            }
            catch (Exception ex)
            {
                Log($"OnTick error: {ex}");
            }
        }

        public async Task SendReportNowAsync(bool sendReport, bool sendSummary)
        {
            if (Interlocked.Exchange(ref _running, 1) == 1)
                throw new InvalidOperationException("A report send is already in progress.");

            try
            {
                var cfg = await _scheduleService.GetAsync() ?? new ScheduledReportSettings();

                if (sendReport)
                {
                    await SendReportAsync(cfg, ReportKind.Transaction);
                    cfg.LastReportSentAt = DateTime.Now;
                }

                if (sendSummary)
                {
                    await SendReportAsync(cfg, ReportKind.Summary);
                    cfg.LastSummarySentAt = DateTime.Now;
                }

                await _scheduleService.SaveAsync(cfg);
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }

        private async Task SendIfDueAsync(ScheduledReportSettings cfg, bool catchUp)
        {
            if (Interlocked.Exchange(ref _running, 1) == 1) return;
            try
            {
                var now = DateTime.Now;

                if (cfg.ReportEnabled && IsDueToday(now, cfg.ReportTimeOfDay, cfg.LastReportSentAt, catchUp))
                {
                    await SendReportAsync(cfg, ReportKind.Transaction);
                    cfg.LastReportSentAt = DateTime.Now;
                    await _scheduleService.SaveAsync(cfg);
                }

                if (cfg.SummaryEnabled && IsDueToday(now, cfg.SummaryTimeOfDay, cfg.LastSummarySentAt, catchUp))
                {
                    await SendReportAsync(cfg, ReportKind.Summary);
                    cfg.LastSummarySentAt = DateTime.Now;
                    await _scheduleService.SaveAsync(cfg);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }

        private static bool IsDueToday(DateTime now, string timeOfDay, DateTime? lastSentAt, bool catchUp)
        {
            if (!TimeSpan.TryParse(timeOfDay, out var tod))
                return false;

            var scheduled = now.Date.Add(tod);

            // Catch-up: time passed today and not yet sent today
            if (lastSentAt == null || lastSentAt.Value.Date != now.Date)
            {
                if (catchUp && now >= scheduled) return true;
                if (!catchUp && now >= scheduled) return true;
                return false;
            }

            return false;
        }

        private async Task SendReportAsync(ScheduledReportSettings cfg, ReportKind kind)
        {
            try
            {
                var recipients = ResolveRecipients(cfg);
                if (string.IsNullOrWhiteSpace(recipients))
                {
                    Log($"[{kind}] Skipped - no recipients configured.");
                    return;
                }

                var now = DateTime.Now;
                var periodStart = now.AddDays(-1).Date; // report covers the previous day
                var periodEnd = now.AddDays(-1).Date;

                string filePath;
                string subject;
                string body;

                if (kind == ReportKind.Transaction)
                {
                    filePath = await _exportService.GenerateTransactionReportAsync(periodStart, periodEnd);
                    subject = $"Daily Transaction Report - {periodStart:dd-MM-yyyy}";
                    body = $"Please find attached the Daily Transaction Report.\n\nGenerated on: {now:dd-MM-yyyy HH:mm:ss}\nPeriod: {periodStart:dd-MM-yyyy}";
                }
                else
                {
                    filePath = await _exportService.GenerateSummaryReportAsync("Product Wise", periodStart, periodEnd);
                    subject = $"Daily Summary Report - {periodStart:dd-MM-yyyy}";
                    body = $"Please find attached the Daily Summary Report.\n\nGenerated on: {now:dd-MM-yyyy HH:mm:ss}\nPeriod: {periodStart:dd-MM-yyyy}";
                }

                var fileName = Path.GetFileName(filePath);
                await _emailService.SendEmailWithAttachmentAsync(recipients, subject, body, filePath, fileName);

                Log($"[{kind}] Sent to '{recipients}' -> {fileName}");

                try { File.Delete(filePath); } catch { }

                _ = _auditService.LogReportExportedAsync("System", $"{kind} (Scheduled Email)", 1);
            }
            catch (Exception ex)
            {
                Log($"[{kind}] Send failed: {ex}");
            }
        }

        private string ResolveRecipients(ScheduledReportSettings cfg)
        {
            if (!string.IsNullOrWhiteSpace(cfg.RecipientsOverride))
                return cfg.RecipientsOverride;

            try
            {
                var company = _companySettingsService.GetAsync().GetAwaiter().GetResult();
                return company?.ReportEmail?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void Log(string message)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
                File.AppendAllText(_logPath, sb.ToString());
            }
            catch { }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _timer = null;
            Log("Scheduler disposed.");
        }

        private enum ReportKind
        {
            Transaction,
            Summary
        }
    }
}
