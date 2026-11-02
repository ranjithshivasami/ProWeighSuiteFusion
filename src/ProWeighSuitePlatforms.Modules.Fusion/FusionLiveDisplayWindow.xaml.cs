using System.Windows;
using System.Windows.Input;

namespace ProWeighSuitePlatforms.Modules.Fusion
{
    public partial class FusionLiveDisplayWindow : Window
    {
        public string BatchNo { get; }

        public FusionLiveDisplayWindow(string batchNo)
        {
            BatchNo = batchNo;
            InitializeComponent();
            DataContext = new LiveDisplayViewModel(batchNo);
            Closed += (_, __) =>
            {
                if (DataContext is LiveDisplayViewModel vm)
                    vm.Stop();
            };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                    Close();
            };
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    }
}
