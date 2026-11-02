namespace ProWeighSuitePlatforms.Models
{
    public class Material
    {
        public int Id { get; set; }

        private string _productName = string.Empty;
        public string ProductName
        {
            get => _productName;
            set => _productName = value?.Trim() ?? string.Empty;
        }

        public string ProductCode { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public int VariantId { get; set; }
        public string VariantName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public int ShelfLife { get; set; } = 365;
        public double TargetWeight { get; set; }
        public WeightUOM TargetWeightUOM { get; set; } = WeightUOM.Kg;
        public bool CheckWeigherRequired { get; set; }
        public double TMinusPercent { get; set; }
        public double TPlusPercent { get; set; }
        public string ToleranceMode { get; set; } = "Relative";
        public double MinWeight { get; set; }
        public double MaxWeight { get; set; }
        public System.DateTime CreatedDate { get; set; } = System.DateTime.Now;
        public System.DateTime? ModifiedDate { get; set; }

        /// <summary>Optional material image path (full file path under %AppData%\ProWeighSuitePlatforms\MaterialImages). Null/empty = no image.</summary>
        public string? ImagePath { get; set; }

        public int SerialNumber { get; set; }
        public string StatusText => IsActive ? "Active" : "Inactive";
        public string UOMText => TargetWeightUOM == WeightUOM.Kg ? "kg" : "g";
    }
}