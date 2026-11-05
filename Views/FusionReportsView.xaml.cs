using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ProWeighSuitePlatforms.ViewModels;

namespace ProWeighSuitePlatforms.Views
{
    public partial class FusionReportsView : UserControl
    {
        private FusionReportsViewModel? _vm;

        public FusionReportsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm is not null)
            {
                _vm.ColumnSelectionChanged -= OnColumnSelectionChanged;
                _vm.ReportColumns.CollectionChanged -= OnReportColumnsChanged;
            }

            if (e.NewValue is FusionReportsViewModel vm)
            {
                _vm = vm;
                vm.ColumnSelectionChanged += OnColumnSelectionChanged;
                vm.ReportColumns.CollectionChanged += OnReportColumnsChanged;
                BuildColumns(vm);
                UpdateColumnVisibility(vm);
            }
            else
            {
                _vm = null;
            }
        }

        private void OnReportColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_vm is null) return;
            BuildColumns(_vm);
            UpdateColumnVisibility(_vm);
        }

        private void OnColumnSelectionChanged(object? sender, EventArgs e)
        {
            if (_vm is not null)
                UpdateColumnVisibility(_vm);
        }

        private void BuildColumns(FusionReportsViewModel vm)
        {
            ReportDataGrid.Columns.Clear();

            // S.No column is always present (dynamic 1-based row index).
            ReportDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "S.No",
                Binding = new Binding("SNo"),
            });

            // Remaining columns are generated dynamically from fusion_transactions schema.
            // Bind to the DB column name (Key) but show the friendly UI name as the header.
            foreach (var dbColName in vm.ReportColumns.Skip(1)) // skip SNo
            {
                ReportDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = FusionReportsViewModel.GetDisplayName(dbColName),
                    Binding = new Binding(dbColName),
                });
            }
        }

        private void UpdateColumnVisibility(FusionReportsViewModel vm)
        {
            if (ReportDataGrid == null || vm == null) return;

            foreach (var column in ReportDataGrid.Columns.OfType<DataGridTextColumn>())
            {
                // The binding path holds the DB column name (the visibility key).
                var key = column.Binding is Binding b ? b.Path.Path : null;
                if (string.IsNullOrEmpty(key)) continue;

                var selectable = vm.GetColumnByKey(key);
                column.Visibility = selectable?.IsSelected == true
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }
    }
}
