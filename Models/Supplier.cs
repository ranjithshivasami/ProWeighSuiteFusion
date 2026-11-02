using System;

namespace ProWeighSuitePlatforms.Models
{
    public static class SupplierOptions
    {
        public static readonly string[] SupplierTypes =
        {
            "Manufacturer",
            "Distributor",
            "Service Provider",
            "Trader"
        };

        public static readonly string[] BusinessCategories =
        {
            "Raw Material",
            "Packaging",
            "Equipment",
            "IT",
            "Logistics",
            "Services",
            "Others"
        };

        public static readonly string[] LegalEntityTypes =
        {
            "Pvt Ltd",
            "LLP",
            "Partnership",
            "Proprietorship",
            "Public Ltd",
            "Others"
        };
    }

    public class Supplier
    {
        public int Id { get; set; }

        // ===== Basic Information =====
        private string _supplierCode = string.Empty;
        public string SupplierCode
        {
            get => _supplierCode;
            set => _supplierCode = value?.Trim() ?? string.Empty;
        }

        private string _supplierName = string.Empty;
        public string SupplierName
        {
            get => _supplierName;
            set => _supplierName = value?.Trim() ?? string.Empty;
        }

        private string _displayName = string.Empty;
        public string DisplayName
        {
            get => _displayName;
            set => _displayName = value?.Trim() ?? string.Empty;
        }

        private string _supplierType = string.Empty;
        public string SupplierType
        {
            get => _supplierType;
            set => _supplierType = value?.Trim() ?? string.Empty;
        }

        private string _businessCategory = string.Empty;
        public string BusinessCategory
        {
            get => _businessCategory;
            set => _businessCategory = value?.Trim() ?? string.Empty;
        }

        public bool Status { get; set; } = true;

        // ===== Business / Legal Details =====
        private string _legalEntityType = string.Empty;
        public string LegalEntityType
        {
            get => _legalEntityType;
            set => _legalEntityType = value?.Trim() ?? string.Empty;
        }

        private string _registrationNumber = string.Empty;
        public string RegistrationNumber
        {
            get => _registrationNumber;
            set => _registrationNumber = value?.Trim() ?? string.Empty;
        }

        private string _taxId = string.Empty;
        /// <summary>Tax ID / GSTIN / VAT - required for tax compliance.</summary>
        public string TaxId
        {
            get => _taxId;
            set => _taxId = value?.Trim() ?? string.Empty;
        }

        private string _website = string.Empty;
        public string Website
        {
            get => _website;
            set => _website = value?.Trim() ?? string.Empty;
        }

        // ===== Contact Information =====
        private string _contactPerson = string.Empty;
        public string ContactPerson
        {
            get => _contactPerson;
            set => _contactPerson = value?.Trim() ?? string.Empty;
        }

        private string _designation = string.Empty;
        public string Designation
        {
            get => _designation;
            set => _designation = value?.Trim() ?? string.Empty;
        }

        private string _phone = string.Empty;
        public string Phone
        {
            get => _phone;
            set => _phone = value?.Trim() ?? string.Empty;
        }

        private string _mobile = string.Empty;
        public string Mobile
        {
            get => _mobile;
            set => _mobile = value?.Trim() ?? string.Empty;
        }

        private string _email = string.Empty;
        public string Email
        {
            get => _email;
            set => _email = value?.Trim() ?? string.Empty;
        }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Display helpers
        public int SerialNumber { get; set; }
        public string StatusText => Status ? "Active" : "Inactive";
    }
}
