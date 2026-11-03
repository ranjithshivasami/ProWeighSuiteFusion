using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;
using ProWeighSuitePlatforms.Data;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class CategoryCreateViewModel : BaseViewModel
    {
        private readonly CategoryViewModel _parent;
        private readonly CategoryService _categoryService;
        private readonly AuditService _auditService;
        private readonly Category? _existingCategory;
        private readonly string _currentUsername;

        public bool IsUpdating => _existingCategory != null;

        public string Title => IsUpdating ? "Update Category" : "Create Category";

        private string _createName = string.Empty;
        public string CreateName
        {
            get => _createName;
            set
            {
                if (SetProperty(ref _createName, value))
                {
                    ValidateName();
                }
            }
        }

        private string _createDescription = string.Empty;
        public string CreateDescription
        {
            get => _createDescription;
            set => SetProperty(ref _createDescription, value);
        }

        private bool _createStatus = true;
        public bool CreateStatus
        {
            get => _createStatus;
            set => SetProperty(ref _createStatus, value);
        }

        private string? _nameError;
        public string? NameError
        {
            get => _nameError;
            set => SetProperty(ref _nameError, value);
        }

        public ICommand CreateCommand { get; }
        public ICommand UpdateCommand { get; }
        public ICommand CancelCreateCommand { get; }

        public CategoryCreateViewModel(CategoryViewModel parent, Category? category)
        {
            _parent = parent;
            _existingCategory = category;
            _auditService = new AuditService();
            
            // Get current username from parent
            _currentUsername = parent.GetCurrentUsername();

            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuiteFusion", "proweigh_suite_fusion.db");
            _categoryService = new CategoryService(new CategoryRepository(dbPath));

            if (category != null)
            {
                CreateName = category.Name;
                CreateDescription = category.Description;
                CreateStatus = category.Status;
            }

            CreateCommand = new RelayCommand(async _ => await CreateCategoryAsync(), _ => CanCreate());
            UpdateCommand = new RelayCommand(async _ => await UpdateCategoryAsync(), _ => CanCreate());
            CancelCreateCommand = new RelayCommand(async _ => await _parent.CancelCreate());
        }

        private bool CanCreate()
        {
            var name = CreateName.Trim();
            return !string.IsNullOrWhiteSpace(name) &&
                   name.Length >= 2 &&
                   name.Length <= 100 &&
                   string.IsNullOrEmpty(NameError);
        }

        private void ValidateName()
        {
            var name = CreateName.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                NameError = "Category name cannot be empty";
            }
            else if (name.Length < 2)
            {
                NameError = "Category name must be at least 2 characters";
            }
            else if (name.Length > 100)
            {
                NameError = "Category name must be less than 100 characters";
            }
            else
            {
                // Check if name is already in use by another category
                var excludeId = _existingCategory?.Id;
                var nameExists = _categoryService.NameExistsAsync(name, excludeId).Result;
                
                if (nameExists)
                {
                    NameError = $"Category name '{name}' already exists";
                }
                else
                {
                    NameError = null;
                }
            }
        }

        private async System.Threading.Tasks.Task CreateCategoryAsync()
        {
            try
            {
                // Final validation before saving
                ValidateName();

                if (!string.IsNullOrEmpty(NameError))
                {
                    MessageBox.Show("Please fix validation errors before saving.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Double-check name uniqueness
                var nameExists = await _categoryService.NameExistsAsync(CreateName.Trim(), null);
                if (nameExists)
                {
                    NameError = $"Category name '{CreateName}' already exists";
                    MessageBox.Show($"Category name '{CreateName}' already exists. Please use a different name.", 
                        "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var category = new Category
                {
                    Name = CreateName,
                    Description = CreateDescription,
                    Status = CreateStatus,
                    CreatedAt = DateTime.Now
                };

                await _categoryService.AddAsync(category);
                
                // Log category creation
                await _auditService.LogAsync(new AuditEntry
                {
                    Action = "Category_Created",
                    Category = AuditCategory.Configuration,
                    Username = _currentUsername,
                    EntityType = "Category",
                    EntityId = category.Id.ToString(),
                    Details = $"Category created: {category.Name}",
                    NewValue = $"Name={category.Name}, Status={category.Status}",
                    Severity = AuditSeverity.Info
                });
                
                MessageBox.Show("Category created successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                await _parent.CancelCreate();
            }
            catch (Exception ex)
            {
                // Log error
                await _auditService.LogErrorAsync(_currentUsername, "Create category", ex);
                
                MessageBox.Show($"Error creating category: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task UpdateCategoryAsync()
        {
            if (_existingCategory == null) return;

            try
            {
                // Final validation before updating
                ValidateName();

                if (!string.IsNullOrEmpty(NameError))
                {
                    MessageBox.Show("Please fix validation errors before updating.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Double-check name uniqueness (excluding current category)
                var nameExists = await _categoryService.NameExistsAsync(CreateName.Trim(), _existingCategory.Id);
                if (nameExists)
                {
                    NameError = $"Category name '{CreateName}' already exists";
                    MessageBox.Show($"Category name '{CreateName}' already exists. Please use a different name.", 
                        "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Store old values for audit
                var oldName = _existingCategory.Name;
                var oldDescription = _existingCategory.Description;
                var oldStatus = _existingCategory.Status;
                
                var category = new Category
                {
                    Id = _existingCategory.Id,
                    Name = CreateName,
                    Description = CreateDescription,
                    Status = CreateStatus,
                    CreatedAt = _existingCategory.CreatedAt
                };

                await _categoryService.UpdateAsync(category);
                
                // Log category update
                await _auditService.LogAsync(new AuditEntry
                {
                    Action = "Category_Updated",
                    Category = AuditCategory.Configuration,
                    Username = _currentUsername,
                    EntityType = "Category",
                    EntityId = category.Id.ToString(),
                    Details = $"Category updated: {category.Name}",
                    OldValue = $"Name={oldName}, Status={oldStatus}",
                    NewValue = $"Name={category.Name}, Status={category.Status}",
                    Severity = AuditSeverity.Info
                });
                
                MessageBox.Show("Category updated successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                await _parent.CancelCreate();
            }
            catch (Exception ex)
            {
                // Log error
                await _auditService.LogErrorAsync(_currentUsername, "Update category", ex);
                
                MessageBox.Show($"Error updating category: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
