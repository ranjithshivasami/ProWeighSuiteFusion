using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Services.Scanners
{
    /// <summary>App-wide scanner service – decoupled facade injected into ViewModels via DI.</summary>
    public interface IScannerService : IAsyncDisposable
    {
        bool IsEnabled { get; }
        bool IsConnected { get; }
        string ActiveBrand { get; }
        /// <summary>Last connection failure reason reported by the provider, if any.</summary>
        string? LastError { get; }
        event EventHandler<ScannerBarcodeEventArgs>? BarcodeScanned;
        event EventHandler<DiscoveredScanner>? ScannerAttached;
        event EventHandler<DiscoveredScanner>? ScannerDetached;

        Task StartAsync(CancellationToken ct = default);
        Task StopAsync();
        Task<IReadOnlyList<DiscoveredScanner>> GetDiscoveredAsync(CancellationToken ct = default);
        Task TriggerAsync(CancellationToken ct = default);
    }
}
