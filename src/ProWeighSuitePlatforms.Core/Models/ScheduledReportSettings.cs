using System;

namespace ProWeighSuitePlatforms.Models
{
    public class ScheduledReportSettings
    {
        public int Id { get; set; }

        // Daily Transaction Report
        public bool ReportEnabled { get; set; } = false;
        public string ReportTimeOfDay { get; set; } = "18:00"; // HH:mm

        // Summary Report
        public bool SummaryEnabled { get; set; } = false;
        public string SummaryTimeOfDay { get; set; } = "18:30"; // HH:mm

        // Optional override; falls back to CompanySettings.ReportEmail when empty
        public string RecipientsOverride { get; set; } = string.Empty;

        public DateTime? LastReportSentAt { get; set; }
        public DateTime? LastSummarySentAt { get; set; }
    }
}
