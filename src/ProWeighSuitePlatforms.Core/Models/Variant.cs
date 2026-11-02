using System;

namespace ProWeighSuitePlatforms.Models
{
    public class Variant
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Active { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        public int SerialNumber { get; set; }
        public string ActiveText => Active ? "Active" : "Inactive";
    }
}
