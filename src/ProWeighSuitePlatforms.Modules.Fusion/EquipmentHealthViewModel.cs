// FUSION-PLAN.md Step 6 — Equipment health over fusion_equipment.
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data.Fusion;
using static ProWeighSuitePlatforms.Data.Fusion.FusionRepository;

namespace ProWeighSuitePlatforms.Modules.Fusion
{
    public sealed class EquipmentHealthViewModel : ViewModels.BaseViewModel
    {
        public string Title => "Equipment health";

        public ObservableCollection<FusionEquipment> Rows { get; } = new();

        private string _equipId = string.Empty;
        public string EquipId { get => _equipId; set => SetProperty(ref _equipId, value); }
        private string _name = string.Empty;
        public string Name { get => _name; set => SetProperty(ref _name, value); }
        private string _status = "Online";
        public string Status { get => _status; set => SetProperty(ref _status, value); }

        private string _statusText = string.Empty;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public ICommand RefreshCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }

        public EquipmentHealthViewModel()
        {
            RefreshCommand = new ViewModels.RelayCommand(async _ => await LoadAsync());
            SaveCommand = new ViewModels.RelayCommand(async _ => await SaveAsync());
            DeleteCommand = new ViewModels.RelayCommand(async _ => await DeleteAsync());
            _ = LoadAsync();
        }

        public async Task LoadAsync()
        {
            try
            {
                await new FusionDatabaseInit(AppConfigurationManager.GetDatabasePath()).EnsureCreatedAsync();
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var rows = await repo.GetEquipmentAsync();
                Rows.Clear();
                foreach (var r in rows) Rows.Add(r);
                StatusText = $"{Rows.Count} equipment record(s).";
            }
            catch (System.Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        public async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(EquipId) || string.IsNullOrWhiteSpace(Name))
            {
                StatusText = "Equipment ID and Name are required.";
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.UpsertEquipmentAsync(EquipId.Trim(), Name.Trim(), Status?.Trim() ?? "Online");
                StatusText = $"Saved {EquipId.Trim()}.";
                EquipId = Name = string.Empty;
                Status = "Online";
                await LoadAsync();
            }
            catch (System.Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
            }
        }

        public async Task DeleteAsync()
        {
            if (string.IsNullOrWhiteSpace(EquipId))
            {
                StatusText = "Enter Equipment ID to delete.";
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.DeleteEquipmentAsync(EquipId.Trim());
                StatusText = $"Deleted {EquipId.Trim()}.";
                EquipId = string.Empty;
                await LoadAsync();
            }
            catch (System.Exception ex)
            {
                StatusText = $"Delete failed: {ex.Message}";
            }
        }
    }
}
