using System;
using Velopack;

namespace ProWeighSuitePlatforms
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Velopack hook handling must be first. Handles install / uninstall /
            // update-apply and exits immediately when running under those hooks.
            VelopackApp.Build().Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
