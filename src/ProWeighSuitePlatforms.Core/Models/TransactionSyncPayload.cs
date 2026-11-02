using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProWeighSuitePlatforms.Models
{
    /// <summary>
    /// Payload sent to the remote API for each weighing transaction sync.
    /// Property order matches the expected API contract.
    /// </summary>
    public class TransactionSyncPayload
    {
        [JsonPropertyName("transaction_uuid")]
        public string TransactionUuid { get; set; } = string.Empty;

        [JsonPropertyName("batch_number")]
        public string BatchNumber { get; set; } = string.Empty;

        [JsonPropertyName("sequence")]
        public int Sequence { get; set; }

        [JsonPropertyName("material_code")]
        public string MaterialCode { get; set; } = string.Empty;

        [JsonPropertyName("material_name")]
        public string MaterialName { get; set; } = string.Empty;

        [JsonPropertyName("achived_weight")]
        public double AchivedWeight { get; set; }

        [JsonPropertyName("target_weight")]
        public double TargetWeight { get; set; }

        [JsonPropertyName("upper_weight")]
        public double UpperWeight { get; set; }

        [JsonPropertyName("lower_weight")]
        public double LowerWeight { get; set; }

        [JsonPropertyName("weighed_by")]
        public string WeighedBy { get; set; } = string.Empty;

        [JsonPropertyName("weighed_date_time")]
        public string WeighedDateTime { get; set; } = string.Empty;

        [JsonPropertyName("manufacturing_date")]
        public string ManufacturingDate { get; set; } = string.Empty;

        [JsonPropertyName("expiry_date")]
        public string ExpiryDate { get; set; } = string.Empty;

        [JsonPropertyName("customer_name")]
        public string CustomerName { get; set; } = string.Empty;

        [JsonPropertyName("remarks")]
        public string Remarks { get; set; } = string.Empty;

        [JsonPropertyName("tare_weight")]
        public double TareWeight { get; set; }

        [JsonPropertyName("equipment_id")]
        public string EquipmentId { get; set; } = string.Empty;

        [JsonPropertyName("location")]
        public string Location { get; set; } = string.Empty;

        /// <summary>
        /// Returns a JSON string containing only the fields listed in <paramref name="fieldKeys"/>.
        /// Field keys should match the JsonPropertyName values (e.g., "transaction_uuid", "batch_number").
        /// If fieldKeys is null or empty, serializes all fields.
        /// </summary>
        public string ToFilteredJson(List<string>? fieldKeys)
        {
            if (fieldKeys == null || fieldKeys.Count == 0)
            {
                return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = false });
            }

            var allFields = new Dictionary<string, object?>
            {
                ["transaction_uuid"] = TransactionUuid,
                ["batch_number"] = BatchNumber,
                ["sequence"] = Sequence,
                ["material_code"] = MaterialCode,
                ["material_name"] = MaterialName,
                ["achived_weight"] = AchivedWeight,
                ["target_weight"] = TargetWeight,
                ["upper_weight"] = UpperWeight,
                ["lower_weight"] = LowerWeight,
                ["weighed_by"] = WeighedBy,
                ["weighed_date_time"] = WeighedDateTime,
                ["manufacturing_date"] = ManufacturingDate,
                ["expiry_date"] = ExpiryDate,
                ["customer_name"] = CustomerName,
                ["remarks"] = Remarks,
                ["tare_weight"] = TareWeight,
                ["equipment_id"] = EquipmentId,
                ["location"] = Location
            };

            var filtered = allFields.Where(kv => fieldKeys.Contains(kv.Key))
                                    .ToDictionary(kv => kv.Key, kv => kv.Value);

            return JsonSerializer.Serialize(filtered, new JsonSerializerOptions { WriteIndented = false });
        }

        /// <summary>
        /// Maps a WeighingTransaction to a TransactionSyncPayload.
        /// </summary>
        public static TransactionSyncPayload FromTransaction(WeighingTransaction t) => new()
        {
            TransactionUuid   = t.Id,
            BatchNumber       = t.BatchNumber,
            Sequence          = t.Sequence,
            MaterialCode      = t.MaterialCode,
            MaterialName      = t.MaterialName,
            AchivedWeight     = Math.Round(t.ActualWeight, 3),
            TargetWeight      = Math.Round(t.TargetWeight, 3),
            UpperWeight       = Math.Round(t.UpperWeight, 3),
            LowerWeight       = Math.Round(t.LowerWeight, 3),
            WeighedBy         = t.WeighedBy,
            WeighedDateTime   = t.WeighedDate,
            ManufacturingDate = t.ManufacturingDate,
            ExpiryDate        = t.ExpiryDate,
            CustomerName      = t.CustomerName,
            Remarks           = t.Remarks,
            TareWeight        = Math.Round(t.TareWeight, 3),
            EquipmentId       = t.EquipmentId,
            Location          = t.Location
        };
    }
}
