using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ProWeighSuitePlatforms.Modules.Fusion
{
    public partial class SupplierMasterView : UserControl
    {
        public SupplierMasterView() => InitializeComponent();

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

        private static bool TryRow(object sender, out FusionSupplier? supplier, out FusionSupplierStockRow? stockRow)
        {
            supplier = null;
            stockRow = null;
            if (sender is not MenuItem mi) return false;
            // Primary: menu inherits the row via ContextMenu.DataContext.
            if (mi.DataContext is FusionSupplier s) { supplier = s; return true; }
            if (mi.DataContext is FusionSupplierStockRow r) { stockRow = r; return true; }
            // Fallback: the ⋯ button that opened the menu carries the row.
            var menu = mi.Parent as ContextMenu;
            if (menu?.PlacementTarget is Button btn)
            {
                if (btn.DataContext is FusionSupplier bs) { supplier = bs; return true; }
                if (btn.DataContext is FusionSupplierStockRow br) { stockRow = br; return true; }
            }
            return false;
        }

        private void EditMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!TryRow(sender, out var supplier, out var stockRow)) return;
            if (DataContext is not SupplierMasterViewModel vm) return;
            if (supplier != null)
            {
                vm.BeginEdit(supplier);
                return;
            }
            if (stockRow != null)
            {
                var sup = vm.Suppliers.FirstOrDefault(x => x.Id == stockRow.SupplierId);
                if (sup != null) vm.BeginEdit(sup);
                else vm.ShowStatus($"Supplier for '{stockRow.SupplierName}' was not found.");
            }
        }

        private void ActivateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!TryRow(sender, out var supplier, out var stockRow)) return;
            if (DataContext is not SupplierMasterViewModel vm) return;
            if (supplier != null) { _ = vm.SetActiveAsync(supplier, true); return; }
            if (stockRow != null) _ = vm.SetActiveAsync(stockRow, true);
        }

        private void DeactivateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!TryRow(sender, out var supplier, out var stockRow)) return;
            if (DataContext is not SupplierMasterViewModel vm) return;
            if (supplier != null) { _ = vm.SetActiveAsync(supplier, false); return; }
            if (stockRow != null) _ = vm.SetActiveAsync(stockRow, false);
        }

        private static CustomPopupPlacement[] RightAlignedPopupCallback(Size popupSize, Size targetSize, Point offset)
        {
            return new[]
            {
                new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height), PopupPrimaryAxis.Horizontal)
            };
        }
    }
}
