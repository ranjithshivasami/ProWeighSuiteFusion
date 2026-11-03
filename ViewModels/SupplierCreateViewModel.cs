using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class SupplierCreateViewModel : BaseViewModel
    {
        private readonly SupplierViewModel _parent;
        private readonly SupplierService _service;
        private readonly AuditService _auditService;
        private readonly Supplier? _existing;
        private readonly string _username;

        public bool IsUpdating => _existing != null;

        public string Title => IsUpdating ? "Update Supplier" : "Create Supplier";

        public string[] SupplierTypes { get; } = SupplierOptions.SupplierTypes;
        public string[] BusinessCategories { get; } = SupplierOptions.BusinessCategories;
        public string[] LegalEntityTypes { get; } = SupplierOptions.LegalEntityTypes;

        private string _code = string.Empty;
        public string SupplierCode { get => _code; set { if (SetProperty(ref _code, value)) { ValidateCode(); RaisePropertyChanged(nameof(CanSave)); CommandManager.InvalidateRequerySuggested(); } } }
        private string? _codeError;
        public string? CodeError { get => _codeError; set => SetProperty(ref _codeError, value); }

        private string _name = string.Empty;
        public string SupplierName { get => _name; set { if (SetProperty(ref _name, value)) { ValidateName(); RaisePropertyChanged(nameof(CanSave)); CommandManager.InvalidateRequerySuggested(); } } }
        private string? _nameError;
        public string? NameError { get => _nameError; set => SetProperty(ref _nameError, value); }

        private string _displayName = string.Empty;
        public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }

        private string _supplierType = string.Empty;
        public string SupplierType { get => _supplierType; set { if (SetProperty(ref _supplierType, value)) { ValidateType(); RaisePropertyChanged(nameof(CanSave)); CommandManager.InvalidateRequerySuggested(); } } }
        private string? _typeError;
        public string? TypeError { get => _typeError; set => SetProperty(ref _typeError, value); }

        private string _businessCategory = string.Empty;
        public string BusinessCategory { get => _businessCategory; set => SetProperty(ref _businessCategory, value); }

        private bool _status = true;
        public bool Status { get => _status; set => SetProperty(ref _status, value); }

        private string _legalEntityType = string.Empty;
        public string LegalEntityType { get => _legalEntityType; set => SetProperty(ref _legalEntityType, value); }
        private string _regNo = string.Empty;
        public string RegistrationNumber { get => _regNo; set => SetProperty(ref _regNo, value); }

        private string _taxId = string.Empty;
        public string TaxId { get => _taxId; set { if (SetProperty(ref _taxId, value)) { ValidateTax(); RaisePropertyChanged(nameof(CanSave)); CommandManager.InvalidateRequerySuggested(); } } }
        private string? _taxError;
        public string? TaxError { get => _taxError; set => SetProperty(ref _taxError, value); }

        private string _website = string.Empty;
        public string Website { get => _website; set { if (SetProperty(ref _website, value)) { ValidateWebsite(); RaisePropertyChanged(nameof(CanSave)); CommandManager.InvalidateRequerySuggested(); } } }
        private string? _websiteError;
        public string? WebsiteError { get => _websiteError; set => SetProperty(ref _websiteError, value); }

        private string _contact = string.Empty;
        public string ContactPerson { get => _contact; set { if (SetProperty(ref _contact, value)) { ValidateContact(); RaisePropertyChanged(nameof(CanSave)); CommandManager.InvalidateRequerySuggested(); } } }
        private string? _contactError;
        public string? ContactError { get => _contactError; set => SetProperty(ref _contactError, value); }

        private string _designation = string.Empty;
        public string Designation { get => _designation; set => SetProperty(ref _designation, value); }

        private string _phone = string.Empty;
        public string Phone { get => _phone; set { if (SetProperty(ref _phone, value)) { ValidatePhone(); RaisePropertyChanged(nameof(CanSave)); CommandManager.InvalidateRequerySuggested(); } } }
        private string? _phoneError;
        public string? PhoneError { get => _phoneError; set => SetProperty(ref _phoneError, value); }

        private string _mobile = string.Empty;
        public string Mobile { get => _mobile; set => SetProperty(ref _mobile, value); }
        private string _email = string.Empty;
        public string Email { get => _email; set { if (SetProperty(ref _email, value)) { ValidateEmail(); RaisePropertyChanged(nameof(CanSave)); CommandManager.InvalidateRequerySuggested(); } } }
        private string? _emailError;
        public string? EmailError { get => _emailError; set => SetProperty(ref _emailError, value); }

        // Only Supplier Code and Supplier Name are required; all other fields are optional.
        public bool CanSave => string.IsNullOrEmpty(CodeError) && string.IsNullOrEmpty(NameError)
            && string.IsNullOrEmpty(TypeError) && string.IsNullOrEmpty(TaxError)
            && string.IsNullOrEmpty(ContactError) && string.IsNullOrEmpty(PhoneError)
            && string.IsNullOrEmpty(EmailError) && string.IsNullOrEmpty(WebsiteError)
            && !string.IsNullOrWhiteSpace(_code) && !string.IsNullOrWhiteSpace(_name);

        public ICommand SaveCommand { get; }
        public ICommand UpdateCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand AutoGenerateCodeCommand { get; }

        public SupplierCreateViewModel(SupplierViewModel parent, Supplier? existing)
        {
            _parent = parent;
            _existing = existing;
            _auditService = new AuditService();
            _username = parent.GetCurrentUsername();
            var dbPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProWeighSuiteFusion", "proweigh_suite_fusion.db");
            _service = new SupplierService(new SupplierRepository(dbPath));

            if (existing != null)
            {
                _code = existing.SupplierCode;
                _name = existing.SupplierName;
                _displayName = existing.DisplayName;
                _supplierType = existing.SupplierType;
                _businessCategory = existing.BusinessCategory;
                _status = existing.Status;
                _legalEntityType = existing.LegalEntityType;
                _regNo = existing.RegistrationNumber;
                _taxId = existing.TaxId;
                _website = existing.Website;
                _contact = existing.ContactPerson;
                _designation = existing.Designation;
                _phone = existing.Phone;
                _mobile = existing.Mobile;
                _email = existing.Email;
            }
            else
            {
                _ = AutoGenerateCodeAsync();
            }

            SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => CanSave);
            UpdateCommand = new RelayCommand(async _ => await UpdateAsync(), _ => CanSave);
            CancelCommand = new RelayCommand(async _ => await _parent.CancelCreate());
            AutoGenerateCodeCommand = new RelayCommand(async _ => await AutoGenerateCodeAsync());
        }

        private async Task AutoGenerateCodeAsync()
        {
            try
            {
                if (_existing != null) return;
                if (!string.IsNullOrWhiteSpace(SupplierCode)) return;
                SupplierCode = await _service.GenerateNextCodeAsync();
            }
            catch { /* ignore */ }
        }

        private void ValidateCode()
        {
            var v = SupplierCode?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(v)) CodeError = "Supplier code is required";
            else if (v.Length > 30) CodeError = "Max 30 characters";
            else
            {
                var exists = _service.CodeExistsAsync(v, _existing?.Id).GetAwaiter().GetResult();
                CodeError = exists ? $"Code '{v}' already exists" : null;
            }
        }

        private void ValidateName()
        {
            var v = SupplierName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(v)) NameError = "Supplier name is required";
            else if (v.Length < 2) NameError = "Min 2 characters";
            else if (v.Length > 150) NameError = "Max 150 characters";
            else NameError = null;
        }

        private void ValidateType()
        {
            // Optional
            TypeError = null;
        }

        private void ValidateTax()
        {
            // Optional
            TaxError = null;
        }

        private void ValidateWebsite()
        {
            var v = Website?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(v)) { WebsiteError = null; return; }
            WebsiteError = Uri.TryCreate(v, UriKind.Absolute, out var u)
                && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) ? null : "Enter valid URL (https://...)";
        }

        private void ValidateContact()
        {
            // Optional
            ContactError = null;
        }

        private void ValidatePhone()
        {
            // Optional: only validate format when a value is entered
            var v = Phone?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(v)) { PhoneError = null; return; }
            var digits = new string(v.Where(char.IsDigit).ToArray());
            PhoneError = digits.Length < 7 ? "Enter valid phone with country code" : null;
        }

        private void ValidateEmail()
        {
            // Optional: only validate format when a value is entered
            var v = Email?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(v)) { EmailError = null; return; }
            EmailError = Regex.IsMatch(v, @"^[^@\s]+@[^@\s]+\.[^@\s]+$") ? null : "Enter valid email address";
        }

        private void ValidateAll()
        {
            ValidateCode(); ValidateName(); ValidateType(); ValidateTax(); ValidateWebsite(); ValidateContact(); ValidatePhone(); ValidateEmail();
            RaisePropertyChanged(nameof(CanSave));
        }

        private Supplier BuildEntity()
        {
            return new Supplier
            {
                Id = _existing?.Id ?? 0,
                SupplierCode = SupplierCode.Trim(),
                SupplierName = SupplierName.Trim(),
                DisplayName = DisplayName?.Trim() ?? string.Empty,
                SupplierType = SupplierType?.Trim() ?? string.Empty,
                BusinessCategory = BusinessCategory?.Trim() ?? string.Empty,
                Status = Status,
                LegalEntityType = LegalEntityType?.Trim() ?? string.Empty,
                RegistrationNumber = RegistrationNumber?.Trim() ?? string.Empty,
                TaxId = TaxId?.Trim() ?? string.Empty,
                Website = Website?.Trim() ?? string.Empty,
                ContactPerson = ContactPerson?.Trim() ?? string.Empty,
                Designation = Designation?.Trim() ?? string.Empty,
                Phone = Phone?.Trim() ?? string.Empty,
                Mobile = Mobile?.Trim() ?? string.Empty,
                Email = Email?.Trim() ?? string.Empty,
                CreatedAt = _existing?.CreatedAt ?? DateTime.Now
            };
        }

        private async Task SaveAsync()
        {
            ValidateAll();
            if (!CanSave) { MessageBox.Show("Please fix validation errors.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            try
            {
                var entity = BuildEntity();
                if (await _service.CodeExistsAsync(entity.SupplierCode, null))
                {
                    CodeError = $"Code '{entity.SupplierCode}' already exists";
                    MessageBox.Show(CodeError, "Duplicate Code", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                await _service.AddAsync(entity);
                await _auditService.LogAsync(new AuditEntry
                {
                    Action = "Supplier_Created",
                    Category = AuditCategory.Configuration,
                    Username = _username,
                    EntityType = "Supplier",
                    Details = $"Supplier created: {entity.SupplierCode} - {entity.SupplierName}",
                    NewValue = $"Code={entity.SupplierCode}, Type={entity.SupplierType}",
                    Severity = AuditSeverity.Info
                });
                MessageBox.Show("Supplier created successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                await _parent.CancelCreate();
            }
            catch (Exception ex)
            {
                await _auditService.LogErrorAsync(_username, "Create supplier", ex);
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task UpdateAsync()
        {
            if (_existing == null) return;
            ValidateAll();
            if (!CanSave) { MessageBox.Show("Please fix validation errors.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            try
            {
                var entity = BuildEntity();
                if (await _service.CodeExistsAsync(entity.SupplierCode, _existing.Id))
                {
                    CodeError = $"Code '{entity.SupplierCode}' already exists";
                    MessageBox.Show(CodeError, "Duplicate Code", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                await _service.UpdateAsync(entity);
                await _auditService.LogAsync(new AuditEntry
                {
                    Action = "Supplier_Updated",
                    Category = AuditCategory.Configuration,
                    Username = _username,
                    EntityType = "Supplier",
                    EntityId = entity.Id.ToString(),
                    Details = $"Supplier updated: {entity.SupplierCode} - {entity.SupplierName}",
                    Severity = AuditSeverity.Info
                });
                MessageBox.Show("Supplier updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                await _parent.CancelCreate();
            }
            catch (Exception ex)
            {
                await _auditService.LogErrorAsync(_username, "Update supplier", ex);
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
