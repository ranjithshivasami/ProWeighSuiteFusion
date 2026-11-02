using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class ReportScheduleViewModel : BaseViewModel
    {
        public string Title => "Report Schedule Settings";

        private readonly ReportScheduleService _scheduleService;
        private readonly ScheduledReportService _service;
        private readonly AuditService _auditService;
        private readonly CompanySettingsService _companySettingsService;

        public ScheduledReportSettings Settings { get; private set; } = new();

        public ICommand SaveCommand { get; }
        public ICommand SendReportNowCommand { get; }
        public ICommand SendSummaryNowCommand { get; }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }
        private string _statusMessage = string.Empty;

        public string ReportTimeError
        {
            get => _reportTimeError;
            private set => SetProperty(ref _reportTimeError, value);
        }
        private string _reportTimeError = string.Empty;

        public string SummaryTimeError
        {
            get => _summaryTimeError;
            private set => SetProperty(ref _summaryTimeError, value);
        }
        private string _summaryTimeError = string.Empty;

        public string RecipientsError
        {
            get => _recipientsError;
            private set => SetProperty(ref _recipientsError, value);
        }
        private string _recipientsError = string.Empty;

        public string CompanyReportEmail
        {
            get => _companyReportEmail;
            private set => SetProperty(ref _companyReportEmail, value);
        }
        private string _companyReportEmail = string.Empty;

        public ReadOnlyCollection<int> Hours { get; } = new(Enumerable.Range(0, 24).ToList());
        public ReadOnlyCollection<int> Minutes { get; } = new(Enumerable.Range(0, 60).ToList());

        public int ReportHour
        {
            get => GetHour(Settings.ReportTimeOfDay);
            set
            {
                if (Settings.ReportTimeOfDay != Compose(value, ReportMinute))
                {
                    Settings.ReportTimeOfDay = Compose(value, ReportMinute);
                    RaisePropertyChanged(nameof(ReportTimeOfDay));
                }
            }
        }

        public int ReportMinute
        {
            get => GetMinute(Settings.ReportTimeOfDay);
            set
            {
                if (Settings.ReportTimeOfDay != Compose(ReportHour, value))
                {
                    Settings.ReportTimeOfDay = Compose(ReportHour, value);
                    RaisePropertyChanged(nameof(ReportTimeOfDay));
                }
            }
        }

        public int SummaryHour
        {
            get => GetHour(Settings.SummaryTimeOfDay);
            set
            {
                if (Settings.SummaryTimeOfDay != Compose(value, SummaryMinute))
                {
                    Settings.SummaryTimeOfDay = Compose(value, SummaryMinute);
                    RaisePropertyChanged(nameof(SummaryTimeOfDay));
                }
            }
        }

        public int SummaryMinute
        {
            get => GetMinute(Settings.SummaryTimeOfDay);
            set
            {
                if (Settings.SummaryTimeOfDay != Compose(SummaryHour, value))
                {
                    Settings.SummaryTimeOfDay = Compose(SummaryHour, value);
                    RaisePropertyChanged(nameof(SummaryTimeOfDay));
                }
            }
        }

        public string ReportTimeOfDay => Settings.ReportTimeOfDay;
        public string SummaryTimeOfDay => Settings.SummaryTimeOfDay;

        private static int GetHour(string time) => TimeSpan.TryParse(time, out var t) ? t.Hours : 0;
        private static int GetMinute(string time) => TimeSpan.TryParse(time, out var t) ? t.Minutes : 0;
        private static string Compose(int hour, int minute) => $"{hour:00}:{minute:00}";

        public bool IsInitialized
        {
            get => _isInitialized;
            private set
            {
                if (SetProperty(ref _isInitialized, value))
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }
        }
        private bool _isInitialized = false;

        public ReportScheduleViewModel(ScheduledReportService scheduledReportService)
        {
            _scheduleService = new ReportScheduleService();
            _service = scheduledReportService;
            _auditService = new AuditService();
            _companySettingsService = new CompanySettingsService(
                new Data.CompanySettingsRepository(Configuration.AppConfigurationManager.GetDatabasePath()));

            SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => true);
            SendReportNowCommand = new RelayCommand(async _ => await SendNowAsync(isReport: true), _ => true);
            SendSummaryNowCommand = new RelayCommand(async _ => await SendNowAsync(isReport: false), _ => true);

            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            try
            {
                await _scheduleService.InitializeAsync();
                var cfg = await _scheduleService.GetAsync();
                if (cfg != null) Settings = cfg;

                RaisePropertyChanged(nameof(ReportHour));
                RaisePropertyChanged(nameof(ReportMinute));
                RaisePropertyChanged(nameof(SummaryHour));
                RaisePropertyChanged(nameof(SummaryMinute));

                await _companySettingsService.InitializeAsync();
                var company = await _companySettingsService.GetAsync();
                CompanyReportEmail = company?.ReportEmail?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReportScheduleViewModel] LoadAsync error: {ex.Message}");
                StatusMessage = $"Failed to load settings: {ex.Message}";
            }

            RaisePropertyChanged(string.Empty);
            IsInitialized = true;
        }

        private bool Validate()
        {
            ReportTimeError = string.Empty;
            SummaryTimeError = string.Empty;
            RecipientsError = string.Empty;
            bool ok = true;

            if (Settings.ReportEnabled && !TimeSpan.TryParse(Settings.ReportTimeOfDay, out _))
            {
                ReportTimeError = "Enter a valid time in HH:mm format.";
                ok = false;
            }

            if (Settings.SummaryEnabled && !TimeSpan.TryParse(Settings.SummaryTimeOfDay, out _))
            {
                SummaryTimeError = "Enter a valid time in HH:mm format.";
                ok = false;
            }

            if (!string.IsNullOrWhiteSpace(Settings.RecipientsOverride))
            {
                var emails = Settings.RecipientsOverride.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var email in emails)
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        RecipientsError = $"'{email}' is not a valid email address.";
                        ok = false;
                        break;
                    }
                }
            }

            if (!ok)
                StatusMessage = "Please correct the fields marked with errors.";

            return ok;
        }

        private async Task SaveAsync()
        {
            if (!Validate()) return;

            StatusMessage = "Saving report schedule settings...";
            try
            {
                await _scheduleService.SaveAsync(Settings);
                await _service.RescheduleAsync();

                await _auditService.LogSettingsChangedAsync(
                    "System",
                    "Report Schedule Settings",
                    null,
                    $"ReportEnabled={Settings.ReportEnabled}, ReportTime={Settings.ReportTimeOfDay}, " +
                    $"SummaryEnabled={Settings.SummaryEnabled}, SummaryTime={Settings.SummaryTimeOfDay}, " +
                    $"RecipientsOverride={Settings.RecipientsOverride}");

                StatusMessage = "Settings saved. Reports will be emailed daily at the configured times (while the app is running).";
                try { NotificationService.ShowInfo("Report Schedule", "Report schedule settings saved"); } catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReportScheduleViewModel] Error saving: {ex.Message}");
                try { NotificationService.ShowError("Report Schedule", $"Failed to save: {ex.Message}"); } catch { }
                MessageBox.Show($"Failed to save report schedule settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Failed to save settings.";
            }
        }

        private async Task SendNowAsync(bool isReport)
        {
            try
            {
                StatusMessage = isReport ? "Generating and sending Transaction Report..." : "Generating and sending Summary Report...";

                var recipients = !string.IsNullOrWhiteSpace(Settings.RecipientsOverride)
                    ? Settings.RecipientsOverride
                    : CompanyReportEmail;

                if (string.IsNullOrWhiteSpace(recipients))
                {
                    MessageBox.Show("No recipients configured. Set the Report Email in Company Settings, or add a recipients override.",
                        "Recipients Not Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
                    StatusMessage = "Send cancelled - no recipients.";
                    return;
                }

                await _service.SendReportNowAsync(isReport, !isReport);
                StatusMessage = (isReport ? "Transaction Report" : "Summary Report") + " sent successfully.";
                try { NotificationService.ShowInfo("Report Schedule", (isReport ? "Transaction Report" : "Summary Report") + " sent"); } catch { }
            }
            catch (Exception ex)
            {
                try { NotificationService.ShowError("Report Schedule", $"Send failed: {ex.Message}"); } catch { }
                MessageBox.Show($"Failed to send report: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"Send failed: {ex.Message}";
            }
        }
    }
}
