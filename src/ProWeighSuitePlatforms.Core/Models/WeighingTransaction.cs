using System;
using System.Text.Json.Serialization;

namespace ProWeighSuitePlatforms.Models
{
    public class WeighingTransaction
    {
        public string Id { get; set; } = string.Empty;
        public int BatchId { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public string MaterialCode { get; set; } = string.Empty;
        public string MaterialName { get; set; } = string.Empty;
        public int Sequence { get; set; }
        public double TargetWeight { get; set; }
        public double LowerWeight { get; set; }
        public double UpperWeight { get; set; }
        public double ActualWeight { get; set; }
        public double Deviation { get; set; }
        public string Status { get; set; } = string.Empty;
        public string WeighedBy { get; set; } = string.Empty;

        // Sent to API as a formatted string so PHP never receives a null or raw DateTime object
        public string WeighedDate { get; set; } = string.Empty;
        public string PrintedDate { get; set; } = string.Empty;

        // Internal-only DateTime values used by the rest of the app (not serialized to JSON)
        [JsonIgnore]
        public DateTime WeighedDateValue { get; set; }
        [JsonIgnore]
        public DateTime? PrintedDateValue { get; set; }

        // Additional fields
        public string CustomerName { get; set; } = string.Empty;
        public string ManufacturingDate { get; set; } = string.Empty;
        public string ExpiryDate { get; set; } = string.Empty;
        public string EquipmentId { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Remarks { get; set; } = string.Empty;
        public double TareWeight { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Variant { get; set; } = string.Empty;

        /// <summary>
        /// Indicates whether this transaction has been successfully synced to the server.
        /// </summary>
        [JsonIgnore]
        public bool IsSynced { get; set; }

        /// <summary>
        /// Raw JSON response body returned by the remote server on the last sync attempt.
        /// Persisted for audit trails and debugging failed syncs.
        /// </summary>
        [JsonIgnore]
        public string? ServerResponse { get; set; }
    }
}
