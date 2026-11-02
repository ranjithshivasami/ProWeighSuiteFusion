using System;
using ProWeighSuitePlatforms.Core.Hosting;
using ProWeighSuitePlatforms.Core.Wpf.Views;
using ProWeighSuitePlatforms.Hosting;
using ProWeighSuitePlatforms.Modules.Fusion;

namespace ProWeighSuitePlatforms.Fusion.Host
{
    public sealed class FusionHostComposition : IHostComposition
    {
        public ProductDescriptor Descriptor { get; } = new(
            "Fusion",
            "Fusion",
            new Version(1, 1, 6),
            LicenseKey: "Fusion");

        public void RegisterModules(ProWeighSuitePlatformsHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.AddModule(new FusionModule());
        }

        public void RegisterViews(IViewFactoryRegistryBuilder registry)
        {
            ArgumentNullException.ThrowIfNull(registry);
            FusionModule.RegisterViews(registry);
        }
    }
}
