// Option B/B2 — auto-capture timer policy, extracted verbatim from
// BatchWeighingViewModel.ScheduleAutoCapture/CancelAutoCapture.
// Owns the interval + CTS; the VM only decides WHEN (ShouldAutoCapture) and WHAT (accept callback).
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Platform.Weighing.Evaluation
{
    public sealed class AutoCaptureScheduler : IDisposable
    {
        private CancellationTokenSource? _cts;
        private int _intervalMs;
        private bool _disposed;

        public AutoCaptureScheduler(int intervalMs = 500) => UpdateInterval(intervalMs);

        public void UpdateInterval(int intervalMs)
            => _intervalMs = intervalMs > 0 ? intervalMs : 500;

        public void Cancel()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Restarts the delay; when it elapses without cancellation, invokes the callback.
        /// Fire-and-forget like the legacy async-void version.
        /// </summary>
        public async void Schedule(Func<Task> onElapsed)
        {
            Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            try
            {
                await Task.Delay(_intervalMs, token).ConfigureAwait(false);
                if (!token.IsCancellationRequested)
                    await onElapsed().ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Cancel();
        }
    }
}
