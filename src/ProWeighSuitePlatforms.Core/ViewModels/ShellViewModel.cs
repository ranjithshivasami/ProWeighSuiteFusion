using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Net.NetworkInformation;
using ProWeighSuitePlatforms.Core.Wpf.Navigation;
using ProWeighSuitePlatforms.Platform.Weighing.Capture;
using ProWeighSuitePlatforms.Hosting;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Services.Scanners;
using ProWeighSuitePlatforms.ViewModels;
using ProWeighSuitePlatforms.Views;
using ProWeighSuitePlatforms.Modules.Infrastructure;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class ShellViewModel : BaseViewModel, IScaleSessionControl
    {
        private const string FusionDashboardRoute = "FusionDashboard";

        private readonly MainViewModel _main;
        private readonly IHostContext _host;
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _networkCheckTimer;
        private readonly DispatcherTimer _syncStatsTimer;

        // ===== Scale/Scanner status for the footer status bar =====
        // The footer must show the real hardware state for every page (Fusion,
        // Nexus, Batch weighing), not just while BatchWeighingViewModel is open.
        private string _scaleStatusText = "Scale not connected";
        public string ScaleStatusText { get => _scaleStatusText; set => SetProperty(ref _scaleStatusText, value); }
        private bool _isScaleConnected;
        public bool IsScaleConnected { get => _isScaleConnected; set => SetProperty(ref _isScaleConnected, value); }
        private IBatchScaleStatus? _trackedBatchVm;
        private readonly DispatcherTimer _deviceStatusTimer;
        private bool? _lastScaleTransportOpen;
        private string _lastScannerStatus = string.Empty;

        private string _scannerStatusText = "Scanner not connected";
        public string ScannerStatusText { get => _scannerStatusText; set => SetProperty(ref _scannerStatusText, value); }
        private string _scannerStatusDetail = "Scanner not connected";
        public string ScannerStatusDetail { get => _scannerStatusDetail; set => SetProperty(ref _scannerStatusDetail, value); }
        private bool _isScannerConnected;
        public bool IsScannerConnected { get => _isScannerConnected; set => SetProperty(ref _isScannerConnected, value); }

        private static ScaleReaderService GetSharedScaleReader() =>
            ScaleReaderService.GetShared(new ProWeighSuitePlatforms.Data.ScaleSettingsRepository(
                ProWeighSuitePlatforms.Configuration.AppConfigurationManager.GetDatabasePath()));

        private void RefreshDeviceStatus()
        {
            RefreshScaleStatus();
            RefreshScannerStatus();
        }

        private void RefreshScaleStatus(bool force = false)
        {
            try
            {
                var reader = GetSharedScaleReader();
                bool connected = reader.IsTransportOpen;
                bool changed = _lastScaleTransportOpen != connected;
                _lastScaleTransportOpen = connected;
                IsScaleConnected = connected;

                if (connected)
                {
                    if (force || changed || string.IsNullOrWhiteSpace(ScaleStatusText)
                        || ScaleStatusText.StartsWith("Scale not connected", StringComparison.OrdinalIgnoreCase)
                        || ScaleStatusText.StartsWith("Connecting", StringComparison.OrdinalIgnoreCase))
                        ScaleStatusText = BuildScaleConnectedText(reader);
                }
                else if (force || changed
                    || ScaleStatusText.StartsWith("Scale connected", StringComparison.OrdinalIgnoreCase))
                {
                    ScaleStatusText = "Scale not connected";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Shell] RefreshScaleStatus failed: {ex.Message}");
            }
        }

        private static string BuildScaleConnectedText(ScaleReaderService reader)
        {
            var s = reader.CurrentSettings;
            if (s == null) return "Scale connected";
            var endpoint = reader.IsTcpMode
                ? $"{s.IpAddress}:{s.Port}"
                : s.PortName;
            var name = string.IsNullOrWhiteSpace(s.ScaleName) ? "Scale" : s.ScaleName;
            return $"Scale connected: {name} ({endpoint}) [{s.Mode} mode]";
        }

        private void RefreshScannerStatus()
        {
            try
            {
                // The saved settings are the source of truth for "configured" –
                // never the app singleton. The singleton is created once at
                // startup and can be stale (brand saved after startup, or a
                // startup race), which used to keep the footer stuck on
                // "Scanner not configured" even after Scanner Settings were
                // configured.
                var settingsBrand = GetConfiguredScannerBrand();
                var svc = _host.ScannerService;

                if (string.Equals(settingsBrand, "None", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyScannerStatus("Scanner not configured", "Scanner not configured", false);
                    return;
                }

                if (string.Equals(settingsBrand, "Wedge", StringComparison.OrdinalIgnoreCase))
                {
                    // Wedge is a valid (passive) configuration: the HID keyboard
                    // wedge types scans into the focused code field, so there is
                    // no connection to open and it is always ready.
                    ApplyScannerStatus("Scanner ready: Wedge (HID keyboard)",
                        "Scanner configured as keyboard wedge (HID). Scans are typed into the focused code field.",
                        true);
                    return;
                }

                // Singleton missing or not matching the configured brand (brand
                // changed in-session, or a startup race) – recreate it and let
                // the next timer tick pick the fresh state up. Failed recreates
                // retry on a 30s cooldown so a persistent provider failure does
                // not spin a new provider every 2s tick.
                if (svc == null
                    || !string.Equals(svc.ActiveBrand, settingsBrand, StringComparison.OrdinalIgnoreCase))
                {
                    bool allowRetry = !_scannerRecreateInFlight
                        && (DateTime.UtcNow - _lastScannerRecreateAttemptUtc).TotalSeconds >= ScannerRecreateRetrySeconds;
                    if (allowRetry)
                    {
                        _lastScannerRecreateAttemptUtc = DateTime.UtcNow;
                        _scannerRecreateInFlight = true;
                        _ = RecreateScannerServiceAsync(settingsBrand);
                    }
                    // The recreate above is in flight (or on cooldown). Don't
                    // flash "not configured" – the settings say the scanner IS
                    // configured.
                    ApplyScannerStatus($"Scanner: {settingsBrand}",
                        $"Scanner service starting for {settingsBrand}...", false);
                    return;
                }

                var brand = ScannerSettings.NormalizeBrand(svc.ActiveBrand);
                var connected = svc.IsConnected;
                var text = connected
                    ? $"Scanner connected: {brand}"
                    : $"Scanner not connected: {brand}";
                // Keep the footer label short; the full reason stays in the
                // tooltip instead of being cut off by the status bar width.
                var error = svc.LastError?.Trim() ?? string.Empty;
                if (error.StartsWith("RAW-FAIL ", StringComparison.OrdinalIgnoreCase))
                    error = error["RAW-FAIL ".Length..].Trim();
                var detail = string.IsNullOrWhiteSpace(error) ? text : $"{text}\r\n{error}";
                ApplyScannerStatus(text, detail, connected);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Shell] RefreshScannerStatus failed: {ex.Message}");
            }
        }

        private void ApplyScannerStatus(string text, string detail, bool connected)
        {
            IsScannerConnected = connected;
            if (string.Equals(_lastScannerStatus, text, StringComparison.Ordinal)
                && string.Equals(_scannerStatusDetail, detail, StringComparison.Ordinal))
            {
                return;
            }

            _lastScannerStatus = text;
            ScannerStatusText = text;
            ScannerStatusDetail = detail;
        }

        private bool _scannerRecreateInFlight;
        private DateTime _lastScannerRecreateAttemptUtc;
        private const int ScannerRecreateRetrySeconds = 30;
        private DateTime _lastScannerSettingsWriteUtc;
        private string _cachedScannerBrand = string.Empty;

        /// <summary>
        /// Brand saved in %APPDATA%\ProWeighSuitePlatforms\appsettings.json,
        /// cached against the file's last-write time so the 2s footer timer does
        /// not hit the disk on every tick.
        /// </summary>
        private string GetConfiguredScannerBrand()
        {
            try
            {
                var settingsService = new AppSettingsService();
                var writeUtc = File.GetLastWriteTimeUtc(settingsService.GetSettingsFilePath());
                if (writeUtc != _lastScannerSettingsWriteUtc || string.IsNullOrEmpty(_cachedScannerBrand))
                {
                    _lastScannerSettingsWriteUtc = writeUtc;
                    _cachedScannerBrand = ScannerSettings.NormalizeBrand(settingsService.Load().Scanner?.Brand);
                }
                return _cachedScannerBrand;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Shell] Read configured scanner brand failed: {ex.Message}");
                return "None";
            }
        }

        private async Task RecreateScannerServiceAsync(string expectedBrand)
        {
            try
            {
                // Stop/dispose the stale service first so a brand change frees
                // the transport (COM port / TCP) before the new provider opens it.
                var old = _host.ScannerService;
                if (old != null)
                {
                    try { await old.StopAsync(); } catch { }
                    try { await old.DisposeAsync(); } catch { }
                }

                var fresh = await ScannerServiceFactory.CreateFromSettingsAsync();
                var disp = System.Windows.Application.Current?.Dispatcher;
                if (disp != null && !disp.CheckAccess())
                    await disp.InvokeAsync(() => _host.SetScannerService(fresh));
                else
                    _host.SetScannerService(fresh);
                System.Diagnostics.Debug.WriteLine($"[Shell] Recreated scanner service for {expectedBrand} (stale was {old?.ActiveBrand ?? "null"})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Shell] Scanner service recreate failed: {ex.Message}");
            }
            finally
            {
                _scannerRecreateInFlight = false;
            }
        }

        private void OnSharedScaleWeightChanged(object? sender, double weight)
        {
            // A reading proves the connection; refresh the footer immediately even
            // when the active page is Fusion/Nexus (no BatchWeighingViewModel).
            if (IsScaleConnected
                && !ScaleStatusText.StartsWith("Scale not connected", StringComparison.OrdinalIgnoreCase)
                && !ScaleStatusText.Contains("read error", StringComparison.OrdinalIgnoreCase))
                return;
            var disp = System.Windows.Application.Current?.Dispatcher;
            if (disp != null && !disp.CheckAccess())
                disp.InvokeAsync(() => RefreshScaleStatus(force: true));
            else
                RefreshScaleStatus(force: true);
        }

        /// <summary>
        /// Opens the scale connection and starts capture/polling with the given settings
        /// (or the saved settings when null). Invoked by the Nexus Collect page's Start
        /// button. The scale is no longer opened at login so the Scale Settings connection
        /// test can use the port before data collection is started.
        /// </summary>
        public async Task<bool> ConnectScaleAsync(ScaleSettings? settings = null)
        {
            if (settings == null)
            {
                var config = new ProWeighSuitePlatforms.Services.ScaleConfigService();
                settings = await config.LoadSettingsAsync();
            }
            // No saved scale (fresh install default is also returned) — keep default status.
            if (settings == null) return false;

            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuitePlatforms", "proweighsuiteplatform.db");
            var svc = GetSharedScaleReader();

            bool ok = await svc.StartReadingAsync(settings);
            if (!ok) return false;

            // Store every line the scale sends into the Nexus transaction table.
            // Subscribe before polling starts so no reading is missed.
            ProductScaleCaptureLocator.Current?.Start(svc, settings, dbPath);
            svc.StartPolling();

            var disp = System.Windows.Application.Current?.Dispatcher;
            if (disp != null)
                await disp.InvokeAsync(() =>
                {
                    _lastScaleTransportOpen = true;
                    IsScaleConnected = true;
                    ScaleStatusText = $"Scale connected: {settings.ScaleName} ({settings.PortName}) [{settings.Mode} mode]";
                });
            return true;
        }

        /// <summary>
        /// Closes the scale connection and detaches the Nexus capture. Invoked by the
        /// Nexus Collect page's Stop button so the Scale Settings connection test can
        /// open the port again.
        /// </summary>
        public void DisconnectScale()
        {
            try
            {
                var svc = GetSharedScaleReader();
                svc.StopReading();
                ProductScaleCaptureLocator.Current?.Stop();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Shell] Scale disconnect error: {ex.Message}");
            }

            _lastScaleTransportOpen = false;
            IsScaleConnected = false;
            ScaleStatusText = "Scale not connected";
        }

        private void OnScaleConnectionError(object? sender, string e)
        {
            var disp = System.Windows.Application.Current?.Dispatcher;
            if (disp != null && !disp.CheckAccess())
                disp.InvokeAsync(() => ApplyScaleConnectionError(e));
            else
                ApplyScaleConnectionError(e);
        }

        private void ApplyScaleConnectionError(string e)
        {
            bool connected = false;
            try { connected = GetSharedScaleReader().IsTransportOpen; } catch { }
            _lastScaleTransportOpen = connected;
            IsScaleConnected = connected;

            if (connected)
                ScaleStatusText = $"Scale connected (read error): {e}";
            else
                ScaleStatusText = string.IsNullOrWhiteSpace(e) ? "Scale not connected" : $"Scale not connected. {e}";
        }

        private void AttachBatchVm(IBatchScaleStatus? vm)
        {
            if (_trackedBatchVm != null)
                _trackedBatchVm.PropertyChanged -= OnBatchVmPropertyChanged;
            _trackedBatchVm = vm;
            if (_trackedBatchVm != null)
            {
                _trackedBatchVm.PropertyChanged += OnBatchVmPropertyChanged;
                ScaleStatusText = _trackedBatchVm.ScaleStatusText;
                IsScaleConnected = _trackedBatchVm.IsScaleConnected;
            }
        }
        private void OnBatchVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_trackedBatchVm == null) return;
            switch (e.PropertyName)
            {
                case nameof(IBatchScaleStatus.ScaleStatusText):
                    ScaleStatusText = _trackedBatchVm.ScaleStatusText;
                    break;
                case nameof(IBatchScaleStatus.IsScaleConnected):
                    IsScaleConnected = _trackedBatchVm.IsScaleConnected;
                    break;
            }
        }

        public User? CurrentUser => _main.CurrentUser;
        
        public string CurrentUserDisplayName => CurrentUser?.Username ?? "Guest";
        
        public string CurrentUserRole => CurrentUser != null 
            ? AuthorizationService.GetRoleDisplayName(CurrentUser.Role) 
            : "None";

        public string CurrentDateTime
        {
            get => _currentDateTime;
            private set => SetProperty(ref _currentDateTime, value);
        }
        private string _currentDateTime = DateTime.Now.ToString("G", CultureInfo.CurrentCulture);

        private bool _isInternetAvailable;
        public bool IsInternetAvailable
        {
            get => _isInternetAvailable;
            private set
            {
                if (SetProperty(ref _isInternetAvailable, value))
                {
                    RaisePropertyChanged(nameof(InternetStatusText));
                    RaisePropertyChanged(nameof(InternetStatusColor));
                }
            }
        }

        public string InternetStatusText => IsInternetAvailable ? "Online" : "Offline";
        public string InternetStatusColor => IsInternetAvailable ? "#4CAF50" : "#F44336";

        public int SyncPendingCount
        {
            get => _syncPendingCount;
            private set => SetProperty(ref _syncPendingCount, value);
        }
        private int _syncPendingCount;

        public int SyncSuccessCount
        {
            get => _syncSuccessCount;
            private set => SetProperty(ref _syncSuccessCount, value);
        }
        private int _syncSuccessCount;

        public int SyncFailedCount
        {
            get => _syncFailedCount;
            private set => SetProperty(ref _syncFailedCount, value);
        }
        private int _syncFailedCount;

        /// <summary>
        /// Visibility gate for every sync-related surface (topbar counters,
        /// Failed syncs / Queue monitor menus, dashboard badge). Driven live by
        /// SyncGate.Changed when the App Settings checkbox toggles.
        /// </summary>
        public bool IsSyncUiVisible => Services.SyncGate.Enabled;

        // ===== Update (Velopack) =====
        private readonly VelopackUpdateService _updateService = new();
        private Velopack.UpdateInfo? _pendingUpdate;

        public bool IsSuperAdmin => _main.CurrentUser?.Role == UserRole.SuperAdmin;
        public bool IsCheckingForUpdate { get => _isCheckingForUpdate; set { if (SetProperty(ref _isCheckingForUpdate, value)) { RaisePropertyChanged(nameof(IsUpdateUiEnabled)); CommandManager.InvalidateRequerySuggested(); } } }
        private bool _isCheckingForUpdate;
        public int DownloadProgress { get => _downloadProgress; set => SetProperty(ref _downloadProgress, value); }
        private int _downloadProgress;
        public string UpdateStatusText { get => _updateStatusText; set => SetProperty(ref _updateStatusText, value); }
        private string _updateStatusText = string.Empty;
        public bool IsUpdateAvailable { get => _isUpdateAvailable; set => SetProperty(ref _isUpdateAvailable, value); }
        private bool _isUpdateAvailable;
        public bool IsUpdateUiEnabled => !IsCheckingForUpdate && DownloadProgress == 0;
        public string CurrentVersionText
        {
            get
            {
                try { return Helpers.AppVersionHelper.GetVersion(); }
                catch { return "1.0.0"; }
            }
        }

        public string CurrentModuleDisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ActiveRouteId))
                    return "Standard Weighing";

                if (string.Equals(ActiveRouteId, "Home", StringComparison.OrdinalIgnoreCase))
                    return "Standard Weighing";

                return _host.CoreHost?.Routes.Find(ActiveRouteId)?.DisplayName
                    ?? ActiveRouteId;
            }
        }

        private object? _currentContent;
        public object? CurrentContent
        {
            get => _currentContent;
            private set
            {
                var previous = _currentContent;
                if (!SetProperty(ref _currentContent, value))
                    return;

                try
                {
                    var previousViewModel = GetContentViewModel(previous);
                    var currentViewModel = GetContentViewModel(value);

                    if (!ReferenceEquals(previousViewModel, currentViewModel))
                        DeactivateContent(previousViewModel);

                    RaisePropertyChanged(nameof(CurrentContentTitle));

                    if (currentViewModel is IBatchScaleStatus batch)
                    {
                        try { AttachBatchVm(batch); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Shell] AttachBatchVm failed: {ex.Message}"); }

                        try { batch.OnViewActivated(); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Shell] BatchWeighing OnViewActivated failed: {ex.Message}"); }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Shell] CurrentContent setter failed: {ex}");
                    try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProWeighSuitePlatforms_crash.log"), $"[{DateTime.Now:O}] CurrentContent setter: {ex}\n---\n"); } catch { }
                }
            }
        }

        public string? CurrentContentTitle
        {
            get
            {
                var viewModel = GetContentViewModel(CurrentContent);
                if (viewModel is null)
                    return null;

                var title = viewModel.GetType()
                    .GetProperty("Title")
                    ?.GetValue(viewModel) as string;
                if (!string.IsNullOrWhiteSpace(title))
                    return title;

                return _host.CoreHost?.Routes.Find(ActiveRouteId)?.DisplayName;
            }
        }

        private static BaseViewModel? GetContentViewModel(object? content) => content switch
        {
            BaseViewModel viewModel => viewModel,
            System.Windows.FrameworkElement element => element.DataContext as BaseViewModel,
            _ => null
        };

        private static void DeactivateContent(BaseViewModel? viewModel)
        {
            try
            {
                if (viewModel is IViewLifecycleAware lifecycleAware)
                    lifecycleAware.OnViewDeactivated();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Shell] DeactivateContent failed: {ex.Message}");
            }
        }

        private string _activeRouteId = string.Empty;
        public string ActiveRouteId
        {
            get => _activeRouteId;
            private set
            {
                if (!SetProperty(ref _activeRouteId, value))
                    return;

                RaisePropertyChanged(nameof(ActiveWorkspaceId));
                RaisePropertyChanged(nameof(ActiveTransactionModuleId));
                RaisePropertyChanged(nameof(IsNexusWorkspaceActive));
                RaisePropertyChanged(nameof(IsFusionWorkspaceActive));
                RaisePropertyChanged(nameof(IsModuleTopNavVisible));
                RaisePropertyChanged(nameof(CurrentModuleDisplayName));
                RefreshNavigationSelection();
            }
        }

        public string? ActiveWorkspaceId
        {
            get
            {
                var moduleId = _host.CoreHost?.Routes.Find(ActiveRouteId)?.ModuleId;
                if (string.Equals(moduleId, "Fusion", StringComparison.OrdinalIgnoreCase))
                    return moduleId;
                if (string.Equals(moduleId, "Nexus", StringComparison.OrdinalIgnoreCase))
                    return moduleId;
                return null;
            }
        }

        public bool IsFusionWorkspaceActive =>
            string.Equals(ActiveWorkspaceId, "Fusion", StringComparison.OrdinalIgnoreCase);

        public bool IsNexusWorkspaceActive =>
            string.Equals(ActiveWorkspaceId, "Nexus", StringComparison.OrdinalIgnoreCase);

        public bool IsModuleTopNavVisible =>
            ModuleTopItems.Count > 0
            && (IsFusionWorkspaceActive || IsNexusWorkspaceActive)
            && !IsFusionMasterDataRouteActive;

        /// <summary>
        /// Master-data screens (Category/Variant/Materials) belong to the Fusion
        /// module but are not Fusion workspace pages, so the module top-nav
        /// (Fusion top menus) is not shown while they are active.
        /// </summary>
        private bool IsFusionMasterDataRouteActive =>
            IsFusionWorkspaceActive
            && (string.Equals(ActiveRouteId, "Category", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ActiveRouteId, "Variant", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ActiveRouteId, "Materials", StringComparison.OrdinalIgnoreCase));

        public string HomeMenuDisplayName => IsFusionModuleSet ? "Fusion" : "Home";

        private bool IsFusionModuleSet =>
            _host.CoreHost?.Modules.Find("Fusion") is not null;

        private bool IsNexusOnlyRequested
        {
            get
            {
                var modules = _host.CoreHost?.Modules.Modules;
                if (modules is null)
                    return false;

                var productModules = modules
                    .Where(module => !string.Equals(
                        module.Metadata.ModuleId, "Core", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                return productModules.Count == 1
                    && string.Equals(
                        productModules[0].Metadata.ModuleId, "Nexus", StringComparison.OrdinalIgnoreCase);
            }
        }

        public string ActiveTransactionModuleId =>
            IsModuleBackedRoute(ActiveRouteId)
                ? ActiveRouteId
                : (IsNexusWorkspaceActive ? "Nexus" : string.Empty);

        public ObservableCollection<ShellNavigationItem> NavigationItems { get; } = new();
        public ObservableCollection<ShellNavigationItem> ModuleTopItems { get; } = new();

        public ICommand NavigateCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand DownloadAndRestartCommand { get; }

        public ShellViewModel(MainViewModel main) : this(main, HostContextLocator.Current!)
        {
        }

        public ShellViewModel(MainViewModel main, IHostContext host)
        {
            _main = main;
            _host = host;
            _host.AttachScaleSessionControl(this);

            var contentContext = _host.ShellContent;
            if (contentContext is not null)
            {
                contentContext.Main = main;
                contentContext.Shell = this;
            }

            ProductNavigationRequests.ShowRouteRequested += OnFusionModuleRequested;

            try
            {
                var scaleReader = GetSharedScaleReader();
                scaleReader.WeightChanged -= OnSharedScaleWeightChanged;
                scaleReader.WeightChanged += OnSharedScaleWeightChanged;
                scaleReader.ConnectionError -= OnScaleConnectionError;
                scaleReader.ConnectionError += OnScaleConnectionError;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Shell] Scale status wiring failed: {ex.Message}");
            }

            _deviceStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _deviceStatusTimer.Tick += (_, __) =>
            {
                RefreshDeviceStatus();
                // Diagnostic: track GDI/USER handle counts so Win32Exception 1816
                // ("not enough quota") can be traced to a leaking resource class.
                ProWeighSuitePlatforms.Modules.Infrastructure.ProcessResourceMonitor.Tick();
            };
            _deviceStatusTimer.Start();
            RefreshDeviceStatus();

            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (_, __) => CurrentDateTime = DateTime.Now.ToString("G", CultureInfo.CurrentCulture);
            _clockTimer.Start();

            _networkCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _networkCheckTimer.Tick += (_, __) => CheckInternetConnection();
            _networkCheckTimer.Start();
            CheckInternetConnection();

            _syncStatsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _syncStatsTimer.Tick += (_, __) => RefreshSyncStats();
            _syncStatsTimer.Start();
            RefreshSyncStats();

            // React live when the App Settings "Enable Sync Setting" checkbox
            // toggles: show/hide sync counters + sync menus, reset counters when
            // disabled.
            Services.SyncGate.Changed += OnSyncGateChanged;

            NavigateCommand = new RelayCommand(
                async parameter => await NavigateToRouteAsync(parameter as string),
                _ => CurrentUser is not null);

            CheckForUpdatesCommand = new RelayCommand(async _ => await CheckForUpdatesAsync(), _ => IsSuperAdmin && IsUpdateUiEnabled);
            DownloadAndRestartCommand = new RelayCommand(async _ => await DownloadAndRestartAsync(), _ => IsSuperAdmin && IsUpdateAvailable && IsUpdateUiEnabled);
            LogoutCommand = new RelayCommand(async _ => await LogoutAsync());
            ExitCommand = new RelayCommand(async _ => await ExitAsync());

            NavigationItems.Add(new ShellNavigationItem(
                "Logout", "Logout", "Logout", null, LogoutCommand));
            NavigationItems.Add(new ShellNavigationItem(
                "Exit", "Exit", "CloseCircle", null, ExitCommand));

            _ = InitializeNavigationAsync();
        }

        private async Task InitializeNavigationAsync()
        {
            var initialRoute = IsFusionModuleSet
                ? FusionDashboardRoute
                : (IsNexusOnlyRequested ? "NexusDashboard" : "Home");

            if (!await NavigateToRouteAsync(initialRoute))
                System.Diagnostics.Debug.WriteLine($"[Shell] Initial route '{initialRoute}' was not opened.");

            await BuildNavigationMenuAsync();
        }

        private void OnFusionModuleRequested(string moduleId, string? batchNo)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
                return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                _ = NavigateToRouteAsync(moduleId);
            else
                dispatcher.BeginInvoke(new Action(() => _ = NavigateToRouteAsync(moduleId)));
        }

        public async Task<bool> NavigateToRouteAsync(string? routeId)
        {
            if (CurrentUser is null || string.IsNullOrWhiteSpace(routeId))
                return false;

            var targetRouteId = ResolveContentRoute(routeId);
            if (string.IsNullOrWhiteSpace(targetRouteId))
                return false;

            if (IsActionRoute(targetRouteId))
            {
                ExecuteRouteAction(targetRouteId);
                return true;
            }

            // Fusion auto-run locks navigation: while a batch is actively
            // weighing the operator must stay on the owning screen until the
            // batch is paused or completed. Re-selecting the owning route stays
            // allowed (it is a no-op) so the owning screen remains reachable.
            if (NavigationLock.TryGetLock(out var lockOwnerRoute, out var lockMessage)
                && !string.Equals(targetRouteId, lockOwnerRoute, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(lockMessage, "Navigation Locked", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var host = _host.CoreHost;
            var viewFactories = _host.ViewFactories;
            if (host is null || viewFactories is null)
                return false;

            try
            {
                var guard = await host.RouteGuard.CanEnterAsync(targetRouteId).ConfigureAwait(true);
                if (!guard.IsAllowed)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Shell] Route '{targetRouteId}' blocked: {guard.Reason}");
                    return false;
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                object view;
                if (dispatcher is not null && !dispatcher.CheckAccess())
                    view = await dispatcher.InvokeAsync(() => viewFactories.CreateView(targetRouteId, host.Services));
                else
                    view = viewFactories.CreateView(targetRouteId, host.Services);

                void Apply()
                {
                    CurrentContent = view;
                    ActiveRouteId = targetRouteId;
                }

                if (dispatcher is not null && !dispatcher.CheckAccess())
                    await dispatcher.InvokeAsync(Apply);
                else
                    Apply();

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Shell] Navigation to '{targetRouteId}' failed: {ex}");
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProWeighSuitePlatforms_crash.log"), $"[{DateTime.Now:O}] NavigateToRoute({targetRouteId}): {ex}\n---\n"); } catch { }
                try
                {
                    MessageBox.Show(
                        $"Could not open this page: {ex.Message}",
                        "Navigation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch { }
                return false;
            }
        }

        private string ResolveContentRoute(string routeId)
        {
            if (string.Equals(routeId, "Home", StringComparison.OrdinalIgnoreCase))
            {
                if (IsFusionModuleSet)
                    return FusionDashboardRoute;

                if (IsNexusOnlyRequested || IsNexusWorkspaceActive)
                    return "NexusDashboard";

                return "Home";
            }

            if (string.Equals(routeId, "Nexus", StringComparison.OrdinalIgnoreCase))
                return "NexusDashboard";

            if (string.Equals(routeId, "ScaleSettings", StringComparison.OrdinalIgnoreCase) &&
                IsNexusWorkspaceActive)
            {
                return "NexusScaleSettings";
            }

            return routeId;
        }

        private bool IsActionRoute(string routeId) =>
            _host.CoreHost?.Menus.All.Any(menu =>
                menu.IsAction &&
                string.Equals(menu.RouteId, routeId, StringComparison.OrdinalIgnoreCase)) == true;

        private void ExecuteRouteAction(string routeId)
        {
            if (string.Equals(routeId, "QueueMonitor", StringComparison.OrdinalIgnoreCase))
            {
                ShowQueueMonitorWindow();
                return;
            }

            if (string.Equals(routeId, "AuditLogs", StringComparison.OrdinalIgnoreCase))
                ShowAuditLogWindow();
        }

        private void ShowQueueMonitorWindow()
        {
            foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
            {
                if (window is QueueMonitorWindow)
                {
                    window.Activate();
                    if (window.WindowState == System.Windows.WindowState.Minimized)
                        window.WindowState = System.Windows.WindowState.Normal;
                    return;
                }
            }

            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var monitor = new QueueMonitorWindow(
                        _host.Queue ?? throw new InvalidOperationException("Sync queue service is not initialized."));
                    monitor.Show();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"Error opening Queue Monitor:\n{ex.Message}",
                        "Queue Monitor Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ShowAuditLogWindow()
        {
            foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
            {
                if (window is AuditLogViewerWindow)
                {
                    window.Activate();
                    if (window.WindowState == System.Windows.WindowState.Minimized)
                        window.WindowState = System.Windows.WindowState.Normal;
                    return;
                }
            }

            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var auditService = new AuditService();
                    var viewModel = new AuditLogViewerViewModel(auditService);
                    var window = new AuditLogViewerWindow { DataContext = viewModel };
                    window.Show();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"Error opening Audit Log Viewer:\n{ex.Message}",
                        "Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private async Task LogoutAsync()
        {
            var username = _main.CurrentUser?.Username ?? "Unknown";

            var owner = System.Windows.Application.Current?.MainWindow;
            var passwordConfirmed = await PasswordPromptWindow.ConfirmPasswordAsync(
                owner ?? new System.Windows.Window(), username);

            if (!passwordConfirmed)
            {
                var auditService = _host.AuditService;
                if (auditService is not null)
                {
                    await auditService.LogSecurityViolationAsync(
                        username, "Logout", "Password verification failed");
                }
                return;
            }

            var logoutAuditService = _host.AuditService;
            if (logoutAuditService is not null)
                await logoutAuditService.LogLogoutAsync(username);

            // A module may hold a navigation lock (e.g. Fusion auto-run).
            // Release it and stop the owning operation so the lock can never
            // strand the workspace after the session ends.
            NavigationLock.ForceRelease();

            CurrentContent = null;
            ActiveRouteId = string.Empty;
            _host.ViewFactories?.ClearCache();
            if (_host.ShellContent is { } context)
                context.Shell = null;

            _main.CurrentUser = null;
            RaisePropertyChanged(nameof(CurrentUser));
            _main.NavigateTo(new LoginViewModel(_main));
        }

        private async Task ExitAsync()
        {
            var username = _main.CurrentUser?.Username ?? "Unknown";

            var owner = System.Windows.Application.Current?.MainWindow;
            var passwordConfirmed = await PasswordPromptWindow.ConfirmPasswordAsync(
                owner ?? new System.Windows.Window(), username);

            if (!passwordConfirmed)
            {
                var auditService = _host.AuditService;
                if (auditService is not null)
                {
                    await auditService.LogSecurityViolationAsync(
                        username, "Exit Application", "Password verification failed");
                }
                return;
            }

            var exitAuditService = _host.AuditService;
            if (exitAuditService is not null)
                await exitAuditService.LogLogoutAsync(username);

            // A module may hold a navigation lock (e.g. Fusion auto-run).
            // Release it and stop the owning operation before shutting down.
            NavigationLock.ForceRelease();

            CurrentContent = null;
            _host.ViewFactories?.ClearCache();

            _host.MarkExitConfirmed();

            System.Windows.Application.Current?.Shutdown();
        }

        private async Task BuildNavigationMenuAsync()
        {
            try
            {
                var host = _host.CoreHost;
                var session = _host.Session;
                if (host is null || session is null || CurrentUser is null)
                    return;

                var enabledModuleIds = host.Modules.Modules
                    .Select(module => module.Metadata.ModuleId)
                    .ToList();
                var effectiveModuleIds = await GetEffectiveModuleIdsAsync();

                var model = await host.MenuBuilder.BuildAsync(
                    new ProWeighSuitePlatforms.Core.Navigation.NavigationBuildContext(
                        session.IsAuthenticated,
                        session.Roles,
                        ActiveModuleId: ActiveWorkspaceId,
                        EnabledModuleIds: enabledModuleIds));

                var hideSyncUi = !Services.SyncGate.Enabled;

                var nodes = model.Primary
                    .Concat(model.Configuration)
                    .Concat(model.Admin)
                    .Where(node => !(hideSyncUi && IsSyncNavigationNode(node)))
                    .Select(node => FilterNavigationNode(node, effectiveModuleIds))
                    .Where(node => node is not null)
                    .Select(node => node!)
                    .OrderBy(node => node.Order)
                    .ThenBy(node => node.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var items = nodes
                    .Select(CreateNavigationItem)
                    .Where(item => item is not null)
                    .Select(item => item!)
                    .ToList();

                var topItems = model.ModuleTop
                    .Where(node => !(hideSyncUi && IsSyncNavigationNode(node)))
                    .Select(node => FilterNavigationNode(node, effectiveModuleIds))
                    .Where(node => node is not null)
                    .Select(node => node!)
                    .Select(CreateNavigationItem)
                    .Where(item => item is not null)
                    .Select(item => item!)
                    .ToList();

                NavigationItems.Clear();
                foreach (var item in items)
                    NavigationItems.Add(item);

                ModuleTopItems.Clear();
                foreach (var item in topItems)
                    ModuleTopItems.Add(item);

                NavigationItems.Add(new ShellNavigationItem(
                    "Logout", "Logout", "Logout", null, LogoutCommand));
                NavigationItems.Add(new ShellNavigationItem(
                    "Exit", "Exit", "CloseCircle", null, ExitCommand));

                RaisePropertyChanged(nameof(IsModuleTopNavVisible));
                RefreshNavigationSelection();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Shell] Navigation menu build failed: {ex.Message}");
            }
        }

        private async Task<System.Collections.Generic.HashSet<string>?> GetEffectiveModuleIdsAsync()
        {
            try
            {
                var registry = _host.ModuleRegistry;
                if (registry is null)
                    return null;

                var modules = await registry
                    .GetEffectiveModulesAsync(CurrentUser?.Role)
                    .ConfigureAwait(true);
                return new System.Collections.Generic.HashSet<string>(
                    modules.Select(module => module.ModuleId),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Shell] Effective module lookup failed: {ex.Message}");
                return null;
            }
        }

        private static ProWeighSuitePlatforms.Core.Navigation.NavigationNode? FilterNavigationNode(
            ProWeighSuitePlatforms.Core.Navigation.NavigationNode node,
            System.Collections.Generic.HashSet<string>? effectiveModuleIds)
        {
            if (effectiveModuleIds is not null &&
                IsModuleBackedRoute(node.Id) &&
                !effectiveModuleIds.Contains(node.Id))
            {
                return null;
            }

            if (node.Children.Count == 0)
                return node;

            var children = node.Children
                .Select(child => FilterNavigationNode(child, effectiveModuleIds))
                .Where(child => child is not null)
                .Select(child => child!)
                .ToList();

            if (node.RouteId is null && children.Count == 0)
                return null;

            return node with { Children = children };
        }

        private static bool IsModuleBackedRoute(string routeId) =>
            ProductModuleCatalog.Find(routeId)?.IsLicenseGated == true;

        /// <summary>
        /// Sync-specific navigation nodes that disappear while sync is disabled
        /// ("Enable Sync Setting" unchecked in App Settings).
        /// </summary>
        private static bool IsSyncNavigationNode(ProWeighSuitePlatforms.Core.Navigation.NavigationNode node) =>
            string.Equals(node.Id, "FailedSyncs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.Id, "QueueMonitor", StringComparison.OrdinalIgnoreCase);

        private ShellNavigationItem? CreateNavigationItem(
            ProWeighSuitePlatforms.Core.Navigation.NavigationNode node)
        {
            var children = node.Children
                .Select(CreateNavigationItem)
                .Where(child => child is not null)
                .Select(child => child!)
                .ToList();

            ICommand? command = node.RouteId is null ? null : NavigateCommand;
            object? parameter = node.RouteId;

            if (node.RouteId is not null &&
                (command is null || !command.CanExecute(parameter)))
            {
                return null;
            }

            if (node.RouteId is null && children.Count == 0)
                return null;

            var title = string.Equals(node.Id, "Home", StringComparison.OrdinalIgnoreCase)
                ? HomeMenuDisplayName
                : node.Title;

            return new ShellNavigationItem(
                node.Id,
                title,
                node.IconKey,
                node.RouteId,
                command,
                parameter,
                // Only the Configuration group's sub-items keep the indented
                // "sub item" template. Top-level Admin entries (Audit logs, Users,
                // Module management, Info) render flush with Primary items,
                // Logout and Exit using the same Menu_Template.
                isCompact: node.Region == ProWeighSuitePlatforms.Core.Navigation.MenuRegion.Configuration,
                children: children);
        }

        private void RefreshNavigationSelection()
        {
            foreach (var item in NavigationItems)
                SetNavigationItemActive(item);
            foreach (var item in ModuleTopItems)
                SetNavigationItemActive(item);
        }

        private bool SetNavigationItemActive(ShellNavigationItem item)
        {
            var childActive = false;
            foreach (var child in item.Children)
                childActive |= SetNavigationItemActive(child);

            item.IsActive = childActive || IsNavigationRouteActive(item.RouteId);
            return item.IsActive;
        }

        private bool IsNavigationRouteActive(string? routeId)
        {
            if (string.IsNullOrWhiteSpace(routeId))
                return false;

            if (string.Equals(routeId, "Home", StringComparison.OrdinalIgnoreCase))
            {
                if (IsFusionModuleSet || IsNexusOnlyRequested)
                    return IsFusionWorkspaceActive || IsNexusWorkspaceActive;
            }

            if (string.Equals(routeId, "Nexus", StringComparison.OrdinalIgnoreCase))
                return IsNexusWorkspaceActive;

            return string.Equals(routeId, ActiveRouteId, StringComparison.OrdinalIgnoreCase);
        }

        private void OnCoreNavigationChanged(
            object? sender,
            ProWeighSuitePlatforms.Core.Navigation.NavigationChangedEventArgs e)
        {
            if (CurrentUser is null)
                return;

            switch (e.Reason)
            {
                case ProWeighSuitePlatforms.Core.Navigation.NavigationChangeReason.Login:
                case ProWeighSuitePlatforms.Core.Navigation.NavigationChangeReason.Logout:
                case ProWeighSuitePlatforms.Core.Navigation.NavigationChangeReason.PermissionsChanged:
                case ProWeighSuitePlatforms.Core.Navigation.NavigationChangeReason.RolesChanged:
                case ProWeighSuitePlatforms.Core.Navigation.NavigationChangeReason.ModuleStateChanged:
                case ProWeighSuitePlatforms.Core.Navigation.NavigationChangeReason.LicenseChanged:
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher is null || dispatcher.CheckAccess())
                    {
                        _ = BuildNavigationMenuAsync();
                    }
                    else
                    {
                        dispatcher.BeginInvoke(new Action(() =>
                        {
                            _ = BuildNavigationMenuAsync();
                        }));
                    }
                    break;
            }
        }

        private void OnSyncGateChanged(object? sender, EventArgs e)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                ApplySyncGateState();
            else
                dispatcher.BeginInvoke(new Action(ApplySyncGateState));
        }

        private void ApplySyncGateState()
        {
            RaisePropertyChanged(nameof(IsSyncUiVisible));
            if (Services.SyncGate.Enabled)
            {
                RefreshSyncStats();
            }
            else
            {
                // No sync is happening: clear the counters and stop refreshing.
                SyncPendingCount = 0;
                SyncSuccessCount = 0;
                SyncFailedCount = 0;
            }

            // Rebuild the nav menu so sync-specific entries (Failed syncs,
            // Queue monitor) appear/disappear immediately.
            _ = BuildNavigationMenuAsync();
        }

        private void RefreshSyncStats()
        {
            if (!Services.SyncGate.Enabled)
            {
                SyncPendingCount = 0;
                SyncSuccessCount = 0;
                SyncFailedCount = 0;
                return;
            }

            try
            {
                var syncService = _host.Queue;
                if (syncService != null)
                {
                    var stats = syncService.GetStats();
                    SyncPendingCount = stats.Pending;
                    SyncSuccessCount = stats.Success;
                    SyncFailedCount = stats.Failures;
                }
            }
            catch
            {
                // Silently ignore - sync service may not be initialized yet
            }
        }

        private void CheckInternetConnection()
        {
            try
            {
                // Ping Google's public DNS server to check internet connectivity
                using (var ping = new Ping())
                {
                    // Send a single ping ?
                    var reply = ping.Send("8.8.8.8", 1000);
                    IsInternetAvailable = (reply.Status == IPStatus.Success);
                }
            }
            catch
            {
                IsInternetAvailable = false;
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            if (!IsSuperAdmin) return;
            IsCheckingForUpdate = true;
            UpdateStatusText = "Checking...";
            IsUpdateAvailable = false;
            _pendingUpdate = null;
            try
            {
                var (info, error) = await _updateService.CheckForUpdatesAsync();
                if (!string.IsNullOrEmpty(error))
                {
                    UpdateStatusText = error!;
                    MessageBox.Show(error, "Update check failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (info == null)
                {
                    UpdateStatusText = "You are on the latest version";
                    MessageBox.Show($"You are on the latest version ({CurrentVersionText}).", "No updates", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                _pendingUpdate = info;
                IsUpdateAvailable = true;
                UpdateStatusText = $"Update {info.TargetFullRelease.Version} available";
                var res = MessageBox.Show($"Update {info.TargetFullRelease.Version} is available.\nCurrent: {CurrentVersionText}\n\nDownload and restart now?", "Update available", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                    await DownloadAndRestartAsync();
            }
            finally
            {
                IsCheckingForUpdate = false;
                RaisePropertyChanged(nameof(IsUpdateUiEnabled));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task DownloadAndRestartAsync()
        {
            if (!IsSuperAdmin || _pendingUpdate == null) return;
            IsCheckingForUpdate = true;
            DownloadProgress = 1;
            UpdateStatusText = "Downloading...";
            try
            {
                var (ok, error) = await _updateService.DownloadAndApplyAsync(_pendingUpdate, p => DownloadProgress = p);
                if (!ok)
                {
                    UpdateStatusText = error ?? "Download failed";
                    MessageBox.Show(UpdateStatusText, "Update failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    DownloadProgress = 0;
                    IsCheckingForUpdate = false;
                    return;
                }
                UpdateStatusText = "Restarting...";
            }
            catch (Exception ex)
            {
                UpdateStatusText = ex.Message;
                MessageBox.Show(ex.Message, "Update failed", MessageBoxButton.OK, MessageBoxImage.Error);
                DownloadProgress = 0;
                IsCheckingForUpdate = false;
            }
        }
    }
}
