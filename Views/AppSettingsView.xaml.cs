using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProWeighSuitePlatforms.Views
{
    public partial class AppSettingsView : UserControl
    {
        public AppSettingsView()
        {
            InitializeComponent();
        }

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex(@"^[0-9]*\.?[0-9]*$");
            var textBox = sender as TextBox;
            if (textBox != null)
            {
                string fullText = textBox.Text.Insert(textBox.SelectionStart, e.Text);
                e.Handled = !regex.IsMatch(fullText);
            }
        }
    }
}
