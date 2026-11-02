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
    public sealed class AppHostContext : IHostContext
    {
        public static AppHostContext Instance { get; } = new();

        private AppHostContext()
        {
        }

        public string Flavor => App.Flavor;

        public ProWeighSuitePlatformsHost? CoreHost => App.CoreHost;

        public LegacySessionContext? Session => App.CoreSession;

        public ShellContentViewContext? ShellContent => App.ShellContentViewContext;

        public ViewFactoryRegistry? ViewFactories => App.CoreViewFactories;

        public IModuleRegistry? ModuleRegistry => App.ModuleRegistry;

        public IScannerService? ScannerService => App.ScannerService;

        public AuditService? AuditService => App.AuditService;

        public LiteQueueSyncService? Queue => App.LiteQueueSyncService;

        public void SetScannerService(IScannerService? service) => App.SetScannerService(service);

        public void AttachScaleSessionControl(IScaleSessionControl control) =>
            App.AttachScaleSessionControl(control);

        public void SetCurrentUser(User? user) => App.CoreSession?.SetUser(user);

        public void NotifyNavigation(NavigationChangeReason reason) => App.NotifyNavigation(reason);

        public void MarkExitConfirmed()
        {
            if (App.Current?.MainWindow is MainWindow mainWindow)
                mainWindow.MarkExitConfirmed();
        }
    }
}
