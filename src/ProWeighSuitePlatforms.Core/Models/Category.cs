using System;

namespace ProWeighSuitePlatforms.Models
{
    public class Category
    {
        public int Id { get; set; }

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => _name = value?.Trim() ?? string.Empty;
        }

        private string _description = string.Empty;
        public string Description
        {
            get => _description;
            set => _description = value?.Trim() ?? string.Empty;
        }

        public bool Status { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        // Display properties
        public int SerialNumber { get; set; }
        public string StatusText => Status ? "Active" : "Inactive";
    }
}
