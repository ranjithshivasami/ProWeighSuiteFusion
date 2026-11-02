using System.Windows;
using System.Windows.Input;

namespace ProWeighSuitePlatforms.Views
{
    public partial class AuditLogViewerWindow : Window
    {
        public AuditLogViewerWindow()
        {
            InitializeComponent();
        }

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Show details on double-click
            var viewModel = DataContext as ViewModels.AuditLogViewerViewModel;
            if (viewModel?.ViewDetailsCommand.CanExecute(null) == true)
            {
                viewModel.ViewDetailsCommand.Execute(null);
            }
        }
    }
}
