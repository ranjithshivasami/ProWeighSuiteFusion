using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ProWeighSuitePlatforms.ViewModels;

namespace ProWeighSuitePlatforms.Views
{
    public partial class FusionReportsView : UserControl
    {
        public FusionReportsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is FusionReportsViewModel vm)
                BuildColumns(vm);
        }

        private void BuildColumns(FusionReportsViewModel vm)
        {
            ReportDataGrid.Columns.Clear();

            // S.No column is already declared in XAML (bound to SNo);
            // add the rest of the columns discovered from fusion_transactions schema.
            foreach (var colName in vm.ReportColumns.Skip(1)) // skip SNo
            {
                var col = new DataGridTextColumn
                {
                    Header = colName,
                    Binding = new Binding(colName),
                };
                ReportDataGrid.Columns.Add(col);
            }
        }
    }
}
