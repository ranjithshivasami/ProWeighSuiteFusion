using System.Linq;
using System.Reflection;
using Xunit;

namespace ProWeighSuitePlatforms.Tests.Core;

[Collection("ProductModuleCatalog")]
public class FusionHostCompositionTests
{
    [Fact]
    public void App_CoreHost_BuildsWithCoreAndFusionModules()
    {
        ProWeighSuitePlatforms.Hosting.HostCompositionLocator.Current ??=
            new ProWeighSuitePlatforms.Fusion.Host.FusionHostComposition();

        var build = typeof(ProWeighSuitePlatforms.App).GetMethod(
            "BuildCoreHost",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(build);

        build!.Invoke(null, null);

        var host = ProWeighSuitePlatforms.App.CoreHost;
        Assert.NotNull(host);
        Assert.NotNull(ProWeighSuitePlatforms.App.CoreSession);
        Assert.NotNull(ProWeighSuitePlatforms.App.CoreViewFactories);
        Assert.NotNull(ProWeighSuitePlatforms.App.CoreNavigation);
        Assert.NotNull(ProWeighSuitePlatforms.App.ShellContentViewContext);

        // Exactly the Core navigation module and the Fusion product module.
        Assert.Equal(2, host!.Modules.Modules.Count);
        Assert.NotNull(host.Modules.Find("Core"));
        Assert.NotNull(host.Modules.Find("Fusion"));

        // Core menu: Home, Master, Configuration, Audit logs, Users, Permissions, Info.
        Assert.NotNull(host.Menus.Find("Home"));
        Assert.NotNull(host.Menus.Find("Configuration"));
        Assert.NotNull(host.Menus.Find("AuditLogs"));
        Assert.NotNull(host.Menus.Find("Users"));
        Assert.NotNull(host.Menus.Find("Permissions"));
        Assert.NotNull(host.Menus.Find("Info"));

        // Configuration children (common configuration screens).
        foreach (var child in new[] { "ScaleSettings", "CompanySettings", "AppSettings", "SyncSettings", "BackupSettings", "ReportSchedule", "LabelTemplates", "PrinterSetup", "PlcSettings", "ScannerSettings" })
            Assert.NotNull(host.Menus.Find(child));

        // Core routes.
        Assert.NotNull(host.Routes.Find("Home"));
        Assert.NotNull(host.Routes.Find("Users"));
        Assert.NotNull(host.Routes.Find("AuditLogs"));
        Assert.NotNull(host.Routes.Find("Info"));

        // Fusion routes.
        Assert.NotNull(host.Routes.Find("FusionDashboard"));
        Assert.NotNull(host.Routes.Find("InwardStock"));
        Assert.NotNull(host.Routes.Find("BatchLots"));
        Assert.NotNull(host.Routes.Find("LiveView"));
        Assert.NotNull(host.Routes.Find("FusionReports"));

        // Permission registry carries core and Fusion keys.
        Assert.Contains(host.Permissions.All, p => p.Key.StartsWith("core."));
        Assert.Contains(host.Permissions.All, p => p.Key.StartsWith("fusion."));

        // View factories cover the core and Fusion screens.
        Assert.NotNull(ProWeighSuitePlatforms.App.CoreViewFactories!.Find("Home"));
        Assert.NotNull(ProWeighSuitePlatforms.App.CoreViewFactories.Find("Users"));
        Assert.NotNull(ProWeighSuitePlatforms.App.CoreViewFactories.Find("Info"));
        Assert.NotNull(ProWeighSuitePlatforms.App.CoreViewFactories.Find("FusionDashboard"));
        Assert.NotNull(ProWeighSuitePlatforms.App.CoreViewFactories.Find("LiveView"));
    }
}
