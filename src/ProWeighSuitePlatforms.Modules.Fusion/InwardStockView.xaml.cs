using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ProWeighSuitePlatforms.Modules.Fusion
{
    public partial class InwardStockView : UserControl
    {
        public InwardStockView()
        {
            InitializeComponent();
        }

        private void RowActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.ContextMenu == null) return;
            button.ContextMenu.PlacementTarget = button;
            // Push the row explicitly: the PlacementTarget.DataContext binding can
            // lag on first open, which previously left menu actions with no row.
            button.ContextMenu.DataContext = button.DataContext;
            button.ContextMenu.Placement = PlacementMode.Custom;
            button.ContextMenu.CustomPopupPlacementCallback = RightAlignedPopupCallback;
            button.ContextMenu.IsOpen = true;
        }

        private static CustomPopupPlacement[] RightAlignedPopupCallback(Size popupSize, Size targetSize, Point offset)
        {
            return new[]
            {
                new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height), PopupPrimaryAxis.Horizontal)
            };
        }

        private void EditMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi) return;
            FusionInwardRow? row = mi.DataContext as FusionInwardRow;
            if (row == null)
            {
                var menu = mi.Parent as ContextMenu;
                if (menu?.PlacementTarget is Button btn)
                    row = btn.DataContext as FusionInwardRow;
            }
            if (row == null) return;
            if (DataContext is InwardStockViewModel vm)
                vm.BeginEdit(row);
        }
    }
}
