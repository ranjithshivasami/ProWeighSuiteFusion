using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Services.Scanners
{
    /// <summary>Fallback – no SDK, relies on HID keyboard wedge to populate MaterialCode TextBox.</summary>
    public sealed class WedgeScannerProvider : IScannerProvider
    {
        public string Brand => "Wedge";
        public bool IsConnected => false;
        public event EventHandler<ScannerBarcodeEventArgs>? BarcodeScanned { add { } remove { } }
        public event EventHandler<DiscoveredScanner>? ScannerAttached { add { } remove { } }
        public event EventHandler<DiscoveredScanner>? ScannerDetached { add { } remove { } }
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<DiscoveredScanner>> GetDiscoveredAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DiscoveredScanner>>(Array.Empty<DiscoveredScanner>());
        public Task TriggerAsync(int scannerId = 0, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReleaseTriggerAsync(int scannerId = 0, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnableAsync(int scannerId = 0, CancellationToken ct = default) => Task.CompletedTask;
        public Task DisableAsync(int scannerId = 0, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public sealed class HoneywellScannerProvider : IScannerProvider
    {
        public string Brand => "Honeywell";
        public bool IsConnected => false;
        public event EventHandler<ScannerBarcodeEventArgs>? BarcodeScanned { add { } remove { } }
        public event EventHandler<DiscoveredScanner>? ScannerAttached { add { } remove { } }
        public event EventHandler<DiscoveredScanner>? ScannerDetached { add { } remove { } }
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<DiscoveredScanner>> GetDiscoveredAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DiscoveredScanner>>(Array.Empty<DiscoveredScanner>());
        public Task TriggerAsync(int scannerId = 0, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReleaseTriggerAsync(int scannerId = 0, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnableAsync(int scannerId = 0, CancellationToken ct = default) => Task.CompletedTask;
        public Task DisableAsync(int scannerId = 0, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public sealed class DatalogicScannerProvider : IScannerProvider
    {
        public string Brand => "Datalogic";
        public bool IsConnected => false;
        public event EventHandler<ScannerBarcodeEventArgs>? BarcodeScanned { add { } remove { } }
        public event EventHandler<DiscoveredScanner>? ScannerAttached { add { } remove { } }
        public event EventHandler<DiscoveredScanner>? ScannerDetached { add { } remove { } }
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<DiscoveredScanner>> GetDiscoveredAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DiscoveredScanner>>(Array.Empty<DiscoveredScanner>());
        public Task TriggerAsync(int scannerId = 0, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReleaseTriggerAsync(int scannerId = 0, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnableAsync(int scannerId = 0, CancellationToken ct = default) => Task.CompletedTask;
        public Task DisableAsync(int scannerId = 0, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
