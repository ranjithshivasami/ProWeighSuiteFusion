using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services.Scanners
{
    public sealed class ScannerService : IScannerService, IAsyncDisposable
    {
        private readonly IScannerProvider _provider;
        private readonly AppSettingsService _settingsService = new();
        private bool _started;

        public ScannerService(IScannerProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            // Forward provider events
            _provider.BarcodeScanned += (s, e) => BarcodeScanned?.Invoke(this, e);
            _provider.ScannerAttached += (s, e) => ScannerAttached?.Invoke(this, e);
            _provider.ScannerDetached += (s, e) => ScannerDetached?.Invoke(this, e);
        }

        public bool IsEnabled => !string.Equals(ActiveBrand, "None", StringComparison.OrdinalIgnoreCase) && !string.Equals(ActiveBrand, "Wedge", StringComparison.OrdinalIgnoreCase);
        public bool IsConnected => _provider.IsConnected;
        public string ActiveBrand => _provider.Brand;
        public string? LastError => _provider is SrkSymd6lProvider srk && !string.IsNullOrWhiteSpace(srk.LastError)
            ? srk.LastError
            : null;

        public event EventHandler<ScannerBarcodeEventArgs>? BarcodeScanned;
        public event EventHandler<DiscoveredScanner>? ScannerAttached;
        public event EventHandler<DiscoveredScanner>? ScannerDetached;

        public async Task StartAsync(CancellationToken ct = default)
        {
            // Only start if brand enabled and WeightCaptureMode is Auto|Hybrid – caller gates, but double-check
            var settings = await _settingsService.LoadAsync();
            var brand = ScannerSettings.NormalizeBrand(settings.Scanner?.Brand);
            if (brand == "None" || brand == "Wedge")
            {
                System.Diagnostics.Debug.WriteLine($"[ScannerService] Brand={brand} – no SDK start");
                return;
            }
            if (!string.Equals(_provider.Brand, brand, StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine($"[ScannerService] Provider brand {_provider.Brand} mismatches settings {brand} – recreate via factory required");
                return;
            }
            if (_started) return;
            await _provider.StartAsync(ct);
            _started = true;
        }

        public Task StopAsync() => _provider.StopAsync();

        public Task<IReadOnlyList<DiscoveredScanner>> GetDiscoveredAsync(CancellationToken ct = default) => _provider.GetDiscoveredAsync(ct);

        public Task TriggerAsync(CancellationToken ct = default) => _provider.TriggerAsync(0, ct);

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
        }

        Task IScannerService.StopAsync() => StopAsync();
    }

    /// <summary>Factory-backed singleton accessor for App.xaml.cs (no Host DI required).</summary>
    public static class ScannerServiceFactory
    {
        public static async Task<IScannerService> CreateFromSettingsAsync()
        {
            var svc = new AppSettingsService();
            var settings = await svc.LoadAsync();
            var factory = new ScannerProviderFactory();
            var provider = factory.Create(settings.Scanner?.Brand);
            var service = new ScannerService(provider);
            // Auto-start whenever a brand is enabled so passive Continuous
            // (ActiveMode) tag reads reach the weighing screen in any capture
            // mode. PLC-triggered scans remain gated on Auto/Hybrid elsewhere.
            if (settings.Scanner?.IsEnabled ?? false)
            {
                await service.StartAsync();
            }
            return service;
        }
    }
}
