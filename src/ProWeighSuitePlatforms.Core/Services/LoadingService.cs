using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Service for showing/hiding loading indicator during navigation
    /// </summary>
    public class LoadingService : INotifyPropertyChanged
    {
        private static LoadingService? _instance;
        public static LoadingService Instance => _instance ??= new LoadingService();

        private bool _isLoading;
        private string _loadingMessage = "Loading...";

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LoadingMessage
        {
            get => _loadingMessage;
            set
            {
                if (_loadingMessage != value)
                {
                    _loadingMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        private LoadingService() { }

        public void Show(string message = "Loading...")
        {
            LoadingMessage = message;
            IsLoading = true;
        }

        public void Hide()
        {
            IsLoading = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
