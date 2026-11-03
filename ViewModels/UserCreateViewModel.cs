using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class UserCreateViewModel : BaseViewModel
    {
        private readonly UserManagementViewModel _parent;
        private readonly IUserService _userService;
        private readonly AuditService _auditService;
        private readonly User? _editingUser;
        private readonly string _currentUsername;

        public string Title => _editingUser == null ? "Create New User" : $"Edit User: {_editingUser.Username}";
        public bool IsEditMode => _editingUser != null;

        public User? CurrentUser => _parent.CurrentUser;
        public bool CanChangeRole => HasPermission(CurrentUser, Permission.ChangeUserRole);

        private string _username = string.Empty;
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        private string _email = string.Empty;
        public string Email
        {
            get => _email;
            set => SetProperty(ref _email, value);
        }

        private string _password = string.Empty;
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        private string _confirmPassword = string.Empty;
        public string ConfirmPassword
        {
            get => _confirmPassword;
            set => SetProperty(ref _confirmPassword, value);
        }

        private UserRole _selectedRole = UserRole.Operator;
        public UserRole SelectedRole
        {
            get => _selectedRole;
            set => SetProperty(ref _selectedRole, value);
        }

        public IEnumerable<UserRole> AvailableRoles => Enum.GetValues(typeof(UserRole)).Cast<UserRole>();

        private bool _isSaving;
        public bool IsSaving
        {
            get => _isSaving;
            set => SetProperty(ref _isSaving, value);
        }

        public string SubmitButtonText => IsEditMode ? "Update User" : "Create User";

        public ICommand SubmitCommand { get; }
        public ICommand CancelCommand { get; }

        public UserCreateViewModel(UserManagementViewModel parent, User? userToEdit)
        {
            _parent = parent;
            _editingUser = userToEdit;
            _auditService = new AuditService();
            
            // Get current username for audit logging
            _currentUsername = parent.CurrentUser?.Username ?? "Unknown";

            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuiteFusion", "proweigh_suite_fusion.db");

            _userService = new UserService(new UserRepository(dbPath));

            SubmitCommand = new RelayCommand(async _ => await SaveAsync(), _ => CanSave());
            CancelCommand = new RelayCommand(async _ => await _parent.CancelCreate());

            if (_editingUser != null)
            {
                Username = _editingUser.Username;
                Email = _editingUser.Email;
                SelectedRole = _editingUser.Role;
            }
            else
            {
                SelectedRole = UserRole.Operator;
            }
        }

        private bool CanSave()
        {
            if (IsSaving) return false;
            if (string.IsNullOrWhiteSpace(Username)) return false;
            if (string.IsNullOrWhiteSpace(Email)) return false;

            if (!IsEditMode)
            {
                if (string.IsNullOrWhiteSpace(Password)) return false;
                if (string.IsNullOrWhiteSpace(ConfirmPassword)) return false;
                if (Password != ConfirmPassword) return false;
            }

            return true;
        }

        private async Task SaveAsync()
        {
            if (IsSaving) return;

            IsSaving = true;

            try
            {
                if (!IsEditMode)
                {
                    if (Password != ConfirmPassword)
                    {
                        MessageBox.Show("Passwords do not match!", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (Password.Length < 4)
                    {
                        MessageBox.Show("Password must be at least 4 characters long!", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var newUser = new User
                    {
                        Username = Username.Trim(),
                        Email = Email.Trim(),
                        Role = SelectedRole
                    };

                    await _userService.CreateUserAsync(newUser, Password);
                    
                    // Log user creation
                    await _auditService.LogUserCreatedAsync(_currentUsername, newUser);

                    MessageBox.Show($"User '{Username}' created successfully!",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    if (!CanChangeRole && SelectedRole != _editingUser!.Role)
                    {
                        // Log unauthorized role change attempt
                        await _auditService.LogSecurityViolationAsync(
                            _currentUsername,
                            $"Change role for user {_editingUser.Username}",
                            "Insufficient permissions");
                        
                        MessageBox.Show("You do not have permission to change user roles!",
                            "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Store old values for audit
                    var oldUser = new User
                    {
                        Id = _editingUser!.Id,
                        Username = _editingUser.Username,
                        Email = _editingUser.Email,
                        Role = _editingUser.Role
                    };
                    
                    _editingUser!.Username = Username.Trim();
                    _editingUser.Email = Email.Trim();
                    _editingUser.Role = SelectedRole;

                    await _userService.UpdateUserAsync(_editingUser);
                    
                    // Log user update
                    await _auditService.LogUserUpdatedAsync(_currentUsername, oldUser, _editingUser);

                    MessageBox.Show($"User '{Username}' updated successfully!",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                await _parent.CancelCreate();
            }
            catch (Exception ex)
            {
                // Log error
                await _auditService.LogErrorAsync(_currentUsername, "Save user", ex);
                
                MessageBox.Show($"Error saving user: {ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSaving = false;
            }
        }
    }
}
