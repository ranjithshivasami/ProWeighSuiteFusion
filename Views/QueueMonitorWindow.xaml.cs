using System.Windows;
using ProWeighSuitePlatforms.Contracts;
using ProWeighSuitePlatforms.ViewModels;

namespace ProWeighSuitePlatforms.Views
{
    public partial class QueueMonitorWindow : Window
    {
        public QueueMonitorWindow(ITransactionRetryQueue queue)
        {
            InitializeComponent();
            DataContext = new QueueMonitorViewModel(queue);
        }
    }
}
