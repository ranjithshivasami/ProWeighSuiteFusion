using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Infrastructure;
using ProWeighSuitePlatforms.Licensing;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Services.Scanners;

namespace ProWeighSuitePlatforms
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        static App()
        {
            ProWeighSuitePlatforms.Services.AppVersionProvider.SetResolver(Helpers.AppVersionHelper.GetVersion);
        }

        private BackupService? _backupService;
        private LiteQueueSyncService? _liteQueueSyncService;
        private NetworkMonitorService? _networkMonitorService;
        private AuditService? _auditService;
        private ScheduledReportService? _scheduledReportService;
        private DailyResetService? _dailyResetService;
        private IScannerService? _scannerService;
        private static ScaleSessionControlBridge? _scaleSessionBridge;

        // Public property for access from repositories
        public static LiteQueueSyncService? LiteQueueSyncService { get; private set; }
        public static IScannerService? ScannerService
        {
            get => ScannerServiceLocator.Current;
            private set => ScannerServiceLocator.Current = value;
        }

        public static void SetScannerService(IScannerService? svc) => ScannerService = svc;

        public static void AttachScaleSessionControl(IScaleSessionControl control)
        {
            _scaleSessionBridge?.Attach(control);
        }

        // Public property for access to audit service
        public static AuditService? AuditService { get; private set; }

        // Public property for access to backup service
        public static BackupService? BackupService { get; private set; }

        // Public property for access to the scheduled report service
        public static ScheduledReportService? ScheduledReportService { get; private set; }

        // Public task that represents application initialization (database, services)
        public static Task InitializationTask { get; private set; } = Task.CompletedTask;

        // Build-stamped branding and license gate. Single-product host: Fusion.
        public static IBrandingProvider? Branding { get; private set; }
        public static ILicenseService? License { get; private set; }

        // Build-time product flavor stamped by the csproj.
        public static string Flavor => BrandingInfo.Flavor;

        public App()
        {
            EventManager.RegisterClassHandler(typeof(TextBox), TextBox.GotFocusEvent, new RoutedEventHandler(OnAnyTextBoxGotFocus));

            // Global crash handlers — prevent any unhandled UI/background exception from killing the process
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private static readonly object _errorDialogLock = new object();
        private static DateTime _lastErrorDialogAt = DateTime.MinValue;

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[GlobalHandler] DispatcherUnhandledException: {e.Exception}");
                LogCrashToFile("DispatcherUnhandledException", e.Exception);
            }
            catch { }
            // Keep app alive — user can continue; show non-blocking message.
            // Never pop a modal dialog while the app is shutting down or a window
            // is closing: MessageBox.Show itself throws InvalidOperationException
            // ("while a Window is closing") in that state. Log only instead.
            e.Handled = true;
            if (IsShuttingDown(e))
                return;

            // Win32Exception 1816 = USER/GDI quota exhausted. A modal dialog here
            // would block the dispatcher on top of an already-broken render state
            // (the "app frozen / not responding" symptom). Log a resource snapshot
            // instead and let the rest of the UI keep running if it can.
            if (e.Exception is System.ComponentModel.Win32Exception win32 &&
                win32.NativeErrorCode == 1816)
            {
                try
                {
                    ProcessResourceMonitor.LogSnapshot("win32-1816");
                }
                catch { }
                return;
            }

            try
            {
                var details = $"{e.Exception.GetType().Name}: {e.Exception.Message}\n\n{e.Exception.StackTrace}";
                // Also write to temp for easier field retrieval
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProWeighSuitePlatforms_crash.log"), $"[{DateTime.Now:O}] DispatcherUnhandledException: {details}\n---\n"); } catch { }

                // Throttle the dialog: a failure inside the layout/render loop fires
                // this handler on every frame, and a modal per frame turns a minor
                // error into an unusable dialog storm (users report it as a crash).
                // Log every occurrence, but show the dialog at most every 5 seconds.
                bool showDialog;
                lock (_errorDialogLock)
                {
                    showDialog = (DateTime.Now - _lastErrorDialogAt).TotalSeconds >= 5;
                    if (showDialog)
                        _lastErrorDialogAt = DateTime.Now;
                }
                if (!showDialog)
                    return;

                var fullMessage = GetFullExceptionMessage(e.Exception);
                try
                {
                    MessageBox.Show($"Unexpected error: {fullMessage}\n\nThe app will continue. If this repeats, restart the app.\n\nDetails logged to %AppData%\\ProWeighSuiteFusion\\crashes\\", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (InvalidOperationException)
                {
                    // Window closing / dispatcher gone — log-only, already handled.
                }
            }
            catch { }
        }

        private static bool _shuttingDown;

        private static bool IsShuttingDown(DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                if (_shuttingDown) return true;
                if (e.Dispatcher.HasShutdownStarted || e.Dispatcher.HasShutdownFinished) return true;
                var app = Application.Current;
                if (app == null) return true;
                if (app.Dispatcher.HasShutdownStarted || app.Dispatcher.HasShutdownFinished) return true;
            }
            catch { }
            return false;
        }

        private static string GetFullExceptionMessage(Exception? ex)
        {
            if (ex == null) return "(no exception)";
            var parts = new System.Collections.Generic.List<string>();
            var current = ex;
            var depth = 0;
            while (current != null && depth < 5)
            {
                parts.Add($"{current.GetType().Name}: {current.Message}");
                current = current.InnerException;
                depth++;
            }
            return string.Join("\n---> ", parts);
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                System.Diagnostics.Debug.WriteLine($"[GlobalHandler] AppDomain Unhandled: {ex}");
                LogCrashToFile("AppDomainUnhandled", ex);
            }
            catch { }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[GlobalHandler] UnobservedTask: {e.Exception}");
                LogCrashToFile("UnobservedTask", e.Exception);
            }
            catch { }
            e.SetObserved();
        }

        private static void LogCrashToFile(string kind, Exception? ex)
        {
            try
            {
                var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProWeighSuiteFusion", "crashes");
                System.IO.Directory.CreateDirectory(dir);
                var file = System.IO.Path.Combine(dir, $"crash_{DateTime.Now:yyyyMMdd}.log");
                System.IO.File.AppendAllText(file, $"[{DateTime.Now:O}] {kind}: {ex}{Environment.NewLine}---{Environment.NewLine}");
            }
            catch { }
        }

        private void ApplyHostVersion()
        {
            try
            {
                var version = typeof(App).Assembly.GetName().Version?.ToString(3);
                if (!string.IsNullOrWhiteSpace(version))
                    Resources["AppVersion"] = version;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] host version stamp failed (non-fatal): {ex.Message}");
            }
        }

        private static void ApplyBrandVisuals()
        {
            try
            {
                var resources = Application.Current?.Resources;
                if (resources == null) return;

                resources["BrandLogoImage"] = BrandingInfo.LogoSource;
                resources["BrandLoginBannerImage"] = BrandingInfo.BannerSource;
                resources["BrandIconImage"] = BrandingInfo.IconSource;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Brand] visual resources failed (non-fatal): {ex.Message}");
            }
        }

        private static void ApplyBrandDefaultTheme()
        {
            // Fresh install starts from the Fusion profile (teal #009688) so the
            // brand is correct without a manual pick.
            try
            {
                var hex = Branding?.PrimaryHex;
                if (string.IsNullOrWhiteSpace(hex)) return;
                ThemeService.ApplySavedTheme(hex, Branding?.Brand);
                ThemeService.ApplySecondary(Branding?.SecondaryHex);
                System.Diagnostics.Debug.WriteLine($"[Theme] brand-default {Branding?.Brand} {hex} (Flavor={Flavor})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Theme] brand default failed (non-fatal): {ex.Message}");
            }
        }

        private static void ApplySavedThemeSynchronously()
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ProWeighSuiteFusion");
                var file = System.IO.Path.Combine(dir, "appsettings.json");
                if (!System.IO.File.Exists(file))
                {
                    ApplyBrandDefaultTheme();
                    return;
                }
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(file));
                string? hex = null;
                string? name = null;
                var custom = false;
                if (doc.RootElement.TryGetProperty("BrandThemeCustom", out var customProp))
                    custom = customProp.ValueKind == System.Text.Json.JsonValueKind.True;
                if (doc.RootElement.TryGetProperty("BrandPrimaryHex", out var hexProp))
                    hex = hexProp.GetString();
                if (doc.RootElement.TryGetProperty("BrandThemeName", out var nameProp))
                    name = nameProp.GetString();
                // Saved hex is only authoritative when an admin explicitly picked a
                // theme. Otherwise the build-stamped brand color wins, so a new
                // build never inherits a stale color from an earlier brand.
                if (!custom || string.IsNullOrWhiteSpace(hex))
                {
                    ApplyBrandDefaultTheme();
                    return;
                }
                ThemeService.ApplySavedTheme(hex, name);
                ThemeService.ApplySecondary(Branding?.SecondaryHex);
                System.Diagnostics.Debug.WriteLine($"[Theme] sync-applied {ThemeService.CurrentName} {ThemeService.CurrentHex}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Theme] sync apply failed (non-fatal): {ex.Message}");
            }
        }

        private static void OnAnyTextBoxGotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                {
                    tb.SelectAll();
                }));
            }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            ApplyHostVersion();

            // Branding + license: simple direct setup, no DI container.
            Branding = new FileBrandingProvider();
            BrandingInfo.Provider = Branding;
            License = new FileLicenseService();
            _scaleSessionBridge = new ScaleSessionControlBridge();

            ApplyBrandVisuals();
            System.Diagnostics.Debug.WriteLine($"[App] Flavor={Flavor} Brand={Branding?.Brand} Title={Branding?.Title}");

            // Hard license-expiry gate: no window at all once expired.
            try
            {
                var expired = License != null && License.IsExpiredAsync().GetAwaiter().GetResult();
                if (expired)
                {
                    MessageBox.Show(
                        "Your license has expired. Please contact your supplier to renew.",
                        "License Expired",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Current.Shutdown(-1);
                    return;
                }
            }
            catch (Exception licenseEx)
            {
                System.Diagnostics.Debug.WriteLine($"[License] startup gate failed: {licenseEx.Message}");
            }

            // Apply brand-default theme synchronously BEFORE any window is created,
            // so the first paint (login) already uses the flavor color. Saved
            // appsettings hex (admin pick) overrides inside ApplySavedThemeSynchronously.
            ApplySavedThemeSynchronously();
            base.OnStartup(e);

            // Apply admin-selected brand theme before any window shows (login uses Brand* variables).
            // Only an explicit admin pick (BrandThemeCustom) overrides the build-stamped brand color.
            try
            {
                var themeSettings = await new AppSettingsService().LoadAsync();
                if (themeSettings.BrandThemeCustom)
                {
                    ThemeService.ApplySavedTheme(themeSettings.BrandPrimaryHex, themeSettings.BrandThemeName);
                    ThemeService.ApplySecondary(Branding?.SecondaryHex);
                }
                else
                {
                    ApplyBrandDefaultTheme();
                }
                System.Diagnostics.Debug.WriteLine($"[Theme] applied {ThemeService.CurrentName} {ThemeService.CurrentHex}");
            }
            catch (Exception themeEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Theme] apply failed (non-fatal): {themeEx.Message}");
            }

            try
            {
                // Enable touch keyboard automatic invocation
                TouchKeyboardHelper.EnableAutomaticInvocation();
                System.Diagnostics.Debug.WriteLine("[TouchKeyboard] Automatic invocation enabled on startup");

                // Initialize database with seed data on first run (core tables + Fusion tables)
                var dbInitService = new DatabaseInitializationService(
                    new IProductDatabaseInitializer[] { new FusionDatabaseInitializer() });

                // Show splash or loading indication
                var dbPath = dbInitService.GetDatabasePath();
                System.Diagnostics.Debug.WriteLine($"Initializing database at: {dbPath}");

                // Start initialization and expose the task so viewmodels can await it
                InitializationTask = dbInitService.InitializeAsync();
                AppInitialization.Task = InitializationTask;

                await InitializationTask;

                System.Diagnostics.Debug.WriteLine("Database initialization completed successfully");

                // Open the saved scale at launch so Live view can weigh as
                // soon as a batch starts. Runs in the background with retries
                // (scale drivers/emulators can take a few seconds to become
                // available) and never blocks or fails startup.
                _ = Task.Run(() => ScaleConnectionService.EnsureConnectedWithRetryAsync());

                // Start backup service
                _backupService = new BackupService();
                BackupService = _backupService; // Make available globally
                await _backupService.InitializeAsync();

                System.Diagnostics.Debug.WriteLine("Backup service initialized");

                // Initialize Audit Service with offline sync
                System.Diagnostics.Debug.WriteLine("[AuditService] Initializing...");
                _auditService = new AuditService();
                AuditService = _auditService; // Make available globally

                // Load sync settings for audit sync endpoint
                var syncSettingsService = new SyncSettingsService();
                var syncSettings = await syncSettingsService.LoadAsync();

                // Master sync switch from sync-settings.json ("Enable Sync Setting"
                // on the App Settings page). Both transaction and audit sync
                // consult this live.
                SyncGate.Enabled = syncSettings?.Enabled ?? true;
                System.Diagnostics.Debug.WriteLine($"[Sync] Enabled={SyncGate.Enabled}");

                // Use configured API URL or fallback to default
                string apiUrl = syncSettings?.ApiBaseUrl ?? "http://localhost/api";
                if (string.IsNullOrWhiteSpace(apiUrl))
                {
                    apiUrl = "http://localhost/api";
                    System.Diagnostics.Debug.WriteLine("[AuditService] WARNING: No API URL configured, using default: http://localhost/api");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[AuditService] Loaded API URL from settings: {apiUrl}");
                }

                // Initialize offline sync for audit logs
                string auditSyncEndpoint = $"{apiUrl}/audit/sync";
                _auditService.InitializeOfflineSync(auditSyncEndpoint, retryIntervalMinutes: 5);
                System.Diagnostics.Debug.WriteLine($"[AuditService] Offline sync initialized with endpoint: {auditSyncEndpoint}");

                // Create HttpClient with loaded settings for transaction sync
                var http = new HttpClient()
                {
                    BaseAddress = new Uri(apiUrl),
                    Timeout = TimeSpan.FromSeconds(30)
                };

                // Initialize LiteQueue sync service for transactions
                _liteQueueSyncService = new LiteQueueSyncService(http);
                LiteQueueSyncService = _liteQueueSyncService;

                // Start network monitor FIRST and wire to sync service BEFORE starting the queue loop
                _networkMonitorService = new NetworkMonitorService();
                await _networkMonitorService.StartAsync();
                _liteQueueSyncService.SetNetworkMonitor(_networkMonitorService);
                System.Diagnostics.Debug.WriteLine($"[NetworkMonitor] Started. Online={_networkMonitorService.IsOnline}");

                // Start background sync worker AFTER network state is known
                await _liteQueueSyncService.StartAsync();
                System.Diagnostics.Debug.WriteLine("[LiteQueue] Service started successfully");

                // Start scheduled report emailing (daily transaction + summary reports)
                _scheduledReportService = new ScheduledReportService();
                ScheduledReportService = _scheduledReportService;
                await _scheduledReportService.InitializeAsync();
                System.Diagnostics.Debug.WriteLine("[ScheduledReport] Service initialized");

                // Start the daily reset service (clears pending sync queue and failed
                // sync records once per day; checks immediately and then every minute).
                _dailyResetService = new DailyResetService(_liteQueueSyncService);
                _dailyResetService.Start();
                System.Diagnostics.Debug.WriteLine("[DailyReset] Service initialized");

                // Initialize industrial scanner (Zebra etc.) – decoupled via IScannerService, brand chosen in AppSettings
                try
                {
                    _scannerService = await ScannerServiceFactory.CreateFromSettingsAsync();
                    ScannerService = _scannerService;
                    System.Diagnostics.Debug.WriteLine($"[Scanner] ActiveBrand={_scannerService.ActiveBrand} IsEnabled={_scannerService.IsEnabled}");
                }
                catch (Exception scanEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[Scanner] init failed: {scanEx.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Startup error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[App] Stack trace: {ex.StackTrace}");

                // Show user-friendly error message
                var result = MessageBox.Show(
                    $"Application initialization encountered an issue:\n\n{ex.Message}\n\n" +
                    $"Would you like to continue anyway?\n\n" +
                    $"Click YES to continue (may work if database already exists)\n" +
                    $"Click NO to exit and check the issue",
                    "Initialization Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    // Exit the application
                    Current.Shutdown(-1);
                    return;
                }

                // If user chose YES, continue - the app might work if database already exists
                System.Diagnostics.Debug.WriteLine("[App] User chose to continue despite initialization warning");
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            _shuttingDown = true;
            if (_scannerService != null)
            {
                try { await _scannerService.StopAsync(); await _scannerService.DisposeAsync(); } catch { }
                System.Diagnostics.Debug.WriteLine("[Scanner] Service stopped");
            }

            if (_dailyResetService != null)
            {
                _dailyResetService.Stop();
                System.Diagnostics.Debug.WriteLine("[DailyReset] Service stopped");
            }

            if (_liteQueueSyncService != null)
            {
                System.Diagnostics.Debug.WriteLine("[LiteQueue] Stopping service...");
                await _liteQueueSyncService.StopAsync();
                System.Diagnostics.Debug.WriteLine("[LiteQueue] Service stopped");
            }

            if (_networkMonitorService != null)
            {
                await _networkMonitorService.StopAsync();
                _networkMonitorService.Dispose();
                System.Diagnostics.Debug.WriteLine("[NetworkMonitor] Service stopped");
            }

            if (_backupService != null)
            {
                _backupService.Dispose();
                System.Diagnostics.Debug.WriteLine("[BackupService] Service stopped");
            }

            if (_scheduledReportService != null)
            {
                _scheduledReportService.Dispose();
                System.Diagnostics.Debug.WriteLine("[ScheduledReport] Service stopped");
            }

            base.OnExit(e);
        }

        /// <summary>
        /// Fusion database bootstrap: creates the Fusion tables (batches, tubs,
        /// supplier stock, settings) next to the core tables.
        /// </summary>
        private sealed class FusionDatabaseInitializer : IProductDatabaseInitializer
        {
            public string Name => "Fusion";

            public Task InitializeAsync(string dbPath)
            {
                var initializer = new FusionDatabaseInit(dbPath);
                return initializer.EnsureCreatedAsync();
            }
        }
    }
}
