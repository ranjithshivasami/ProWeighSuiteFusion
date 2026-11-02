using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace ProWeighSuitePlatforms.Modules.Fusion
{
    public partial class TubMasterView : UserControl
    {
        public TubMasterView()
        {
            InitializeComponent();
        }

        private void CyclesTextBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is not TextBox tb || tb.Tag is not TubMasterRow row) return;
            if (DataContext is TubMasterViewModel vm)
                _ = vm.SaveCyclesAsync(row);
        }

        private static readonly Regex CapacityInputRegex = new("^[0-9.,]+$");

        // Capacity: digits + decimal separators only (validated as positive number in VM).
        private void Capacity_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !CapacityInputRegex.IsMatch(e.Text);
        }

        private void Capacity_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                var text = (string)e.DataObject.GetData(typeof(string));
                if (!CapacityInputRegex.IsMatch(text))
                    e.CancelCommand();
            }
            else
            {
                e.CancelCommand();
            }
        }

        private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row.Item is not TubMasterRow row) return;
            // Commit completes after this event; persist on background priority.
            Dispatcher.InvokeAsync(async () =>
            {
                if (DataContext is TubMasterViewModel vm)
                    await vm.SaveCyclesAsync(row);
            }, DispatcherPriority.Background);
        }

        private void TubConfigButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.ContextMenu == null) return;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = PlacementMode.Custom;
            button.ContextMenu.CustomPopupPlacementCallback = RightAlignedPopupCallback;
            button.ContextMenu.IsOpen = true;
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

        private static bool TryRow(object sender, out TubMasterRow? row)
        {
            row = null;
            if (sender is not MenuItem mi) return false;
            // Primary: menu inherits the row via ContextMenu.DataContext.
            if (mi.DataContext is TubMasterRow direct) { row = direct; return true; }
            // Fallback: the ⋯ button that opened the menu carries the row.
            var menu = mi.Parent as ContextMenu;
            if (menu?.PlacementTarget is Button btn && btn.DataContext is TubMasterRow fromBtn)
            {
                row = fromBtn;
                return true;
            }
            return false;
        }

        private void EditMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!TryRow(sender, out var row) || row == null) return;
            if (DataContext is TubMasterViewModel vm)
                vm.BeginEdit(row);
        }

        private void BlockMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!TryRow(sender, out var row) || row == null) return;
            if (DataContext is TubMasterViewModel vm)
                _ = vm.SetBlockedAsync(row, true);
        }

        private void UnblockMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!TryRow(sender, out var row) || row == null) return;
            if (DataContext is TubMasterViewModel vm)
                _ = vm.SetBlockedAsync(row, false);
        }

        private void ReplaceMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!TryRow(sender, out var row) || row == null) return;
            if (DataContext is TubMasterViewModel vm)
                _ = vm.MarkReplacedAsync(row);
        }
    }
}
