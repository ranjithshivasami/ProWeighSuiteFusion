using System.Windows.Input;
using System.Collections.ObjectModel;
using MaterialDesignThemes.Wpf;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class MenuItemViewModel : BaseViewModel
    {
        public string Title { get; set; } = string.Empty;
        public PackIconKind Icon { get; set; }
        public ICommand? Command { get; set; }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }
    }
}
