// FUSION-PLAN.md Step 3 — placeholder content for Fusion tabs whose real
// ViewModels land in Step 4 (masters/transactions) and Step 5 (dashboard).
// Never touches weighing_*; has no DB access at all.
using ProWeighSuitePlatforms.ViewModels;

namespace ProWeighSuitePlatforms.ViewModels
{
    public sealed class FusionPlaceholderViewModel : BaseViewModel
    {
        public string ModuleId { get; }
        public string Title { get; }
        public string StatusText { get; } = "Placeholder — full UI lands in Step 4/5 per FUSION-PLAN.md.";

        public FusionPlaceholderViewModel(string moduleId, string displayName)
        {
            ModuleId = moduleId;
            Title = displayName;
        }
    }
}
