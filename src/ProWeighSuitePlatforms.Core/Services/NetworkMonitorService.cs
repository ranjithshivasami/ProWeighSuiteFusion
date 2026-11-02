using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Monitors internet connectivity by combining OS network-availability events
    /// with a lightweight periodic HTTP ping. Raises <see cref="ConnectivityChanged"/>
    /// whenever online/offline state changes.
    /// </summary>
    public class NetworkMonitorService : IDisposable
    {
        // ------------------------------------------------------------------
        // Configuration
        // ------------------------------------------------------------------
        private const string PingUrl            = "http://clients3.google.com/generate_204";
        private const int    PingTimeoutSeconds = 5;
        private const int    PollIntervalOnline  = 15;   // seconds while online
        private const int    PollIntervalOffline = 5;    // seconds while offline (faster recovery)

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------
        private volatile bool _isOnline;
        private CancellationTokenSource? _cts;
        private Task? _pollingTask;

        // Raised by OS events to wake the poll loop early (avoids waiting full interval)
        private readonly ManualResetEventSlim _wakeSignal = new(false);

        // Ensures only one connectivity check runs at a time
        private readonly SemaphoreSlim _checkLock = new(1, 1);

        private bool _disposed;

        // ------------------------------------------------------------------
        // Public surface
        // ------------------------------------------------------------------

        /// <summary>Current connectivity state (true = internet reachable).</summary>
        public bool IsOnline => _isOnline;

        /// <summary>
        /// Raised on the thread-pool whenever online/offline state flips.
        /// Argument is the new <c>isOnline</c> value.
        /// </summary>
        public event Action<bool>? ConnectivityChanged;

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        public NetworkMonitorService()
        {
            // Subscribe to OS-level network change events for instant response
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged      += OnNetworkAddressChanged;
        }

        /// <summary>
        /// Runs an initial check then starts the background polling loop.
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            // Force an immediate check � always fire ConnectivityChanged on startup
            // so consumers always know the real state before the first queue iteration.
            await CheckConnectivityAsync(forceEvent: true);

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pollingTask = Task.Run(() => PollLoopAsync(_cts.Token), CancellationToken.None);

            Debug.WriteLine($"[NetworkMonitor] Started. Online={_isOnline}");
        }

        /// <summary>Stops the background polling loop.</summary>
        public async Task StopAsync()
        {
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged      -= OnNetworkAddressChanged;

            try
            {
                _cts?.Cancel();
                // Wake the poll loop so it exits cleanly instead of waiting for delay
                _wakeSignal.Set();

                if (_pollingTask != null)
                    await _pollingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _pollingTask = null;
            }

            Debug.WriteLine("[NetworkMonitor] Stopped.");
        }

        // ------------------------------------------------------------------
        // Poll loop
        // ------------------------------------------------------------------

        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Wait for either the poll interval or an early wake from an OS event
                    int delayMs = (_isOnline ? PollIntervalOnline : PollIntervalOffline) * 1000;
                    _wakeSignal.Wait(delayMs, token); // blocks, but releases on Set() or token cancel
                    _wakeSignal.Reset();

                    if (token.IsCancellationRequested)
                        break;

                    await CheckConnectivityAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NetworkMonitor] Poll error: {ex.Message}");
                }
            }
        }

        // ------------------------------------------------------------------
        // Core connectivity check
        // ------------------------------------------------------------------

        /// <summary>
        /// Pings the internet and fires <see cref="ConnectivityChanged"/> if state changed.
        /// <paramref name="forceEvent"/> forces the event even if state did not change �
        /// used on startup so consumers always receive the initial state.
        /// </summary>
        private async Task CheckConnectivityAsync(bool forceEvent = false)
        {
            // Non-blocking try � if another check is already running, skip this one
            // (the running one will update _isOnline when it finishes)
            if (!await _checkLock.WaitAsync(0))
            {
                Debug.WriteLine("[NetworkMonitor] Check already in progress � skipping.");
                return;
            }

            try
            {
                bool reachable = await IsInternetReachableAsync();
                bool changed   = reachable != _isOnline;

                if (changed)
                {
                    _isOnline = reachable;
                    Debug.WriteLine($"[NetworkMonitor] Connectivity changed ? {(reachable ? "ONLINE" : "OFFLINE")}");
                }

                if (changed || forceEvent)
                {
                    try { ConnectivityChanged?.Invoke(reachable); }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[NetworkMonitor] ConnectivityChanged handler error: {ex.Message}");
                    }
                }
            }
            finally
            {
                _checkLock.Release();
            }
        }

        private static async Task<bool> IsInternetReachableAsync()
        {
            // Fast OS-level check � avoids HTTP when NIC is clearly down
            if (!NetworkInterface.GetIsNetworkAvailable())
                return false;

            try
            {
                using var client  = new HttpClient { Timeout = TimeSpan.FromSeconds(PingTimeoutSeconds) };
                using var request = new HttpRequestMessage(HttpMethod.Head, PingUrl);
                using var resp    = await client.SendAsync(request).ConfigureAwait(false);
                // Google's /generate_204 returns 204; anything below 500 means reachable
                return (int)resp.StatusCode < 500;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------
        // OS event handlers � wake the poll loop immediately
        // ------------------------------------------------------------------

        private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            Debug.WriteLine($"[NetworkMonitor] OS NetworkAvailabilityChanged: available={e.IsAvailable}");
            _wakeSignal.Set(); // wake poll loop ? triggers CheckConnectivityAsync immediately
        }

        private void OnNetworkAddressChanged(object? sender, EventArgs e)
        {
            Debug.WriteLine("[NetworkMonitor] OS NetworkAddressChanged � waking poll loop...");
            _wakeSignal.Set();
        }

        // ------------------------------------------------------------------
        // IDisposable
        // ------------------------------------------------------------------

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged      -= OnNetworkAddressChanged;

            _cts?.Cancel();
            _cts?.Dispose();
            _wakeSignal.Set(); // unblock poll loop if waiting
            _wakeSignal.Dispose();
            _checkLock.Dispose();
        }
    }
}
