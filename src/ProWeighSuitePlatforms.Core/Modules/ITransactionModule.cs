// Phase 1 — additive only. No existing code modified by this file.
// UI-agnostic contract so Core never references concrete Views/ViewModels.
namespace ProWeighSuitePlatforms.Modules.Contracts
{
    /// <summary>
    /// Describes one pluggable transaction module. Module IDs are registered by
    /// the owning product module, not by Core.
    /// UI factories (CreateView/CreateViewModel) arrive in Phase 2 to avoid
    /// Core ↔ Views circular references during Phase 1.
    /// </summary>
    public interface ITransactionModule
    {
        string ModuleId { get; }
        string DisplayName { get; }
        string IconKey { get; }
        string RequiredCapability { get; }
        string Description { get; }
    }
}
