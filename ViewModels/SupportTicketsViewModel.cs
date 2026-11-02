// FUSION-PLAN.md Step 6 — Support tickets over fusion_tickets.
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;
using static ProWeighSuitePlatforms.Data.FusionRepository;

namespace ProWeighSuitePlatforms.ViewModels
{
    public sealed class SupportTicketsViewModel : ViewModels.BaseViewModel
    {
        public string Title => "Support tickets";

        public ObservableCollection<FusionTicket> Rows { get; } = new();

        private FusionTicket? _selected;
        public FusionTicket? Selected
        {
            get => _selected;
            set => SetProperty(ref _selected, value);
        }

        private string _newTitle = string.Empty;
        public string NewTitle { get => _newTitle; set => SetProperty(ref _newTitle, value); }

        private bool _openOnly;
        public bool OpenOnly
        {
            get => _openOnly;
            set { if (SetProperty(ref _openOnly, value)) _ = LoadAsync(); }
        }

        private string _statusText = string.Empty;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public ICommand RefreshCommand { get; }
        public ICommand AddCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand ReopenCommand { get; }

        public SupportTicketsViewModel()
        {
            RefreshCommand = new ViewModels.RelayCommand(async _ => await LoadAsync());
            AddCommand = new ViewModels.RelayCommand(async _ => await AddAsync());
            CloseCommand = new ViewModels.RelayCommand(async _ => await SetStatusAsync("Closed"));
            ReopenCommand = new ViewModels.RelayCommand(async _ => await SetStatusAsync("Open"));
            _ = LoadAsync();
        }

        public async Task LoadAsync()
        {
            try
            {
                await new FusionDatabaseInit(AppConfigurationManager.GetDatabasePath()).EnsureCreatedAsync();
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var rows = await repo.GetTicketsAsync(OpenOnly);
                Rows.Clear();
                foreach (var r in rows) Rows.Add(r);
                StatusText = $"{Rows.Count} ticket(s).";
            }
            catch (System.Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        public async Task AddAsync()
        {
            if (string.IsNullOrWhiteSpace(NewTitle))
            {
                StatusText = "Enter a ticket title first.";
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.AddTicketAsync(NewTitle.Trim());
                StatusText = "Ticket raised.";
                NewTitle = string.Empty;
                await LoadAsync();
            }
            catch (System.Exception ex)
            {
                StatusText = $"Raise failed: {ex.Message}";
            }
        }

        private async Task SetStatusAsync(string status)
        {
            if (Selected == null)
            {
                StatusText = "Select a ticket first.";
                return;
            }
            try
            {
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                await repo.SetTicketStatusAsync(Selected.Id, status);
                StatusText = $"Ticket {Selected.Id} → {status}.";
                await LoadAsync();
            }
            catch (System.Exception ex)
            {
                StatusText = $"Update failed: {ex.Message}";
            }
        }
    }
}
