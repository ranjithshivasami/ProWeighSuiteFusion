using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProWeighSuitePlatforms.Security;
using ProWeighSuitePlatforms.Data;
using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.ViewModels
{
    public class CompanySettingsViewModel : BaseViewModel
    {
        private readonly ICurrentUserContext _main;
        private readonly CompanySettingsService _companyService;
        private readonly AuditService _auditService;

        public string Title => "Company Settings";

        // Permission-based properties
        public bool CanEditCompanySettings => HasPermission(_main.CurrentUser, Permission.EditCompanySettings);

        private string _companyName = string.Empty;
        public string CompanyName
        {
            get => _companyName;
            set
            {
                if (SetProperty(ref _companyName, value))
                {
                    ValidateCompanyName();
                }
            }
        }

        private string _materialCodePrefix = string.Empty;
        public string MaterialCodePrefix
        {
            get => _materialCodePrefix;
            set
            {
                if (SetProperty(ref _materialCodePrefix, value))
                {
                    ValidateMaterialCodePrefix();
                }
            }
        }

        private string _address = string.Empty;
        public string Address
        {
            get => _address;
            set
            {
                if (SetProperty(ref _address, value))
                {
                    ValidateAddress();
                }
            }
        }

        private string _gstNo = string.Empty;
        public string GSTNo
        {
            get => _gstNo;
            set
            {
                if (SetProperty(ref _gstNo, value))
                {
                    ValidateGSTNo();
                }
            }
        }

        private string _contact = string.Empty;
        public string Contact
        {
            get => _contact;
            set
            {
                if (SetProperty(ref _contact, value))
                {
                    ValidateContact();
                }
            }
        }

        private string _reportEmail = string.Empty;
        public string ReportEmail
        {
            get => _reportEmail;
            set
            {
                if (SetProperty(ref _reportEmail, value))
                {
                    ValidateReportEmail();
                }
            }
        }

        private string? _companyNameError;
        public string? CompanyNameError
        {
            get => _companyNameError;
            set => SetProperty(ref _companyNameError, value);
        }

        private string? _materialCodePrefixError;
        public string? MaterialCodePrefixError
        {
            get => _materialCodePrefixError;
            set => SetProperty(ref _materialCodePrefixError, value);
        }

        private string? _addressError;
        public string? AddressError
        {
            get => _addressError;
            set => SetProperty(ref _addressError, value);
        }

        private string? _gstNoError;
        public string? GSTNoError
        {
            get => _gstNoError;
            set => SetProperty(ref _gstNoError, value);
        }

        private string? _contactError;
        public string? ContactError
        {
            get => _contactError;
            set => SetProperty(ref _contactError, value);
        }

        private string? _reportEmailError;
        public string? ReportEmailError
        {
            get => _reportEmailError;
            set => SetProperty(ref _reportEmailError, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand SaveCommand { get; }

        public CompanySettingsViewModel(ICurrentUserContext main)
        {
            _main = main;
            _auditService = new AuditService();

            var dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProWeighSuitePlatforms", "proweighsuiteplatform.db");
            
            _companyService = new CompanySettingsService(new CompanySettingsRepository(dbPath));

            SaveCommand = new RelayCommand(async _ => await SaveSettingsAsync());

            _ = LoadSettingsAsync();
        }

        private void ValidateCompanyName()
        {
            if (string.IsNullOrWhiteSpace(CompanyName))
            {
                CompanyNameError = "Company name is required";
            }
            else if (CompanyName.Trim().Length < 2)
            {
                CompanyNameError = "Company name must be at least 2 characters";
            }
            else if (CompanyName.Trim().Length > 100)
            {
                CompanyNameError = "Company name cannot exceed 100 characters";
            }
            else
            {
                CompanyNameError = null;
            }
        }

        private void ValidateMaterialCodePrefix()
        {
            if (string.IsNullOrWhiteSpace(MaterialCodePrefix))
            {
                MaterialCodePrefixError = null;
            }
            else if (MaterialCodePrefix.Trim().Length < 2)
            {
                MaterialCodePrefixError = "Prefix must be at least 2 characters";
            }
            else if (MaterialCodePrefix.Trim().Length > 10)
            {
                MaterialCodePrefixError = "Prefix cannot exceed 10 characters";
            }
            else
            {
                MaterialCodePrefixError = null;
            }
        }

        private void ValidateAddress()
        {
            if (string.IsNullOrWhiteSpace(Address))
            {
                AddressError = null;
            }
            else if (Address.Trim().Length < 10)
            {
                AddressError = "Address must be at least 10 characters";
            }
            else if (Address.Trim().Length > 500)
            {
                AddressError = "Address cannot exceed 500 characters";
            }
            else
            {
                AddressError = null;
            }
        }

        private void ValidateGSTNo()
        {
            if (string.IsNullOrWhiteSpace(GSTNo))
            {
                GSTNoError = null;
            }
            else if (GSTNo.Trim().Length != 15)
            {
                GSTNoError = "GST number must be exactly 15 characters";
            }
            else
            {
                GSTNoError = null;
            }
        }

        private void ValidateContact()
        {
            if (string.IsNullOrWhiteSpace(Contact))
            {
                ContactError = null;
            }
            else if (Contact.Trim().Length < 10)
            {
                ContactError = "Contact must be at least 10 characters";
            }
            else if (Contact.Trim().Length > 50)
            {
                ContactError = "Contact cannot exceed 50 characters";
            }
            else
            {
                ContactError = null;
            }
        }

        private void ValidateReportEmail()
        {
            if (string.IsNullOrWhiteSpace(ReportEmail))
            {
                ReportEmailError = "Report email is required";
                return;
            }

            var trimmed = ReportEmail.Trim();
            if (trimmed.Length > 500)
            {
                ReportEmailError = "Email cannot exceed 500 characters";
                return;
            }

            var emails = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (emails.Length == 0)
            {
                ReportEmailError = "Report email is required";
                return;
            }

            foreach (var email in emails)
            {
                if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase))
                {
                    ReportEmailError = $"'{email}' is not a valid email address";
                    return;
                }
            }

            ReportEmailError = null;
        }

        private bool ValidateAll()
        {
            ValidateCompanyName();
            ValidateMaterialCodePrefix();
            ValidateAddress();
            ValidateGSTNo();
            ValidateContact();
            ValidateReportEmail();

            return CompanyNameError == null 
                && MaterialCodePrefixError == null 
                && AddressError == null 
                && GSTNoError == null 
                && ContactError == null
                && ReportEmailError == null;
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                await _companyService.InitializeAsync();
                var settings = await _companyService.GetAsync();

                if (settings != null)
                {
                    CompanyName = settings.CompanyName;
                    MaterialCodePrefix = settings.MaterialCodePrefix;
                    Address = settings.Address;
                    GSTNo = settings.GSTNo;
                    Contact = settings.Contact;
                    ReportEmail = settings.ReportEmail;
                    StatusMessage = "Settings loaded successfully.";
                    
                    // Validate after loading
                    ValidateCompanyName();
                    ValidateMaterialCodePrefix();
                    ValidateAddress();
                    ValidateGSTNo();
                    ValidateContact();
                    ValidateReportEmail();
                }
                else
                {
                    StatusMessage = "No settings found. Please configure company settings.";
                    CompanyNameError = null;
                    MaterialCodePrefixError = null;
                    AddressError = null;
                    GSTNoError = null;
                    ContactError = null;
                    ReportEmailError = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading company settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                CheckPermissionOrThrow(_main.CurrentUser, Permission.EditCompanySettings);

                if (!ValidateAll())
                {
                    StatusMessage = "Please fix validation errors before saving.";
                    return;
                }

                var settings = new CompanySettings
                {
                    CompanyName = CompanyName.Trim(),
                    MaterialCodePrefix = MaterialCodePrefix.Trim().ToUpperInvariant(),
                    Address = Address.Trim(),
                    GSTNo = GSTNo.Trim().ToUpperInvariant(),
                    Contact = Contact.Trim(),
                    ReportEmail = ReportEmail.Trim(),
                    UpdatedAt = DateTime.Now
                };

                await _companyService.SaveAsync(settings);

                var username = _main.CurrentUser?.Username ?? "Unknown";
                await _auditService.LogCompanySettingsSavedAsync(
                    username,
                    $"CompanyName={settings.CompanyName}, Prefix={settings.MaterialCodePrefix}, GST={settings.GSTNo}, Contact={settings.Contact}, ReportEmail={settings.ReportEmail}");

                StatusMessage = "Settings saved successfully.";
                MessageBox.Show("Company settings saved successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException)
            {
                await _auditService.LogSecurityViolationAsync(
                    _main.CurrentUser?.Username ?? "Unknown",
                    "Edit company settings",
                    "Insufficient permissions");

                MessageBox.Show(
                    "You do not have permission to edit company settings.\n\nPlease contact your administrator.",
                    "Access Denied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                await _auditService.LogErrorAsync(_main.CurrentUser?.Username ?? "Unknown", "Save company settings", ex);

                MessageBox.Show($"Error saving company settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"Error: {ex.Message}";
            }
        }
    }
}
