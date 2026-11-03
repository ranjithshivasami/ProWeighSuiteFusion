using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ProWeighSuitePlatforms.Configuration;
using ProWeighSuitePlatforms.Models;
using System.Collections.Generic;
using System.Linq;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Service for logging all user operations and system events
    /// </summary>
    public class AuditService
    {
        private readonly string _auditLogPath;
        private static readonly object _lockObject = new();
        private OfflineAuditSyncService? _offlineSyncService;

        public AuditService()
        {
            var dbPath = AppConfigurationManager.GetDatabasePath();
            var auditDir = Path.Combine(Path.GetDirectoryName(dbPath) ?? "", "AuditLogs");
            
            if (!Directory.Exists(auditDir))
            {
                Directory.CreateDirectory(auditDir);
            }

            // Daily log file
            _auditLogPath = Path.Combine(auditDir, $"audit_{DateTime.Now:yyyyMMdd}.log");
        }

        /// <summary>
        /// Initialize offline sync service
        /// </summary>
        public void InitializeOfflineSync(string syncEndpoint, int retryIntervalMinutes = 5)
        {
            if (_offlineSyncService == null)
            {
                _offlineSyncService = new OfflineAuditSyncService(this, syncEndpoint, retryIntervalMinutes);
                
                // Subscribe to status changes
                _offlineSyncService.SyncStatusChanged += (sender, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[AuditService] Sync status: {e.Status} - {e.Message}");
                };
            }
        }

        /// <summary>
        /// Get offline sync service (if initialized)
        /// </summary>
        public OfflineAuditSyncService? GetOfflineSyncService() => _offlineSyncService;

        /// <summary>
        /// Log an audit entry
        /// Note: Timestamps are stored in UTC and converted to local time when displayed
        /// </summary>
        public async Task LogAsync(AuditEntry entry)
        {
            try
            {
                // Store timestamp in UTC for consistency across time zones
                entry.Timestamp = DateTime.UtcNow;
                entry.MachineName = Environment.MachineName;

                var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions 
                { 
                    WriteIndented = false 
                });

                lock (_lockObject)
                {
                    File.AppendAllText(_auditLogPath, json + Environment.NewLine);
                }

                // Queue for offline sync ONLY if it's a Transaction category entry
                if (_offlineSyncService != null && entry.Category == AuditCategory.Transaction)
                {
                    await _offlineSyncService.QueueForSyncAsync(entry);
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Audit] Failed to log: {ex.Message}");
            }
        }

        /// <summary>
        /// Log user login
        /// </summary>
        public async Task LogLoginAsync(string username, bool success, string? ipAddress = null)
        {
            await LogAsync(new AuditEntry
            {
                Action = success ? "Login_Success" : "Login_Failed",
                Category = AuditCategory.Authentication,
                Username = username,
                Details = success ? "User logged in successfully" : "Failed login attempt",
                IpAddress = ipAddress,
                Severity = success ? AuditSeverity.Info : AuditSeverity.Warning
            });
        }

        /// <summary>
        /// Log user logout
        /// </summary>
        public async Task LogLogoutAsync(string username)
        {
            await LogAsync(new AuditEntry
            {
                Action = "Logout",
                Category = AuditCategory.Authentication,
                Username = username,
                Details = "User logged out",
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log material creation
        /// </summary>
        public async Task LogMaterialCreatedAsync(string username, Material material)
        {
            await LogAsync(new AuditEntry
            {
                Action = "Material_Created",
                Category = AuditCategory.Material,
                Username = username,
                EntityType = "Material",
                EntityId = material.Id.ToString(),
                Details = $"Material created: {material.ProductCode} - {material.ProductName}",
                OldValue = null,
                NewValue = JsonSerializer.Serialize(material),
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log material update
        /// </summary>
        public async Task LogMaterialUpdatedAsync(string username, Material oldMaterial, Material newMaterial)
        {
            await LogAsync(new AuditEntry
            {
                Action = "Material_Updated",
                Category = AuditCategory.Material,
                Username = username,
                EntityType = "Material",
                EntityId = newMaterial.Id.ToString(),
                Details = $"Material updated: {newMaterial.ProductCode} - {newMaterial.ProductName}",
                OldValue = JsonSerializer.Serialize(oldMaterial),
                NewValue = JsonSerializer.Serialize(newMaterial),
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log material deletion
        /// </summary>
        public async Task LogMaterialDeletedAsync(string username, Material material)
        {
            await LogAsync(new AuditEntry
            {
                Action = "Material_Deleted",
                Category = AuditCategory.Material,
                Username = username,
                EntityType = "Material",
                EntityId = material.Id.ToString(),
                Details = $"Material deleted: {material.ProductCode} - {material.ProductName}",
                OldValue = JsonSerializer.Serialize(material),
                Severity = AuditSeverity.Warning
            });
        }

        /// <summary>
        /// Log transaction creation
        /// </summary>
        public async Task LogTransactionCreatedAsync(string username, WeighingTransaction transaction)
        {
            await LogAsync(new AuditEntry
            {
                Action = "Transaction_Created",
                Category = AuditCategory.Transaction,
                Username = username,
                EntityType = "Transaction",
                EntityId = transaction.Id.ToString(),
                Details = $"Transaction created: Batch {transaction.BatchNumber}, Material {transaction.MaterialCode}, Weight {transaction.ActualWeight}kg",
                NewValue = JsonSerializer.Serialize(new
                {
                    transaction.Id,
                    transaction.BatchNumber,
                    transaction.MaterialCode,
                    transaction.ActualWeight,
                    transaction.Status
                }),
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log a successful Fusion weigh transaction. Uses the Transaction
        /// category so it is queued by the offline audit sync alongside the
        /// main weighing ledger.
        /// </summary>
        public async Task LogFusionTransactionAsync(string username, FusionTransaction transaction)
        {
            await LogAsync(new AuditEntry
            {
                Action = "Fusion_Transaction_Created",
                Category = AuditCategory.Transaction,
                Username = username,
                EntityType = "FusionTransaction",
                EntityId = transaction.Id,
                Details = $"Fusion weigh: batch {transaction.BatchNo}, tub {transaction.TubId}, lot {transaction.LotNo}, {transaction.Weight:0.###} {transaction.Uom}, {transaction.Nuts} nuts",
                NewValue = JsonSerializer.Serialize(new
                {
                    transaction.Id,
                    transaction.BatchNo,
                    transaction.TubId,
                    transaction.Epc,
                    transaction.TubCycles,
                    transaction.SupplierId,
                    transaction.SupplierName,
                    transaction.GrnNo,
                    transaction.BayId,
                    transaction.LotNo,
                    transaction.Weight,
                    transaction.Uom,
                    transaction.Nuts,
                    transaction.OperatorName,
                    transaction.RecordedAt
                }),
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log a Fusion operation (batch lifecycle, stock, master data, config).
        /// </summary>
        public async Task LogFusionEventAsync(
            string username,
            string action,
            string entityType,
            string? entityId,
            string details,
            AuditCategory category = AuditCategory.Transaction,
            AuditSeverity severity = AuditSeverity.Info,
            string? oldValue = null,
            string? newValue = null)
        {
            await LogAsync(new AuditEntry
            {
                Action = action,
                Category = category,
                Username = username,
                EntityType = entityType,
                EntityId = entityId,
                Details = details,
                OldValue = oldValue,
                NewValue = newValue,
                Severity = severity
            });
        }

        /// <summary>
        /// Log user creation
        /// </summary>
        public async Task LogUserCreatedAsync(string adminUsername, User newUser)
        {
            await LogAsync(new AuditEntry
            {
                Action = "User_Created",
                Category = AuditCategory.UserManagement,
                Username = adminUsername,
                EntityType = "User",
                EntityId = newUser.Id.ToString(),
                Details = $"User created: {newUser.Username} with role {newUser.Role}",
                NewValue = JsonSerializer.Serialize(new { newUser.Username, newUser.Role }),
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log user update
        /// </summary>
        public async Task LogUserUpdatedAsync(string adminUsername, User oldUser, User newUser)
        {
            await LogAsync(new AuditEntry
            {
                Action = "User_Updated",
                Category = AuditCategory.UserManagement,
                Username = adminUsername,
                EntityType = "User",
                EntityId = newUser.Id.ToString(),
                Details = $"User updated: {newUser.Username}",
                OldValue = JsonSerializer.Serialize(new { oldUser.Username, oldUser.Role }),
                NewValue = JsonSerializer.Serialize(new { newUser.Username, newUser.Role }),
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log user deletion
        /// </summary>
        public async Task LogUserDeletedAsync(string adminUsername, User deletedUser)
        {
            await LogAsync(new AuditEntry
            {
                Action = "User_Deleted",
                Category = AuditCategory.UserManagement,
                Username = adminUsername,
                EntityType = "User",
                EntityId = deletedUser.Id.ToString(),
                Details = $"User deleted: {deletedUser.Username}",
                OldValue = JsonSerializer.Serialize(new { deletedUser.Username, deletedUser.Role }),
                Severity = AuditSeverity.Warning
            });
        }

        /// <summary>
        /// Log password change
        /// </summary>
        public async Task LogPasswordChangedAsync(string username, bool byAdmin = false)
        {
            await LogAsync(new AuditEntry
            {
                Action = "Password_Changed",
                Category = AuditCategory.Security,
                Username = username,
                Details = byAdmin ? $"Password changed by administrator" : "User changed own password",
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log settings change
        /// </summary>
        public async Task LogSettingsChangedAsync(string username, string settingName, string? oldValue, string? newValue)
        {
            await LogAsync(new AuditEntry
            {
                Action = "Settings_Changed",
                Category = AuditCategory.Configuration,
                Username = username,
                Details = $"Setting changed: {settingName}",
                OldValue = oldValue,
                NewValue = newValue,
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log report export
        /// </summary>
        public async Task LogReportExportedAsync(string username, string reportType, int recordCount)
        {
            await LogAsync(new AuditEntry
            {
                Action = "Report_Exported",
                Category = AuditCategory.Report,
                Username = username,
                Details = $"Exported {reportType} report with {recordCount} records",
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log database backup
        /// </summary>
        public async Task LogDatabaseBackupAsync(string username, string backupPath, bool success)
        {
            await LogAsync(new AuditEntry
            {
                Action = success ? "Backup_Success" : "Backup_Failed",
                Category = AuditCategory.System,
                Username = username,
                Details = success ? $"Database backed up to: {backupPath}" : "Database backup failed",
                Severity = success ? AuditSeverity.Info : AuditSeverity.Error
            });
        }

        /// <summary>
        /// Log database restore
        /// </summary>
        public async Task LogDatabaseRestoreAsync(string username, string restorePath, bool success)
        {
            await LogAsync(new AuditEntry
            {
                Action = success ? "Restore_Success" : "Restore_Failed",
                Category = AuditCategory.System,
                Username = username,
                Details = success ? $"Database restored from: {restorePath}" : "Database restore failed",
                Severity = success ? AuditSeverity.Warning : AuditSeverity.Error
            });
        }

        /// <summary>
        /// Log security violation
        /// </summary>
        public async Task LogSecurityViolationAsync(string username, string action, string reason)
        {
            await LogAsync(new AuditEntry
            {
                Action = "Security_Violation",
                Category = AuditCategory.Security,
                Username = username,
                Details = $"Unauthorized attempt: {action} - Reason: {reason}",
                Severity = AuditSeverity.Critical
            });
        }

        /// <summary>
        /// Log application error
        /// </summary>
        public async Task LogErrorAsync(string username, string action, Exception exception)
        {
            await LogAsync(new AuditEntry
            {
                Action = "Error",
                Category = AuditCategory.System,
                Username = username,
                Details = $"Error in {action}: {exception.Message}",
                NewValue = exception.StackTrace,
                Severity = AuditSeverity.Error
            });
        }

        /// <summary>
        /// Log role permissions matrix saved
        /// </summary>
        public async Task LogPermissionsChangedAsync(string username, int permissionCount)
        {
            await LogAsync(new AuditEntry
            {
                Action = "Permissions_Saved",
                Category = AuditCategory.Security,
                Username = username,
                Details = $"Role permissions matrix updated: {permissionCount} permission(s) saved",
                Severity = AuditSeverity.Warning
            });
        }

        /// <summary>
        /// Log role permissions reset to defaults
        /// </summary>
        public async Task LogPermissionsResetAsync(string username)
        {
            await LogAsync(new AuditEntry
            {
                Action = "Permissions_Reset",
                Category = AuditCategory.Security,
                Username = username,
                Details = "Role permissions reset to default values",
                Severity = AuditSeverity.Warning
            });
        }

        /// <summary>
        /// Log company settings saved
        /// </summary>
        public async Task LogCompanySettingsSavedAsync(string username, string newValues)
        {
            await LogAsync(new AuditEntry
            {
                Action = "CompanySettings_Saved",
                Category = AuditCategory.Configuration,
                Username = username,
                EntityType = "CompanySettings",
                Details = "Company settings updated",
                NewValue = newValues,
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log password changed by admin for another user
        /// </summary>
        public async Task LogPasswordChangedByAdminAsync(string adminUsername, string targetUsername)
        {
            await LogAsync(new AuditEntry
            {
                Action = "Password_Changed",
                Category = AuditCategory.Security,
                Username = adminUsername,
                EntityType = "User",
                Details = $"Password changed by administrator for user: {targetUsername}",
                Severity = AuditSeverity.Warning
            });
        }

        /// <summary>
        /// Log CSV template downloaded
        /// </summary>
        public async Task LogCsvTemplateDownloadedAsync(string username, string entityType, string filePath)
        {
            await LogAsync(new AuditEntry
            {
                Action = $"{entityType}_TemplateDownloaded",
                Category = AuditCategory.Configuration,
                Username = username,
                EntityType = entityType,
                Details = $"{entityType} CSV template downloaded to: {filePath}",
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log printer configuration saved
        /// </summary>
        public async Task LogPrinterConfigSavedAsync(string username, ZT231PrinterConfig config)
        {
            var details = config.ConnectionType == "Ethernet"
                ? $"ConnectionType=Ethernet, IP={config.IpAddress}, Port={config.Port}, Subnet={config.SubnetMask}, Gateway={config.Gateway}"
                : $"ConnectionType=USB/COM, Port={config.ComPort}, BaudRate={config.BaudRate}, DataBits={config.DataBits}, Parity={config.Parity}, StopBits={config.StopBits}, FlowControl={config.FlowControl}";

            await LogAsync(new AuditEntry
            {
                Action = "PrinterConfig_Saved",
                Category = AuditCategory.Configuration,
                Username = username,
                EntityType = "PrinterConfig",
                Details = $"Printer configuration saved. {details}",
                NewValue = JsonSerializer.Serialize(config),
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log printer test print sent
        /// </summary>
        public async Task LogPrinterTestPrintAsync(string username, string connectionInfo, bool success)
        {
            await LogAsync(new AuditEntry
            {
                Action = success ? "PrinterTest_Success" : "PrinterTest_Failed",
                Category = AuditCategory.Configuration,
                Username = username,
                EntityType = "PrinterConfig",
                Details = success
                    ? $"Test print sent successfully via {connectionInfo}"
                    : $"Test print failed via {connectionInfo}",
                Severity = success ? AuditSeverity.Info : AuditSeverity.Warning
            });
        }

        /// <summary>
        /// Log ZPL label template created
        /// </summary>
        public async Task LogZplTemplateCreatedAsync(string username, ZplLabelTemplate template)
        {
            await LogAsync(new AuditEntry
            {
                Action = "ZplTemplate_Created",
                Category = AuditCategory.Configuration,
                Username = username,
                EntityType = "ZplTemplate",
                EntityId = template.Id.ToString(),
                Details = $"ZPL template created: '{template.Name}' ({template.LabelWidthMm}x{template.LabelHeightMm}mm @ {template.Dpi}dpi)",
                NewValue = JsonSerializer.Serialize(new { template.Id, template.Name, template.Description, template.LabelWidthMm, template.LabelHeightMm, template.Dpi }),
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log ZPL label template updated
        /// </summary>
        public async Task LogZplTemplateUpdatedAsync(string username, ZplLabelTemplate template)
        {
            await LogAsync(new AuditEntry
            {
                Action = "ZplTemplate_Updated",
                Category = AuditCategory.Configuration,
                Username = username,
                EntityType = "ZplTemplate",
                EntityId = template.Id.ToString(),
                Details = $"ZPL template updated: '{template.Name}' ({template.LabelWidthMm}x{template.LabelHeightMm}mm @ {template.Dpi}dpi)",
                NewValue = JsonSerializer.Serialize(new { template.Id, template.Name, template.Description, template.LabelWidthMm, template.LabelHeightMm, template.Dpi }),
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log ZPL label template deleted
        /// </summary>
        public async Task LogZplTemplateDeletedAsync(string username, ZplLabelTemplate template)
        {
            await LogAsync(new AuditEntry
            {
                Action = "ZplTemplate_Deleted",
                Category = AuditCategory.Configuration,
                Username = username,
                EntityType = "ZplTemplate",
                EntityId = template.Id.ToString(),
                Details = $"ZPL template deleted: '{template.Name}'",
                OldValue = JsonSerializer.Serialize(new { template.Id, template.Name, template.Description }),
                Severity = AuditSeverity.Warning
            });
        }

        /// <summary>
        /// Log ZPL label template set as active
        /// </summary>
        public async Task LogZplTemplateSetActiveAsync(string username, ZplLabelTemplate template)
        {
            await LogAsync(new AuditEntry
            {
                Action = "ZplTemplate_SetActive",
                Category = AuditCategory.Configuration,
                Username = username,
                EntityType = "ZplTemplate",
                EntityId = template.Id.ToString(),
                Details = $"ZPL template set as active: '{template.Name}'",
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log sync (API) settings saved
        /// </summary>
        public async Task LogSyncSettingsSavedAsync(string username, string apiBaseUrl, int retryCount, int retryDelaySeconds)
        {
            await LogAsync(new AuditEntry
            {
                Action = "SyncSettings_Saved",
                Category = AuditCategory.Configuration,
                Username = username,
                EntityType = "SyncSettings",
                Details = $"Sync settings saved: ApiBaseUrl={apiBaseUrl}, RetryCount={retryCount}, RetryDelay={retryDelaySeconds}s",
                NewValue = JsonSerializer.Serialize(new { ApiBaseUrl = apiBaseUrl, RetryCount = retryCount, RetryDelaySeconds = retryDelaySeconds }),
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Log permission matrix sync (auto-registering newly shipped menus/features)
        /// </summary>
        public async Task LogPermissionsSyncedAsync(string username, int addedCount)
        {
            await LogAsync(new AuditEntry
            {
                Action = "Permissions_Synced",
                Category = AuditCategory.Security,
                Username = username,
                Details = $"Permission matrix sync: {addedCount} new permission(s) registered",
                Severity = AuditSeverity.Info
            });
        }

        /// <summary>
        /// Create sync payload from audit entries for a specific date range
        /// </summary>
        public async Task<AuditSyncPayload> CreateSyncPayloadAsync(DateTime fromDate, DateTime toDate)
        {
            var entries = await ReadAuditEntriesAsync(fromDate, toDate);
            
            var payload = new AuditSyncPayload
            {
                SyncBatchId = Guid.NewGuid().ToString(),
                SyncTimestamp = DateTime.UtcNow,
                SourceSystem = Environment.MachineName,
                AppVersion = AppVersionProvider.GetVersion(),
                DateRangeFrom = fromDate,
                DateRangeTo = toDate,
                Metadata = new Dictionary<string, string>
                {
                    { "database_path", AppConfigurationManager.GetDatabasePath() },
                    { "log_directory", Path.GetDirectoryName(_auditLogPath) ?? "" }
                }
            };

            foreach (var entry in entries)
            {
                payload.AuditEntries.Add(new AuditSyncEntry
                {
                    EntryId = $"{entry.Timestamp:yyyyMMddHHmmss}_{entry.Action}_{Guid.NewGuid().ToString("N").Substring(0, 8)}",
                    Timestamp = entry.Timestamp,
                    Action = entry.Action,
                    Category = entry.Category.ToString(),
                    Username = entry.Username,
                    EntityType = entry.EntityType,
                    EntityId = entry.EntityId,
                    Details = entry.Details,
                    OldValue = entry.OldValue,
                    NewValue = entry.NewValue,
                    IpAddress = entry.IpAddress,
                    MachineName = entry.MachineName,
                    Severity = entry.Severity.ToString()
                });
            }

            return payload;
        }

        /// <summary>
        /// Create sync payload from today's audit entries
        /// </summary>
        public async Task<AuditSyncPayload> CreateTodaySyncPayloadAsync()
        {
            var today = DateTime.Today;
            return await CreateSyncPayloadAsync(today, today.AddDays(1).AddSeconds(-1));
        }

        /// <summary>
        /// Create sync payload from all unsynced audit entries
        /// </summary>
        public async Task<AuditSyncPayload> CreateUnsyncedPayloadAsync()
        {
            var allFiles = GetAllAuditLogFiles();
            var allEntries = new List<AuditEntry>();

            foreach (var file in allFiles)
            {
                try
                {
                    var entries = await ReadAuditLogFileAsync(file);
                    allEntries.AddRange(entries);
                }
                catch
                {
                    // Continue processing other files
                }
            }

            var payload = new AuditSyncPayload
            {
                SyncBatchId = Guid.NewGuid().ToString(),
                SyncTimestamp = DateTime.UtcNow,
                SourceSystem = Environment.MachineName,
                AppVersion = AppVersionProvider.GetVersion(),
                Metadata = new Dictionary<string, string>
                {
                    { "total_log_files", allFiles.Length.ToString() },
                    { "database_path", AppConfigurationManager.GetDatabasePath() }
                }
            };

            if (allEntries.Any())
            {
                payload.DateRangeFrom = allEntries.Min(e => e.Timestamp);
                payload.DateRangeTo = allEntries.Max(e => e.Timestamp);

                foreach (var entry in allEntries.OrderBy(e => e.Timestamp))
                {
                    payload.AuditEntries.Add(new AuditSyncEntry
                    {
                        EntryId = $"{entry.Timestamp:yyyyMMddHHmmss}_{entry.Action}_{Guid.NewGuid().ToString("N").Substring(0, 8)}",
                        Timestamp = entry.Timestamp,
                        Action = entry.Action,
                        Category = entry.Category.ToString(),
                        Username = entry.Username,
                        EntityType = entry.EntityType,
                        EntityId = entry.EntityId,
                        Details = entry.Details,
                        OldValue = entry.OldValue,
                        NewValue = entry.NewValue,
                        IpAddress = entry.IpAddress,
                        MachineName = entry.MachineName,
                        Severity = entry.Severity.ToString()
                    });
                }
            }

            return payload;
        }

        /// <summary>
        /// Read audit entries from log files within date range
        /// </summary>
        public async Task<List<AuditEntry>> ReadAuditEntriesAsync(DateTime fromDate, DateTime toDate)
        {
            var entries = new List<AuditEntry>();
            var auditDir = Path.GetDirectoryName(_auditLogPath);
            
            if (string.IsNullOrEmpty(auditDir) || !Directory.Exists(auditDir))
                return entries;

            var logFiles = Directory.GetFiles(auditDir, "audit_*.log");

            foreach (var logFile in logFiles)
            {
                try
                {
                    // Parse date from filename: audit_20240123.log
                    var fileName = Path.GetFileNameWithoutExtension(logFile);
                    var datePart = fileName.Replace("audit_", "");
                    
                    if (DateTime.TryParseExact(datePart, "yyyyMMdd", null, 
                        System.Globalization.DateTimeStyles.None, out var fileDate))
                    {
                        // Check if file date is within range
                        if (fileDate.Date >= fromDate.Date && fileDate.Date <= toDate.Date)
                        {
                            var fileEntries = await ReadAuditLogFileAsync(logFile);
                            
                            // Filter by exact timestamp range
                            var filteredEntries = fileEntries.Where(e => 
                                e.Timestamp >= fromDate && e.Timestamp <= toDate).ToList();
                            

                            entries.AddRange(filteredEntries);
                        }
                    }
                }
                catch
                {
                    // Skip invalid files
                }
            }

            return entries.OrderBy(e => e.Timestamp).ToList();
        }

        /// <summary>
        /// Read all entries from a specific audit log file
        /// </summary>
        private async Task<List<AuditEntry>> ReadAuditLogFileAsync(string filePath)
        {
            var entries = new List<AuditEntry>();

            if (!File.Exists(filePath))
                return entries;

            try
            {
                var lines = await File.ReadAllLinesAsync(filePath);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        var entry = JsonSerializer.Deserialize<AuditEntry>(line);
                        if (entry != null)
                        {
                            entries.Add(entry);
                        }
                    }
                    catch
                    {
                        // Skip malformed lines
                    }
                }
            }
            catch
            {
                // Return empty list if file can't be read
            }

            return entries;
        }

        /// <summary>
        /// Export sync payload to JSON file
        /// </summary>
        public async Task<string> ExportSyncPayloadAsync(AuditSyncPayload payload, string? outputPath = null)
        {
            if (string.IsNullOrEmpty(outputPath))
            {
                var exportDir = Path.Combine(
                    Path.GetDirectoryName(_auditLogPath) ?? "", 
                    "Exports");
                
                if (!Directory.Exists(exportDir))
                    Directory.CreateDirectory(exportDir);

                outputPath = Path.Combine(exportDir, 
                    $"audit_sync_{payload.SyncBatchId}_{DateTime.Now:yyyyMMddHHmmss}.json");
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(payload, options);
            await File.WriteAllTextAsync(outputPath, json);

            return outputPath;
        }

        /// <summary>
        /// Get audit statistics for a date range
        /// </summary>
        public async Task<AuditStatistics> GetStatisticsAsync(DateTime fromDate, DateTime toDate)
        {
            var entries = await ReadAuditEntriesAsync(fromDate, toDate);

            var stats = new AuditStatistics
            {
                TotalEntries = entries.Count,
                DateRangeFrom = fromDate,
                DateRangeTo = toDate,
                EntriesByCategory = entries.GroupBy(e => e.Category)
                    .ToDictionary(g => g.Key.ToString(), g => g.Count()),
                EntriesBySeverity = entries.GroupBy(e => e.Severity)
                    .ToDictionary(g => g.Key.ToString(), g => g.Count()),
                EntriesByUser = entries.GroupBy(e => e.Username)
                    .ToDictionary(g => g.Key, g => g.Count()),
                TopActions = entries.GroupBy(e => e.Action)
                    .OrderByDescending(g => g.Count())
                    .Take(10)
                    .ToDictionary(g => g.Key, g => g.Count())
            };

            return stats;
        }

        /// <summary>
        /// Get audit log file path
        /// </summary>
        public string GetAuditLogPath() => _auditLogPath;

        /// <summary>
        /// Get all audit log files
        /// </summary>
        public string[] GetAllAuditLogFiles()
        {
            var auditDir = Path.GetDirectoryName(_auditLogPath);
            if (string.IsNullOrEmpty(auditDir) || !Directory.Exists(auditDir))
                return Array.Empty<string>();

            return Directory.GetFiles(auditDir, "audit_*.log");
        }
    }

    /// <summary>
    /// Audit log entry
    /// Note: Timestamp is stored in UTC format and should be converted to local time when displayed to users
    /// </summary>
    public class AuditEntry
    {
        /// <summary>
        /// Timestamp in UTC format. Convert to local time when displaying to users.
        /// </summary>
        public DateTime Timestamp { get; set; }
        public string Action { get; set; } = string.Empty;
        public AuditCategory Category { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public string? EntityId { get; set; }
        public string Details { get; set; } = string.Empty;
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? IpAddress { get; set; }
        public string MachineName { get; set; } = string.Empty;
        public AuditSeverity Severity { get; set; }
    }

    /// <summary>
    /// Audit categories
    /// </summary>
    public enum AuditCategory
    {
        Authentication,
        Authorization,
        UserManagement,
        Material,
        Transaction,
        Configuration,
        Security,
        System,
        Report,
        Backup
    }

    /// <summary>
    /// Severity levels
    /// </summary>
    public enum AuditSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// Audit sync payload
    /// </summary>
    public class AuditSyncPayload
    {
        public string SyncBatchId { get; set; } = string.Empty;
        public DateTime SyncTimestamp { get; set; }
        public string SourceSystem { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
        public DateTime DateRangeFrom { get; set; }
        public DateTime DateRangeTo { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
        public List<AuditSyncEntry> AuditEntries { get; set; } = new();
    }

    /// <summary>
    /// Audit sync entry
    /// </summary>
    public class AuditSyncEntry
    {
        public string EntryId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public string? EntityId { get; set; }
        public string Details { get; set; } = string.Empty;
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? IpAddress { get; set; }
        public string MachineName { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
    }

    /// <summary>
    /// Audit statistics summary
    /// </summary>
    public class AuditStatistics
    {
        public int TotalEntries { get; set; }
        public DateTime DateRangeFrom { get; set; }
        public DateTime DateRangeTo { get; set; }
        public Dictionary<string, int> EntriesByCategory { get; set; } = new();
        public Dictionary<string, int> EntriesBySeverity { get; set; } = new();
        public Dictionary<string, int> EntriesByUser { get; set; } = new();
        public Dictionary<string, int> TopActions { get; set; } = new();
    }
}

