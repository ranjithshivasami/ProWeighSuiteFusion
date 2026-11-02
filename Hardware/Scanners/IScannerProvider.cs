using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Services.Scanners
{
    /// <summary>Vendor-specific provider – one per brand SDK (Zebra CoreScanner, Honeywell, etc.).</summary>
    public interface IScannerProvider : IAsyncDisposable
    {
        string Brand { get; }
        bool IsConnected { get; }
        event EventHandler<ScannerBarcodeEventArgs>? BarcodeScanned;
        event EventHandler<DiscoveredScanner>? ScannerAttached;
        event EventHandler<DiscoveredScanner>? ScannerDetached;

        Task StartAsync(CancellationToken ct = default);
        Task StopAsync();
        Task<IReadOnlyList<DiscoveredScanner>> GetDiscoveredAsync(CancellationToken ct = default);
        Task TriggerAsync(int scannerId = 0, CancellationToken ct = default);
        Task ReleaseTriggerAsync(int scannerId = 0, CancellationToken ct = default);
        Task EnableAsync(int scannerId = 0, CancellationToken ct = default);
        Task DisableAsync(int scannerId = 0, CancellationToken ct = default);
    }
}
