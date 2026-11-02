using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using ProWeighSuitePlatforms.ViewModels;
namespace ProWeighSuitePlatforms.Views
{
    public partial class FusionLiveView : UserControl
    {
        public FusionLiveView()
        {
            InitializeComponent();
        }

        private async void ManualEpcBox_KeyDown(object sender, KeyEventArgs e)
        {
            // Wedge-scanner rhythm: scan (types + Enter) -> weigh -> refocus for next tub.
            // While auto run is ON the flow is automatic (RFID + weight), so Enter is ignored.
            if (e.Key != Key.Enter || DataContext is not FusionLiveViewModel vm) return;
            if (vm.IsAutoRunning) { e.Handled = true; return; }
            e.Handled = true;
            await vm.WeighNextTubAsync();
            ManualEpcBox.Focus();
            ManualEpcBox.SelectAll();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Navigating away must NOT stop a running auto-run – it only ends via
            // the Pause batch / Complete batch buttons. While auto-run is active
            // the scale + scanner feeds stay attached, so RFID scans and weight
            // changes are still captured from other menus (e.g. Bay status)
            // exactly like Display mode. Outside auto-run the feeds detach so
            // weighing cannot continue in the background; OnLoaded re-attaches
            // them when the view returns.
            if (DataContext is FusionLiveViewModel vm)
                vm.OnViewUnloaded();
        }

        private bool _shownOnce;
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not FusionLiveViewModel vm) return;

            vm.OnViewLoaded();

            // First show: the VM constructor already loads the batch/feeds.
            // On every re-show (returning from another menu) re-attach the feeds
            // OnUnloaded detached and refresh the run state, so a started
            // auto-run keeps working after navigation.
            if (!_shownOnce)
            {
                _shownOnce = true;
                return;
            }

            _ = vm.LoadAsync();
        }
    }
}
