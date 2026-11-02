using System;
using Microsoft.Extensions.DependencyInjection;
using ProWeighSuitePlatforms.Core.Wpf.Views;
using ProWeighSuitePlatforms.Platform.Weighing.Contracts;
using ProWeighSuitePlatforms.ViewModels;
using ProWeighSuitePlatforms.Views;

namespace ProWeighSuitePlatforms.Platform.Weighing.Wpf.Navigation
{
    public static class WeighingTransactionsViewRegistration
    {
        public static void RegisterViews(IViewFactoryRegistryBuilder registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            registry.Register(new ViewFactoryRegistration(
                "TransactionsMenu",
                services => new RecentTransactionsViewModel(
                    services.GetRequiredService<ITransactionRetryQueue>()),
                viewModel => new RecentTransactionsView { DataContext = viewModel }));
        }
    }
}
