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
using ProWeighSuitePlatforms.Views;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class UserManagementViewModel : BaseViewModel
    {
        private readonly ICurrentUserContext _main;
        private readonly IUserService _userService;
        private readonly AuditService _auditService;

        public ObservableCollection<User> Users { get; } = new();
        public ObservableCollection<User> PagedUsers { get; } = new();

        public string Title => IsCreating && CreateViewModel != null ? CreateViewModel.Title : "User Management";

        public User? CurrentUser => _main.CurrentUser;

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    CurrentPage = 1;
                    ApplyFilterAndPage();
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
                    ApplyFilterAndPage();
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
                    ApplyFilterAndPage();
                    RaisePropertyChanged(nameof(PageInfo));
                    RaisePropertyChanged(nameof(ShowingText));
                }
            }
        }

        private int _totalPages = 1;
        public int TotalPages
        {
            get => _totalPages;
            private set
            {
                if (SetProperty(ref _totalPages, value))
                    RaisePropertyChanged(nameof(PageInfo));
            }
        }

        private int _totalRecords;
        public int TotalRecords
        {
            get => _totalRecords;
            private set
            {
                if (SetProperty(ref _totalRecords, value))
                    RaisePropertyChanged(nameof(PageInfo));
            }
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

        private User? _selectedUser;
        public User? SelectedUser
        {
            get => _selectedUser;
            set => SetProperty(ref _selectedUser, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

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

        private UserCreateViewModel? _createViewModel;
        public UserCreateViewModel? CreateViewModel
        {
            get => _createViewModel;
            set
            {
                if (SetProperty(ref _createViewModel, value))
                    RaisePropertyChanged(nameof(Title));
            }
        }

        public bool CanCreateUser => HasPermission(CurrentUser, Permission.CreateUser);
        public bool CanEditUser => HasPermission(CurrentUser, Permission.EditUser);
        public bool CanDeleteUser => HasPermission(CurrentUser, Permission.DeleteUser);
        public bool CanChangeUserRole => HasPermission(CurrentUser, Permission.ChangeUserRole);
        public bool CanChangePassword => CurrentUser != null && (CurrentUser.Role == UserRole.SuperAdmin || CurrentUser.Role == UserRole.Admin);

        /// <summary>
        /// Returns true if the logged-in user is allowed to edit the target user's account.
        /// Rules:
        ///   - The EditUser permission must be granted.
        ///   - A SuperAdmin may only edit another SuperAdmin account.
        ///   - A non-SuperAdmin may only edit non-SuperAdmin accounts (never SuperAdmins).
        /// </summary>
        public bool CanEditUserFor(User? targetUser)
        {
            if (CurrentUser == null || targetUser == null) return false;
            if (!CanEditUser) return false;

            var currentIsSuperAdmin = CurrentUser.Role == UserRole.SuperAdmin;
            var targetIsSuperAdmin  = targetUser.Role == UserRole.SuperAdmin;
            return currentIsSuperAdmin == targetIsSuperAdmin;
        }

        /// <summary>
        /// Returns true if the logged-in user is allowed to change the target user's password.
        /// Rules:
        ///   - SuperAdmin password ? only another SuperAdmin can change it
        ///   - Admin / Operator password ? SuperAdmin or Admin can change it
        /// </summary>
        public bool CanChangePasswordFor(User? targetUser)
        {
            if (CurrentUser == null || targetUser == null) return false;

            // SuperAdmin password can only be changed by another SuperAdmin
            if (targetUser.Role == UserRole.SuperAdmin)
                return CurrentUser.Role == UserRole.SuperAdmin;

            // Admin/Operator passwords can be changed by SuperAdmin or Admin
            return CurrentUser.Role == UserRole.SuperAdmin || CurrentUser.Role == UserRole.Admin;
        }

        public ICommand CreateUserCommand { get; }
        public ICommand EditUserCommand { get; }
        public ICommand DeleteUserCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ChangePasswordCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand LastPageCommand { get; }
        public ICommand GoToPageCommand { get; }
        public ObservableCollection<PageNumberItem> PageNumbers { get; } = new();

        private ObservableCollection<User> _allUsers = new();

        public UserManagementViewModel(ICurrentUserContext main)
        {
            _main = main;
            _auditService = new AuditService();

            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuitePlatforms", "proweighsuiteplatform.db");

            _userService = new UserService(new UserRepository(dbPath));

            CreateUserCommand = new RelayCommand(_ => StartCreate(), _ => CanCreateUser && !IsLoading && !IsCreating);
            EditUserCommand = new RelayCommand(param => StartEdit(param as User), _ => CanEditUser && !IsLoading && !IsCreating);
            DeleteUserCommand = new RelayCommand(async param => await DeleteUserAsync(param as User), _ => CanDeleteUser && !IsLoading && !IsCreating);
            RefreshCommand = new RelayCommand(async _ => await LoadUsersAsync(), _ => !IsLoading && !IsCreating);
            ChangePasswordCommand = new RelayCommand(param => ChangePassword(param as User), _ => CanChangePassword && !IsLoading && !IsCreating);
            NextPageCommand = new RelayCommand(_ => { if (CurrentPage < TotalPages) CurrentPage++; });
            PrevPageCommand = new RelayCommand(_ => { if (CurrentPage > 1) CurrentPage--; });
            FirstPageCommand = new RelayCommand(_ => { if (CurrentPage != 1) CurrentPage = 1; });
            LastPageCommand = new RelayCommand(_ => { if (CurrentPage != TotalPages) CurrentPage = TotalPages; });
            GoToPageCommand = new RelayCommand(param =>
            {
                int page = param is int i ? i : (int.TryParse(param?.ToString(), out var p) ? p : -1);
                if (page >= 1 && page <= Math.Max(1, TotalPages) && page != CurrentPage)
                    CurrentPage = page;
            });

            System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
            {
                await InitializeAsync();
            });
        }

        private async Task InitializeAsync()
        {
            if (IsLoading) return;
            
            IsLoading = true;

            try
            {
                await _userService.InitializeAsync();
                await LoadUsersAsync();
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error initializing user management: {ex.Message}",
                        "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadUsersAsync()
        {
            try
            {
                var users = await _userService.GetAllUsersAsync();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Users.Clear();
                    foreach (var user in users)
                    {
                        Users.Add(user);
                    }

                    CurrentPage = 1;
                    ApplyFilterAndPage();
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error loading users: {ex.Message}",
                        "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private void ApplyFilterAndPage()
        {
            var query = Users.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var searchText = SearchText.ToLowerInvariant();
                query = query.Where(u =>
                    u.Username.ToLowerInvariant().Contains(searchText) ||
                    (u.Email ?? string.Empty).ToLowerInvariant().Contains(searchText) ||
                    u.Role.ToString().ToLowerInvariant().Contains(searchText));
            }

            var total = query.Count();
            TotalRecords = total;
            TotalPages = total == 0 ? 1 : (int)System.Math.Ceiling(total / (double)PageSize);
            if (CurrentPage > TotalPages) CurrentPage = TotalPages;
            RaisePropertyChanged(nameof(PageInfo));
            RaisePropertyChanged(nameof(ShowingText));
            var page = query.Skip((CurrentPage - 1) * PageSize).Take(PageSize).ToList();

            PagedUsers.Clear();
            var index = (CurrentPage - 1) * PageSize + 1;
            foreach (var user in page)
            {
                PagedUsers.Add(user);
            }

            RefreshPageNumbers();
            RaisePropertyChanged(nameof(ShowingText));

            if (PagedUsers.Count > 0)
            {
                SelectedUser = PagedUsers[0];
            }
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

        public void StartCreate()
        {
            CreateViewModel = new UserCreateViewModel(this, null);
            IsCreating = true;
        }

        public void StartEdit(User? user)
        {
            if (user == null) return;

            // Enforce role-based restriction before opening the edit form
            if (!CanEditUserFor(user))
            {
                var reason = user.Role == UserRole.SuperAdmin
                    ? "Only a Super Admin can edit another Super Admin's account."
                    : "Super Admins cannot edit regular user accounts.";

                MessageBox.Show(
                    reason,
                    "Access Denied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                _ = _auditService.LogSecurityViolationAsync(
                    CurrentUser?.Username ?? "Unknown",
                    $"Edit user {user.Username} ({user.Role})",
                    "Insufficient role privileges");

                return;
            }

            SelectedUser = user;
            CreateViewModel = new UserCreateViewModel(this, user);
            IsCreating = true;
        }

        public async Task CancelCreate()
        {
            IsCreating = false;
            SelectedUser = null;
            CreateViewModel = null;
            await LoadUsersAsync();
        }

        private async Task DeleteUserAsync(User? user)
        {
            if (user == null) return;

            SelectedUser = user;

            var result = MessageBox.Show(
                $"Are you sure you want to delete user '{user.Username}'?\n\n" +
                "This action cannot be undone.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                IsLoading = true;

                try
                {
                    // Require password confirmation before deleting a user
                    var owner = Application.Current?.MainWindow;
                    var username = _main.CurrentUser?.Username ?? string.Empty;
                    var passwordConfirmed = await PasswordPromptWindow.ConfirmPasswordAsync(owner ?? new Window(), username);
                     if (!passwordConfirmed)
                     {
                         // Log security violation
                         await _auditService.LogSecurityViolationAsync(
                             username,
                             $"Delete user {user.Username}",
                             "Password verification failed");
                         
                         MessageBox.Show(
                             "Password incorrect or cancelled.",
                             "Access Denied",
                             MessageBoxButton.OK,
                             MessageBoxImage.Warning);
                         return;
                     }
                    
                    await _userService.DeleteUserAsync(user.Id);
                    
                    // Log user deletion
                    await _auditService.LogUserDeletedAsync(username, user);

                    MessageBox.Show($"User '{user.Username}' deleted successfully!",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    await LoadUsersAsync();
                    SelectedUser = null;
                }
                catch (Exception ex)
                {
                    // Log error
                    await _auditService.LogErrorAsync(
                        _main.CurrentUser?.Username ?? "Unknown",
                        "Delete user",
                        ex);
                    
                    MessageBox.Show($"Error deleting user: {ex.Message}",
                        "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        private void ChangePassword(User? user)
        {
            if (user == null) return;

            // Enforce role-based restriction before opening the window
            if (!CanChangePasswordFor(user))
            {
                var reason = user.Role == UserRole.SuperAdmin
                    ? "Only a Super Admin can change another Super Admin's password."
                    : "You do not have permission to change this user's password.";

                MessageBox.Show(
                    reason,
                    "Access Denied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                _ = _auditService.LogSecurityViolationAsync(
                    CurrentUser?.Username ?? "Unknown",
                    $"Change password for {user.Username} ({user.Role})",
                    "Insufficient role privileges");

                return;
            }

            SelectedUser = user;
            var owner = Application.Current?.MainWindow;
            var window = new UserChangePasswordWindow(user, CurrentUser!);
            if (owner != null)
                window.Owner = owner;
            window.ShowDialog();
        }
    }
}