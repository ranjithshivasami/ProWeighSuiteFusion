using System;

namespace ProWeighSuitePlatforms.Models
{
    public class WeighingBatch
    {
        public int Id { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public double BatchSize { get; set; }
        public int TotalBatches { get; set; }
        public int CurrentBatchNumber { get; set; }
        public string Status { get; set; } = string.Empty; // InProgress, Completed, Aborted, Paused
        public string StartedBy { get; set; } = string.Empty;
        public DateTime StartedDate { get; set; }
        public DateTime? CompletedDate { get; set; }
        public string Notes { get; set; } = string.Empty;
    }
}
