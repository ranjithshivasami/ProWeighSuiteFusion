using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Service for syncing audit logs to remote server
    /// </summary>
    public class AuditSyncService
    {
        private readonly AuditService _auditService;
        private readonly HttpClient _httpClient;
        private readonly string _syncEndpoint;

        public AuditSyncService(AuditService auditService, string syncEndpoint)
        {
            _auditService = auditService;
            _syncEndpoint = syncEndpoint;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
        }

        /// <summary>
        /// Sync today's audit logs to remote server
        /// </summary>
        public async Task<AuditSyncResponse> SyncTodayAsync()
        {
            try
            {
                var payload = await _auditService.CreateTodaySyncPayloadAsync();
                return await SendSyncPayloadAsync(payload);
            }
            catch (Exception ex)
            {
                return new AuditSyncResponse
                {
                    Success = false,
                    ErrorMessage = $"Sync failed: {ex.Message}",
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Sync audit logs for a specific date range
        /// </summary>
        public async Task<AuditSyncResponse> SyncDateRangeAsync(DateTime fromDate, DateTime toDate)
        {
            try
            {
                var payload = await _auditService.CreateSyncPayloadAsync(fromDate, toDate);
                return await SendSyncPayloadAsync(payload);
            }
            catch (Exception ex)
            {
                return new AuditSyncResponse
                {
                    Success = false,
                    ErrorMessage = $"Sync failed: {ex.Message}",
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Sync all unsynced audit logs
        /// </summary>
        public async Task<AuditSyncResponse> SyncAllUnsyncedAsync()
        {
            try
            {
                var payload = await _auditService.CreateUnsyncedPayloadAsync();
                return await SendSyncPayloadAsync(payload);
            }
            catch (Exception ex)
            {
                return new AuditSyncResponse
                {
                    Success = false,
                    ErrorMessage = $"Sync failed: {ex.Message}",
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Send sync payload to remote server via HTTP POST
        /// </summary>
        private async Task<AuditSyncResponse> SendSyncPayloadAsync(AuditSyncPayload payload)
        {
            try
            {
                // Serialize payload to JSON
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                };
                
                var json = JsonSerializer.Serialize(payload, jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Add custom headers
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("X-Sync-Batch-Id", payload.SyncBatchId);
                _httpClient.DefaultRequestHeaders.Add("X-Source-System", payload.SourceSystem);
                _httpClient.DefaultRequestHeaders.Add("X-App-Version", payload.AppVersion);

                // Send POST request
                var response = await _httpClient.PostAsync(_syncEndpoint, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var syncResponse = JsonSerializer.Deserialize<AuditSyncResponse>(responseJson, jsonOptions);
                    
                    return syncResponse ?? new AuditSyncResponse
                    {
                        Success = true,
                        SyncBatchId = payload.SyncBatchId,
                        SyncedCount = payload.AuditEntries.Count,
                        ProcessedAt = DateTime.UtcNow
                    };
                }
                else
                {
                    return new AuditSyncResponse
                    {
                        Success = false,
                        SyncBatchId = payload.SyncBatchId,
                        ErrorMessage = $"HTTP {response.StatusCode}: {await response.Content.ReadAsStringAsync()}",
                        ProcessedAt = DateTime.UtcNow
                    };
                }
            }
            catch (Exception ex)
            {
                return new AuditSyncResponse
                {
                    Success = false,
                    SyncBatchId = payload.SyncBatchId,
                    ErrorMessage = $"Network error: {ex.Message}",
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Export sync payload to file instead of sending to server
        /// (useful for offline sync or manual transfer)
        /// </summary>
        public async Task<string> ExportTodayToFileAsync(string? outputPath = null)
        {
            var payload = await _auditService.CreateTodaySyncPayloadAsync();
            return await _auditService.ExportSyncPayloadAsync(payload, outputPath);
        }

        /// <summary>
        /// Export sync payload for date range to file
        /// </summary>
        public async Task<string> ExportDateRangeToFileAsync(DateTime fromDate, DateTime toDate, string? outputPath = null)
        {
            var payload = await _auditService.CreateSyncPayloadAsync(fromDate, toDate);
            return await _auditService.ExportSyncPayloadAsync(payload, outputPath);
        }

        /// <summary>
        /// Test connection to sync endpoint
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync(_syncEndpoint + "/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
