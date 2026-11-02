using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProWeighSuitePlatforms.Views
{
    public partial class ScaleSettingsView : UserControl
    {
        public ScaleSettingsView()
        {
            InitializeComponent();
        }

        private void ScaleCode_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^\d$");
        }
    }
}