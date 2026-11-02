using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services.Scanners
{
    public interface IScannerProviderFactory
    {
        IScannerProvider Create(string? brand);
    }

    public sealed class ScannerProviderFactory : IScannerProviderFactory
    {
        public IScannerProvider Create(string? brand)
        {
            var normalized = ScannerSettings.NormalizeBrand(brand);
            return normalized switch
            {
                "Zebra" => new ZebraScannerProvider(),
                "Honeywell" => new HoneywellScannerProvider(),
                "Datalogic" => new DatalogicScannerProvider(),
                "Wedge" => new WedgeScannerProvider(),
                "Srk" => new SrkSymd6lProvider(),
                _ => new NullScannerProvider()
            };
        }
    }

    public sealed class NullScannerProvider : IScannerProvider
    {
        public string Brand => "None";
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
