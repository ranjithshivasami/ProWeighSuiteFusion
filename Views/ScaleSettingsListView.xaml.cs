using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ProWeighSuitePlatforms.Views
{
    public partial class ScaleSettingsListView : UserControl
    {
        public ScaleSettingsListView()
        {
            InitializeComponent();
        }

        private void RowActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.ContextMenu == null) return;

            button.ContextMenu.PlacementTarget = button;
            // Right-align: menu's right edge flush with button's right edge
            button.ContextMenu.Placement = PlacementMode.Custom;
            button.ContextMenu.CustomPopupPlacementCallback = RightAlignedPopupCallback;
            button.ContextMenu.IsOpen = true;
        }

        private static CustomPopupPlacement[] RightAlignedPopupCallback(Size popupSize, Size targetSize, Point offset)
        {
            // X = target width - popup width => right edges align; Y = target height => opens just below button
            return new[]
            {
                new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height), PopupPrimaryAxis.Horizontal)
            };
        }
    }
}
