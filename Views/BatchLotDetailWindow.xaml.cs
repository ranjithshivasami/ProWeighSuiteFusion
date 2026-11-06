using System.Windows;

namespace ProWeighSuitePlatforms.Views
{
    /// <summary>
    /// Large popup showing the lot details for a selected batch.
    /// </summary>
    public partial class BatchLotDetailWindow : Window
    {
        public BatchLotDetailWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
