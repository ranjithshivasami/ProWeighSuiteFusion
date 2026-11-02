using System;
using Microsoft.Extensions.DependencyInjection;
using ProWeighSuitePlatforms.Core.Navigation;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Core.Wpf.Views;
using ProWeighSuitePlatforms.ViewModels;
using ProWeighSuitePlatforms.Views;

namespace ProWeighSuitePlatforms.Core.Wpf.Navigation
{
    public static class CoreMasterDataViewRegistration
    {
        public static void RegisterViews(IViewFactoryRegistryBuilder registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            registry.Register(new ViewFactoryRegistration(
                "Category",
                services => new CategoryViewModel(services.GetRequiredService<ICurrentUserContext>()),
                viewModel => new CategoryView { DataContext = viewModel },
                RouteCacheMode.KeepAlive));

            registry.Register(new ViewFactoryRegistration(
                "Variant",
                services => new VariantViewModel(services.GetRequiredService<ICurrentUserContext>()),
                viewModel => new VariantView { DataContext = viewModel },
                RouteCacheMode.KeepAlive));

            registry.Register(new ViewFactoryRegistration(
                "Materials",
                services => new MaterialsViewModel(services.GetRequiredService<ICurrentUserContext>()),
                viewModel => new MaterialsView { DataContext = viewModel },
                RouteCacheMode.KeepAlive));

            registry.Register(new ViewFactoryRegistration(
                "Supplier",
                services => new SupplierViewModel(services.GetRequiredService<ICurrentUserContext>()),
                viewModel => new SupplierView { DataContext = viewModel },
                RouteCacheMode.KeepAlive));
        }
    }
}
