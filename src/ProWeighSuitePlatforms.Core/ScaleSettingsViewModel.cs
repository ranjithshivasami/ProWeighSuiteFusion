using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Core.Security;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class ScaleSettingsViewModel : BaseViewModel
    {
        private readonly ICurrentUserContext _main;
        private readonly ScaleSettingsService _scaleService;

        public string Title => IsCreating && CreateViewModel != null ? CreateViewModel.Title : "Scale Settings";

        // Permission-based properties
        public bool CanCreateScaleSettings => HasPermission(_main.CurrentUser, Permission.CreateScaleSettings);
        public bool CanEditScaleSettings => HasPermission(_main.CurrentUser, Permission.EditScaleSettings);
        public bool CanDeleteScaleSettings => HasPermission(_main.CurrentUser, Permission.DeleteScaleSettings);

        private bool _isCreating;
        public bool IsCreating
        {
            get => _isCreating;
            set
            {
                if (SetProperty(ref _isCreating, value))
                    RaisePropertyChanged(nameof(Title));
            }
        }

        private ScaleSettingsCreateViewModel? _createViewModel;
        public ScaleSettingsCreateViewModel? CreateViewModel
        {
            get => _createViewModel;
            set
            {
                if (SetProperty(ref _createViewModel, value))
                    RaisePropertyChanged(nameof(Title));
            }
        }

        public ICommand StartCreateCommand { get; }
        public ICommand StartUpdateRowCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand LastPageCommand { get; }
        public ICommand GoToPageCommand { get; }
        public ObservableCollection<PageNumberItem> PageNumbers { get; } = new();

        public ObservableCollection<ScaleSettings> PagedScales { get; } = new();

        private ScaleSettings? _selected;
        public ScaleSettings? Selected
        {
            get => _selected;
            set => SetProperty(ref _selected, value);
        }

        private string _filterText = string.Empty;
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    CurrentPage = 1;
                    UpdatePagedData();
                }
            }
        }

        private int _pageSize = 10;
        public int PageSize
        {
            get => _pageSize;
            set
            {
                if (SetProperty(ref _pageSize, value))
                {
                    CurrentPage = 1;
                    UpdatePagedData();
                }
            }
        }

        private int _currentPage = 1;
        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    UpdatePagedData();
                    RaisePropertyChanged(nameof(TotalPages));
                    RaisePropertyChanged(nameof(PageInfo));
                }
            }
        }

        public int TotalPages => (int)Math.Ceiling((double)FilteredScales.Count / PageSize);

        private int _totalRecords;
        public int TotalRecords
        {
            get => _totalRecords;
            private set => SetProperty(ref _totalRecords, value);
        }

        public string PageInfo => $"Page {CurrentPage} of {TotalPages}  ({TotalRecords} records)";

        // Basic-Datatables style footer: "Showing 1 to 10 of 14 entries"
        public string ShowingText
        {
            get
            {
                if (TotalRecords == 0) return "Showing 0 to 0 of 0 entries";
                var from = (CurrentPage - 1) * PageSize + 1;
                var to = Math.Min(CurrentPage * PageSize, TotalRecords);
                return $"Showing {from} to {to} of {TotalRecords} entries";
            }
        }

        private ObservableCollection<ScaleSettings> _allScales = new();
        private ObservableCollection<ScaleSettings> FilteredScales
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FilterText))
                    return _allScales;

                var filtered = _allScales.Where(s =>
                    s.ScaleName.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                    s.PortName.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

                return new ObservableCollection<ScaleSettings>(filtered);
            }
        }

        public ScaleSettingsViewModel(ICurrentUserContext main)
        {
            _main = main;

            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuitePlatforms", "proweighsuiteplatform.db");
            _scaleService = new ScaleSettingsService(new ScaleSettingsRepository(dbPath));

            StartCreateCommand = new RelayCommand(_ => StartCreate(), _ => CanCreateScaleSettings);
            StartUpdateRowCommand = new RelayCommand(param => StartUpdate(param as ScaleSettings), _ => CanEditScaleSettings);
            DeleteCommand = new RelayCommand(async param => await DeleteScaleAsync(param as ScaleSettings), _ => CanDeleteScaleSettings);
            PrevPageCommand = new RelayCommand(_ => PrevPage(), _ => CurrentPage > 1);
            NextPageCommand = new RelayCommand(_ => NextPage(), _ => CurrentPage < TotalPages);
            FirstPageCommand = new RelayCommand(_ => { if (CurrentPage != 1) CurrentPage = 1; });
            LastPageCommand = new RelayCommand(_ => { if (CurrentPage != TotalPages) CurrentPage = TotalPages; });
            GoToPageCommand = new RelayCommand(param =>
            {
                if (param is int page && page >= 1 && page <= Math.Max(1, TotalPages) && page != CurrentPage)
                    CurrentPage = page;
            });

            _ = LoadScalesAsync();
        }

        public void StartCreate()
        {
            CreateViewModel = new ScaleSettingsCreateViewModel(this, null);
            IsCreating = true;
        }

        public void StartUpdate(ScaleSettings? scale)
        {
            if (scale == null) return;
            Selected = scale;
            CreateViewModel = new ScaleSettingsCreateViewModel(this, scale);
            IsCreating = true;
        }

        public async Task CancelCreate()
        {
            IsCreating = false;
            Selected = null;
            CreateViewModel = null;
            await LoadScalesAsync();
        }

        private async Task LoadScalesAsync()
        {
            try
            {
                // Don't initialize here - already done at app startup
                var scales = await _scaleService.GetAllAsync();

                _allScales.Clear();
                foreach (var scale in scales)
                {
                    _allScales.Add(scale);
                }

                UpdatePagedData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading scales: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdatePagedData()
        {
            var filtered = FilteredScales;
            TotalRecords = filtered.Count;
            if (CurrentPage < 1) _currentPage = 1;
            var pages = Math.Max(1, (int)Math.Ceiling((double)filtered.Count / Math.Max(1, PageSize)));
            if (_currentPage > pages) _currentPage = pages;
            var paged = filtered
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            PagedScales.Clear();
            foreach (var scale in paged)
            {
                PagedScales.Add(scale);
            }

            RefreshPageNumbers();
            RaisePropertyChanged(nameof(CurrentPage));
            RaisePropertyChanged(nameof(TotalPages));
            RaisePropertyChanged(nameof(PageInfo));
            RaisePropertyChanged(nameof(ShowingText));
        }

        private void RefreshPageNumbers()
        {
            PageNumbers.Clear();
            var total = Math.Max(1, TotalPages);
            // Sliding window of max 5 page buttons (Basic Datatables style: Previous 1 2 Next)
            var start = Math.Max(1, Math.Min(CurrentPage - 2, total - 4));
            var end = Math.Min(total, start + 4);
            start = Math.Max(1, end - 4);
            for (var i = start; i <= end; i++)
                PageNumbers.Add(new PageNumberItem { Number = i, IsCurrent = i == CurrentPage });
        }

        public sealed class PageNumberItem
        {
            public int Number { get; set; }
            public bool IsCurrent { get; set; }
        }

        private void PrevPage()
        {
            if (CurrentPage > 1)
                CurrentPage--;
        }

        private void NextPage()
        {
            if (CurrentPage < TotalPages)
                CurrentPage++;
        }

        private async Task DeleteScaleAsync(ScaleSettings? scale)
        {
            if (scale == null) return;

            try
            {
                CheckPermissionOrThrow(_main.CurrentUser, Permission.DeleteScaleSettings);
                
                var result = MessageBox.Show(
                    $"Are you sure you want to delete scale '{scale.ScaleName}'?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await _scaleService.DeleteAsync(scale.Id);
                    await LoadScalesAsync();
                    MessageBox.Show("Scale deleted successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(
                    "You do not have permission to delete scale settings.\n\nPlease contact your administrator.",
                    "Access Denied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting scale: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
