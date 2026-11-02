using System.Windows;

namespace ProWeighSuitePlatforms.Views
{
    public partial class FailedSyncDetailsWindow : Window
    {
        public FailedSyncDetailsWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
