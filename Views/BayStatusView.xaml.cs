using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using ProWeighSuitePlatforms.ViewModels;
namespace ProWeighSuitePlatforms.Views
{
    public partial class BayStatusView : UserControl
    {
        public BayStatusView()
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

        private void CapacityTextBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is not TextBox tb || tb.Tag is not BayStatusRow row) return;
            // Push the explicit binding, then persist.
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (DataContext is BayStatusViewModel vm)
                _ = vm.SaveCapacityAsync(row, tb.Text);
        }

        private static bool TryRow(object sender, out BayStatusRow? row)
        {
            row = null;
            if (sender is not MenuItem mi) return false;
            // Primary: menu inherits the row via ContextMenu.DataContext.
            if (mi.DataContext is BayStatusRow direct) { row = direct; return true; }
            // Fallback: the ⋯ button that opened the menu carries the row.
            var menu = mi.Parent as ContextMenu;
            if (menu?.PlacementTarget is Button btn && btn.DataContext is BayStatusRow fromBtn)
            {
                row = fromBtn;
                return true;
            }
            return false;
        }

        private void EditMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!TryRow(sender, out var row) || row == null) return;
            if (DataContext is BayStatusViewModel vm)
                vm.BeginEdit(row);
        }

        private void ActivateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!TryRow(sender, out var row) || row == null) return;
            if (DataContext is not BayStatusViewModel vm) return;
            if (row.IsActive)
            {
                vm.ShowStatus($"Bay '{row.BayId}' is already active.");
                return;
            }
            _ = vm.ToggleActiveAsync(row);
        }

        private void DeactivateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!TryRow(sender, out var row) || row == null) return;
            if (DataContext is not BayStatusViewModel vm) return;
            if (!row.IsActive)
            {
                vm.ShowStatus($"Bay '{row.BayId}' is already inactive.");
                return;
            }
            _ = vm.ToggleActiveAsync(row);
        }
    }
}
