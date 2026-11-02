// Fusion settings page — extensible configuration values over fusion_settings.
// Condition bands for inward/supplier stock: expired cutoff + near-expiry threshold.
// Add more settings here as needed (same load/save pattern).
using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Services;

using ProWeighSuitePlatforms.Models;
namespace ProWeighSuitePlatforms.ViewModels
{
    public sealed class FusionSettingsViewModel : ViewModels.BaseViewModel
    {
        public string Title => "Fusion settings";

        private readonly AuditService _audit = new();
        private readonly string _username;

        private string _nearExpiryDaysText = FusionSettingsDefaults.NearExpiryDays.ToString();
        public string NearExpiryDaysText
        {
            get => _nearExpiryDaysText;
            set
            {
                if (SetProperty(ref _nearExpiryDaysText, value))
                    RaisePropertyChanged(nameof(BandsPreview));
            }
        }

        private string _expiredBelowDaysText = FusionSettingsDefaults.ExpiredBelowDays.ToString();
        public string ExpiredBelowDaysText
        {
            get => _expiredBelowDaysText;
            set
            {
                if (SetProperty(ref _expiredBelowDaysText, value))
                    RaisePropertyChanged(nameof(BandsPreview));
            }
        }

        // Live preview of the resulting bands as the day fields are edited.
        public string BandsPreview
        {
            get
            {
                bool okN = int.TryParse((_nearExpiryDaysText ?? string.Empty).Trim(), out var n);
                bool okE = int.TryParse((_expiredBelowDaysText ?? string.Empty).Trim(), out var e);
                if (!okN || !okE || n < 0 || e > n)
                    return "Bands: enter valid days (expired-below ≤ near-expiry).";
                return $"Bands: < {e} → Expired · {e}–{n} → Near Expiry · > {n} → Fresh (zero balance → Consumed).";
            }
        }

        private string _statusText = string.Empty;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        // Tub replacement rule (persisted; Tub master loads it as the default).
        private string _replaceAtCyclesText = FusionSettingsDefaults.TubReplaceAtCycles.ToString();
        public string ReplaceAtCyclesText
        {
            get => _replaceAtCyclesText;
            set { if (SetProperty(ref _replaceAtCyclesText, value)) ValidateReplaceAtCycles(); }
        }

        private string _warnPercentText = FusionSettingsDefaults.TubWarnPercent.ToString(CultureInfo.InvariantCulture);
        public string WarnPercentText
        {
            get => _warnPercentText;
            set { if (SetProperty(ref _warnPercentText, value)) ValidateWarnPercent(); }
        }

        private string? _replaceAtCyclesError;
        public string? ReplaceAtCyclesError { get => _replaceAtCyclesError; private set => SetProperty(ref _replaceAtCyclesError, value); }
        private string? _warnPercentError;
        public string? WarnPercentError { get => _warnPercentError; private set => SetProperty(ref _warnPercentError, value); }

        public bool HasTubErrors => !string.IsNullOrEmpty(ReplaceAtCyclesError)
            || !string.IsNullOrEmpty(WarnPercentError);

        // Zero return: when true, Live must see the scale cleared/0 after each
        // weigh before the next weight is captured.
        private bool _zeroReturnEnabled = FusionSettingsDefaults.ZeroReturn;
        public bool ZeroReturnEnabled
        {
            get => _zeroReturnEnabled;
            set => SetProperty(ref _zeroReturnEnabled, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand SaveCommand { get; }

        public FusionSettingsViewModel(string username = "System")
        {
            _username = string.IsNullOrWhiteSpace(username) ? "System" : username;
            RefreshCommand = new ViewModels.RelayCommand(async _ => await LoadAsync());
            SaveCommand = new ViewModels.RelayCommand(async _ => await SaveAllAsync());
            _ = LoadAsync();
        }

        public async Task LoadAsync()
        {
            try
            {
                await new FusionDatabaseInit(AppConfigurationManager.GetDatabasePath()).EnsureCreatedAsync();
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var (near, expiredBelow) = await repo.GetConditionThresholdsAsync();
                NearExpiryDaysText = near.ToString();
                ExpiredBelowDaysText = expiredBelow.ToString();
                var (replaceAt, warn) = await repo.GetTubReplacementSettingsAsync();
                _replaceAtCyclesText = replaceAt.ToString();
                RaisePropertyChanged(nameof(ReplaceAtCyclesText));
                _warnPercentText = warn.ToString(CultureInfo.InvariantCulture);
                RaisePropertyChanged(nameof(WarnPercentText));
                var zeroRaw = await repo.GetSettingAsync(FusionSettingsKeys.ZeroReturn, "0");
                var zt = (zeroRaw ?? string.Empty).Trim().ToLowerInvariant();
                ZeroReturnEnabled = zt == "1" || zt == "true" || zt == "yes" || zt == "on";
                ReplaceAtCyclesError = WarnPercentError = null;
                StatusText = $"Loaded. Days left < {expiredBelow} → Expired · {expiredBelow}–{near} → Near Expiry · > {near} → Fresh.";
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }
        internal static string? ValidateReplaceAtCycles(string? text)
        {
            var v = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v))
                return "Replace-at cycles is required.";
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.CurrentCulture, out var n)
                && !int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return "Replace-at must be a whole number.";
            if (n <= 0)
                return "Replace-at must be greater than 0.";
            if (n > 1000000)
                return "Replace-at must not exceed 1000000.";
            return null;
        }

        internal static string? ValidateWarnPercent(string? text)
        {
            var v = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v))
                return "Warn % is required.";
            if (!double.TryParse(v, NumberStyles.Number, CultureInfo.CurrentCulture, out var d)
                && !double.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out d))
                return "Warn % must be a number.";
            if (d < 1 || d > 99)
                return "Warn % must be between 1 and 99.";
            return null;
        }

        private void ValidateReplaceAtCycles() => ReplaceAtCyclesError = ValidateReplaceAtCycles(ReplaceAtCyclesText);
        private void ValidateWarnPercent() => WarnPercentError = ValidateWarnPercent(WarnPercentText);

        public async Task SaveAllAsync()
        {
            // One common save for every card on this page.
            if (!int.TryParse((NearExpiryDaysText ?? string.Empty).Trim(), out var near) || near < 0 || near > 365)
            {
                StatusText = "Enter near-expiry days between 0 and 365.";
                return;
            }
            if (!int.TryParse((ExpiredBelowDaysText ?? string.Empty).Trim(), out var expiredBelow) || expiredBelow < -30 || expiredBelow > 365)
            {
                StatusText = "Enter expired-below days between -30 and 365.";
                return;
            }
            if (expiredBelow > near)
            {
                StatusText = "Expired-below days must be ≤ near-expiry days.";
                return;
            }
            ValidateReplaceAtCycles();
            ValidateWarnPercent();
            if (HasTubErrors)
            {
                StatusText = !string.IsNullOrEmpty(ReplaceAtCyclesError)
                    ? ReplaceAtCyclesError
                    : WarnPercentError ?? "Please fix validation errors before saving.";
                return;
            }
            int.TryParse((ReplaceAtCyclesText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var replaceAt);
            if (replaceAt <= 0)
                int.TryParse((ReplaceAtCyclesText ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out replaceAt);
            double.TryParse((WarnPercentText ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var warn);
            if (warn < 1 || warn > 99)
                double.TryParse((WarnPercentText ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out warn);
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var oldSettings = await repo.GetSettingsAsync();
                await repo.SetSettingAsync(FusionSettingsKeys.NearExpiryDays, near.ToString());
                await repo.SetSettingAsync(FusionSettingsKeys.ExpiredBelowDays, expiredBelow.ToString());
                await repo.SetSettingAsync(FusionSettingsKeys.TubReplaceAtCycles, replaceAt.ToString());
                await repo.SetSettingAsync(FusionSettingsKeys.TubWarnPercent, warn.ToString(CultureInfo.InvariantCulture));
                await repo.SetSettingAsync(FusionSettingsKeys.ZeroReturn, ZeroReturnEnabled ? "1" : "0");
                var newSettings = await repo.GetSettingsAsync();
                await _audit.LogFusionEventAsync(_username, "Fusion_Settings_Updated",
                    "FusionSettings", "fusion_settings",
                    $"Fusion settings saved: near-expiry {near} day(s), expired-below {expiredBelow} day(s), " +
                    $"tub replace-at {replaceAt} cycles, warn {warn}%, zero return {(ZeroReturnEnabled ? "ON" : "OFF")}.",
                    AuditCategory.Configuration, AuditSeverity.Info,
                    JsonSerializer.Serialize(oldSettings), JsonSerializer.Serialize(newSettings));
                StatusText = $"Saved all settings. Bands < {expiredBelow} → Expired · {expiredBelow}–{near} → Near Expiry · > {near} → Fresh; tubs replace at {replaceAt} cycles, warn at {warn}%; zero return {(ZeroReturnEnabled ? "ON" : "OFF")}.";
            }
            catch (Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
                await _audit.LogErrorAsync(_username, "Save Fusion settings", ex);
            }
        }
    }
}
