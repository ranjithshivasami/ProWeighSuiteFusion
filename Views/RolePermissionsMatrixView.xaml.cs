using ProWeighSuitePlatforms.ViewModels;
using System.Diagnostics;
using System.Windows.Controls;

namespace ProWeighSuitePlatforms.Views
{
    public partial class RolePermissionsMatrixView : UserControl
    {
        public RolePermissionsMatrixView()
        {
            InitializeComponent();
            
            // Force solid background
            this.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#e6e6e6"));
            
            // Diagnostic logging
            this.Loaded += (s, e) =>
            {
                Debug.WriteLine("=== RolePermissionsMatrixView LOADED ===");
                Debug.WriteLine($"Background: {this.Background}");
                Debug.WriteLine($"DataContext Type: {this.DataContext?.GetType().Name ?? "NULL"}");
                
                if (this.DataContext is RolePermissionsMatrixViewModel vm)
                {
                    Debug.WriteLine($"ViewModel.Title: {vm.Title}");
                    Debug.WriteLine($"ViewModel.PermissionRows.Count: {vm.PermissionRows.Count}");
                    Debug.WriteLine($"ViewModel.CurrentUser: {vm.CurrentUser?.Username ?? "NULL"}");
                }
                else
                {
                    Debug.WriteLine("WARNING: DataContext is NOT RolePermissionsMatrixViewModel!");
                }
            };
        }
    }
}
