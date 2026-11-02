using System;
using System.Windows.Input;
using ProWeighSuitePlatforms.Security;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class MainViewModel : BaseViewModel, ICurrentUserContext
    {
        private BaseViewModel _currentViewModel = null!;

        public BaseViewModel CurrentViewModel
        {
            get => _currentViewModel;
            set => SetProperty(ref _currentViewModel, value);
        }

        private User? _currentUser;
        public User? CurrentUser
        {
            get => _currentUser;
            set => SetProperty(ref _currentUser, value);
        }

        public ICommand NavigateCommand { get; }

        public event EventHandler? LoginSuccessful;

        public MainViewModel()
        {
            var login = new LoginViewModel(this);
            CurrentViewModel = login;

            NavigateCommand = new RelayCommand(o =>
            {
                if (o is string s)
                {
                    switch (s)
                    {
                        case "Dashboard": CurrentViewModel = new DashboardViewModel(this, CurrentUser); break;
                        case "Settings": CurrentViewModel = new SettingsViewModel(this); break;
                        case "Users": CurrentViewModel = new UserManagementViewModel(this); break;
                        case "Reports": CurrentViewModel = new ReportsViewModel(this); break;
                        case "Login": CurrentViewModel = new LoginViewModel(this); break;
                    }
                }
            });
        }

        public void NavigateTo(BaseViewModel vm)
        {
            CurrentViewModel = vm;

            // If navigating to ShellViewModel, it means login was successful
            if (vm is ShellViewModel)
            {
                LoginSuccessful?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
