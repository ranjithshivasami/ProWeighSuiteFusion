// Fusion "Batch status" screen: every batch with one-click lot detail expansion.
// Completed batches moved here from the Live view pill strip.
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.ViewModels
{
    public sealed class BatchStatusViewModel : BaseViewModel
    {
        public string Title => "Batch status";

        public ObservableCollection<FusionBatch> Batches { get; } = new();
        public ObservableCollection<FusionPlanLot> Lots { get; } = new();

        private FusionBatch? _selectedBatch;
        public FusionBatch? SelectedBatch
        {
            get => _selectedBatch;
            set
            {
                if (SetProperty(ref _selectedBatch, value))
                {
                    RaisePropertyChanged(nameof(HasSelection));
                    RaisePropertyChanged(nameof(LotsHeader));
                    _ = LoadLotsAsync(value?.BatchNo);
                }
            }
        }

        public bool HasSelection => _selectedBatch != null;
        public bool HasLots => Lots.Count > 0;

        public string LotsHeader => _selectedBatch is null
            ? "Lot details"
            : $"Lot details - {_selectedBatch.BatchNo} ({_selectedBatch.Status})";

        private string _statusText = string.Empty;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public ICommand RefreshCommand { get; }

        public BatchStatusViewModel()
        {
            RefreshCommand = new RelayCommand(async _ => await LoadAsync());
            _ = LoadAsync();
        }

        public async Task LoadAsync()
        {
            try
            {
                await new FusionDatabaseInit(AppConfigurationManager.GetDatabasePath()).EnsureCreatedAsync();
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var batches = await repo.GetBatchesAsync();

                var keep = _selectedBatch?.BatchNo;
                Batches.Clear();
                foreach (var b in batches)
                    Batches.Add(b);

                SelectedBatch = batches.FirstOrDefault(b =>
                        string.Equals(b.BatchNo, keep, StringComparison.OrdinalIgnoreCase))
                    ?? batches.FirstOrDefault();

                StatusText = Batches.Count == 0
                    ? "No batches yet. Create one in the Batch creation screen."
                    : $"{Batches.Count} batch(es) listed.";
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        private async Task LoadLotsAsync(string? batchNo)
        {
            Lots.Clear();
            if (string.IsNullOrWhiteSpace(batchNo))
            {
                RaisePropertyChanged(nameof(HasLots));
                return;
            }

            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                foreach (var lot in await repo.GetPlanLotsAsync(batchNo))
                    Lots.Add(lot);
            }
            catch (Exception ex)
            {
                StatusText = $"Lot load failed: {ex.Message}";
            }
            finally
            {
                RaisePropertyChanged(nameof(HasLots));
            }
        }
    }
}
