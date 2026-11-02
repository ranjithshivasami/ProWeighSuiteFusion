using System.Windows;
using ProWeighSuitePlatforms.ViewModels;

namespace ProWeighSuitePlatforms.Views
{
    public partial class MaterialsCreateWindow : Window
    {
        public MaterialsCreateWindow()
        {
            InitializeComponent();
        }

        public MaterialsCreateWindow(MaterialsViewModel vm) : this()
        {
            DataContext = vm;
            // Inline navigation is used; window content not set programmatically.
            // Content = new MaterialsCreateView { DataContext = vm };
        }

        private void MaterialsCreateView_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}