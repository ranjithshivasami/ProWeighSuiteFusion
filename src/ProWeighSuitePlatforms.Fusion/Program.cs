using System;
using Velopack;

namespace ProWeighSuitePlatforms.Fusion.Host
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            VelopackApp.Build().Run();

            ProWeighSuitePlatforms.Hosting.HostCompositionLocator.Current = new FusionHostComposition();

            var app = new ProWeighSuitePlatforms.App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
