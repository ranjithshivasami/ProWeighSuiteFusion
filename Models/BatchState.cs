using System;

namespace ProWeighSuitePlatforms.Models
{
    /// <summary>
    /// Represents the in-memory state of an active batch that needs to survive app restarts.
    /// Serialized to a local JSON file so the batch can resume when the app reopens.
    /// </summary>
    public class BatchState
    {
        public int BatchId { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public string BatchBase { get; set; } = string.Empty;
        public int BatchSequence { get; set; }
        public int CurrentBatchNo { get; set; }
        public int TotalBatches { get; set; }
        public double TotalAchievedWeight { get; set; }
        public string SelectedUOM { get; set; } = "kg";
        public string TargetValue { get; set; } = string.Empty;
        public string MaterialCode { get; set; } = string.Empty;
        public string TransactionMaterialCode { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public double TargetWeight { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public DateTime? ManufacturingDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string Remark { get; set; } = string.Empty;
        public string TareWeight { get; set; } = string.Empty;
        public string StartedBy { get; set; } = string.Empty;
        public DateTime StartedDate { get; set; }
        public string LastTransactionId { get; set; } = string.Empty;
        public string? CurrentMaterialImagePath { get; set; }
    }
}
