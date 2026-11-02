using ProWeighSuitePlatforms.Core.Hosting;
using ProWeighSuitePlatforms.Core.Wpf.Views;

namespace ProWeighSuitePlatforms.Hosting
{
    public interface IHostComposition
    {
        ProductDescriptor Descriptor { get; }

        void RegisterModules(ProWeighSuitePlatformsHostBuilder builder);

        void RegisterViews(IViewFactoryRegistryBuilder registry);
    }

    public static class HostCompositionLocator
    {
        public static IHostComposition? Current { get; set; }
    }
}
