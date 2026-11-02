using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class VariantCreateViewModel : BaseViewModel
    {
        private readonly VariantViewModel _parentViewModel;
        private readonly VariantService _variantService;
        private readonly AuditService _auditService;
        private readonly Variant? _editingVariant;
        private readonly string _currentUsername;

        public string Title => _editingVariant == null ? "Create New Variant" : "Update Variant";
        public string SubmitButtonText => _editingVariant == null ? "Create" : "Update";

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value))
                {
                    NameError = string.Empty;
                }
            }
        }

        private string _description = string.Empty;
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        private bool _active = true;
        public bool Active
        {
            get => _active;
            set => SetProperty(ref _active, value);
        }

        private string _nameError = string.Empty;
        public string NameError
        {
            get => _nameError;
            set => SetProperty(ref _nameError, value);
        }

        public ICommand SubmitCommand { get; }
        public ICommand CancelCommand { get; }

        public VariantCreateViewModel(VariantViewModel parentViewModel, Variant? editingVariant)
        {
            _parentViewModel = parentViewModel;
            _editingVariant = editingVariant;
            _auditService = new AuditService();
            
            // Get current username from parent
            _currentUsername = parentViewModel.GetCurrentUsername();

            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuitePlatforms", "proweighsuiteplatform.db");
            _variantService = new VariantService(new Data.VariantRepository(dbPath));

            if (_editingVariant != null)
            {
                Name = _editingVariant.Name;
                Description = _editingVariant.Description;
                Active = _editingVariant.Active;
            }

            SubmitCommand = new RelayCommand(async _ => await SubmitAsync());
            CancelCommand = new RelayCommand(async _ => await _parentViewModel.CancelCreate());
        }

        private async Task SubmitAsync()
        {
            if (!await ValidateAsync())
                return;

            try
            {
                if (_editingVariant == null)
                {
                    var variant = new Variant
                    {
                        Name = Name.Trim(),
                        Description = Description?.Trim() ?? string.Empty,
                        Active = Active,
                        CreatedAt = DateTime.Now
                    };

                    await _variantService.AddAsync(variant);
                    
                    // Log variant creation
                    await _auditService.LogAsync(new AuditEntry
                    {
                        Action = "Variant_Created",
                        Category = AuditCategory.Configuration,
                        Username = _currentUsername,
                        EntityType = "Variant",
                        EntityId = variant.Id.ToString(),
                        Details = $"Variant created: {variant.Name}",
                        NewValue = $"Name={variant.Name}, Active={variant.Active}",
                        Severity = AuditSeverity.Info
                    });
                    
                    MessageBox.Show("Variant created successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Store old values for audit
                    var oldName = _editingVariant.Name;
                    var oldDescription = _editingVariant.Description;
                    var oldActive = _editingVariant.Active;
                    
                    _editingVariant.Name = Name.Trim();
                    _editingVariant.Description = Description?.Trim() ?? string.Empty;
                    _editingVariant.Active = Active;

                    await _variantService.UpdateAsync(_editingVariant);
                    
                    // Log variant update
                    await _auditService.LogAsync(new AuditEntry
                    {
                        Action = "Variant_Updated",
                        Category = AuditCategory.Configuration,
                        Username = _currentUsername,
                        EntityType = "Variant",
                        EntityId = _editingVariant.Id.ToString(),
                        Details = $"Variant updated: {_editingVariant.Name}",
                        OldValue = $"Name={oldName}, Active={oldActive}",
                        NewValue = $"Name={_editingVariant.Name}, Active={_editingVariant.Active}",
                        Severity = AuditSeverity.Info
                    });
                    
                    MessageBox.Show("Variant updated successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }

                await _parentViewModel.CancelCreate();
            }
            catch (Exception ex)
            {
                // Log error
                await _auditService.LogErrorAsync(_currentUsername, "Save variant", ex);
                
                MessageBox.Show($"Error saving variant: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<bool> ValidateAsync()
        {
            NameError = string.Empty;

            if (string.IsNullOrWhiteSpace(Name))
            {
                NameError = "Name is required";
                return false;
            }

            if (Name.Trim().Length < 2)
            {
                NameError = "Name must be at least 2 characters";
                return false;
            }

            if (Name.Length > 100)
            {
                NameError = "Name cannot exceed 100 characters";
                return false;
            }

            var excludeId = _editingVariant?.Id;
            if (await _variantService.NameExistsAsync(Name.Trim(), excludeId))
            {
                NameError = "This name already exists. Please use a unique name";
                return false;
            }

            return true;
        }
    }
}
