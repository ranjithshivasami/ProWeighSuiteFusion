using System.Windows;

namespace ProWeighSuitePlatforms.Views
{
    public partial class AuditLogDetailsWindow : Window
    {
        public AuditLogDetailsWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
