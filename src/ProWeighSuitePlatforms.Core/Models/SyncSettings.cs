using System.Collections.Generic;

namespace ProWeighSuitePlatforms.Models
{
    public class SyncSettings
    {
        /// <summary>
        /// Master switch for background sync (transaction queue + audit offline
        /// sync). When false the services pause uploads but keep collecting
        /// locally so nothing is lost. Default true preserves existing behavior.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Primary API Base URL - Required. Determines sync success/failure status in Qumonitor.
        /// Transactions are marked as synced (is_synced=true) only when this endpoint succeeds.
        /// </summary>
        public string ApiBaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Secondary API Base URL - Optional. For internal testing purposes only.
        /// Does NOT affect Qumonitor status or sync failure counts.
        /// Transactions are mirrored to this endpoint but its success/failure is logged separately.
        /// </summary>
        public string ApiBaseUrl2 { get; set; } = string.Empty;

        public int RetryCount { get; set; } = 3;
        public int RetryDelaySeconds { get; set; } = 2;

        public List<string>? FieldMappings { get; set; }
    }
}