using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Services.Scanners;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class AppSettingsViewModel : BaseViewModel
    {
        public string Title => "Application Settings";

        private readonly AppSettingsService _svc = new();
        private readonly SyncSettingsService _syncSettingsService = new();
        private readonly AuditService _auditService = new();
        private readonly string _currentUsername;
        private readonly string _brandDefaultHex;
        private readonly string _brandDefaultName;
        private AppSettings _settings = new();
        private SyncSettings _syncSettings = new();

        // Target UOM (moved from Scale Settings)
        public string TargetUOM
        {
            get => _settings.TargetUOM;
            set
            {
                _settings.TargetUOM = string.IsNullOrWhiteSpace(value) ? "count" : value;
                RaisePropertyChanged(nameof(TargetUOM));
                RaisePropertyChanged(nameof(TargetUomIsCount));
                RaisePropertyChanged(nameof(TargetUomIsKg));
            }
        }

        public bool TargetUomIsCount
        {
            get => TargetUOM == "count";
            set { if (value) TargetUOM = "count"; }
        }

        public bool TargetUomIsKg
        {
            get => TargetUOM == "kg";
            set { if (value) TargetUOM = "kg"; }
        }

        // Product Presence Threshold (in kg): minimum net weight for a product to be
        // considered present. Below this the weighing flow reports "No Product".
        // Exposed as text so the field can be cleared / edited freely; the value is
        // parsed on every keystroke and any problem surfaces as a friendly message
        // instead of a raw WPF conversion error.
        public string PresenceThresholdText
        {
            get => _presenceThresholdText;
            set
            {
                if (!SetProperty(ref _presenceThresholdText, value)) return;

                if (string.IsNullOrWhiteSpace(value))
                {
                    PresenceThresholdError = "Presence threshold is required.";
                    return;
                }

                var styles = NumberStyles.AllowLeadingWhite
                           | NumberStyles.AllowTrailingWhite
                           | NumberStyles.AllowDecimalPoint;
                if (double.TryParse(value, styles, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
                {
                    _settings.PresenceThreshold = parsed;
                    PresenceThresholdError = string.Empty;
                }
                else
                {
                    PresenceThresholdError = "Enter a valid number with a single decimal point (e.g. 0.05).";
                }
            }
        }

        private string _presenceThresholdText = string.Empty;

        private string _presenceThresholdError = string.Empty;
        public string PresenceThresholdError
        {
            get => _presenceThresholdError;
            private set
            {
                if (SetProperty(ref _presenceThresholdError, value))
                    RaisePropertyChanged(nameof(HasPresenceThresholdError));
            }
        }

        public bool HasPresenceThresholdError => !string.IsNullOrWhiteSpace(_presenceThresholdError);

        // Scale Resolution (decimal kg): smallest measurable value of the scale.
        // Exposed as text so the field can be cleared / edited freely; the value is
        // parsed on every keystroke and any problem surfaces as a friendly message.
        public string ScaleResolutionText
        {
            get => _scaleResolutionText;
            set
            {
                if (!SetProperty(ref _scaleResolutionText, value)) return;

                if (string.IsNullOrWhiteSpace(value))
                {
                    ScaleResolutionError = "Scale resolution is required.";
                    return;
                }

                var styles = NumberStyles.AllowLeadingWhite
                           | NumberStyles.AllowTrailingWhite
                           | NumberStyles.AllowDecimalPoint;
                if (decimal.TryParse(value, styles, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                {
                    _settings.ScaleResolution = parsed;
                    ScaleResolutionError = string.Empty;
                }
                else
                {
                    ScaleResolutionError = "Enter a valid number greater than 0 (e.g. 0.01).";
                }
            }
        }

        private string _scaleResolutionText = string.Empty;

        private string _scaleResolutionError = string.Empty;
        public string ScaleResolutionError
        {
            get => _scaleResolutionError;
            private set
            {
                if (SetProperty(ref _scaleResolutionError, value))
                    RaisePropertyChanged(nameof(HasScaleResolutionError));
            }
        }

        public bool HasScaleResolutionError => !string.IsNullOrWhiteSpace(_scaleResolutionError);

        // Maximum Weight (decimal kg): maximum capacity of the weighing scale.
        public string MaxWeightText
        {
            get => _maxWeightText;
            set
            {
                if (!SetProperty(ref _maxWeightText, value)) return;

                if (string.IsNullOrWhiteSpace(value))
                {
                    MaxWeightError = "Maximum weight is required.";
                    return;
                }

                var styles = NumberStyles.AllowLeadingWhite
                           | NumberStyles.AllowTrailingWhite
                           | NumberStyles.AllowDecimalPoint;
                if (decimal.TryParse(value, styles, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                {
                    _settings.MaxWeight = parsed;
                    MaxWeightError = string.Empty;

                    // Cross-field validation: max must exceed scale resolution.
                    if (_settings.ScaleResolution > 0 && parsed <= _settings.ScaleResolution)
                        MaxWeightError = "Maximum weight must be greater than Scale Resolution.";
                }
                else
                {
                    MaxWeightError = "Enter a valid number greater than 0 (e.g. 100).";
                }
            }
        }

        private string _maxWeightText = string.Empty;

        private string _maxWeightError = string.Empty;
        public string MaxWeightError
        {
            get => _maxWeightError;
            private set
            {
                if (SetProperty(ref _maxWeightError, value))
                    RaisePropertyChanged(nameof(HasMaxWeightError));
            }
        }

        public bool HasMaxWeightError => !string.IsNullOrWhiteSpace(_maxWeightError);

        // Weights to be Recorded
        public bool RecordAtAccept
        {
            get => _settings.RecordAtAccept;
            set { _settings.RecordAtAccept = value; RaisePropertyChanged(nameof(RecordAtAccept)); }
        }

        public bool RecordAtUnderWeight
        {
            get => _settings.RecordAtUnderWeight;
            set { _settings.RecordAtUnderWeight = value; RaisePropertyChanged(nameof(RecordAtUnderWeight)); }
        }

        public bool RecordAtOverWeight
        {
            get => _settings.RecordAtOverWeight;
            set { _settings.RecordAtOverWeight = value; RaisePropertyChanged(nameof(RecordAtOverWeight)); }
        }

        // Login security
        public bool EnableOTP
        {
            get => _settings.EnableOTP;
            set { _settings.EnableOTP = value; RaisePropertyChanged(nameof(EnableOTP)); }
        }

        // Printer triggers
        public bool PrintAtAccept
        {
            get => _settings.PrintAtAccept;
            set { _settings.PrintAtAccept = value; RaisePropertyChanged(nameof(PrintAtAccept)); }
        }

        public bool PrintAtUnderWeight
        {
            get => _settings.PrintAtUnderWeight;
            set { _settings.PrintAtUnderWeight = value; RaisePropertyChanged(nameof(PrintAtUnderWeight)); }
        }

        public bool PrintAtOverWeight
        {
            get => _settings.PrintAtOverWeight;
            set { _settings.PrintAtOverWeight = value; RaisePropertyChanged(nameof(PrintAtOverWeight)); }
        }

        // Weight Capture (moved from ScaleSettings → AppSettings, global) — now 3 modes: Manual / Auto / Hybrid
        public string WeightCaptureMode
        {
            get => _settings.WeightCaptureMode;
            set
            {
                var normalized = Models.AppSettings.NormalizeWeightCaptureMode(value);
                if (_settings.WeightCaptureMode == normalized) return;
                _settings.WeightCaptureMode = normalized;
                RaisePropertyChanged(nameof(WeightCaptureMode));
                RaisePropertyChanged(nameof(IsAutoCapture));
                RaisePropertyChanged(nameof(IsCaptureManual));
                RaisePropertyChanged(nameof(IsCaptureAuto));
                RaisePropertyChanged(nameof(IsCaptureHybrid));
                RaisePropertyChanged(nameof(IsHybrid));
                ValidateCaptureInterval();
                ValidateScanInterval();
            }
        }

        public bool IsCaptureManual
        {
            get => WeightCaptureMode == "Manual";
            set { if (value) WeightCaptureMode = "Manual"; }
        }
        public bool IsCaptureAuto
        {
            get => WeightCaptureMode == "Auto";
            set { if (value) WeightCaptureMode = "Auto"; }
        }
        public bool IsCaptureHybrid
        {
            get => WeightCaptureMode == "Hybrid";
            set { if (value) WeightCaptureMode = "Hybrid"; }
        }
        public bool IsHybrid => WeightCaptureMode == "Hybrid";

        // Legacy bool — kept for compat, maps to WeightCaptureMode != Manual
        public bool IsAutoCapture
        {
            get => _settings.IsAutoCapture;
            set => WeightCaptureMode = value ? "Auto" : "Manual";
        }

        // Product Scan — shown only when Hybrid (Checkweigher or Integrate PLC)
        public string ScanMode
        {
            get => _settings.ScanMode;
            set
            {
                var n = Models.AppSettings.NormalizeScanMode(value);
                if (_settings.ScanMode == n) return;
                _settings.ScanMode = n;
                RaisePropertyChanged(nameof(ScanMode));
                RaisePropertyChanged(nameof(IsScanManual));
                RaisePropertyChanged(nameof(IsScanAuto));
                ValidateScanInterval();
            }
        }
        public bool IsScanManual
        {
            get => ScanMode == "Manual";
            set { if (value) ScanMode = "Manual"; }
        }
        public bool IsScanAuto
        {
            get => ScanMode == "Auto";
            set { if (value) ScanMode = "Auto"; }
        }

        // Industrial Scanner – brand list, chosen in App Settings, decoupled via DI
        public IReadOnlyList<ScannerBrandDescriptor> ScannerBrands => ScannerCatalog.Brands;

        /// <summary>
        /// True when the scanner section (Brand/Filter/AutoTrigger) was edited on
        /// this page. SaveAsync uses it to avoid clobbering the Scanner section
        /// saved from Configuration → Scanner Settings with this page's stale
        /// snapshot (read-modify-write).
        /// </summary>
        private bool _scannerDirty;

        public string ScannerBrand
        {
            get => _settings.Scanner.Brand;
            set
            {
                var n = ScannerSettings.NormalizeBrand(value);
                if (_settings.Scanner.Brand == n) return;
                _settings.Scanner.Brand = n;
                _scannerDirty = true;
                RaisePropertyChanged(nameof(ScannerBrand));
                RaisePropertyChanged(nameof(ScannerBrandDisplay));
                RaisePropertyChanged(nameof(IsScannerEnabled));
            }
        }
        public string ScannerBrandDisplay => ScannerCatalog.Find(ScannerBrand)?.DisplayName ?? ScannerBrand;
        public bool IsScannerEnabled => _settings.Scanner.IsEnabled;
        public string ScannerIdFilter
        {
            get => _settings.Scanner.ScannerIdFilter;
            set
            {
                var v = value?.Trim() ?? string.Empty;
                if (_settings.Scanner.ScannerIdFilter == v) return;
                _settings.Scanner.ScannerIdFilter = v;
                _scannerDirty = true;
                RaisePropertyChanged(nameof(ScannerIdFilter));
            }
        }
        public bool ScannerAutoTrigger
        {
            get => _settings.Scanner.AutoTriggerOnScan;
            set
            {
                if (_settings.Scanner.AutoTriggerOnScan == value) return;
                _settings.Scanner.AutoTriggerOnScan = value;
                _scannerDirty = true;
                RaisePropertyChanged(nameof(ScannerAutoTrigger));
            }
        }

        public string CaptureIntervalText
        {
            get => _captureIntervalText;
            set
            {
                if (!SetProperty(ref _captureIntervalText, value)) return;
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (IsCaptureAuto) CaptureIntervalError = "Interval is required when Auto capture is selected.";
                    else CaptureIntervalError = string.Empty;
                    return;
                }
                if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    if (parsed < 100) CaptureIntervalError = "Must be at least 100 ms";
                    else if (parsed > 60000) CaptureIntervalError = "Must be at most 60000 ms";
                    else
                    {
                        _settings.CaptureIntervalMs = parsed;
                        CaptureIntervalError = string.Empty;
                    }
                }
                else
                {
                    CaptureIntervalError = "Enter a whole number between 100 and 60000.";
                }
            }
        }
        private string _captureIntervalText = string.Empty;

        private string _captureIntervalError = string.Empty;
        public string CaptureIntervalError
        {
            get => _captureIntervalError;
            private set
            {
                if (SetProperty(ref _captureIntervalError, value))
                    RaisePropertyChanged(nameof(HasCaptureIntervalError));
            }
        }
        public bool HasCaptureIntervalError => !string.IsNullOrWhiteSpace(_captureIntervalError);

        private void ValidateCaptureInterval()
        {
            if (!IsCaptureAuto)
            {
                CaptureIntervalError = string.Empty;
                return;
            }
            if (string.IsNullOrWhiteSpace(_captureIntervalText))
                CaptureIntervalError = "Interval is required when Auto capture is selected.";
            else if (!int.TryParse(_captureIntervalText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) || p < 100 || p > 60000)
                CaptureIntervalError = "Must be between 100 and 60000 ms";
            else
                CaptureIntervalError = string.Empty;
        }

        public string ScanIntervalText
        {
            get => _scanIntervalText;
            set
            {
                if (!SetProperty(ref _scanIntervalText, value)) return;
                if (string.IsNullOrWhiteSpace(value))
                {
                    if (IsHybrid) ScanIntervalError = "Scan interval is required when Hybrid is selected.";
                    else ScanIntervalError = string.Empty;
                    return;
                }
                if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    if (parsed < 100) ScanIntervalError = "Must be at least 100 ms";
                    else if (parsed > 60000) ScanIntervalError = "Must be at most 60000 ms";
                    else
                    {
                        _settings.ScanIntervalMs = parsed;
                        ScanIntervalError = string.Empty;
                    }
                }
                else
                {
                    ScanIntervalError = "Enter a whole number between 100 and 60000.";
                }
            }
        }
        private string _scanIntervalText = string.Empty;
        private string _scanIntervalError = string.Empty;
        public string ScanIntervalError
        {
            get => _scanIntervalError;
            private set
            {
                if (SetProperty(ref _scanIntervalError, value))
                    RaisePropertyChanged(nameof(HasScanIntervalError));
            }
        }
        public bool HasScanIntervalError => !string.IsNullOrWhiteSpace(_scanIntervalError);
        private void ValidateScanInterval()
        {
            if (!IsHybrid)
            {
                ScanIntervalError = string.Empty;
                return;
            }
            if (string.IsNullOrWhiteSpace(_scanIntervalText))
                ScanIntervalError = "Scan interval is required when Hybrid is selected.";
            else if (!int.TryParse(_scanIntervalText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) || p < 100 || p > 60000)
                ScanIntervalError = "Must be between 100 and 60000 ms";
            else
                ScanIntervalError = string.Empty;
        }

        public ICommand SaveCommand { get; }
        public ICommand PreviewThemeCommand { get; }
        public ICommand ResetBrandThemeCommand { get; }

        // Brand theme — admin-switchable accent color
        public IReadOnlyList<ThemeService.ThemeOption> BrandThemes => ThemeService.BuiltInThemes;

        private ThemeService.ThemeOption? _selectedBrandTheme;
        public ThemeService.ThemeOption? SelectedBrandTheme
        {
            get => _selectedBrandTheme;
            set
            {
                if (SetProperty(ref _selectedBrandTheme, value) && value != null)
                {
                    // Picking a preset is an explicit admin choice: persist the
                    // override so it survives a rebuild of the brand.
                    _settings.BrandThemeCustom = true;
                    BrandPrimaryHexText = value.PrimaryHex;
                    BrandThemeError = string.Empty;
                    try
                    {
                        ThemeService.Apply(value.PrimaryHex, value.Name);
                    }
                    catch (Exception ex)
                    {
                        BrandThemeError = $"Could not apply theme '{value.Name}': {ex.Message}";
                    }
                }
            }
        }

        private string _brandPrimaryHexText = "#FF9F00";
        public string BrandPrimaryHexText
        {
            get => _brandPrimaryHexText;
            set
            {
                if (!SetProperty(ref _brandPrimaryHexText, value)) return;
                // Live-validate only; Apply happens via preset setter or Apply button
                if (string.IsNullOrWhiteSpace(value))
                    BrandThemeError = "Enter a hex color (e.g. #086BE8) or pick a preset.";
                else
                {
                    var t = value.Trim();
                    if (!t.StartsWith("#")) t = "#" + t;
                    if (t.Length == 4 || t.Length == 7 || t.Length == 9)
                        BrandThemeError = string.Empty;
                    else
                        BrandThemeError = "Use #RGB, #RRGGBB or #AARRGGBB (e.g. #086BE8).";
                }
            }
        }

        private string _brandThemeError = string.Empty;
        public string BrandThemeError
        {
            get => _brandThemeError;
            private set
            {
                if (SetProperty(ref _brandThemeError, value))
                    RaisePropertyChanged(nameof(HasBrandThemeError));
            }
        }
        public bool HasBrandThemeError => !string.IsNullOrWhiteSpace(_brandThemeError);

        // Sync master switch — persisted in sync-settings.json and applied live
        // via SyncGate so transaction + audit sync pause immediately on toggle.
        public bool EnableSync
        {
            get => _syncSettings.Enabled;
            set
            {
                if (_syncSettings.Enabled == value) return;
                _syncSettings.Enabled = value;
                SyncGate.Enabled = value;
                RaisePropertyChanged(nameof(EnableSync));
            }
        }

        public AppSettingsViewModel(string currentUsername = "System",
            string? brandPrimaryHex = null, string? brandSecondaryHex = null, string? brandName = null)
        {
            _currentUsername = currentUsername;
            // Build-stamped brand color (Hosts/<flavor>/branding.json). Used as the
            // default when no admin theme override has been saved, and as the
            // "Use brand default" reset target.
            _brandDefaultHex = Models.AppSettings.NormalizeBrandHex(brandPrimaryHex);
            _brandDefaultName = string.IsNullOrWhiteSpace(brandName) ? "Brand" : brandName.Trim();
            SaveCommand = new RelayCommand(async _ => await SaveAsync());
            PreviewThemeCommand = new RelayCommand(_ => PreviewTheme());
            ResetBrandThemeCommand = new RelayCommand(_ => ResetBrandTheme());

            // Load settings synchronously so the view binds with the persisted values
            // from the very first frame. Previously LoadAsync() was fire-and-forget:
            // any edit made before the async load completed was silently overwritten
            // (e.g. the "Validate weight with external API" checkbox snapping back to
            // unchecked and then being saved back to disk as false).
            _settings = LoadSettingsNow();
            _syncSettings = LoadSyncSettingsNow();
            ApplyLoadedSettings();
        }

        private SyncSettings LoadSyncSettingsNow()
        {
            try
            {
                // Synchronous load (plain File.ReadAllText) — same UI-thread
                // safety rationale as LoadSettingsNow above.
                return _syncSettingsService.Load();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettingsViewModel] Synchronous sync-settings load failed, using defaults: {ex.Message}");
                return new SyncSettings();
            }
        }

        private AppSettings LoadSettingsNow()
        {
            try
            {
                // Synchronous load (plain File.ReadAllText): the view model is
                // constructed on the UI thread during navigation, and blocking on
                // an async file read there can freeze the whole app.
                return _svc.Load();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettingsViewModel] Synchronous settings load failed, using defaults: {ex.Message}");
                return new AppSettings();
            }
        }

        private void PreviewTheme()
        {
            if (!string.IsNullOrWhiteSpace(BrandThemeError)) return;
            var hex = Models.AppSettings.NormalizeBrandHex(BrandPrimaryHexText);
            BrandPrimaryHexText = hex;
            // Explicit Apply of a custom hex = admin theme override.
            _settings.BrandThemeCustom = true;
            try
            {
                ThemeService.Apply(hex, SelectedBrandTheme?.Name ?? "Custom");
            }
            catch (Exception ex)
            {
                BrandThemeError = $"Could not preview theme: {ex.Message}";
            }
        }

        private void ResetBrandTheme()
        {
            // Return to the build-stamped brand color and clear the override flag
            // so the brand color stays authoritative across rebuilds.
            _settings.BrandThemeCustom = false;
            BrandThemeError = string.Empty;
            BrandPrimaryHexText = _brandDefaultHex;
            _selectedBrandTheme = null;
            foreach (var t in ThemeService.BuiltInThemes)
            {
                if (string.Equals(t.PrimaryHex, _brandDefaultHex, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedBrandTheme = t;
                    break;
                }
            }
            RaisePropertyChanged(nameof(SelectedBrandTheme));
            try
            {
                ThemeService.Apply(_brandDefaultHex, _selectedBrandTheme?.Name ?? _brandDefaultName);
            }
            catch (Exception ex)
            {
                BrandThemeError = $"Could not apply brand default theme: {ex.Message}";
            }
        }

        public async Task LoadAsync()
        {
            _settings = await _svc.LoadAsync();
            ApplyLoadedSettings();
        }

        private void ApplyLoadedSettings()
        {
            // Ensure global capture defaults if migrating from ScaleSettings
            if (_settings.CaptureIntervalMs <= 0) _settings.CaptureIntervalMs = 500;
            RaisePropertyChanged(nameof(TargetUOM));
            RaisePropertyChanged(nameof(TargetUomIsCount));
            RaisePropertyChanged(nameof(TargetUomIsKg));
            _presenceThresholdText = _settings.PresenceThreshold.ToString("0.###", CultureInfo.InvariantCulture);
            PresenceThresholdError = string.Empty;
            RaisePropertyChanged(nameof(PresenceThresholdText));
            _scaleResolutionText = _settings.ScaleResolution.ToString("0.###", CultureInfo.InvariantCulture);
            ScaleResolutionError = string.Empty;
            RaisePropertyChanged(nameof(ScaleResolutionText));
            _maxWeightText = _settings.MaxWeight.ToString("0.###", CultureInfo.InvariantCulture);
            MaxWeightError = string.Empty;
            RaisePropertyChanged(nameof(MaxWeightText));
            RaisePropertyChanged(nameof(RecordAtAccept));
            RaisePropertyChanged(nameof(RecordAtUnderWeight));
            RaisePropertyChanged(nameof(RecordAtOverWeight));
            RaisePropertyChanged(nameof(EnableOTP));
            RaisePropertyChanged(nameof(PrintAtAccept));
            RaisePropertyChanged(nameof(PrintAtUnderWeight));
            RaisePropertyChanged(nameof(PrintAtOverWeight));
            RaisePropertyChanged(nameof(WeightCaptureMode));
            RaisePropertyChanged(nameof(IsCaptureManual));
            RaisePropertyChanged(nameof(IsCaptureAuto));
            RaisePropertyChanged(nameof(IsCaptureHybrid));
            RaisePropertyChanged(nameof(IsAutoCapture));
            _captureIntervalText = _settings.CaptureIntervalMs.ToString(CultureInfo.InvariantCulture);
            CaptureIntervalError = string.Empty;
            RaisePropertyChanged(nameof(CaptureIntervalText));
            ValidateCaptureInterval();
            // Scan (Hybrid only)
            if (_settings.ScanIntervalMs <= 0) _settings.ScanIntervalMs = 1000;
            RaisePropertyChanged(nameof(ScanMode));
            RaisePropertyChanged(nameof(IsScanManual));
            RaisePropertyChanged(nameof(IsScanAuto));
            RaisePropertyChanged(nameof(IsHybrid));
            _scanIntervalText = _settings.ScanIntervalMs.ToString(CultureInfo.InvariantCulture);
            ScanIntervalError = string.Empty;
            RaisePropertyChanged(nameof(ScanIntervalText));
            ValidateScanInterval();
            RaisePropertyChanged(nameof(ScannerBrand));
            RaisePropertyChanged(nameof(ScannerBrandDisplay));
            RaisePropertyChanged(nameof(IsScannerEnabled));
            RaisePropertyChanged(nameof(ScannerIdFilter));
            RaisePropertyChanged(nameof(ScannerAutoTrigger));
            RaisePropertyChanged(nameof(ScannerBrands));
            // Brand theme — the build-stamped brand color is authoritative unless
            // an admin explicitly picked an override (BrandThemeCustom).
            var useBrandDefault = !_settings.BrandThemeCustom;
            var savedHex = useBrandDefault
                ? _brandDefaultHex
                : Models.AppSettings.NormalizeBrandHex(_settings.BrandPrimaryHex);
            var savedName = useBrandDefault ? _brandDefaultName : _settings.BrandThemeName;
            _brandPrimaryHexText = savedHex;
            BrandThemeError = string.Empty;
            RaisePropertyChanged(nameof(BrandPrimaryHexText));
            ThemeService.ThemeOption? match = null;
            foreach (var t in ThemeService.BuiltInThemes)
            {
                if (string.Equals(t.PrimaryHex, savedHex, StringComparison.OrdinalIgnoreCase))
                { match = t; break; }
            }
            if (match == null)
            {
                foreach (var t in ThemeService.BuiltInThemes)
                {
                    if (string.Equals(t.Name, savedName, StringComparison.OrdinalIgnoreCase))
                    { match = t; break; }
                }
            }
            _selectedBrandTheme = match;
            RaisePropertyChanged(nameof(SelectedBrandTheme));
            if (!useBrandDefault)
            {
                try
                {
                    ThemeService.ApplySavedTheme(savedHex, _settings.BrandThemeName);
                }
                catch (Exception ex)
                {
                    BrandThemeError = $"Saved theme '{_settings.BrandThemeName}' ({savedHex}) could not be applied: {ex.Message}";
                }
            }
            // Sync master switch
            RaisePropertyChanged(nameof(EnableSync));
        }

        public async Task SaveAsync()
        {
            try
            {
                // Presence Threshold must be a valid non-negative number before saving.
                if (!string.IsNullOrWhiteSpace(PresenceThresholdError) || string.IsNullOrWhiteSpace(PresenceThresholdText))
                {
                    var validationMessage = string.IsNullOrWhiteSpace(PresenceThresholdError)
                        ? "Please enter a valid product presence threshold."
                        : PresenceThresholdError;
                    MessageBox.Show(validationMessage, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Scale Resolution and Maximum Weight are required and must be valid.
                if (!string.IsNullOrWhiteSpace(ScaleResolutionError) || string.IsNullOrWhiteSpace(ScaleResolutionText))
                {
                    var validationMessage = string.IsNullOrWhiteSpace(ScaleResolutionError)
                        ? "Please enter a valid scale resolution."
                        : ScaleResolutionError;
                    MessageBox.Show(validationMessage, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(MaxWeightError) || string.IsNullOrWhiteSpace(MaxWeightText))
                {
                    var validationMessage = string.IsNullOrWhiteSpace(MaxWeightError)
                        ? "Please enter a valid maximum weight."
                        : MaxWeightError;
                    MessageBox.Show(validationMessage, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Cross-field validation: maximum weight must exceed scale resolution.
                if (_settings.MaxWeight <= _settings.ScaleResolution)
                {
                    MessageBox.Show("Maximum weight must be greater than Scale Resolution.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Weight Capture — interval only for Auto (not Hybrid per request: don't show CaptureInterval for Hybrid)
                ValidateCaptureInterval();
                if (IsCaptureAuto && (!string.IsNullOrWhiteSpace(CaptureIntervalError) || string.IsNullOrWhiteSpace(CaptureIntervalText)))
                {
                    var msg = string.IsNullOrWhiteSpace(CaptureIntervalError) ? "Please enter a valid capture interval (100-60000 ms)." : CaptureIntervalError;
                    MessageBox.Show(msg, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!IsCaptureAuto)
                {
                    // normalize interval when not Auto (Manual or Hybrid — Hybrid uses Scan interval)
                    _settings.CaptureIntervalMs = int.TryParse(_captureIntervalText, out var p) && p >= 100 && p <= 60000 ? p : 500;
                }

                // Product Scan — only when Hybrid
                ValidateScanInterval();
                if (IsHybrid && (!string.IsNullOrWhiteSpace(ScanIntervalError) || string.IsNullOrWhiteSpace(ScanIntervalText)))
                {
                    var msg = string.IsNullOrWhiteSpace(ScanIntervalError) ? "Please enter a valid scan interval (100-60000 ms)." : ScanIntervalError;
                    MessageBox.Show(msg, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!IsHybrid)
                {
                    _settings.ScanIntervalMs = int.TryParse(_scanIntervalText, out var p) && p >= 100 && p <= 60000 ? p : 1000;
                }

                // Brand theme — validate, persist, apply live
                if (!string.IsNullOrWhiteSpace(BrandThemeError))
                {
                    MessageBox.Show(BrandThemeError, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var brandHex = Models.AppSettings.NormalizeBrandHex(BrandPrimaryHexText);
                var brandName = SelectedBrandTheme?.Name
                    ?? (_settings.BrandThemeCustom ? ThemeService.CurrentName : _brandDefaultName);
                // If custom hex doesn't match the selected preset, mark as Custom
                if (SelectedBrandTheme != null && !string.Equals(SelectedBrandTheme.PrimaryHex, brandHex, StringComparison.OrdinalIgnoreCase))
                    brandName = "Custom";
                // A hex that differs from the build-stamped brand color is an
                // explicit admin pick: persist it as an override. Saving without
                // touching the theme keeps BrandThemeCustom=false, so the brand
                // color stays authoritative.
                if (!string.Equals(brandHex, _brandDefaultHex, StringComparison.OrdinalIgnoreCase))
                    _settings.BrandThemeCustom = true;
                _settings.BrandPrimaryHex = brandHex;
                _settings.BrandThemeName = brandName;
                ThemeService.Apply(brandHex, brandName);

                // Read-modify-write: preserve sections saved by other pages
                // (e.g. Configuration → Scanner Settings) unless the scanner
                // section was edited on this page. Without this, saving App
                // Settings after Scanner Settings silently reset Brand to the
                // stale value this VM loaded at construction, leaving the footer
                // stuck on "Scanner not configured".
                if (!_scannerDirty)
                {
                    var freshSettings = await _svc.LoadAsync();
                    _settings.Scanner = freshSettings.Scanner ?? new ScannerSettings();
                }

                await _svc.SaveAsync(_settings);

                // Persist the sync master switch (sync-settings.json). The live
                // SyncGate was already updated when the checkbox toggled.
                await _syncSettingsService.SaveAsync(_syncSettings);

                await _auditService.LogSettingsChangedAsync(
                    _currentUsername,
                    "Application Settings",
                    null,
                    $"TargetUOM={_settings.TargetUOM}, PresenceThreshold={_settings.PresenceThreshold}, ScaleResolution={_settings.ScaleResolution}, MaxWeight={_settings.MaxWeight}, RecordAtAccept={_settings.RecordAtAccept}, RecordAtUnderWeight={_settings.RecordAtUnderWeight}, RecordAtOverWeight={_settings.RecordAtOverWeight}, PrintAtAccept={_settings.PrintAtAccept}, PrintAtUnderWeight={_settings.PrintAtUnderWeight}, PrintAtOverWeight={_settings.PrintAtOverWeight}, EnableOTP={_settings.EnableOTP}, WeightCaptureMode={_settings.WeightCaptureMode}, CaptureIntervalMs={_settings.CaptureIntervalMs}, ScanMode={_settings.ScanMode}, ScanIntervalMs={_settings.ScanIntervalMs}, ScannerBrand={_settings.Scanner.Brand}, ScannerIdFilter={_settings.Scanner.ScannerIdFilter}, ScannerAutoTrigger={_settings.Scanner.AutoTriggerOnScan}, BrandTheme={_settings.BrandThemeName}, BrandPrimaryHex={_settings.BrandPrimaryHex}");

                var captureDisplay = WeightCaptureMode == "Hybrid" ? $"Hybrid ({_settings.CaptureIntervalMs} ms, Scan {ScanMode} {ScanIntervalText} ms)" : IsAutoCapture ? $"Auto ({_settings.CaptureIntervalMs} ms)" : "Manual";
                var message =
                    "Application settings saved successfully!\n\n" +
                    $"  Target UOM: {_settings.TargetUOM}\n" +
                    $"  Product Presence Threshold: {_settings.PresenceThreshold:0.###} kg\n" +
                    $"  Scale Resolution: {_settings.ScaleResolution:0.###} kg\n" +
                    $"  Maximum Weight: {_settings.MaxWeight:0.###} kg\n" +
                    $"  Record at Accept: {(RecordAtAccept ? "Yes" : "No")}\n" +
                    $"  Record at Under Weight: {(RecordAtUnderWeight ? "Yes" : "No")}\n" +
                    $"  Record at Over Weight: {(RecordAtOverWeight ? "Yes" : "No")}\n\n" +
                    $"Weight Capture: {captureDisplay}\n\n" +
                    "Printing triggers:\n" +
                    $"  Print at Accept: {(PrintAtAccept ? "Yes" : "No")}\n" +
                    $"  Print at Under Weight: {(PrintAtUnderWeight ? "Yes" : "No")}\n" +
                    $"  Print at Over Weight: {(PrintAtOverWeight ? "Yes" : "No")}\n\n" +
                    $"Security: Enable OTP for Super Admin: {(EnableOTP ? "Yes" : "No")}\n\n" +
                    $"Brand Theme: {brandName} ({brandHex})\n\n" +
                    "Changes take effect on the next weighing transaction. Batch Weighing will pick up the new capture mode after refresh.";

                MessageBox.Show(
                    message,
                    "Settings Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to save application settings: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
