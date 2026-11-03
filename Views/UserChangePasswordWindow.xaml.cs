using System;
using System.Threading.Tasks;
using System.Windows;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.Views
{
    public partial class UserChangePasswordWindow : Window
    {
        private readonly User _user;
        private readonly User _currentUser;
        private readonly IUserService _userService;
        private readonly AuditService _auditService;
        private bool _isSaving;

        public string WindowTitle => $"Change Password � {_user.Username}";

        public string Username => _user.Username;

        public UserChangePasswordWindow(User user, User currentUser)
        {
            _user = user;
            _currentUser = currentUser;

            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuiteFusion", "proweigh_suite_fusion.db");

            _userService = new UserService(new UserRepository(dbPath));
            _auditService = new AuditService();

            InitializeComponent();
            DataContext = this;
        }

        public bool IsSaving
        {
            get => _isSaving;
            set => _isSaving = value;
        }

        private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            // Password is read directly from the PasswordBox in Save_Click
        }

        private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            // Password is read directly from the PasswordBox in Save_Click
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_isSaving) return;

            // Final server-side role guard � prevents bypass via direct window instantiation
            bool allowed = _user.Role == UserRole.SuperAdmin
                ? _currentUser.Role == UserRole.SuperAdmin
                : _currentUser.Role == UserRole.SuperAdmin || _currentUser.Role == UserRole.Admin;

            if (!allowed)
            {
                await _auditService.LogSecurityViolationAsync(
                    _currentUser.Username,
                    $"Change password for {_user.Username} ({_user.Role})",
                    "Insufficient role privileges");

                MessageBox.Show(
                    "Only a Super Admin can change another Super Admin's password.",
                    "Access Denied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var newPassword = NewPasswordBox.Password;
            var confirmPassword = ConfirmPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                MessageBox.Show("Please enter a new password.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (newPassword.Length < 4)
            {
                MessageBox.Show("Password must be at least 4 characters long!",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (newPassword != confirmPassword)
            {
                MessageBox.Show("Passwords do not match!", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isSaving = true;

            try
            {
                var success = await _userService.ChangePasswordAsync(_user.Id, newPassword);

                if (success)
                {
                    await _auditService.LogPasswordChangedByAdminAsync(_currentUser.Username, _user.Username);
                    MessageBox.Show($"Password changed successfully for user '{_user.Username}'!",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show("Failed to change password. User not found.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error changing password: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isSaving = false;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
