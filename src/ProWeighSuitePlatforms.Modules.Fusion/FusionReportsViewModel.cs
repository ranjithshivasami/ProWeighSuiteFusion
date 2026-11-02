// FUSION-PLAN.md Step 6 — Fusion Reports over fusion_transactions only.
// Date-filtered grid + CSV export (same SaveFileDialog pattern as other VMs).
using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data.Fusion;

namespace ProWeighSuitePlatforms.Modules.Fusion
{
    public sealed class FusionReportsViewModel : ViewModels.BaseViewModel
    {
        public string Title => "Reports";

        public ObservableCollection<FusionTransaction> Rows { get; } = new();

        private DateTime _from = DateTime.Today.AddDays(-7);
        public DateTime From { get => _from; set => SetProperty(ref _from, value); }
        private DateTime _to = DateTime.Today;
        public DateTime To { get => _to; set => SetProperty(ref _to, value); }

        private string _statusText = string.Empty;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public ICommand RefreshCommand { get; }
        public ICommand ExportCsvCommand { get; }

        public FusionReportsViewModel()
        {
            RefreshCommand = new ViewModels.RelayCommand(async _ => await LoadAsync());
            ExportCsvCommand = new ViewModels.RelayCommand(async _ => await ExportCsvAsync());
            _ = LoadAsync();
        }

        public async Task LoadAsync()
        {
            try
            {
                await new FusionDatabaseInit(AppConfigurationManager.GetDatabasePath()).EnsureCreatedAsync();
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var rows = await repo.GetTransactionsByDateAsync(
                    From.ToString("yyyy-MM-dd"), To.ToString("yyyy-MM-dd"));
                Rows.Clear();
                double total = 0;
                foreach (var r in rows) { Rows.Add(r); total += r.WeightKg; }
                StatusText = $"{Rows.Count} transaction(s), {total:0.###} kg from {From:dd-MM-yyyy} to {To:dd-MM-yyyy}.";
            }
            catch (System.Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        public async Task ExportCsvAsync()
        {
            if (Rows.Count == 0)
            {
                StatusText = "Nothing to export — load a range first.";
                return;
            }
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"FusionReport_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                    Filter = "CSV file (*.csv)|*.csv",
                };
                if (dlg.ShowDialog() != true) return;
                var sb = new StringBuilder("Id,BatchNo,TubId,SupplierId,WeightKg,RecordedAt\r\n");
                foreach (var r in Rows)
                    sb.Append($"{r.Id},{r.BatchNo},{r.TubId},{r.SupplierId},{r.WeightKg},{r.RecordedAt}\r\n");
                await System.IO.File.WriteAllTextAsync(dlg.FileName, sb.ToString(), Encoding.UTF8);
                StatusText = $"Exported {Rows.Count} row(s) to {dlg.FileName}.";
            }
            catch (System.Exception ex)
            {
                StatusText = $"Export failed: {ex.Message}";
            }
        }
    }
}
