// Phase 1 — additive only. Hardware behind interfaces so workflows are testable
// and StandardWeighing never references Modbus/serial/Scanner SDKs directly.
using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Modules.Contracts
{
    public interface IScaleReader
    {
        bool IsConnected { get; }
        Task<double> ReadStableWeightAsync(CancellationToken ct = default);
    }

    public interface IPlcClient
    {
        bool IsEnabled { get; }
        Task WaitForInterlockAsync(CancellationToken ct = default);
    }

    public interface IRfidReader
    {
        bool IsEnabled { get; }
        Task<string?> ReadEpcAsync(CancellationToken ct = default);
    }
}
