using System.Windows.Controls;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.Views
{
    public partial class LoadingIndicator : UserControl
    {
        public LoadingIndicator()
        {
            InitializeComponent();
            DataContext = LoadingService.Instance;
        }
    }
}
