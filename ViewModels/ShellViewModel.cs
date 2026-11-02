using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Net.NetworkInformation;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Infrastructure;
using ProWeighSuitePlatforms.Licensing;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Navigation;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Services.Scanners;
using ProWeighSuitePlatforms.ViewModels;
using ProWeighSuitePlatforms.Views;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class ShellViewModel : BaseViewModel, IScaleSessionControl
    {
        private const string FusionDashboardRoute = "FusionDashboard";

        private readonly MainViewModel _main;
        private readonly AuthorizationService _auth = new();
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _networkCheckTimer;
        private readonly DispatcherTimer _syncStatsTimer;
        private readonly Dictionary<string, object> _keepAliveViews = new(StringComparer.OrdinalIgnoreCase);

        // ===== Scale/Scanner status for the footer status bar =====
        private string _scaleStatusText = "Scale not connected";
        public string ScaleStatusText { get => _scaleStatusText; set => SetProperty(ref _scaleStatusText, value); }
        private bool _isScaleConnected;
        public bool IsScaleConnected { get => _isScaleConnected; set => SetProperty(ref _isScaleConnected, value); }
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
            ScaleReaderService.GetShared(new ScaleSettingsRepository(
                AppConfigurationManager.GetDatabasePath()));

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
                var svc = App.ScannerService;

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
                var old = App.ScannerService;
                if (old != null)
                {
                    try { await old.StopAsync(); } catch { }
                    try { await old.DisposeAsync(); } catch { }
                }

                var fresh = await ScannerServiceFactory.CreateFromSettingsAsync();
                var disp = System.Windows.Application.Current?.Dispatcher;
                if (disp != null && !disp.CheckAccess())
                    await disp.InvokeAsync(() => App.SetScannerService(fresh));
                else
                    App.SetScannerService(fresh);
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
            // when the active page is a Fusion screen with no batch view model.
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
        /// (or the saved settings when null).
        /// </summary>
        public async Task<bool> ConnectScaleAsync(ScaleSettings? settings = null)
        {
            if (settings == null)
            {
                var config = new ScaleConfigService();
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

            // Store every line the scale sends into the transaction table.
            // Subscribe before polling starts so no reading is missed.
            Capture.ProductScaleCaptureLocator.Current?.Start(svc, settings, dbPath);
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
        /// Closes the scale connection and detaches the capture so the Scale
        /// Settings connection test can open the port again.
        /// </summary>
        public void DisconnectScale()
        {
            try
            {
                var svc = GetSharedScaleReader();
                svc.StopReading();
                Capture.ProductScaleCaptureLocator.Current?.Stop();
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
        /// Failed syncs / Queue monitor menus). Driven live by SyncGate.Changed
        /// when the App Settings checkbox toggles.
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
                    return "Fusion";

                return RouteNames.TryGetValue(ActiveRouteId, out var name)
                    ? name
                    : "Fusion";
            }
        }

        private object? _currentContent;
        public object? CurrentContent
        {
            get => _currentContent;
            private set
            {
                if (SetProperty(ref _currentContent, value))
                    RaisePropertyChanged(nameof(CurrentContentTitle));
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

                return RouteNames.TryGetValue(ActiveRouteId, out var name) ? name : null;
            }
        }

        private static BaseViewModel? GetContentViewModel(object? content) => content switch
        {
            BaseViewModel viewModel => viewModel,
            System.Windows.FrameworkElement element => element.DataContext as BaseViewModel,
            _ => null
        };

        private string _activeRouteId = string.Empty;
        public string ActiveRouteId
        {
            get => _activeRouteId;
            private set
            {
                if (!SetProperty(ref _activeRouteId, value))
                    return;

                RaisePropertyChanged(nameof(IsModuleTopNavVisible));
                RaisePropertyChanged(nameof(CurrentModuleDisplayName));
                RefreshNavigationSelection();
            }
        }

        /// <summary>True while a Fusion page is on screen (Fusion screens + master data).</summary>
        public bool IsFusionWorkspaceActive =>
            FusionRouteIds.Contains(ActiveRouteId)
            || IsFusionMasterDataRouteActive;

        /// <summary>
        /// Master-data screens (Category/Variant/Materials) are Fusion pages but
        /// not Fusion workspace pages, so the module top-nav is hidden while
        /// they are active.
        /// </summary>
        private bool IsFusionMasterDataRouteActive =>
            string.Equals(ActiveRouteId, "Category", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ActiveRouteId, "Variant", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ActiveRouteId, "Materials", StringComparison.OrdinalIgnoreCase);

        public bool IsModuleTopNavVisible =>
            ModuleTopItems.Count > 0
            && IsFusionWorkspaceActive
            && !IsFusionMasterDataRouteActive;

        public string HomeMenuDisplayName => "Fusion";

        public ObservableCollection<ShellNavigationItem> NavigationItems { get; } = new();
        public ObservableCollection<ShellNavigationItem> ModuleTopItems { get; } = new();

        public ICommand NavigateCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand DownloadAndRestartCommand { get; }

        public ShellViewModel(MainViewModel main)
        {
            _main = main;
            App.AttachScaleSessionControl(this);

            ProductNavigationRequests.ShowRouteRequested += OnRouteRequested;

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
                ProcessResourceMonitor.Tick();
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

            // Rebuild the menus whenever an administrator saves the role-permission matrix.
            AuthorizationService.PermissionsChanged += OnPermissionsChanged;

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
            if (!await NavigateToRouteAsync(FusionDashboardRoute))
                System.Diagnostics.Debug.WriteLine($"[Shell] Initial route '{FusionDashboardRoute}' was not opened.");

            await BuildNavigationMenuAsync();
        }

        private void OnRouteRequested(string routeId, string? batchNo)
        {
            if (string.IsNullOrWhiteSpace(routeId))
                return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                _ = NavigateToRouteAsync(routeId);
            else
                dispatcher.BeginInvoke(new Action(() => _ = NavigateToRouteAsync(routeId)));
        }

        private void OnPermissionsChanged(object? sender, EventArgs e)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                _ = BuildNavigationMenuAsync();
            else
                dispatcher.BeginInvoke(new Action(() => _ = BuildNavigationMenuAsync()));
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

            try
            {
                // Menu visibility already filters by role; double-check here so a
                // stale menu or a direct request cannot open a forbidden page.
                if (!CanAccessRoute(targetRouteId))
                {
                    System.Diagnostics.Debug.WriteLine($"[Shell] Route '{targetRouteId}' blocked: missing permission.");
                    return false;
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                object view;
                if (dispatcher is not null && !dispatcher.CheckAccess())
                    view = await dispatcher.InvokeAsync(() => CreateView(targetRouteId));
                else
                    view = CreateView(targetRouteId);

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

        private static string ResolveContentRoute(string routeId)
        {
            if (string.Equals(routeId, "Home", StringComparison.OrdinalIgnoreCase))
                return FusionDashboardRoute;

            return routeId;
        }

        private bool IsActionRoute(string routeId) =>
            string.Equals(routeId, "QueueMonitor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(routeId, "AuditLogs", StringComparison.OrdinalIgnoreCase);

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
                        App.LiteQueueSyncService ?? throw new InvalidOperationException("Sync queue service is not initialized."));
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
                var auditService = App.AuditService;
                if (auditService is not null)
                {
                    await auditService.LogSecurityViolationAsync(
                        username, "Logout", "Password verification failed");
                }
                return;
            }

            var logoutAuditService = App.AuditService;
            if (logoutAuditService is not null)
                await logoutAuditService.LogLogoutAsync(username);

            // A Fusion operation may hold a navigation lock (auto-run).
            // Release it and stop the owning operation so the lock can never
            // strand the workspace after the session ends.
            NavigationLock.ForceRelease();

            CurrentContent = null;
            ActiveRouteId = string.Empty;
            _keepAliveViews.Clear();

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
                var auditService = App.AuditService;
                if (auditService is not null)
                {
                    await auditService.LogSecurityViolationAsync(
                        username, "Exit Application", "Password verification failed");
                }
                return;
            }

            var exitAuditService = App.AuditService;
            if (exitAuditService is not null)
                await exitAuditService.LogLogoutAsync(username);

            // A Fusion operation may hold a navigation lock (auto-run).
            // Release it and stop the owning operation before shutting down.
            NavigationLock.ForceRelease();

            CurrentContent = null;
            _keepAliveViews.Clear();

            if (System.Windows.Application.Current?.MainWindow is MainWindow mainWindow)
                mainWindow.MarkExitConfirmed();

            System.Windows.Application.Current?.Shutdown();
        }

        // ===== Menu model (hard-coded; no route/menu registry) =====

        private sealed record MenuDef(
            string Id,
            string Title,
            string Icon,
            string? RouteId,
            MenuRegion Region,
            int Order,
            string? ParentId = null,
            Permission[]? Permissions = null,
            bool IsAction = false);

        private enum MenuRegion { Primary, Configuration, Admin, ModuleTop }

        private static readonly MenuDef[] Menus =
        {
            // Sidebar — primary
            new("Home", "Fusion", "Home", "Home", MenuRegion.Primary, 10, Permissions: new[] { Permission.ViewDashboard }),
            new("Master", "Master", "Database", null, MenuRegion.Primary, 80),
            new("QueueMonitor", "Queue monitor", "MonitorDashboard", "QueueMonitor", MenuRegion.Primary, 100, IsAction: true),
            new("FailedSyncs", "Failed syncs", "SyncAlert", "FailedSyncs", MenuRegion.Primary, 110),

            // Sidebar — admin
            new("AuditLogs", "Audit logs", "ClipboardTextClock", "AuditLogs", MenuRegion.Admin, 120,
                Permissions: new[] { Permission.ViewRolePermissions, Permission.ViewCompanySettings }, IsAction: true),
            new("Users", "Users", "AccountMultiple", "Users", MenuRegion.Admin, 121,
                Permissions: new[] { Permission.ViewUsers }),
            new("Permissions", "Role permissions", "ShieldAccount", "Permissions", MenuRegion.Admin, 122,
                Permissions: new[] { Permission.ViewRolePermissions }),
            new("Info", "Info", "Information", "Info", MenuRegion.Admin, 160),

            // Sidebar — configuration (children of the Configuration group)
            new("Configuration", "Configuration", "CogOutline", null, MenuRegion.Configuration, 90),
            new("ScaleSettings", "Scale settings", "Scale", "ScaleSettings", MenuRegion.Configuration, 10,
                ParentId: "Configuration", Permissions: new[] { Permission.ViewScaleSettings }),
            new("CompanySettings", "Company settings", "OfficeBuilding", "CompanySettings", MenuRegion.Configuration, 20,
                ParentId: "Configuration", Permissions: new[] { Permission.ViewCompanySettings }),
            new("AppSettings", "App settings", "Cog", "AppSettings", MenuRegion.Configuration, 30,
                ParentId: "Configuration", Permissions: new[] { Permission.ViewAppSettings }),
            new("SyncSettings", "Sync settings", "Sync", "SyncSettings", MenuRegion.Configuration, 40,
                ParentId: "Configuration", Permissions: new[] { Permission.ViewSyncSettings }),
            new("BackupSettings", "Backup settings", "BackupRestore", "BackupSettings", MenuRegion.Configuration, 50,
                ParentId: "Configuration", Permissions: new[] { Permission.ViewBackupSettings }),
            new("BackupLog", "Backup log", "History", "BackupLog", MenuRegion.Configuration, 60,
                ParentId: "Configuration", Permissions: new[] { Permission.ViewBackupLog }),
            new("ReportSchedule", "Report schedule", "CalendarClock", "ReportSchedule", MenuRegion.Configuration, 70,
                ParentId: "Configuration", Permissions: new[] { Permission.ViewReportSchedule }),
            new("LabelTemplates", "Label templates", "TagText", "LabelTemplates", MenuRegion.Configuration, 80,
                ParentId: "Configuration", Permissions: new[] { Permission.ViewLabelTemplates }),
            new("PrinterSetup", "Printer setup", "Printer", "PrinterSetup", MenuRegion.Configuration, 90,
                ParentId: "Configuration", Permissions: new[] { Permission.ViewPrinterSetup }),
            new("PlcSettings", "PLC settings", "Chip", "PlcSettings", MenuRegion.Configuration, 100,
                ParentId: "Configuration", Permissions: new[] { Permission.ViewPlcSettings }),
            new("ScannerSettings", "Scanner settings", "BarcodeScan", "ScannerSettings", MenuRegion.Configuration, 110,
                ParentId: "Configuration", Permissions: new[] { Permission.ViewScannerSettings }),
            new("CleanUpDatabase", "Clean up database", "DatabaseRemove", "CleanUpDatabase", MenuRegion.Configuration, 120,
                ParentId: "Configuration", Permissions: new[] { Permission.ViewCleanUpDatabase }),

            // Sidebar — Master group children (Fusion master data)
            new("Category", "Category", "Tag", "Category", MenuRegion.Primary, 10,
                ParentId: "Master", Permissions: new[] { Permission.ViewCategory }),
            new("Variant", "Variant", "Shape", "Variant", MenuRegion.Primary, 20,
                ParentId: "Master", Permissions: new[] { Permission.ViewVariant }),
            new("Materials", "Materials", "CubeOutline", "Materials", MenuRegion.Primary, 30,
                ParentId: "Master", Permissions: new[] { Permission.ViewMaterials }),

            // Fusion top nav (shown above the content while a Fusion page is active)
            new("FusionDashboard", "Dashboard", "ViewDashboard", "FusionDashboard", MenuRegion.ModuleTop, 10,
                Permissions: new[] { Permission.ViewDashboard }),
            new("FusionSettings", "Fusion settings", "Cog", "FusionSettings", MenuRegion.ModuleTop, 20,
                Permissions: new[] { Permission.ViewFusionSettings }),
            new("BayStatus", "Bay status", "MapMarkerRadius", "BayStatus", MenuRegion.ModuleTop, 30,
                Permissions: new[] { Permission.ViewBayStatus }),
            new("SupplierMaster", "Supplier master", "TruckDelivery", "SupplierMaster", MenuRegion.ModuleTop, 40,
                Permissions: new[] { Permission.ViewSupplierMaster }),
            new("TubMaster", "Tub master", "PackageVariantClosed", "TubMaster", MenuRegion.ModuleTop, 50,
                Permissions: new[] { Permission.ViewTubMaster }),
            new("InwardStock", "Inward and supplier stock", "PackageVariantClosedPlus", "InwardStock", MenuRegion.ModuleTop, 60,
                Permissions: new[] { Permission.ViewInwardStock }),
            new("BatchLots", "Batch creation and lots", "LayersTriple", "BatchLots", MenuRegion.ModuleTop, 70,
                Permissions: new[] { Permission.ViewBatchLots }),
            new("BatchStatus", "Batch status", "ListStatus", "BatchStatus", MenuRegion.ModuleTop, 75,
                Permissions: new[] { Permission.ViewBatchStatus }),
            new("LiveView", "Live view", "MonitorDashboard", "LiveView", MenuRegion.ModuleTop, 80,
                Permissions: new[] { Permission.ViewLiveView }),
            new("FusionReports", "Reports", "FileChart", "FusionReports", MenuRegion.ModuleTop, 90,
                Permissions: new[] { Permission.ViewFusionReports }),
        };

        private static readonly HashSet<string> FusionRouteIds = new(StringComparer.OrdinalIgnoreCase)
        {
            FusionRoutes.Dashboard, FusionRoutes.Settings, FusionRoutes.BayStatus,
            FusionRoutes.SupplierMaster, FusionRoutes.TubMaster, FusionRoutes.InwardStock,
            FusionRoutes.BatchLots, FusionRoutes.BatchStatus, FusionRoutes.LiveView, FusionRoutes.Reports,
        };

        private static readonly Dictionary<string, string> RouteNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["FusionDashboard"] = "Fusion Dashboard",
            ["FusionSettings"] = "Fusion settings",
            ["BayStatus"] = "Bay status",
            ["SupplierMaster"] = "Supplier master",
            ["TubMaster"] = "Tub master",
            ["InwardStock"] = "Inward and supplier stock",
            ["BatchLots"] = "Batch creation and lots",
            ["BatchStatus"] = "Batch status",
            ["LiveView"] = "Live view",
            ["FusionReports"] = "Reports",
            ["Category"] = "Categories",
            ["Variant"] = "Variants",
            ["Materials"] = "Materials",
            ["ScaleSettings"] = "Scale settings",
            ["CompanySettings"] = "Company settings",
            ["AppSettings"] = "App settings",
            ["SyncSettings"] = "Sync settings",
            ["BackupSettings"] = "Backup settings",
            ["BackupLog"] = "Backup log",
            ["ReportSchedule"] = "Report schedule",
            ["LabelTemplates"] = "Label templates",
            ["PrinterSetup"] = "Printer setup",
            ["PlcSettings"] = "PLC settings",
            ["ScannerSettings"] = "Scanner settings",
            ["CleanUpDatabase"] = "Clean up database",
            ["FailedSyncs"] = "Failed syncs",
            ["QueueMonitor"] = "Queue monitor",
            ["AuditLogs"] = "Audit logs",
            ["Users"] = "Users",
            ["Permissions"] = "Role permissions",
            ["Info"] = "Info",
        };

        private bool CanSee(params Permission[]? permissions) =>
            permissions is null || permissions.Length == 0 || _auth.HasAnyPermission(CurrentUser, permissions);

        private bool CanAccessRoute(string routeId)
        {
            var def = Menus.FirstOrDefault(m =>
                string.Equals(m.RouteId, routeId, StringComparison.OrdinalIgnoreCase));
            // Routes without a menu definition are internal (e.g. never blocked).
            return def is null || CanSee(def.Permissions);
        }

        private Task BuildNavigationMenuAsync()
        {
            try
            {
                if (CurrentUser is null)
                    return Task.CompletedTask;

                var hideSyncUi = !Services.SyncGate.Enabled;

                var sidebar = Menus
                    .Where(m => m.Region != MenuRegion.ModuleTop)
                    .Where(m => !(hideSyncUi && IsSyncMenu(m.Id)))
                    .Where(m => m.ParentId is null)
                    .Where(m => CanSee(m.Permissions))
                    .OrderBy(m => m.Order)
                    .Select(m => CreateNavigationItem(m, hideSyncUi))
                    .Where(item => item is not null)
                    .Select(item => item!)
                    .ToList();

                var top = Menus
                    .Where(m => m.Region == MenuRegion.ModuleTop)
                    .Where(m => CanSee(m.Permissions))
                    .OrderBy(m => m.Order)
                    .Select(m => new ShellNavigationItem(
                        m.Id, m.Title, m.Icon, m.RouteId, NavigateCommand, m.RouteId))
                    .ToList();

                NavigationItems.Clear();
                foreach (var item in sidebar)
                    NavigationItems.Add(item);

                ModuleTopItems.Clear();
                foreach (var item in top)
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

            return Task.CompletedTask;
        }

        private ShellNavigationItem? CreateNavigationItem(MenuDef def, bool hideSyncUi)
        {
            var children = Menus
                .Where(m => string.Equals(m.ParentId, def.Id, StringComparison.OrdinalIgnoreCase))
                .Where(m => !(hideSyncUi && IsSyncMenu(m.Id)))
                .Where(m => CanSee(m.Permissions))
                .OrderBy(m => m.Order)
                .Select(m => new ShellNavigationItem(
                    m.Id,
                    m.Title,
                    m.Icon,
                    m.RouteId,
                    m.RouteId is null ? null : NavigateCommand,
                    m.RouteId))
                .ToList();

            if (def.RouteId is null && children.Count == 0)
                return null;

            var title = string.Equals(def.Id, "Home", StringComparison.OrdinalIgnoreCase)
                ? HomeMenuDisplayName
                : def.Title;

            return new ShellNavigationItem(
                def.Id,
                title,
                def.Icon,
                def.RouteId,
                def.RouteId is null ? null : NavigateCommand,
                def.RouteId,
                isCompact: def.Region == MenuRegion.Configuration,
                children: children);
        }

        private static bool IsSyncMenu(string id) =>
            string.Equals(id, "FailedSyncs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(id, "QueueMonitor", StringComparison.OrdinalIgnoreCase);

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
                return string.Equals(ActiveRouteId, FusionDashboardRoute, StringComparison.OrdinalIgnoreCase);

            return string.Equals(routeId, ActiveRouteId, StringComparison.OrdinalIgnoreCase);
        }

        // ===== View creation (direct; no view factory registry) =====

        private object CreateView(string routeId)
        {
            if (routeId.Equals("FusionDashboard", StringComparison.OrdinalIgnoreCase))
                return GetOrCreateKeepAlive("FusionDashboard", () =>
                    View(new FusionDashboardViewModel(CurrentUser?.Username ?? "System"), new FusionDashboardView()));

            if (routeId.Equals("FusionSettings", StringComparison.OrdinalIgnoreCase))
                return View(new FusionSettingsViewModel(CurrentUser?.Username ?? "System"), new FusionSettingsView());

            if (routeId.Equals("BayStatus", StringComparison.OrdinalIgnoreCase))
                return View(new BayStatusViewModel(CurrentUser?.Username ?? "System"), new BayStatusView());

            if (routeId.Equals("SupplierMaster", StringComparison.OrdinalIgnoreCase))
                return View(new SupplierMasterViewModel(CurrentUser?.Username ?? "System"), new SupplierMasterView());

            if (routeId.Equals("TubMaster", StringComparison.OrdinalIgnoreCase))
                return View(new TubMasterViewModel(CurrentUser?.Username ?? "System"), new TubMasterView());

            if (routeId.Equals("InwardStock", StringComparison.OrdinalIgnoreCase))
                return View(new InwardStockViewModel(CurrentUser?.Username ?? "System"), new InwardStockView());

            if (routeId.Equals("BatchLots", StringComparison.OrdinalIgnoreCase))
                return View(new FusionBatchViewModel(CurrentUser?.Username ?? "System"), new FusionBatchView());

            if (routeId.Equals("BatchStatus", StringComparison.OrdinalIgnoreCase))
                return View(new BatchStatusViewModel(), new BatchStatusView());

            if (routeId.Equals("LiveView", StringComparison.OrdinalIgnoreCase))
                return GetOrCreateKeepAlive("LiveView", () =>
                    View(new FusionLiveViewModel(CurrentUser?.Username ?? "System"), new FusionLiveView()));

            if (routeId.Equals("FusionReports", StringComparison.OrdinalIgnoreCase))
                return View(new FusionReportsViewModel(CurrentUser?.Username ?? "System"), new FusionReportsView());

            if (routeId.Equals("Category", StringComparison.OrdinalIgnoreCase))
                return View(new CategoryViewModel(_main), new CategoryView());

            if (routeId.Equals("Variant", StringComparison.OrdinalIgnoreCase))
                return View(new VariantViewModel(_main), new VariantView());

            if (routeId.Equals("Materials", StringComparison.OrdinalIgnoreCase))
                return View(new MaterialsViewModel(_main), new MaterialsView());

            if (routeId.Equals("Users", StringComparison.OrdinalIgnoreCase))
                return GetOrCreateKeepAlive("Users", () =>
                    View(new UserManagementViewModel(_main), new UserManagementView()));

            if (routeId.Equals("Permissions", StringComparison.OrdinalIgnoreCase))
                return View(new RolePermissionsMatrixViewModel(_main), new RolePermissionsMatrixView());

            if (routeId.Equals("Info", StringComparison.OrdinalIgnoreCase))
                return View(new InfoViewModel(), new InfoView());

            if (routeId.Equals("ScaleSettings", StringComparison.OrdinalIgnoreCase))
                return View(new SettingsViewModel(_main), new ScaleSettingsView());

            if (routeId.Equals("CompanySettings", StringComparison.OrdinalIgnoreCase))
                return View(new CompanySettingsViewModel(_main), new CompanySettingsView());

            if (routeId.Equals("AppSettings", StringComparison.OrdinalIgnoreCase))
            {
                var branding = BrandingInfo.Provider;
                return View(
                    new AppSettingsViewModel(
                        CurrentUser?.Username ?? "System",
                        branding?.PrimaryHex,
                        branding?.SecondaryHex,
                        branding?.Brand),
                    new AppSettingsView());
            }

            if (routeId.Equals("SyncSettings", StringComparison.OrdinalIgnoreCase))
                return View(new SyncSettingsViewModel(CurrentUser?.Username ?? "System"), new SyncSettingsView());

            if (routeId.Equals("BackupSettings", StringComparison.OrdinalIgnoreCase))
                return View(
                    new BackupSettingsViewModel(App.BackupService ?? new BackupService()),
                    new BackupSettingsView());

            if (routeId.Equals("BackupLog", StringComparison.OrdinalIgnoreCase))
                return View(new BackupLogViewModel(), new BackupLogView());

            if (routeId.Equals("ReportSchedule", StringComparison.OrdinalIgnoreCase))
                return View(
                    new ReportScheduleViewModel(App.ScheduledReportService ?? new ScheduledReportService()),
                    new ReportScheduleView());

            if (routeId.Equals("LabelTemplates", StringComparison.OrdinalIgnoreCase))
                return View(new ZplTemplateViewModel(CurrentUser?.Username ?? "System"), new ZplTemplateView());

            if (routeId.Equals("PrinterSetup", StringComparison.OrdinalIgnoreCase))
                return View(new PrinterConnectionViewModel(CurrentUser?.Username ?? "System"), new PrinterConnectionView());

            if (routeId.Equals("PlcSettings", StringComparison.OrdinalIgnoreCase))
                return View(new ModbusSettingsViewModel(), new ModbusSettingsView());

            if (routeId.Equals("ScannerSettings", StringComparison.OrdinalIgnoreCase))
                return View(new ScannerSettingsViewModel(CurrentUser?.Username ?? "System"), new ScannerSettingsView());

            if (routeId.Equals("CleanUpDatabase", StringComparison.OrdinalIgnoreCase))
                return View(
                    new CleanUpDatabaseViewModel(
                        _main,
                        App.LiteQueueSyncService ?? throw new InvalidOperationException("Sync queue service is not initialized."),
                        BrandingInfo.Flavor),
                    new CleanUpDatabaseView());

            if (routeId.Equals("FailedSyncs", StringComparison.OrdinalIgnoreCase))
                return View(
                    new FailedSyncViewerViewModel(
                        App.LiteQueueSyncService ?? throw new InvalidOperationException("Sync queue service is not initialized.")),
                    new FailedSyncViewerView());

            throw new InvalidOperationException($"No view is registered for route '{routeId}'.");
        }

        private static object View(BaseViewModel viewModel, System.Windows.FrameworkElement view)
        {
            view.DataContext = viewModel;
            return view;
        }

        private object GetOrCreateKeepAlive(string key, Func<object> factory)
        {
            if (_keepAliveViews.TryGetValue(key, out var cached))
                return cached;

            var view = factory();
            _keepAliveViews[key] = view;
            return view;
        }

        // ===== Sync / network / update =====

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
                var syncService = App.LiteQueueSyncService;
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
