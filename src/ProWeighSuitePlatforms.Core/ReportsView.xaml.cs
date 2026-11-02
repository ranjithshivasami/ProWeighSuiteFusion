using System.Windows.Controls;
using ProWeighSuitePlatforms.ViewModels;

namespace ProWeighSuitePlatforms.Views
{
    public partial class ReportsView : UserControl
    {
        public ReportsView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is ReportsViewModel vm)
            {
                vm.ColumnSelectionChanged += OnColumnSelectionChanged;
                UpdateColumnVisibility(vm);
            }
        }

        private void OnColumnSelectionChanged(object? sender, System.EventArgs e)
        {
            if (DataContext is ReportsViewModel vm)
                UpdateColumnVisibility(vm);
        }

        private void UpdateColumnVisibility(ReportsViewModel vm)
        {
            if (ReportDataGrid == null) return;

            foreach (var column in ReportDataGrid.Columns)
            {
                var header = column.Header?.ToString();
                if (string.IsNullOrEmpty(header)) continue;

                var selectable = vm.GetColumnByKey(header);
                column.Visibility = selectable?.IsSelected == true
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;
            }
        }
    }
}
