using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProWeighSuitePlatforms.Views
{
    public partial class MaterialsUpdateView : UserControl
    {
        public MaterialsUpdateView()
        {
            InitializeComponent();
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            // Only allow positive integers
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }
    }
}
