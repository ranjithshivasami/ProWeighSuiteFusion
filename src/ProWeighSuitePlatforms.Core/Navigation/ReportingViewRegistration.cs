using System;
using Microsoft.Extensions.DependencyInjection;
using ProWeighSuitePlatforms.Core.Navigation;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Core.Wpf.Views;
using ProWeighSuitePlatforms.ViewModels;
using ProWeighSuitePlatforms.Views;

namespace ProWeighSuitePlatforms.Platform.Reporting.Wpf.Navigation
{
    public static class ReportingViewRegistration
    {
        public static void RegisterViews(IViewFactoryRegistryBuilder registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            registry.Register(new ViewFactoryRegistration(
                "Reports",
                services => new ReportsViewModel(services.GetRequiredService<ICurrentUserContext>()),
                viewModel => new ReportsView { DataContext = viewModel },
                RouteCacheMode.KeepAlive));

            registry.Register(new ViewFactoryRegistration(
                "SummaryReport",
                services => new SummaryReportViewModel(services.GetRequiredService<ICurrentUserContext>()),
                viewModel => new SummaryReportView { DataContext = viewModel }));
        }
    }
}
