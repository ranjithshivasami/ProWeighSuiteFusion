using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ProWeighSuitePlatforms.Models
{
    /// <summary>
    /// Payload structure for syncing audit logs to remote server
    /// </summary>
    public class AuditSyncPayload
    {
        /// <summary>
        /// Unique identifier for this sync batch
        /// </summary>
        [JsonPropertyName("sync_batch_id")]
        public string SyncBatchId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Timestamp when this sync payload was created
        /// </summary>
        [JsonPropertyName("sync_timestamp")]
        public DateTime SyncTimestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Source system identifier (e.g., machine name or location)
        /// </summary>
        [JsonPropertyName("source_system")]
        public string SourceSystem { get; set; } = Environment.MachineName;

        /// <summary>
        /// Application version
        /// </summary>
        [JsonPropertyName("app_version")]
        public string AppVersion { get; set; } = string.Empty;

        /// <summary>
        /// Collection of audit entries to sync
        /// </summary>
        [JsonPropertyName("audit_entries")]
        public List<AuditSyncEntry> AuditEntries { get; set; } = new();

        /// <summary>
        /// Total count of entries in this payload
        /// </summary>
        [JsonPropertyName("entry_count")]
        public int EntryCount => AuditEntries.Count;

        /// <summary>
        /// Date range of entries in this payload
        /// </summary>
        [JsonPropertyName("date_range_from")]
        public DateTime? DateRangeFrom { get; set; }

        [JsonPropertyName("date_range_to")]
        public DateTime? DateRangeTo { get; set; }

        /// <summary>
        /// Optional metadata for additional context
        /// </summary>
        [JsonPropertyName("metadata")]
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Individual audit entry for sync payload
    /// </summary>
    public class AuditSyncEntry
    {
        /// <summary>
        /// Unique identifier for this audit entry
        /// </summary>
        [JsonPropertyName("entry_id")]
        public string EntryId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Timestamp of the audited event (UTC)
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Action performed (e.g., "Login_Success", "Material_Created")
        /// </summary>
        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Category of the audit event
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Username who performed the action
        /// </summary>
        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Type of entity affected (e.g., "Material", "User")
        /// </summary>
        [JsonPropertyName("entity_type")]
        public string? EntityType { get; set; }

        /// <summary>
        /// ID of the entity affected
        /// </summary>
        [JsonPropertyName("entity_id")]
        public string? EntityId { get; set; }

        /// <summary>
        /// Description of the event
        /// </summary>
        [JsonPropertyName("details")]
        public string Details { get; set; } = string.Empty;

        /// <summary>
        /// Previous value (for updates)
        /// </summary>
        [JsonPropertyName("old_value")]
        public string? OldValue { get; set; }

        /// <summary>
        /// New value (for creates/updates)
        /// </summary>
        [JsonPropertyName("new_value")]
        public string? NewValue { get; set; }

        /// <summary>
        /// IP address if available
        /// </summary>
        [JsonPropertyName("ip_address")]
        public string? IpAddress { get; set; }

        /// <summary>
        /// Machine name where the event occurred
        /// </summary>
        [JsonPropertyName("machine_name")]
        public string MachineName { get; set; } = string.Empty;

        /// <summary>
        /// Severity level
        /// </summary>
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response from sync API
    /// </summary>
    public class AuditSyncResponse
    {
        /// <summary>
        /// Indicates if sync was successful
        /// </summary>
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        /// <summary>
        /// Sync batch ID that was processed
        /// </summary>
        [JsonPropertyName("sync_batch_id")]
        public string SyncBatchId { get; set; } = string.Empty;

        /// <summary>
        /// Number of entries successfully synced
        /// </summary>
        [JsonPropertyName("synced_count")]
        public int SyncedCount { get; set; }

        /// <summary>
        /// Number of entries that failed
        /// </summary>
        [JsonPropertyName("failed_count")]
        public int FailedCount { get; set; }

        /// <summary>
        /// Error message if sync failed
        /// </summary>
        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Timestamp when server processed the sync
        /// </summary>
        [JsonPropertyName("processed_at")]
        public DateTime ProcessedAt { get; set; }

        /// <summary>
        /// IDs of entries that failed (if any)
        /// </summary>
        [JsonPropertyName("failed_entry_ids")]
        public List<string> FailedEntryIds { get; set; } = new();

        /// <summary>
        /// Additional messages or warnings
        /// </summary>
        [JsonPropertyName("messages")]
        public List<string> Messages { get; set; } = new();
    }
}
