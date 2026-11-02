using System;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;
using Microsoft.Data.Sqlite;

namespace ProWeighSuitePlatforms.Data
{
    public class ReportScheduleRepository
    {
        private readonly string _connectionString;

        public ReportScheduleRepository(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        public async Task InitializeAsync()
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS report_schedule (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                report_enabled INTEGER NOT NULL DEFAULT 0,
                report_time_of_day TEXT NOT NULL DEFAULT '18:00',
                summary_enabled INTEGER NOT NULL DEFAULT 0,
                summary_time_of_day TEXT NOT NULL DEFAULT '18:30',
                recipients_override TEXT NOT NULL DEFAULT '',
                last_report_sent_at TEXT,
                last_summary_sent_at TEXT
            );";
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<ScheduledReportSettings?> GetAsync()
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT id, report_enabled, report_time_of_day, summary_enabled,
                                       summary_time_of_day, recipients_override,
                                       last_report_sent_at, last_summary_sent_at
                                FROM report_schedule LIMIT 1";
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new ScheduledReportSettings
                {
                    Id = reader.GetInt32(0),
                    ReportEnabled = reader.GetInt32(1) == 1,
                    ReportTimeOfDay = reader.IsDBNull(2) ? "18:00" : reader.GetString(2),
                    SummaryEnabled = reader.GetInt32(3) == 1,
                    SummaryTimeOfDay = reader.IsDBNull(4) ? "18:30" : reader.GetString(4),
                    RecipientsOverride = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    LastReportSentAt = reader.IsDBNull(6) ? (DateTime?)null : DateTime.Parse(reader.GetString(6)),
                    LastSummarySentAt = reader.IsDBNull(7) ? (DateTime?)null : DateTime.Parse(reader.GetString(7))
                };
            }
            return null;
        }

        public async Task<int> SaveAsync(ScheduledReportSettings settings)
        {
            EntityTextNormalizer.Trim(settings);
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var existing = await GetAsync();
            var cmd = conn.CreateCommand();

            var lastReport = settings.LastReportSentAt.HasValue
                ? (object)settings.LastReportSentAt.Value.ToString("yyyy-MM-dd HH:mm:ss")
                : DBNull.Value;
            var lastSummary = settings.LastSummarySentAt.HasValue
                ? (object)settings.LastSummarySentAt.Value.ToString("yyyy-MM-dd HH:mm:ss")
                : DBNull.Value;

            if (existing == null)
            {
                cmd.CommandText = @"
                INSERT INTO report_schedule (report_enabled, report_time_of_day, summary_enabled,
                                             summary_time_of_day, recipients_override,
                                             last_report_sent_at, last_summary_sent_at)
                VALUES ($reportEnabled, $reportTime, $summaryEnabled, $summaryTime,
                        $recipients, $lastReport, $lastSummary);
                SELECT last_insert_rowid();";

                cmd.Parameters.AddWithValue("$reportEnabled", settings.ReportEnabled ? 1 : 0);
                cmd.Parameters.AddWithValue("$reportTime", settings.ReportTimeOfDay ?? "18:00");
                cmd.Parameters.AddWithValue("$summaryEnabled", settings.SummaryEnabled ? 1 : 0);
                cmd.Parameters.AddWithValue("$summaryTime", settings.SummaryTimeOfDay ?? "18:30");
                cmd.Parameters.AddWithValue("$recipients", settings.RecipientsOverride ?? string.Empty);
                cmd.Parameters.AddWithValue("$lastReport", lastReport);
                cmd.Parameters.AddWithValue("$lastSummary", lastSummary);

                var res = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(res);
            }
            else
            {
                cmd.CommandText = @"
                UPDATE report_schedule SET
                    report_enabled = $reportEnabled,
                    report_time_of_day = $reportTime,
                    summary_enabled = $summaryEnabled,
                    summary_time_of_day = $summaryTime,
                    recipients_override = $recipients,
                    last_report_sent_at = $lastReport,
                    last_summary_sent_at = $lastSummary
                WHERE id = $id";

                cmd.Parameters.AddWithValue("$id", existing.Id);
                cmd.Parameters.AddWithValue("$reportEnabled", settings.ReportEnabled ? 1 : 0);
                cmd.Parameters.AddWithValue("$reportTime", settings.ReportTimeOfDay ?? "18:00");
                cmd.Parameters.AddWithValue("$summaryEnabled", settings.SummaryEnabled ? 1 : 0);
                cmd.Parameters.AddWithValue("$summaryTime", settings.SummaryTimeOfDay ?? "18:30");
                cmd.Parameters.AddWithValue("$recipients", settings.RecipientsOverride ?? string.Empty);
                cmd.Parameters.AddWithValue("$lastReport", lastReport);
                cmd.Parameters.AddWithValue("$lastSummary", lastSummary);

                return await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}
