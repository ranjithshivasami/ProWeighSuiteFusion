using System;

namespace ProWeighSuitePlatforms.Models
{
    public class ReprintRecord
    {
        public int Id { get; set; }
        public string TransactionId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string ReprintedBy { get; set; } = string.Empty;
        public string ReprintedAt { get; set; } = string.Empty;   // "yyyy-MM-dd HH:mm:ss"

        // Denormalised for display / audit convenience
        public string BatchNumber { get; set; } = string.Empty;
        public string MaterialCode { get; set; } = string.Empty;
    }
}
