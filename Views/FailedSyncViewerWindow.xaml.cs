using System;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.Views
{
    public partial class FailedSyncViewerWindow : Window
    {
        public FailedSyncViewerWindow()
        {
            InitializeComponent();
            Loaded += OnWindowLoaded;
            Closed += OnWindowClosed;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // Reload the list immediately when the daily auto-reset clears records
            DailyResetService.DailyResetCompleted += OnDailyResetCompleted;
        }

        private void OnWindowClosed(object? sender, EventArgs e)
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

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Show details on double-click
            var viewModel = DataContext as ViewModels.FailedSyncViewerViewModel;
            if (viewModel?.ViewDetailsCommand.CanExecute(null) == true)
            {
                viewModel.ViewDetailsCommand.Execute(null);
            }
        }
    }
}
