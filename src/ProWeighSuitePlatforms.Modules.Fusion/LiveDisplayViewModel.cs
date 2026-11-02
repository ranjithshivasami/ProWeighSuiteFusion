// Shop-floor display for a running batch: big banner, progress and key figures.
// Polls the database; closed with the Exit button or Esc.
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data.Fusion;

namespace ProWeighSuitePlatforms.Modules.Fusion
{
    public sealed class LiveDisplayLot
    {
        public string Lot { get; set; } = string.Empty;
        public string DonePlan { get; set; } = string.Empty;
        public double Pct { get; set; }
    }

    public sealed class LiveDisplayViewModel : ViewModels.BaseViewModel
    {
        public string BatchNo { get; }

        private string _bannerText = string.Empty;
        public string BannerText { get => _bannerText; private set => SetProperty(ref _bannerText, value); }
        private string _subText = string.Empty;
        public string SubText { get => _subText; private set => SetProperty(ref _subText, value); }
        private double _progressPct;
        public double ProgressPct { get => _progressPct; private set => SetProperty(ref _progressPct, value); }
        private string _weightText = "-";
        public string WeightText { get => _weightText; private set => SetProperty(ref _weightText, value); }
        private string _tubsText = "-";
        public string TubsText { get => _tubsText; private set => SetProperty(ref _tubsText, value); }
        private string _nutsText = "-";
        public string NutsText { get => _nutsText; private set => SetProperty(ref _nutsText, value); }
        private string _tatText = "-";
        public string TatText { get => _tatText; private set => SetProperty(ref _tatText, value); }

        public ObservableCollection<LiveDisplayLot> Lots { get; } = new();

        private readonly DispatcherTimer _timer;

        public LiveDisplayViewModel(string batchNo)
        {
            BatchNo = batchNo;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += async (_, __) => await LoadAsync();
            _timer.Start();
            _ = LoadAsync();
        }

        public void Stop() => _timer.Stop();

        public async Task LoadAsync()
        {
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var batch = (await repo.GetBatchesAsync())
                    .FirstOrDefault(b => string.Equals(b.BatchNo, BatchNo, StringComparison.OrdinalIgnoreCase));
                if (batch == null)
                {
                    BannerText = "Batch not found.";
                    SubText = BatchNo;
                    return;
                }
                var lots = await repo.GetPlanLotsAsync(BatchNo);
                int np = Math.Max(1, batch.NutsPerTub);
                int done = lots.Sum(l => l.DoneTubs);
                double kg = lots.Sum(l => l.DoneKg);
                int target = batch.TargetTubs > 0 ? batch.TargetTubs : lots.Sum(l => l.Tubs);
                var (tot, pz) = FusionLiveViewModel.TatFor(batch);
                BannerText = $"{batch.BatchNo} · {(batch.Status ?? string.Empty).ToUpperInvariant()}";
                SubText = $"Turn around {FusionLiveViewModel.FDur(tot)} · running {FusionLiveViewModel.FDur(tot - pz)}";
                ProgressPct = target > 0 ? Math.Min(100, (double)done / target * 100) : 0;
                WeightText = $"{kg:N0} kg";
                TubsText = $"{done:N0} / {target:N0}";
                NutsText = $"{(done * np):N0}";
                TatText = FusionLiveViewModel.FDur(tot);
                Lots.Clear();
                foreach (var l in lots)
                    Lots.Add(new LiveDisplayLot
                    {
                        Lot = l.LotNo,
                        DonePlan = $"{l.DoneTubs:N0} / {l.Tubs:N0}",
                        Pct = l.Tubs > 0 ? Math.Min(100, (double)l.DoneTubs / l.Tubs * 100) : 0,
                    });
            }
            catch
            {
                // Keep the last frame on transient errors.
            }
        }
    }
}
