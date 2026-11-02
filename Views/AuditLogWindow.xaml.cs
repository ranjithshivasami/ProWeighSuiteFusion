using System.Windows;
using ProWeighSuitePlatforms.ViewModels;

namespace ProWeighSuitePlatforms.Views
{
    public partial class AuditLogWindow : Window
    {
        public AuditLogWindow()
        {
            InitializeComponent();
            DataContext = new AuditLogViewModel();
        }
    }
}
