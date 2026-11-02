using System.Windows.Controls;
using System.Windows.Input;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.Views
{
    public partial class FailedSyncViewerView : UserControl
    {
        public FailedSyncViewerView()
        {
            InitializeComponent();
            Loaded += OnViewLoaded;
            Unloaded += OnViewUnloaded;
        }

        private void OnViewLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            DailyResetService.DailyResetCompleted += OnDailyResetCompleted;
        }

        private void OnViewUnloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            DailyResetService.DailyResetCompleted -= OnDailyResetCompleted;
        }

        private void OnDailyResetCompleted(object? sender, DailyResetEventArgs e)
        {
            var viewModel = DataContext as ViewModels.FailedSyncViewerViewModel;
            if (viewModel?.RefreshCommand.CanExecute(null) == true)
            {
                viewModel.RefreshCommand.Execute(null);
            }
        }

        private void ActionsMenuItem_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // Close the popup after menu click so the global spinner is visible
            ActionsMenuButton.IsChecked = false;
        }

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var viewModel = DataContext as ViewModels.FailedSyncViewerViewModel;
            if (viewModel?.ViewDetailsCommand.CanExecute(null) == true)
            {
                viewModel.ViewDetailsCommand.Execute(null);
            }
        }
    }
}
