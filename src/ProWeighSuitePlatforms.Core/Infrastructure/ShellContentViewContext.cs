using ProWeighSuitePlatforms.ViewModels;

namespace ProWeighSuitePlatforms.Modules.Infrastructure
{
    // Runtime bridge between the shell and module view factories. The Core host is
    // built before MainViewModel/ShellViewModel exist, so the shell assigns these
    // once and route factories resolve them through the host service provider.
    public sealed class ShellContentViewContext
    {
        public MainViewModel? Main { get; set; }

        public ShellViewModel? Shell { get; set; }

        public string CurrentUsername => Main?.CurrentUser?.Username ?? "System";
    }
}
