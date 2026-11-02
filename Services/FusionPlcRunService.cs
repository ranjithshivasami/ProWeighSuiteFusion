// PLC listener for the Fusion live run (mirrors the Hybrid PLC workflow convention).
// Watches the "capture" discrete input (default address 2: 0 = scan,
// 1 = weighing in process, 2 = capture) and raises WeighReceived on its rising
// edge. The live view then captures the check-weigher weight, records the tub
// transaction and refreshes. Call AcknowledgeAsync afterwards to write the
// result back (1 = ok, 5 = reject, default holding address 1).
using System;
using System.Threading;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public sealed class FusionPlcRunService : IAsyncDisposable
    {
        private readonly ConfigManager _configManager = new();

        private ModbusRtuClient? _plc;
        private CancellationTokenSource? _pollCts;
        private Task? _pollTask;
        private bool _lastCaptureHigh;
        private int _consecutiveFaults;

        /// <summary>Discrete input address of the capture signal. Default 2.</summary>
        public ushort CaptureInputAddress { get; set; } = 2;
        /// <summary>Holding register address for the result write-back. Default 1.</summary>
        public ushort ResultRegisterAddress { get; set; } = 1;

        public event EventHandler? WeighReceived;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<string>? Faulted;

        public bool IsRunning => _pollTask != null && !_pollTask.IsCompleted;

        public async Task StartAsync(CancellationToken ct = default)
        {
            if (IsRunning) return;
            PlcConfig? cfg = null;
            try
            {
                cfg = await _configManager.LoadPlcConfigAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"PLC configuration could not be loaded: {ex.Message}");
            }
            if (cfg == null)
                throw new InvalidOperationException("PLC is not configured. Configure it in Settings first.");

            _plc = new ModbusRtuClient(cfg);
            await _plc.OpenAsync();

            _lastCaptureHigh = false;
            _consecutiveFaults = 0;
            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;
            _pollTask = Task.Run(() => PollLoopAsync(token), token);
            StatusChanged?.Invoke(this, "Fusion PLC listener started @100ms");
        }

        public async Task StopAsync()
        {
            try { _pollCts?.Cancel(); } catch { }
            if (_pollTask != null) try { await Task.WhenAny(_pollTask, Task.Delay(800)); } catch { }
            _pollCts?.Dispose(); _pollCts = null; _pollTask = null;

            if (_plc != null)
            {
                try { await _plc.CloseAsync(); } catch { }
                _plc.Dispose(); _plc = null;
            }
            StatusChanged?.Invoke(this, "Fusion PLC listener stopped");
        }

        /// <summary>Writes the weigh result back to the PLC (1 = ok, 5 = reject).</summary>
        public async Task AcknowledgeAsync(bool ok)
        {
            if (_plc == null) return;
            try
            {
                await _plc.WriteSingleRegisterAsync(ResultRegisterAddress, (ushort)(ok ? 1 : 5));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FusionPlc] Acknowledge failed: {ex.Message}");
            }
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_plc == null) return;
                    bool high = await _plc.ReadDiscreteInputAsync(CaptureInputAddress, token);
                    _consecutiveFaults = 0;
                    if (high && !_lastCaptureHigh)
                    {
                        _lastCaptureHigh = true;
                        WeighReceived?.Invoke(this, EventArgs.Empty);
                    }
                    else if (!high)
                    {
                        _lastCaptureHigh = false;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _consecutiveFaults++;
                    System.Diagnostics.Debug.WriteLine($"[FusionPlc] Poll fault #{_consecutiveFaults}: {ex.Message}");
                    if (_consecutiveFaults == 5)
                        Faulted?.Invoke(this, $"PLC read failed ({ex.Message}). Still listening...");
                }
                try
                {
                    await Task.Delay(100, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        public async ValueTask DisposeAsync() => await StopAsync();
    }
}
