using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProWeighSuitePlatforms.Views
{
    public partial class ScaleSettingsCreateView : UserControl
    {
        public ScaleSettingsCreateView()
        {
            InitializeComponent();
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void ScaleCode_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^\d$");
        }
    }
}
