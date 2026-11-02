using System;
using Microsoft.Extensions.DependencyInjection;
using ProWeighSuitePlatforms.Core.Navigation;
using ProWeighSuitePlatforms.Core.Wpf.Views;
using ProWeighSuitePlatforms.ViewModels;

namespace ProWeighSuitePlatforms.Modules.Infrastructure
{
    public static class ModuleViewRegistration
    {
        public static void RegisterView(
            IViewFactoryRegistryBuilder registry,
            string routeId,
            Func<IServiceProvider, object> viewModelFactory,
            Func<object, object> viewFactory,
            RouteCacheMode cacheMode = RouteCacheMode.Transient)
        {
            registry.Register(new ViewFactoryRegistration(
                routeId,
                viewModelFactory,
                viewFactory,
                cacheMode));
        }

        public static ShellContentViewContext Context(IServiceProvider services) =>
            services.GetRequiredService<ShellContentViewContext>();

        public static MainViewModel RequireMain(ShellContentViewContext context) =>
            context.Main ?? throw new InvalidOperationException(
                "The shell content context has no MainViewModel.");

        public static ShellViewModel RequireShell(ShellContentViewContext context) =>
            context.Shell ?? throw new InvalidOperationException(
                "The shell content context has no ShellViewModel.");
    }
}
