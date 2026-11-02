// FUSION-PLAN.md Step 4 — Traceability: tub event trail over fusion_tub_events.
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Data;

using ProWeighSuitePlatforms.Models;
namespace ProWeighSuitePlatforms.ViewModels
{
    public sealed class TraceabilityViewModel : ViewModels.BaseViewModel
    {
        public string Title => "Traceability";

        public ObservableCollection<FusionTubEvent> Events { get; } = new();

        private string _tubId = string.Empty;
        public string TubId { get => _tubId; set => SetProperty(ref _tubId, value); }

        private string _statusText = "Enter a Tub ID to trace its journey.";
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public ICommand SearchCommand { get; }

        public TraceabilityViewModel()
        {
            SearchCommand = new ViewModels.RelayCommand(async _ => await SearchAsync());
        }

        public async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(TubId))
            {
                StatusText = "Enter a Tub ID first.";
                return;
            }
            try
            {
                await new FusionDatabaseInit(AppConfigurationManager.GetDatabasePath()).EnsureCreatedAsync();
                var repo = new FusionRepository(AppConfigurationManager.GetDatabasePath());
                var rows = await repo.GetTubEventsAsync(TubId.Trim());
                Events.Clear();
                foreach (var e in rows) Events.Add(e);
                StatusText = rows.Count == 0
                    ? $"No events for tub {TubId.Trim()} yet."
                    : $"{rows.Count} event(s) for tub {TubId.Trim()}.";
            }
            catch (System.Exception ex)
            {
                StatusText = $"Search failed: {ex.Message}";
            }
        }
    }
}
