using System;

namespace ProWeighSuitePlatforms.Models
{
    public class RolePermission
    {
        public int Id { get; set; }
        public UserRole Role { get; set; }
        public Permission Permission { get; set; }
        public bool IsGranted { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? ModifiedDate { get; set; }
        public string ModifiedBy { get; set; } = string.Empty;
    }
}
