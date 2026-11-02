using ProWeighSuitePlatforms.Core.Hosting;
using ProWeighSuitePlatforms.Core.Navigation;
using ProWeighSuitePlatforms.Core.Wpf.Views;
using ProWeighSuitePlatforms.Adapters;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Modules.Contracts;
using ProWeighSuitePlatforms.Modules.Infrastructure;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Services.Scanners;

namespace ProWeighSuitePlatforms.Hosting
{
    public interface IHostContext
    {
        string Flavor { get; }

        ProWeighSuitePlatformsHost? CoreHost { get; }

        LegacySessionContext? Session { get; }

        ShellContentViewContext? ShellContent { get; }

        ViewFactoryRegistry? ViewFactories { get; }

        IModuleRegistry? ModuleRegistry { get; }

        IScannerService? ScannerService { get; }

        AuditService? AuditService { get; }

        LiteQueueSyncService? Queue { get; }

        void SetScannerService(IScannerService? service);

        void AttachScaleSessionControl(IScaleSessionControl control);

        void SetCurrentUser(User? user);

        void NotifyNavigation(NavigationChangeReason reason);

        void MarkExitConfirmed();
    }
}
