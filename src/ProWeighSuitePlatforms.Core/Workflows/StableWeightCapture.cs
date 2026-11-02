// Stable-weight capture over the shared check-weigher service.
// Polls SI readings and accepts once the last N consecutive samples agree
// within ToleranceKg. Returns null on timeout (or when the scale is offline).
// Tunables: RequiredSamples / ToleranceKg / SampleIntervalMs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProWeighSuitePlatforms.Services
{
    public sealed class StableWeightCapture
    {
        private readonly ScaleReaderService _scale;

        public StableWeightCapture(ScaleReaderService scale)
        {
            _scale = scale ?? throw new ArgumentNullException(nameof(scale));
        }

        public int RequiredSamples { get; set; } = 3;
        public double ToleranceKg { get; set; } = 0.05;
        public int SampleIntervalMs { get; set; } = 200;

        public async Task<double?> CaptureAsync(TimeSpan timeout)
        {
            int need = Math.Max(2, RequiredSamples);
            double tol = Math.Max(0.001, ToleranceKg);
            var window = new Queue<double>();
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                while (true)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    double? w = await _scale.RequestWeightAsync();
                    if (w.HasValue && w.Value > 0)
                    {
                        window.Enqueue(w.Value);
                        while (window.Count > need) window.Dequeue();
                        if (window.Count == need && window.Max() - window.Min() <= tol)
                            return Math.Round(window.Average(), 2);
                    }
                    else
                    {
                        // Lost/empty signal resets the stability window.
                        window.Clear();
                    }
                    await Task.Delay(Math.Max(50, SampleIntervalMs), cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }
    }
}
